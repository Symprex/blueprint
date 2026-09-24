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
        rendered.Warnings.Select(static warning => warning[..warning.IndexOf(':', StringComparison.Ordinal)]).Should().BeEquivalentTo(
            "BodyPopulation.Payload", "SubmitUndescribedBody.Payload", "SubmitUndescribedBody.Items");
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

/// <summary>Submits payloads of types the serializer context does not describe.</summary>
[AllowAnonymous]
[RootLink("/body-widgets/undescribed")]
[HttpPost]
public partial class SubmitUndescribedBodyCommand : ICommand<string>
{
    public UndescribedBodyPayload Payload { get; set; } = new();

    public List<UndescribedBodyItem> Items { get; set; } = [];

    public ConverterWrittenBodyId Id { get; set; }

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
