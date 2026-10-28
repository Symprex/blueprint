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
        public FallbackTestApp(bool allowRuntimeTypeFallback)
        {
            this.AppOwnedSerializerContext = new RuntimeTypeFallbackJsonContext();
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
/// Registers <see cref="OpenHolder" /> and <see cref="string" />, but not <see cref="UnregisteredPayload" />.
/// </summary>
[JsonSerializable(typeof(OpenHolder))]
[JsonSerializable(typeof(string))]
internal sealed partial class RuntimeTypeFallbackJsonContext : JsonSerializerContext;
