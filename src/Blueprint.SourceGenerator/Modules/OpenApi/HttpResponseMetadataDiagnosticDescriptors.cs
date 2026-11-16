using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Diagnostic descriptors for projecting an operation's HTTP responses into its descriptor.
/// </summary>
internal static class HttpResponseMetadataDiagnosticDescriptors
{
    /// <summary>
    /// BPOA003: <c>[ProducesContentType]</c> declares a value that is not a <c>type/subtype</c> media type. The attribute is
    /// read from source, never constructed, so nothing else rejects it, and it would become an invalid OpenAPI content key.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidDeclaredContentType = new(
        id: "BPOA003",
        title: "Declared content type is not a media type",
        messageFormat: "The operation '{0}' declares the content type '{1}' in [ProducesContentType], which is not a 'type/subtype' media type",
        category: "OpenApi",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
