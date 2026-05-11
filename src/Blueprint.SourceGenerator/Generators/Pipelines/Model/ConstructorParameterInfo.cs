namespace Blueprint.SourceGenerator.Generators.Pipelines.Model;

/// <summary>
/// Represents a parameter of an external handler class constructor, used to generate a direct
/// <c>new Handler(sp.GetRequiredService&lt;X&gt;(), ...)</c> call instead of the reflection-heavy
/// <c>ActivatorUtilities.CreateInstance&lt;Handler&gt;(sp)</c>.
/// </summary>
/// <param name="ParameterTypeName">The fully qualified type name of the constructor parameter.</param>
public sealed record ConstructorParameterInfo(
    string ParameterTypeName) : IEquatable<ConstructorParameterInfo>
{
    /// <inheritdoc />
    public bool Equals(ConstructorParameterInfo? other)
    {
        return other is not null
            && (ReferenceEquals(this, other) || this.ParameterTypeName == other.ParameterTypeName);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return this.ParameterTypeName.GetHashCode();
    }
}
