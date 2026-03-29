namespace Blueprint.Model;

/// <summary>
/// Represents one of two possible semantic result values without coupling the contract layer to any host-specific
/// execution behavior.
/// </summary>
/// <typeparam name="T1">The first supported result type.</typeparam>
/// <typeparam name="T2">The second supported result type.</typeparam>
public readonly struct Results<T1, T2>
    where T1 : notnull
    where T2 : notnull
{
    private readonly object? _value;
    private readonly byte _memberIndex;

    private Results(object value, byte memberIndex)
    {
        ArgumentNullException.ThrowIfNull(value);

        this._value = value;
        this._memberIndex = memberIndex;
    }

    /// <summary>
    /// Gets a value indicating whether this union currently contains the first supported result type.
    /// </summary>
    public bool IsT1 => this._memberIndex == 1;

    /// <summary>
    /// Gets a value indicating whether this union currently contains the second supported result type.
    /// </summary>
    public bool IsT2 => this._memberIndex == 2;

    /// <summary>
    /// Gets the contained first result value.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the union currently contains a different member type.</exception>
    public T1 AsT1 => this.IsT1 ? (T1)this._value! : throw CreateInvalidAccessException(typeof(T1), this._value);

    /// <summary>
    /// Gets the contained second result value.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the union currently contains a different member type.</exception>
    public T2 AsT2 => this.IsT2 ? (T2)this._value! : throw CreateInvalidAccessException(typeof(T2), this._value);

    /// <summary>
    /// Creates a union value from the first supported result type.
    /// </summary>
    /// <param name="value">The result value to store.</param>
    public static implicit operator Results<T1, T2>(T1 value)
    {
        return new Results<T1, T2>(value, memberIndex: 1);
    }

    /// <summary>
    /// Creates a union value from the second supported result type.
    /// </summary>
    /// <param name="value">The result value to store.</param>
    public static implicit operator Results<T1, T2>(T2 value)
    {
        return new Results<T1, T2>(value, memberIndex: 2);
    }

    private static InvalidOperationException CreateInvalidAccessException(Type expectedType, object? actualValue)
    {
        if (actualValue is null)
        {
            return new InvalidOperationException("The current union value has not been set.");
        }

        var actualTypeName = actualValue.GetType().FullName ?? actualValue.GetType().Name;
        var expectedTypeName = expectedType.FullName ?? expectedType.Name;
        return new InvalidOperationException($"The current union value is {actualTypeName}, not {expectedTypeName}.");
    }
}
