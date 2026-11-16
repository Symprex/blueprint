using System.Text.Json;
using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a collection property whose collection type has a converter of its own on the serializer options is still
/// documented as an array of its element type, and that the element type and the types only it reaches are components.
/// </summary>
/// <remarks>
/// A converter that reads a collection's items one at a time, so that a single bad item is refused on its own, is
/// registered for the collection type on the options. The context combined with those options then describes the
/// collection as <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.None"/>, with no element type. The
/// document used to stop there: the element type and every type reached only through it had no component, and the
/// property's items were a bare object. The converter writes the array it reads, so the CLR type's element still says
/// what each item is.
/// </remarks>
public sealed class OpenApiConverterWrittenCollectionTests
{
    [Fact]
    public void A_request_body_collection_written_by_an_options_converter_references_its_element_component()
    {
        using var document = Render(new ConvertedCollectionTestApp());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        var items = schemas.GetProperty("SubmitConvertedItems").GetProperty("properties").GetProperty("items");
        items.GetProperty("type").GetString().Should().Be("array");
        items.GetProperty("items").GetProperty("$ref").GetString().Should().Be("#/components/schemas/ConvertedCollectionItem");

        schemas.GetProperty("ConvertedCollectionItem").GetProperty("properties").GetProperty("Photo")
            .GetProperty("$ref").GetString().Should().Be("#/components/schemas/ConvertedCollectionItemPhoto");
        schemas.TryGetProperty("ConvertedCollectionItemPhoto", out _).Should().BeTrue();
    }

    [Fact]
    public void A_response_collection_written_by_an_options_converter_references_its_element_component()
    {
        using var document = Render(new ConvertedCollectionTestApp());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        var items = schemas.GetProperty("ConvertedCollectionApiResource").GetProperty("properties").GetProperty("Items");
        items.GetProperty("type").GetString().Should().Be("array");
        items.GetProperty("items").GetProperty("$ref").GetString().Should().Be("#/components/schemas/ConvertedCollectionItem");
    }

    [Fact]
    public void Without_the_converter_the_same_collection_renders_the_same_way()
    {
        // The baseline the converted collection is held to: the context alone describes the array as an Enumerable.
        using var converted = Render(new ConvertedCollectionTestApp());
        using var plain = Render(new IntegrationTestBlueprintApp());

        foreach (var (component, property) in new[] { ("SubmitConvertedItems", "items"), ("ConvertedCollectionApiResource", "Items") })
        {
            Property(converted, component, property).Should().Be(Property(plain, component, property));
        }

        foreach (var component in new[] { "ConvertedCollectionItem", "ConvertedCollectionItemPhoto" })
        {
            converted.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(component).GetRawText()
                .Should().Be(plain.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(component).GetRawText());
        }
    }

    private static string Property(JsonDocument document, string component, string property)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(component)
            .GetProperty("properties").GetProperty(property).GetRawText();
    }

    private static JsonDocument Render(BlueprintApp app)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(app, new OpenApiInfoOptions());

        return JsonDocument.Parse(rendered.Json);
    }

    /// <summary>The integration app with a converter for <see cref="ConvertedCollectionItem"/>[] on its serializer options.</summary>
    private sealed class ConvertedCollectionTestApp : IntegrationTestBlueprintApp
    {
        public ConvertedCollectionTestApp()
        {
            this.AppOwnedSerializerContext = new IntegrationTestAppJsonContext(
                new JsonSerializerOptions { Converters = { new ConvertedCollectionItemsConverter() } });
        }
    }
}

/// <summary>An item of a collection a converter on the options reads one item at a time.</summary>
public sealed class ConvertedCollectionItem
{
    public string? Name { get; set; }

    public ConvertedCollectionItemPhoto Photo { get; set; } = new();
}

/// <summary>A type only <see cref="ConvertedCollectionItem"/> reaches.</summary>
public sealed class ConvertedCollectionItemPhoto
{
    public string? ContentType { get; set; }
}

/// <summary>A resource holding a collection a converter on the options writes.</summary>
public sealed partial class ConvertedCollectionApiResource : ApiResource
{
    public ConvertedCollectionItem[] Items { get; set; } = [];
}

/// <summary>Reads and writes a <see cref="ConvertedCollectionItem"/>[] one item at a time.</summary>
public sealed class ConvertedCollectionItemsConverter : JsonConverter<ConvertedCollectionItem[]>
{
    public override ConvertedCollectionItem[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var itemInfo = options.GetTypeInfo(typeof(ConvertedCollectionItem));
        var items = new List<ConvertedCollectionItem>();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            items.Add((ConvertedCollectionItem)JsonSerializer.Deserialize(ref reader, itemInfo)!);
        }

        return [.. items];
    }

    public override void Write(Utf8JsonWriter writer, ConvertedCollectionItem[] value, JsonSerializerOptions options)
    {
        var itemInfo = options.GetTypeInfo(typeof(ConvertedCollectionItem));
        writer.WriteStartArray();

        foreach (var item in value)
        {
            JsonSerializer.Serialize(writer, item, itemInfo);
        }

        writer.WriteEndArray();
    }
}

/// <summary>Submits a collection a converter on the options reads.</summary>
[AllowAnonymous]
[RootLink("/converted-items")]
[HttpPost]
public partial class SubmitConvertedItemsCommand : ICommand<ConvertedCollectionApiResource>
{
    public ConvertedCollectionItem[] Items { get; set; } = [];

    public static ConvertedCollectionApiResource Handle(SubmitConvertedItemsCommand command)
    {
        return new ConvertedCollectionApiResource { Items = command.Items };
    }
}
