using System.Collections.Concurrent;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a request-body property whose type the serializer context does not describe is documented as legacy
/// Blueprint documented it where the document can do so, and named in a warning where it cannot.
/// </summary>
/// <remarks>
/// <para>
/// Legacy generated a schema for any type by reflection. Where the property's type is an operation's own type, as
/// Signature365's VCard <c>PreviewVCardFieldsQuery.Body</c> is an <c>UpdateVCardFieldsCommand</c>, legacy's schema for
/// it was that operation's request body, the one schema it made for the type. VCard's committed document references
/// <c>UpdateVCardFieldsCommand</c> from the property, and so does this document now.
/// </para>
/// <para>
/// Any other type needs the serializer context to describe it before the document can, so the document says which type
/// and property, rather than leaving the property an unexplained <c>object</c>.
/// </para>
/// </remarks>
public sealed class OpenApiUndescribedBodyTypeTests
{
    [Theory]
    [InlineData(OpenApiRequestBodyNaming.OperationName, "UpdateBodyWidget")]
    [InlineData(OpenApiRequestBodyNaming.OperationType, "UpdateBodyWidgetCommand")]
    public void A_property_of_an_operations_type_references_that_operations_request_body(OpenApiRequestBodyNaming naming, string requestBody)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions { RequestBodyNaming = naming });
        using var document = JsonDocument.Parse(rendered.Json);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        var preview = schemas.GetProperty(naming == OpenApiRequestBodyNaming.OperationName ? "PreviewBodyWidget" : "PreviewBodyWidgetCommand");
        preview.GetProperty("properties").GetProperty("body").GetProperty("$ref").GetString().Should().Be("#/components/schemas/" + requestBody);
        schemas.GetProperty(requestBody).GetProperty("properties").EnumerateObject().Select(static p => p.Name).Should().Contain("name");
        rendered.Warnings.Should().NotContain(static warning => warning.Contains(nameof(UpdateBodyWidgetCommand), StringComparison.Ordinal));
    }

    [Fact]
    public void A_property_of_a_type_the_serializer_context_does_not_describe_is_named_in_a_warning()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());
        using var document = JsonDocument.Parse(rendered.Json);

        document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("SubmitUndescribedBody")
            .GetProperty("properties").GetProperty("payload").GetProperty("type").GetString().Should().Be("object");
        rendered.Warnings.Should().ContainSingle(static warning => warning.Contains(typeof(UndescribedBodyPayload).FullName!, StringComparison.Ordinal))
            .Which.Should().Contain("SubmitUndescribedBody").And.Contain("Payload").And.Contain(nameof(IntegrationTestAppJsonContext));
        rendered.Warnings.Should().ContainSingle(static warning => warning.Contains(typeof(UndescribedBodyItem).FullName!, StringComparison.Ordinal));
    }

    [Fact]
    public void Only_the_types_the_serializer_context_does_not_describe_are_warned_about()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());

        // Every other request-body property of the integration app has a type the context describes, a component, an
        // operation's own type, or an open value; BodyPopulation's payload is the one other fixture it does not describe.
        // SubmitUndescribedBody.Id (R27): a converter-written type is not described any better than an undescribed one —
        // the document still shows it bare — so it is warned about too, the same as Payload/Items. Tags
        // (IReadOnlyDictionary<string, string>) and Extra (JsonDocument) are not warned about: a dictionary's values are
        // checked on their own (string is described), and a JsonDocument is an open value, like object/JsonElement/JsonNode.
        // SearchUndescribedFilter.Filter (R27): a query parameter is now checked too.
        rendered.Warnings.Select(static warning => warning[..warning.IndexOf(':', StringComparison.Ordinal)]).Should().BeEquivalentTo(
            "BodyPopulation.Payload", "SubmitUndescribedBody.Payload", "SubmitUndescribedBody.Items", "SubmitUndescribedBody.Id", "SearchUndescribedFilter.Filter");
    }

    [Fact]
    public void A_converter_written_type_the_context_still_cannot_describe_as_an_object_is_named_in_a_warning()
    {
        // R27: JsonSerializerContext.GetTypeInfo succeeds for a [JsonConverter]-attributed type (it has a converter),
        // so the earlier "GetTypeInfo succeeds → described" check missed it entirely — the document renders it bare
        // ({"type": "object"}, no properties), exactly like a type with no metadata at all, but no warning named it.
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());
        using var document = JsonDocument.Parse(rendered.Json);

        document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("SubmitUndescribedBody")
            .GetProperty("properties").GetProperty("id").GetProperty("type").GetString().Should().Be("object");
        rendered.Warnings.Should().ContainSingle(static warning => warning.Contains(nameof(ConverterWrittenBodyId), StringComparison.Ordinal))
            .Which.Should().Contain("SubmitUndescribedBody").And.Contain("Id");
    }

    [Fact]
    public void A_query_parameter_of_a_type_the_serializer_context_does_not_describe_is_named_in_a_warning()
    {
        // R27: a query parameter, unlike a request-body property, was never checked at all — an undescribed complex
        // query parameter rendered bare with no warning naming it.
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());
        using var document = JsonDocument.Parse(rendered.Json);

        var filterParam = document.RootElement.GetProperty("paths").GetProperty("/body-widgets/undescribed-filter")
            .GetProperty("get").GetProperty("parameters").EnumerateArray()
            .Single(static p => p.GetProperty("name").GetString() == "filter");
        filterParam.GetProperty("schema").GetProperty("type").GetString().Should().Be("object");

        rendered.Warnings.Should().ContainSingle(static warning => warning.Contains(typeof(UndescribedQueryFilter).FullName!, StringComparison.Ordinal)
            && warning.Contains("SearchUndescribedFilter", StringComparison.Ordinal))
            .Which.Should().Contain("Filter");
    }

    [Fact]
    public void An_undescribed_types_warning_says_to_declare_it_on_the_serializer_context()
    {
        // Declaring the type is what makes the document describe it: the context then gives it an Object type info, and
        // the property (or a collection's items) references its component.
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());
        var type = typeof(UndescribedBodyPayload).FullName;
        var itemType = typeof(UndescribedBodyItem).FullName;

        rendered.Warnings.Should().Contain(
            $"SubmitUndescribedBody.Payload: the serializer context IntegrationTestAppJsonContext does not describe {type}, so the "
            + $"OpenAPI document shows it as an object with no properties. Declare [JsonSerializable(typeof({type}))] on "
            + "IntegrationTestAppJsonContext for the document to describe it as a component.");
        rendered.Warnings.Should().Contain(
            $"SubmitUndescribedBody.Items: the serializer context IntegrationTestAppJsonContext does not describe {itemType}, so the "
            + $"OpenAPI document shows it as an object with no properties. Declare [JsonSerializable(typeof({itemType}))] on "
            + "IntegrationTestAppJsonContext for the document to describe it as a component.");
    }

    [Fact]
    public void A_converter_written_types_warning_does_not_say_to_declare_what_the_context_already_describes()
    {
        // R131: the context already describes a converter-written type (GetTypeInfo succeeds, with JsonTypeInfoKind.None),
        // so "does not describe it" and "declare it" were both false: declaring it again changes nothing. The document maps
        // a property to a string through IParsable<T> or a TypeConverter to string (MapClrTypeToOpenApiSchema) before it
        // would leave it an object, which is what describes a converter that writes a string.
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());
        var type = typeof(ConverterWrittenBodyId).FullName;

        rendered.Warnings.Should().Contain(
            $"SubmitUndescribedBody.Id: the serializer context IntegrationTestAppJsonContext describes {type} as JsonTypeInfoKind.None, "
            + "written by a converter rather than as an object with properties, so the OpenAPI document shows it as an object "
            + "with no properties. Declaring it on IntegrationTestAppJsonContext does not change that, as the document cannot see "
            + $"what a converter writes. If the converter writes a string, implement IParsable<T> on {type}, or give it a "
            + "[TypeConverter] that converts it to string, for the document to describe it as a string.");
    }

    [Fact]
    public void A_query_parameters_warning_says_a_parameter_is_never_a_component()
    {
        // R131: a query parameter's schema is never a component (its object branch renders bare, with no $ref), so
        // declaring its type on the context changes nothing in the document; only IParsable<T> or a TypeConverter to
        // string makes MapClrTypeToOpenApiSchema describe it, as a string.
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());
        var type = typeof(UndescribedQueryFilter).FullName;

        rendered.Warnings.Should().Contain(
            $"SearchUndescribedFilter.Filter: the OpenAPI document shows the query parameter's type {type} as an object with no "
            + $"properties. A query parameter is never a component, so declaring [JsonSerializable(typeof({type}))] on "
            + $"IntegrationTestAppJsonContext does not change that. Implement IParsable<T> on {type}, or give it a [TypeConverter] "
            + "that converts it to string, for the document to describe it as a string.");
    }

    [Fact]
    public void A_dictionary_of_described_values_and_a_JsonDocument_are_not_warned_about()
    {
        // R27 false positives: IReadOnlyDictionary<,> (not just IDictionary<,>) and JsonDocument (not just JsonElement)
        // must not be mistaken for undescribed types.
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());

        rendered.Warnings.Should().NotContain(static warning => warning.Contains("Tags", StringComparison.Ordinal));
        rendered.Warnings.Should().NotContain(static warning => warning.Contains("Extra", StringComparison.Ordinal));
    }

    [Fact]
    public void Mapping_the_api_logs_each_warning()
    {
        var logged = new ConcurrentQueue<string>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new WarningRecordingLoggerProvider(logged));
        builder.Services
            .AddSingleton<IntegrationTestBlueprintApp>()
            .AddSingleton<IntegrationTestBlueprintAppExecutor>();
        var app = builder.Build();

        app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        logged.Should().Contain(static message => message.Contains(typeof(UndescribedBodyPayload).FullName!, StringComparison.Ordinal));
    }

    private sealed class WarningRecordingLoggerProvider(ConcurrentQueue<string> messages) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new WarningRecordingLogger(messages);
        }

        public void Dispose()
        {
        }
    }

    private sealed class WarningRecordingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel == LogLevel.Warning;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                messages.Enqueue(formatter(state, exception));
            }
        }
    }
}

