namespace Blueprint.Runtime.Http;

/// <summary>
/// How the OpenAPI document names the schema components of CLR types.
/// </summary>
public enum OpenApiSchemaNaming
{
    /// <summary>
    /// The type's simple name; a generic type is named <c>{Name}Of{Argument}</c>, with the arguments' simple names
    /// joined by <c>And</c> and trimmed of an <c>ApiResource</c>, <c>Resource</c>, <c>DTO</c> or <c>Response</c>
    /// suffix (<c>PagedApiResource&lt;TemplateApiResource&gt;</c> is <c>PagedApiResourceOfTemplate</c>).
    /// </summary>
    Default = 0,

    /// <summary>
    /// The names legacy Blueprint's NJsonSchema generator gave, so a consumer's generated clients keep the type names
    /// they import. A generic type is named <c>{Name}Of{Argument}</c> recursively, the arguments joined by
    /// <c>And</c> and never trimmed (<c>PagedApiResource&lt;TemplateApiResource&gt;</c> is
    /// <c>PagedApiResourceOfTemplateApiResource</c>, <c>Pair&lt;A, List&lt;B&gt;&gt;</c> is <c>PairOfAAndListOfB</c>);
    /// <see cref="short"/>, <see cref="int"/> and <see cref="long"/> are <c>Short</c>, <c>Integer</c> and
    /// <c>Long</c>; <c>Nullable&lt;T&gt;</c> is <c>Nullable{T}</c>; an array is <c>{T}[]</c>; a nested type is its
    /// own simple name. A type annotated with <c>[OpenApiSchemaName]</c> takes that name, as an argument too.
    /// </summary>
    /// <remarks>
    /// <see cref="OpenApiInfoOptions.SchemaNameFormatter"/> is applied to these names — for request bodies keyed by type
    /// and ResourceEvent envelopes as well — so a consumer can reproduce the rest of its legacy naming there. Each
    /// parameter's schema is titled with the name of the type a <see cref="Nullable{T}"/> wraps, formatted the same way,
    /// as legacy titled its root parameter schemas.
    /// </remarks>
    Legacy = 1,
}
