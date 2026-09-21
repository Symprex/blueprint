using Blueprint.Http;
using Blueprint.Authorisation;
using Blueprint.OpenApi;
using Blueprint.Properties;

namespace SharedDocumentUsingsApp;

/// <summary>Consumer touching all four legacy namespaces from one document.</summary>
public sealed class Consumer
{
    /// <summary>Holds the markers so the usings are not merely decorative.</summary>
    public object[] Markers { get; } =
    [
        new LegacyHttpMarker(),
        new LegacyAuthMarker(),
        new LegacyOpenApiMarker(),
        new LegacyPropertiesMarker(),
    ];
}
