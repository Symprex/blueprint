using System.Buffers;

namespace Blueprint.Runtime.Http;

/// <summary>
/// An <see cref="IBufferWriter{T}"/> over arrays rented from <see cref="ArrayPool{T}.Shared"/>, which holds a serialised
/// response body until it has been written and then gives its array back, so a large body is never a new array of its own.
/// </summary>
/// <remarks>
/// <para>
/// A growing <see cref="ArrayBufferWriter{T}"/> allocates every array it grows through, the last of them the size of the
/// body, which for a body over 85,000 bytes is a large-object-heap array that only a gen2 collection frees. The thread's
/// buffer used to avoid that by being reused, but a write that did not finish at once had to take it (see
/// <c>HttpOperationResultExecutor.WriteThreadBufferAsync</c>), after which the thread grew a new one from 512 bytes on its
/// next response: under load, every large response paid for the whole chain of arrays.
/// </para>
/// <para>
/// An instance is not thread-safe: it belongs to one response at a time. The array it holds goes back to the pool in
/// <see cref="Release"/>, which the owner calls once the memory it exposed through <see cref="WrittenMemory"/> is no
/// longer read, and not before.
/// </para>
/// </remarks>
internal sealed class PooledBufferWriter : IBufferWriter<byte>
{
    private const int InitialCapacity = 1024;

    // An array this long or shorter stays with the writer between responses, so a typical small body never visits the
    // pool; a longer one goes back to it, so an idle thread does not hold a large body's memory.
    private const int RetainedCapacity = 16 * 1024;

    private byte[]? _array;
    private int _written;

    /// <summary>
    /// Gets the number of bytes written since the last <see cref="Clear"/>.
    /// </summary>
    public int WrittenCount => this._written;

    /// <summary>
    /// Gets the bytes written since the last <see cref="Clear"/>, valid until <see cref="Release"/> or the next write.
    /// </summary>
    public ReadOnlyMemory<byte> WrittenMemory => this._array is null ? default : this._array.AsMemory(0, this._written);

    /// <summary>
    /// Forgets what has been written, keeping the array to write into.
    /// </summary>
    public void Clear()
    {
        this._written = 0;
    }

    /// <summary>
    /// Forgets what has been written and gives back an array too long to keep between responses, as the owner calls it
    /// when the written memory is no longer read.
    /// </summary>
    public void Release()
    {
        this._written = 0;

        if (this._array is { Length: > RetainedCapacity } array)
        {
            this._array = null;
            ArrayPool<byte>.Shared.Return(array);
        }
    }

    /// <summary>
    /// Gives back the array whatever its length, as the owner calls it for an instance it will not use again.
    /// </summary>
    public void ReturnArray()
    {
        this._written = 0;

        if (this._array is { } array)
        {
            this._array = null;
            ArrayPool<byte>.Shared.Return(array);
        }
    }

    /// <summary>
    /// Writes what has been written to <paramref name="body"/> and gives the array back once that write is over.
    /// </summary>
    /// <param name="body">The stream to write to.</param>
    /// <param name="cancellationToken">The token the write observes.</param>
    /// <param name="detached">
    /// <see langword="true"/> when the write did not finish at once, so this instance belongs to it until it does and the
    /// caller must not use it again (a body stream may read the memory it was given until its write completes, so a
    /// thread's next response must serialise into another instance).
    /// </param>
    /// <returns>The body stream's write.</returns>
    public ValueTask WriteToAsync(Stream body, CancellationToken cancellationToken, out bool detached)
    {
        var writeTask = body.WriteAsync(this.WrittenMemory, cancellationToken);

        if (writeTask.IsCompleted)
        {
            detached = false;
            this.Release();
            return writeTask;
        }

        detached = true;
        return this.AwaitWriteAndReturnArrayAsync(writeTask);
    }

    /// <inheritdoc />
    public void Advance(int count)
    {
        if (count < 0 || this._array is null || this._written > this._array.Length - count)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        this._written += count;
    }

    /// <inheritdoc />
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        var array = this.EnsureCapacity(sizeHint);

        return array.AsMemory(this._written);
    }

    /// <inheritdoc />
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        var array = this.EnsureCapacity(sizeHint);

        return array.AsSpan(this._written);
    }

    private async ValueTask AwaitWriteAndReturnArrayAsync(ValueTask writeTask)
    {
        try
        {
            await writeTask.ConfigureAwait(false);
        }
        finally
        {
            this.ReturnArray();
        }
    }

    private byte[] EnsureCapacity(int sizeHint)
    {
        if (sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }

        if (sizeHint == 0)
        {
            sizeHint = 1;
        }

        var array = this._array;

        if (array is null)
        {
            return this._array = ArrayPool<byte>.Shared.Rent(Math.Max(InitialCapacity, sizeHint));
        }

        if (array.Length - this._written >= sizeHint)
        {
            return array;
        }

        var required = (long)this._written + sizeHint;
        var grown = Math.Max(required, (long)array.Length * 2);

        if (grown > Array.MaxLength)
        {
            grown = required > Array.MaxLength ? throw new OutOfMemoryException("The response body is too large to buffer.") : Array.MaxLength;
        }

        var larger = ArrayPool<byte>.Shared.Rent((int)grown);
        Buffer.BlockCopy(array, 0, larger, 0, this._written);
        this._array = larger;
        ArrayPool<byte>.Shared.Return(array);

        return larger;
    }
}