/// <summary>Updates a widget; its request body is the schema a property of its type references.</summary>
[AllowAnonymous]
[RootLink("/body-widgets/{Id}")]
[HttpPut]
public partial class UpdateBodyWidgetCommand : ICommand<string>
{
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;

    public static string Handle(UpdateBodyWidgetCommand command)
    {
        return command.Name;
    }
}

/// <summary>Previews the widget an update would make, given the update in its body.</summary>
[AllowAnonymous]
[RootLink("/body-widgets/preview")]
[HttpPost]
public partial class PreviewBodyWidgetCommand : ICommand<string>
{
    public UpdateBodyWidgetCommand Body { get; set; } = null!;

    public static string Handle(PreviewBodyWidgetCommand command)
    {
        return command.Body.Name;
    }
}

/// <summary>A payload no type the serializer context declares reaches.</summary>
public sealed class UndescribedBodyPayload
{
    public string? Text { get; set; }
}

/// <summary>An item of a collection no type the serializer context declares reaches.</summary>
public sealed class UndescribedBodyItem
{
    public string? Text { get; set; }
}

/// <summary>
/// An <see cref="IParsable{TSelf}"/> query-bound value with no <see cref="System.ComponentModel.TypeConverter"/> — it
/// satisfies the binding validator (which accepts IParsable), so it is a legitimate query parameter, but it is neither a
/// known primitive/collection nor <see cref="Blueprint.Runtime.Http.OpenApiStringConvertibleTypes.IsConvertibleToString"/>
/// (that check only recognises a TypeConverter), so it renders as an inline "object" schema, and the serializer context
/// does not describe it either.
/// </summary>
public sealed class UndescribedQueryFilter
{
    public string? Text { get; set; }

