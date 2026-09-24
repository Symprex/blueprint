namespace Blueprint.Runtime.Http;

/// <summary>
/// How the OpenAPI document keys the schema component of an operation's request body.
/// </summary>
public enum OpenApiRequestBodyNaming
{
    /// <summary>
    /// The operation's name (<c>CreatePartnerCompany</c>).
    /// </summary>
    OperationName = 0,

    /// <summary>
    /// The operation's CLR type, named under the <see cref="OpenApiSchemaNaming"/> in force
    /// (<c>CreatePartnerCompanyCommand</c>), as legacy Blueprint keyed it. Generated clients that tell commands from
    /// queries by a <c>Command</c> suffix on the body's name need this.
    /// </summary>
    OperationType = 1,
}
