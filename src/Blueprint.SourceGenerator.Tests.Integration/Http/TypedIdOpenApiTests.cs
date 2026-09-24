using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a type written by a converter — a strongly typed id, or a byte array — is documented as the string it
/// is written as, as legacy Blueprint documented it, rather than as an unknown <c>{}</c> or an <c>object</c>.
/// </summary>
/// <remarks>
/// <para>
/// The id is shaped as Signature365's <c>ApiId</c> is: a struct with a <see cref="TypeConverter"/> to and from string
/// and a System.Text.Json converter that writes the string, and no <c>IParsable</c>. Legacy described types through
/// Newtonsoft.Json's contracts, and Newtonsoft treats a type whose type converter converts to string as a string.
/// </para>
/// <para>
/// Every expected schema is legacy's, from its NJsonSchema 11.1 settings run on the same shapes: the id is
/// <c>{"type": "string"}</c>, the nullable one adds <c>"nullable": true</c>, and a <c>byte[]</c> is
/// <c>{"type": "string", "format": "byte"}</c>. Signature365's committed Partners document gives its <c>ApiId</c>
/// path parameters <c>{"type": "string"}</c> too.
/// </para>
/// </remarks>
public sealed class TypedIdOpenApiTests
{
    [Fact]
    public void A_typed_id_property_is_documented_as_a_string()
    {
        using var document = Render();
        var properties = Component(document, "TypedIdWidgetApiResource").GetProperty("properties");

        Schema(properties.GetProperty("Id")).Should().Be("type=\"string\"");
        Schema(properties.GetProperty("ParentId")).Should().Be("type=\"string\"; nullable=true");
    }

    [Fact]
    public void A_byte_array_property_is_documented_as_a_base64_string()
    {
        using var document = Render();

        Schema(Component(document, "TypedIdWidgetApiResource").GetProperty("properties").GetProperty("Content"))
            .Should().Be("type=\"string\"; format=\"byte\"");
    }

    [Fact]
    public void A_typed_id_parameter_is_documented_as_a_string()
    {
        using var document = Render();
        var parameters = document.RootElement.GetProperty("paths").GetProperty("/typed-ids/{Id}").GetProperty("get").GetProperty("parameters");

        Schema(Parameter(parameters, "Id").GetProperty("schema")).Should().Be("type=\"string\"");
        Parameter(parameters, "other").GetProperty("schema").GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public void A_typed_id_request_body_property_is_documented_as_a_string()
    {
        using var document = Render();

        Component(document, "CreateTypedIdWidget").GetProperty("properties").GetProperty("id").GetProperty("type").GetString()
            .Should().Be("string");
    }

    private static JsonDocument Render()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());

        return JsonDocument.Parse(rendered.Json);
    }

    private static JsonElement Component(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(name);
    }

    private static JsonElement Parameter(JsonElement parameters, string name)
    {
        return parameters.EnumerateArray().Single(p => p.GetProperty("name").GetString() == name);
    }

    /// <summary>The schema's members other than its description, as <c>name=json</c> pairs in the order written.</summary>
    private static string Schema(JsonElement schema)
    {
        return string.Join("; ", schema.EnumerateObject().Where(static p => p.Name != "description").Select(static p => $"{p.Name}={p.Value.GetRawText()}"));
    }
}

/// <summary>An id shaped as Signature365's <c>ApiId</c>: a type converter to and from string, and a JSON converter.</summary>
[TypeConverter(typeof(TypedIdFixtureTypeConverter))]
[JsonConverter(typeof(TypedIdFixtureJsonConverter))]
public readonly struct TypedIdFixture
{
    public TypedIdFixture(string value)
    {
        this.Value = value;
    }

    public string Value { get; }
}

/// <summary>Converts a <see cref="TypedIdFixture"/> to and from its string.</summary>
public sealed class TypedIdFixtureTypeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(string) || base.CanConvertTo(context, destinationType);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        return value is string text ? new TypedIdFixture(text) : base.ConvertFrom(context, culture, value);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        return destinationType == typeof(string) && value is TypedIdFixture id ? id.Value : base.ConvertTo(context, culture, value, destinationType);
    }
}

/// <summary>Writes a <see cref="TypedIdFixture"/> as its string.</summary>
public sealed class TypedIdFixtureJsonConverter : JsonConverter<TypedIdFixture>
{
    public override TypedIdFixture Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return new TypedIdFixture(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, TypedIdFixture value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}

/// <summary>A resource identified by typed ids, with binary content.</summary>
public sealed class TypedIdWidgetApiResource
{
    public TypedIdFixture Id { get; init; }

    public TypedIdFixture? ParentId { get; init; }

    public byte[] Content { get; init; } = [];
}

/// <summary>Gets a widget by its typed id, filtered by another.</summary>
[AllowAnonymous]
[RootLink("/typed-ids/{Id}")]
public partial class GetTypedIdWidgetQuery : IQuery<TypedIdWidgetApiResource>
{
    public TypedIdFixture Id { get; set; }

    public TypedIdFixture? Other { get; set; }

    public static TypedIdWidgetApiResource Handle(GetTypedIdWidgetQuery query)
    {
        return new TypedIdWidgetApiResource { Id = query.Id };
    }
}

/// <summary>Creates a widget, given its typed id in the body.</summary>
[AllowAnonymous]
[RootLink("/typed-ids")]
[HttpPost]
public partial class CreateTypedIdWidgetCommand : ICommand<TypedIdWidgetApiResource>
{
    public TypedIdFixture Id { get; set; }

    public static TypedIdWidgetApiResource Handle(CreateTypedIdWidgetCommand command)
    {
        return new TypedIdWidgetApiResource { Id = command.Id };
    }
}
