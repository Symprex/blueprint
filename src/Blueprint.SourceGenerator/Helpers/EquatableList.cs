using System.Collections;
using JetBrains.Annotations;

namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// An immutable, equatable array. This is equivalent to <see cref="Array"/> but with value equality support.
/// </summary>
/// <typeparam name="T">The type of values in the array.</typeparam>
public readonly struct EquatableList<T> : IEquatable<EquatableList<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    /// <summary>
    /// The underlying <typeparamref name="T"/> list.
    /// </summary>
    private readonly List<T> _list;

    /// <summary>
    /// Initializes a new instance of the <see cref="EquatableList{T}"/> struct.
    /// </summary>
    public EquatableList()
    {
        this._list = [];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EquatableList{T}"/> struct.
    /// </summary>
    /// <param name="array">The input array to wrap.</param>
    public EquatableList(T[] array)
    {
        this._list = new List<T>(array);
    }

    /// <summary>
    /// Gets the length of the array, or 0 if the array is null
    /// </summary>
    public int Count => this._list.Count;

    /// <summary>
    /// Gets the element at the specified index.
    /// </summary>
    /// <param name="index">The index of the element to get.</param>
    public T this[int index] => this._list[index];

    /// <summary>
    /// Checks whether two <see cref="EquatableList{T}"/> values are the same.
    /// </summary>
    /// <param name="left">The first <see cref="EquatableList{T}"/> value.</param>
    /// <param name="right">The second <see cref="EquatableList{T}"/> value.</param>
    /// <returns>Whether <paramref name="left"/> and <paramref name="right"/> are equal.</returns>
    public static bool operator ==(EquatableList<T> left, EquatableList<T> right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Checks whether two <see cref="EquatableList{T}"/> values are not the same.
    /// </summary>
    /// <param name="left">The first <see cref="EquatableList{T}"/> value.</param>
    /// <param name="right">The second <see cref="EquatableList{T}"/> value.</param>
    /// <returns>Whether <paramref name="left"/> and <paramref name="right"/> are not equal.</returns>
    public static bool operator !=(EquatableList<T> left, EquatableList<T> right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Adds a new item to this list.
    /// </summary>
    /// <param name="item">The item to add.</param>
    public void Add(T item)
    {
        this._list.Add(item);
    }

    /// <inheritdoc/>
    public bool Equals(EquatableList<T> other)
    {
        var left = this._list;
        var right = other._list;

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is EquatableList<T> array && this.Equals(array);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (this._list is not { } items)
        {
            return 0;
        }

        HashCode hashCode = default;

        for (var i = 0; i < items.Count; i++)
        {
            hashCode.Add(items[i]);
        }

        return hashCode.ToHashCode();
    }

    /// <summary>
    /// Returns a copy of the underlying items as a new array.
    /// </summary>
    /// <returns>A new array containing the items.</returns>
    public T[] AsArray()
    {
        return this._list.ToArray();
    }

    /// <inheritdoc/>
    [MustDisposeResource]
    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return ((IEnumerable<T>)this._list).GetEnumerator();
    }

    /// <inheritdoc/>
    [MustDisposeResource]
    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<T>)this._list).GetEnumerator();
    }
}
