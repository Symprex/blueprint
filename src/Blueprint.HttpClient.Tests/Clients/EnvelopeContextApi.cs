using System.Text.Json.Serialization;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.HttpClient.Tests.Clients;

/// <summary>
/// A typed client whose serializer context declares neither of the types its call needs, only a type that reaches both.
/// The build treats warnings as errors, so this compiling means BPCL007 counts what the context reaches.
/// </summary>
[BlueprintClient(SerializerContext = typeof(EnvelopeJsonContext))]
[BlueprintClientInclude(typeof(CreateTemplateCommand))]
public partial class EnvelopeContextApi;

/// <summary>
/// The only type <see cref="EnvelopeJsonContext"/> declares, reaching the command and the template it returns.
/// </summary>
public sealed class TemplateEnvelope
{
    /// <summary>
    /// Gets or sets the command.
    /// </summary>
    public CreateTemplateCommand? Command { get; set; }

    /// <summary>
    /// Gets or sets the template.
    /// </summary>
    public TemplateApiResource? Template { get; set; }
}

/// <summary>
/// A System.Text.Json source-generated context that declares only <see cref="TemplateEnvelope"/>.
/// </summary>
[JsonSerializable(typeof(TemplateEnvelope))]
public sealed partial class EnvelopeJsonContext : JsonSerializerContext;
