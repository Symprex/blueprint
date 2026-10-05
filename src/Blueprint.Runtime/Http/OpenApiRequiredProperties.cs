namespace Blueprint.Runtime.Http;

/// <summary>
/// Which properties and parameters the OpenAPI document marks as required.
/// </summary>
public enum OpenApiRequiredProperties
{
    /// <summary>
    /// Those declared required: a serializer-required member (a C# <c>required</c> member or <c>[JsonRequired]</c>) of a
    /// response type, a <c>[Required]</c> property of a request body or query, and every route parameter.
    /// </summary>
    Declared = 0,

    /// <summary>
    /// As legacy Blueprint marked them: every property and parameter whose type is not nullable — a value type that is
    /// not <c>Nullable&lt;T&gt;</c>, or a reference type annotated as not nullable — and every <c>[Required]</c> one, which
    /// is then not documented as nullable, besides those <see cref="Declared"/> marks.
    /// </summary>
    NonNullable = 1,
}