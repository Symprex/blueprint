using System.Text.Json;
using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a <see cref="Type"/> property, or a collection of them, is documented as a string, whether it is a
/// response property or a request body property.
/// </summary>
/// <remarks>
/// A converter writes a <see cref="Type"/> as its name, so the only shape a document can give one is a string.
/// Signature365's Portal <c>TemplateOptions.SkippedProcessors</c> (an <c>IEnumerable&lt;Type&gt;</c>) was documented as an
/// array with no <c>items</c>, which its client generator cannot type and throws on; legacy wrote
/// <c>"items": {"type": "string"}</c>.
/// </remarks>
public sealed class OpenApiSystemTypeTests
{
    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void A_response_property_of_System_Type_is_a_string(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        var properties = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("SystemTypeApiResource").GetProperty("properties");

        properties.GetProperty("Kind").GetProperty("type").GetString().Should().Be("string");
        properties.GetProperty("SkippedProcessors").GetProperty("type").GetString().Should().Be("array");
        properties.GetProperty("SkippedProcessors").GetProperty("items").GetProperty("type").GetString().Should().Be("string");
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void A_request_body_property_of_System_Type_is_a_string(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        var properties = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("SubmitSystemType")
            .GetProperty("properties").EnumerateObject().ToDictionary(static property => property.Name.ToLowerInvariant(), static property => property.Value);

        properties["handler"].GetProperty("type").GetString().Should().Be("string");
        properties["handlers"].GetProperty("items").GetProperty("type").GetString().Should().Be("string");
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming });

        return JsonDocument.Parse(rendered.Json);
    }
}

/// <summary>Holds a <see cref="Type"/> and a collection of them, as Signature365's <c>TemplateOptions</c> does.</summary>
public sealed class SystemTypeApiResource
{
    [JsonConverter(typeof(SystemTypeNameConverter))]
    public Type? Kind { get; set; }

    [JsonConverter(typeof(SystemTypeNamesConverter))]
    public IEnumerable<Type>? SkippedProcessors { get; set; }
}

/// <summary>Writes a <see cref="Type"/> as its name, as an app that serialises one must.</summary>
public sealed class SystemTypeNameConverter : JsonConverter<Type?>
{
    public override Type? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return Type.GetType(reader.GetString() ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, Type? value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value?.FullName);
    }
}

/// <summary>Writes a collection of <see cref="Type"/> as their names.</summary>
public sealed class SystemTypeNamesConverter : JsonConverter<IEnumerable<Type>?>
{
    public override IEnumerable<Type>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return null;
    }

    public override void Write(Utf8JsonWriter writer, IEnumerable<Type>? value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var type in value ?? [])
        {
            writer.WriteStringValue(type.FullName);
        }

        writer.WriteEndArray();
    }
}

/// <summary>Reads the resource holding the <see cref="Type"/> properties.</summary>
[AllowAnonymous]
[RootLink("/system-type")]
public partial class GetSystemTypeQuery : IQuery<SystemTypeApiResource>
{
    public static SystemTypeApiResource Handle(GetSystemTypeQuery query)
    {
        return new SystemTypeApiResource();
    }
}

/// <summary>Submits a <see cref="Type"/> and a collection of them in a body.</summary>
[AllowAnonymous]
[RootLink("/system-type")]
[HttpPost]
public partial class SubmitSystemTypeCommand : ICommand<string>
{
    public Type? Handler { get; set; }

    public IEnumerable<Type>? Handlers { get; set; }

    public static string Handle(SubmitSystemTypeCommand command)
    {
        return command.Handler?.FullName ?? string.Empty;
    }
}
