using System.Text.Json;
using Blueprint.Runtime.Http;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves the pooled response buffer keeps every byte written through it when it grows, and starts clean after it is
/// cleared or released.
/// </summary>
public sealed class PooledBufferWriterTests
{
    [Fact]
    public void When_a_body_much_larger_than_the_first_array_is_written_in_small_steps_then_every_byte_is_kept_in_order()
    {
        var writer = new PooledBufferWriter();
        var expected = new byte[300_000];
        new Random(42).NextBytes(expected);

        for (var offset = 0; offset < expected.Length;)
        {
            var count = Math.Min(777, expected.Length - offset);
            var span = writer.GetSpan(count);
            expected.AsSpan(offset, count).CopyTo(span);
            writer.Advance(count);
            offset += count;
        }

        writer.WrittenCount.Should().Be(expected.Length);
        writer.WrittenMemory.ToArray().Should().Equal(expected);
    }

    [Fact]
    public void When_a_single_request_is_larger_than_the_array_then_the_array_grows_to_fit_it()
    {
        var writer = new PooledBufferWriter();

        writer.GetMemory(5).Length.Should().BeGreaterThanOrEqualTo(5);
        writer.Advance(5);

        writer.GetMemory(500_000).Length.Should().BeGreaterThanOrEqualTo(500_000);
        writer.WrittenCount.Should().Be(5);
    }

    [Fact]
    public void When_the_writer_is_released_then_the_next_body_starts_empty_and_is_not_mixed_with_the_last()
    {
        var writer = new PooledBufferWriter();
        writer.GetSpan(100_000).Fill(7);
        writer.Advance(100_000);

        writer.Release();
        writer.GetSpan(3)[..3].Fill(9);
        writer.Advance(3);

        writer.WrittenMemory.ToArray().Should().Equal(9, 9, 9);
    }

    [Fact]
    public void When_the_writer_backs_a_utf8_json_writer_then_a_large_document_is_the_serialised_one()
    {
        var items = Enumerable.Range(0, 5_000).Select(i => new { id = i, name = "item " + i }).ToArray();
        var expected = JsonSerializer.SerializeToUtf8Bytes(items);
        var buffer = new PooledBufferWriter();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            JsonSerializer.Serialize(writer, items);
        }

        buffer.WrittenMemory.ToArray().Should().Equal(expected);
    }

    [Fact]
    public void When_the_count_advanced_is_beyond_what_was_asked_for_then_it_is_rejected()
    {
        var writer = new PooledBufferWriter();
        var length = writer.GetSpan(1).Length;

        var advance = () => writer.Advance(length + 1);

        advance.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void When_the_body_stream_throws_before_its_write_starts_then_the_large_array_is_given_back()
    {
        var writer = new PooledBufferWriter();
        writer.GetSpan(100_000).Fill(7);
        writer.Advance(100_000);

        var write = () => writer.WriteToAsync(new ThrowingStream(), CancellationToken.None, out _);

        write.Should().Throw<IOException>();
        writer.WrittenCount.Should().Be(0);
        writer.GetMemory(1).Length.Should().BeLessThan(100_000);
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            throw new IOException("The connection was reset.");
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new IOException("The connection was reset.");
        }
    }
}
