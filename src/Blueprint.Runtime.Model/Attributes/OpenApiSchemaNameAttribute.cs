namespace Blueprint.Model.Attributes;

/// <summary>
/// Gives the OpenAPI schema component for the annotated type the name specified, in place of the one derived from the
/// type — as NJsonSchema's <c>[JsonSchema("Name")]</c> did for legacy Blueprint.
/// </summary>
/// <remarks>
/// The name is used wherever the type's name is: as its own component key, and, under
/// <c>OpenApiSchemaNaming.Legacy</c>, inside the name of a generic type it is an argument of. Any
/// <c>OpenApiInfoOptions.SchemaNameFormatter</c> is still applied to the result.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class OpenApiSchemaNameAttribute : Attribute
{
    /// <summary>
    /// Initialises a new instance of the <see cref="OpenApiSchemaNameAttribute"/> class.
    /// </summary>
    /// <param name="name">The schema component name for the annotated type.</param>
    public OpenApiSchemaNameAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        this.Name = name;
    }

    /// <summary>
    /// Gets the schema component name for the annotated type.
    /// </summary>
    public string Name { get; }
}
