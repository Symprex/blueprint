namespace Blueprint.Model.Attributes;

/// <summary>
/// Provides a representative example value for the annotated property or parameter in OpenAPI documentation.
/// Unlike <see cref="System.ComponentModel.DefaultValueAttribute"/>, which declares the server-side default,
/// this attribute declares a sample value for API consumers and tooling (Swagger UI, code generators).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = true, AllowMultiple = false)]
public sealed class OpenApiExampleAttribute : Attribute
{
    /// <summary>
    /// Initialises a new instance of the <see cref="OpenApiExampleAttribute"/> class.
    /// </summary>
    /// <param name="value">The example value. Must be a compile-time constant (string, numeric, or boolean).</param>
    public OpenApiExampleAttribute(object value)
    {
        this.Value = value;
    }

    /// <summary>
    /// Gets the example value to render in the OpenAPI <c>example</c> field.
    /// </summary>
    public object Value { get; }
}