    // Duck-typed (not IParsable<T>) — satisfies the binding validator's "add a static TryParse method" option without
    // implementing the interface, so it is not caught by InferIParsableFormat's IParsable`-interface check either.
    public static bool TryParse(string? s, out UndescribedQueryFilter result)
    {
        result = new UndescribedQueryFilter { Text = s };
        return true;
    }
}

/// <summary>Searches with a query parameter of a type the serializer context does not describe (R27).</summary>
[AllowAnonymous]
[RootLink("/body-widgets/undescribed-filter")]
[HttpGet]
public partial class SearchUndescribedFilterQuery : IQuery<string>
{
    public UndescribedQueryFilter Filter { get; set; } = new();

    public static string Handle(SearchUndescribedFilterQuery query)
    {
        return query.Filter.Text ?? string.Empty;
    }
}

/// <summary>Submits payloads of types the serializer context does not describe.</summary>
[AllowAnonymous]
[RootLink("/body-widgets/undescribed")]
[HttpPost]
public partial class SubmitUndescribedBodyCommand : ICommand<string>
{
    public UndescribedBodyPayload Payload { get; set; } = new();

    public List<UndescribedBodyItem> Items { get; set; } = [];

    public ConverterWrittenBodyId Id { get; set; }

    public IReadOnlyDictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

    public JsonDocument? Extra { get; set; }

    public static string Handle(SubmitUndescribedBodyCommand command)
    {
        return command.Payload.Text ?? string.Empty;
    }
}

/// <summary>A value the serializer context describes through its converter alone, as neither an object nor a component.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(ConverterWrittenBodyIdJsonConverter))]
public readonly struct ConverterWrittenBodyId
{
    public string? Value { get; init; }
}

/// <summary>Writes a <see cref="ConverterWrittenBodyId"/> as its string.</summary>
public sealed class ConverterWrittenBodyIdJsonConverter : System.Text.Json.Serialization.JsonConverter<ConverterWrittenBodyId>
{
    public override ConverterWrittenBodyId Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        return new ConverterWrittenBodyId { Value = reader.GetString() };
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, ConverterWrittenBodyId value, System.Text.Json.JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
