using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves <see cref="BlueprintAppSerializerContextAttribute.AllowRuntimeTypeFallback" />: a declared response root
/// holding, behind an <see cref="object" /> member, a type the app's serializer context does not register is written
/// with reflection metadata and a warning naming the type once, and without the opt-in still fails as before.
/// </summary>
public sealed class RuntimeTypeFallbackTests
{
    [Fact]
    public async Task With_the_opt_in_an_unregistered_runtime_type_behind_an_object_member_is_written_and_logged_once()
    {
        var logged = new ConcurrentQueue<string>();
        using var services = CreateServices(logged);
        var app = new FallbackTestApp(allowRuntimeTypeFallback: true);

        var first = CreateHttpContext(services);
        await HttpOperationResultExecutor.WriteJsonBodyAsync(first, app, new OpenHolder { Value = new UnregisteredPayload("first") });

        var second = CreateHttpContext(services);
        await HttpOperationResultExecutor.WriteJsonBodyAsync(second, app, new OpenHolder { Value = new UnregisteredPayload("second") });

        ReadBody(first).Should().Be("""{"Value":{"Name":"first"}}""");
        ReadBody(second).Should().Be("""{"Value":{"Name":"second"}}""");
        logged.Should().ContainSingle().Which.Should().Contain(typeof(UnregisteredPayload).FullName!);
    }

    [Fact]
    public async Task With_the_opt_in_an_unregistered_JsonElement_behind_an_object_member_is_written_and_logged_once()
    {
        var logged = new ConcurrentQueue<string>();
        using var services = CreateServices(logged);
        var app = new FallbackTestApp(allowRuntimeTypeFallback: true);
        using var proxied = JsonDocument.Parse("""{"code":"upstream"}""");

        var first = CreateHttpContext(services);
        await HttpOperationResultExecutor.WriteJsonBodyAsync(first, app, new OpenHolder { Value = proxied.RootElement });

        var second = CreateHttpContext(services);
        await HttpOperationResultExecutor.WriteJsonBodyAsync(second, app, new OpenHolder { Value = proxied.RootElement });

        ReadBody(first).Should().Be("""{"Value":{"code":"upstream"}}""");
        ReadBody(second).Should().Be("""{"Value":{"code":"upstream"}}""");
        logged.Should().ContainSingle().Which.Should().Contain(typeof(JsonElement).FullName!);
    }

