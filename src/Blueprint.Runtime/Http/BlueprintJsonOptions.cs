using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blueprint.Http;

/// <summary>
/// Configuration options for the <see cref="System.Text.Json"/> serializer that Blueprint
/// uses when writing HTTP response bodies. Register in DI via
/// <c>services.AddOptions&lt;BlueprintJsonOptions&gt;()</c> and configure
/// <see cref="SerializerOptions"/> to add NodaTime converters, custom naming policies, or
/// other serializer customizations.
/// </summary>
public class BlueprintJsonOptions
{
    /// <summary>
    /// Default <see cref="JsonSerializerOptions"/> used when no consumer-provided options
    /// are registered. Blueprint creates these with camelCase property naming, relaxed
    /// JSON escaping, and <see cref="JsonStringEnumConverter"/>.
    /// </summary>
    internal static readonly JsonSerializerOptions DefaultSerializerOptions = CreateDefaultOptions();

    /// <summary>
    /// Gets the <see cref="JsonSerializerOptions"/> that will be used when Blueprint serializes
    /// HTTP response bodies.
    /// </summary>
    public JsonSerializerOptions SerializerOptions { get; } = CreateDefaultOptions();

    /// <summary>
    /// Creates a fresh set of <see cref="JsonSerializerOptions"/> with Blueprint's default
    /// configuration: camelCase property naming, case-insensitive deserialization,
    /// relaxed JSON escaping, and string enum conversion.
    /// </summary>
    /// <returns>A new <see cref="JsonSerializerOptions"/> instance.</returns>
    public static JsonSerializerOptions CreateDefaultOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

            // Responses are written directly to the HTTP stream (not embedded in HTML),
            // so relaxed escaping is safe and avoids unnecessary encoding overhead.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