    [Fact]
    public async Task With_the_opt_in_an_unregistered_subclass_of_a_registered_root_is_written_through_the_fallback_and_logged()
    {
        var logged = new ConcurrentQueue<string>();
        using var services = CreateServices(logged);
        var app = new FallbackTestApp(allowRuntimeTypeFallback: true);

        var httpContext = CreateHttpContext(services);
        await HttpOperationResultExecutor.WriteJsonBodyAsync(httpContext, app, new UnregisteredSubclass { Name = "base", Extra = new UnregisteredPayload("extra") });

        ReadBody(httpContext).Should().Be("""{"Extra":{"Name":"extra"},"Name":"base"}""");
        logged.Should().Contain(static message => message.Contains(typeof(UnregisteredSubclass).FullName!, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task With_the_opt_in_an_async_iterator_root_is_still_written_asynchronously(bool inlineWriter)
    {
        using var services = CreateServices(new ConcurrentQueue<string>());
        var app = new FallbackTestApp(allowRuntimeTypeFallback: true);

        var httpContext = CreateHttpContext(services);
        await WriteAsync(inlineWriter, httpContext, app, ItemsAsync());

        ReadBody(httpContext).Should().Be("""["a","b"]""");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task With_the_opt_in_an_unregistered_root_holding_an_async_iterator_is_still_written_asynchronously(bool inlineWriter)
    {
        var logged = new ConcurrentQueue<string>();
        using var services = CreateServices(logged);
        var app = new FallbackTestApp(allowRuntimeTypeFallback: true);

        var httpContext = CreateHttpContext(services);
        await WriteAsync(inlineWriter, httpContext, app, new StreamingSubclass { Name = "streamed", Items = ItemsAsync() });

        ReadBody(httpContext).Should().Be("""{"Items":["a","b"],"Name":"streamed"}""");
        logged.Should().Contain(static message => message.Contains(typeof(StreamingSubclass).FullName!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_the_opt_in_a_value_written_by_a_base_types_converter_is_written_by_it()
    {
        using var services = CreateServices(new ConcurrentQueue<string>());
        var context = new RuntimeTypeFallbackJsonContext(new JsonSerializerOptions { Converters = { new ShapeConverter() } });
        var app = new FallbackTestApp(allowRuntimeTypeFallback: true, context);

        var httpContext = CreateHttpContext(services);
        await HttpOperationResultExecutor.WriteJsonBodyAsync(httpContext, app, new OpenHolder { Value = new Square() });

        ReadBody(httpContext).Should().Be("""{"Value":"Square"}""");
    }

    [Fact]
    public async Task With_the_opt_in_a_registered_runtime_type_is_written_without_a_warning()
    {
        var logged = new ConcurrentQueue<string>();
        using var services = CreateServices(logged);
        var app = new FallbackTestApp(allowRuntimeTypeFallback: true);

        var httpContext = CreateHttpContext(services);
        await HttpOperationResultExecutor.WriteJsonBodyAsync(httpContext, app, new OpenHolder { Value = "text" });

        ReadBody(httpContext).Should().Be("""{"Value":"text"}""");
        logged.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_the_opt_in_an_unregistered_runtime_type_behind_an_object_member_still_fails()
    {
        using var services = CreateServices(new ConcurrentQueue<string>());
        var app = new FallbackTestApp(allowRuntimeTypeFallback: false);

        var act = () => HttpOperationResultExecutor.WriteJsonBodyAsync(CreateHttpContext(services), app, new OpenHolder { Value = new UnregisteredPayload("strict") });

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage($"*{typeof(UnregisteredPayload).FullName}*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Two_apps_sharing_one_context_each_keep_their_own_fallback_policy_whichever_writes_first(bool strictFirst)
    {
        using var services = CreateServices(new ConcurrentQueue<string>());
        var shared = new RuntimeTypeFallbackJsonContext();
        var strict = new FallbackTestApp(allowRuntimeTypeFallback: false, shared);
        var fallback = new FallbackTestApp(allowRuntimeTypeFallback: true, shared);

        Func<Task> Write(BlueprintApp app)
        {
            return () => HttpOperationResultExecutor.WriteJsonBodyAsync(CreateHttpContext(services), app, new OpenHolder { Value = new UnregisteredPayload("shared") });
        }

        if (strictFirst)
        {
            await Write(strict).Should().ThrowAsync<NotSupportedException>();
            await Write(fallback).Should().NotThrowAsync();
        }
        else
        {
            await Write(fallback).Should().NotThrowAsync();
            await Write(strict).Should().ThrowAsync<NotSupportedException>();
        }
    }

    [Fact]
    public async Task With_the_opt_in_an_unregistered_number_keeps_the_options_number_handling()
    {
        using var services = CreateServices(new ConcurrentQueue<string>());
        var context = new RuntimeTypeFallbackJsonContext(new JsonSerializerOptions { NumberHandling = JsonNumberHandling.WriteAsString });
        var app = new FallbackTestApp(allowRuntimeTypeFallback: true, context);

        var httpContext = CreateHttpContext(services);
        await HttpOperationResultExecutor.WriteJsonBodyAsync(httpContext, app, new OpenHolder { Value = 42 });

        ReadBody(httpContext).Should().Be("""{"Value":"42"}""");
    }

    [Fact]
    public async Task With_the_opt_in_an_unregistered_objects_nullable_number_keeps_the_options_number_handling()
    {
        using var services = CreateServices(new ConcurrentQueue<string>());
        var context = new RuntimeTypeFallbackJsonContext(new JsonSerializerOptions { NumberHandling = JsonNumberHandling.WriteAsString });
        var app = new FallbackTestApp(allowRuntimeTypeFallback: true, context);

        var httpContext = CreateHttpContext(services);
        await HttpOperationResultExecutor.WriteJsonBodyAsync(httpContext, app, new OpenHolder { Value = new NullableNumberPayload { Count = 42 } });

        ReadBody(httpContext).Should().Be("""{"Value":{"Count":"42"}}""");
    }

    [Fact]
    public async Task With_the_opt_in_a_collection_left_unwritten_is_not_warned_about()
    {
        var logged = new ConcurrentQueue<string>();
        using var services = CreateServices(logged);
        var app = new FallbackTestApp(allowRuntimeTypeFallback: true);

        var httpContext = CreateHttpContext(services);
        await HttpOperationResultExecutor.WriteJsonBodyAsync(httpContext, app, new OpenHolder { Value = new PayloadWithOptionalItems { Name = "no items" } });

        ReadBody(httpContext).Should().Be("""{"Value":{"Name":"no items"}}""");
        logged.Should().ContainSingle().Which.Should().Contain(typeof(PayloadWithOptionalItems).FullName!);
    }

    [Fact]
    public async Task With_the_opt_in_an_unregistered_dictionary_keyed_by_a_converted_type_writes_its_keys_and_logs_the_key_type()
    {
        var logged = new ConcurrentQueue<string>();
        using var services = CreateServices(logged);
        var app = new FallbackTestApp(allowRuntimeTypeFallback: true);

        var httpContext = CreateHttpContext(services);
        await HttpOperationResultExecutor.WriteJsonBodyAsync(httpContext, app, new OpenHolder { Value = new Dictionary<WidgetKey, string> { [new WidgetKey(7)] = "seven" } });

        ReadBody(httpContext).Should().Be("""{"Value":{"widget-7":"seven"}}""");
        logged.Should().Contain(static message => message.Contains(typeof(WidgetKey).FullName!, StringComparison.Ordinal));
    }

    private static async IAsyncEnumerable<string> ItemsAsync()
    {
        await Task.Yield();
        yield return "a";
        yield return "b";
    }

    // Both writers: the executor's, and the generated pipelines' inline one, which needs an operation to write for.
    private static Task WriteAsync(bool inlineWriter, HttpContext httpContext, BlueprintApp app, object content)
    {
        if (!inlineWriter)
        {
            return HttpOperationResultExecutor.WriteJsonBodyAsync(httpContext, app, content);
        }

        var hostServices = new ServiceCollection()
            .AddLogging()
            .AddBlueprintApi<Generators.OperationDescriptors.IntegrationTestBlueprintApp, Generators.OperationDescriptors.IntegrationTestBlueprintAppExecutor>()
            .BuildServiceProvider();
        var descriptor = hostServices.GetRequiredService<Generators.OperationDescriptors.IntegrationTestBlueprintApp>()
            .FindOperation(typeof(GetLinkWireWidgetQuery));
        var context = new Blueprint.Runtime.Exec.ApiOperationContext(httpContext.RequestServices, app, descriptor, CancellationToken.None);

        return Blueprint.Runtime.Exec.PipelineHelpers.WriteOkJsonResponseAsync(httpContext, context, content);
    }

    private static ServiceProvider CreateServices(ConcurrentQueue<string> logged)
    {
        return new ServiceCollection()
            .AddLogging(builder => builder.AddProvider(new WarningRecordingLoggerProvider(logged)))
            .BuildServiceProvider();
    }

    private static DefaultHttpContext CreateHttpContext(IServiceProvider services)
    {
        return new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = new MemoryStream() },
        };
    }

    private static string ReadBody(HttpContext httpContext)
    {
        return Encoding.UTF8.GetString(((MemoryStream)httpContext.Response.Body).ToArray());
    }

    private sealed class FallbackTestApp : BlueprintApp
    {
        public FallbackTestApp(bool allowRuntimeTypeFallback, RuntimeTypeFallbackJsonContext? context = null)
        {
            this.AppOwnedSerializerContext = context ?? new RuntimeTypeFallbackJsonContext();
            this.AppOwnedSerializerContextType = typeof(RuntimeTypeFallbackJsonContext);
            this.AllowRuntimeTypeFallback = allowRuntimeTypeFallback;
        }
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

/// <summary>
/// A declared response root whose member's static type does not say what it holds.
/// </summary>
public sealed class OpenHolder
{
    /// <summary>
    /// Gets or sets the open-typed value.
    /// </summary>
    public object? Value { get; set; }
}

/// <summary>
/// A type <see cref="RuntimeTypeFallbackJsonContext" /> does not register.
/// </summary>
/// <param name="Name">The payload's name.</param>
public sealed record UnregisteredPayload(string Name);

/// <summary>
/// A subclass of <see cref="RegisteredBase" /> the context does not register, holding an async stream.
/// </summary>
public sealed class StreamingSubclass : RegisteredBase
{
    /// <summary>
    /// Gets or sets the streamed items.
    /// </summary>
    public IAsyncEnumerable<string>? Items { get; set; }
}

/// <summary>
/// A base type whose converter also writes its subclasses.
/// </summary>
public abstract class Shape;

/// <summary>
/// A subclass of <see cref="Shape" /> the context does not register, written by <see cref="ShapeConverter" />.
/// </summary>
public sealed class Square : Shape;

/// <summary>
/// Writes any <see cref="Shape" /> as its type name, accepting subclasses through <see cref="CanConvert" />.
/// </summary>
public sealed class ShapeConverter : JsonConverter<Shape>
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        return typeof(Shape).IsAssignableFrom(typeToConvert);
    }

    /// <inheritdoc />
    public override Shape Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Shape value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.GetType().Name);
    }
}

/// <summary>
/// A response root the context registers, which may be returned as a subclass it does not.
/// </summary>
public class RegisteredBase
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string? Name { get; set; }
}

/// <summary>
/// A subclass of <see cref="RegisteredBase" /> the context does not register.
/// </summary>
public sealed class UnregisteredSubclass : RegisteredBase
{
    /// <summary>
    /// Gets or sets a value only the subclass has.
    /// </summary>
    public object? Extra { get; set; }
}

/// <summary>
/// A type the context does not register, holding a nullable number.
/// </summary>
public sealed class NullableNumberPayload
{
    /// <summary>
    /// Gets or sets the count.
    /// </summary>
    public int? Count { get; set; }
}

/// <summary>
/// A type the context does not register, whose collection is left out when it is null.
/// </summary>
public sealed class PayloadWithOptionalItems
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the items, written only when there are any.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<UnregisteredPayload>? Items { get; set; }
}

/// <summary>
/// A dictionary key written by its own converter, as a property name.
/// </summary>
/// <param name="Id">The key's number.</param>
[JsonConverter(typeof(WidgetKeyConverter))]
public sealed record WidgetKey(int Id);

/// <summary>
/// Writes a <see cref="WidgetKey" /> as <c>widget-{Id}</c>, as a value or as a property name.
/// </summary>
public sealed class WidgetKeyConverter : JsonConverter<WidgetKey>
{
    /// <inheritdoc />
    public override WidgetKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return Parse(reader.GetString()!);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, WidgetKey value, JsonSerializerOptions options)
    {
        writer.WriteStringValue($"widget-{value.Id}");
    }

    /// <inheritdoc />
    public override WidgetKey ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return Parse(reader.GetString()!);
    }

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, WidgetKey value, JsonSerializerOptions options)
    {
        writer.WritePropertyName($"widget-{value.Id}");
    }

    private static WidgetKey Parse(string text)
    {
        return new(int.Parse(text["widget-".Length..], System.Globalization.CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Registers <see cref="OpenHolder" /> and <see cref="string" />, but not <see cref="UnregisteredPayload" />.
/// </summary>
[JsonSerializable(typeof(OpenHolder))]
[JsonSerializable(typeof(RegisteredBase))]
[JsonSerializable(typeof(string))]
internal sealed partial class RuntimeTypeFallbackJsonContext : JsonSerializerContext;
