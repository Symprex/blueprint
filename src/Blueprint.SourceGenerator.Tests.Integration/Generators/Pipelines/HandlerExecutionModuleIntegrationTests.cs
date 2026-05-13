using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

public class HandlerExecutionModuleIntegrationTests
{
    [Fact]
    public async Task When_static_handler_method_returns_typed_result_then_generated_pipeline_returns_it()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(StaticTypedHandlerQuery));
        var context = CreateContext(app, StaticTypedHandlerQuery.Configure(), new StaticTypedHandlerQuery { Message = "typed-result" });

        StaticTypedHandlerQuery.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("typed-result");
        StaticTypedHandlerQuery.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_handler_has_di_parameter_then_generated_pipeline_resolves_service_and_invokes_it()
    {
        var formatter = new RecordingFormatter("formatted:");
        using var serviceProvider = new ServiceCollection()
            .AddSingleton<IHandlerExecutionFormatter>(formatter)
            .BuildServiceProvider();
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(DependencyInjectedHandlerQuery));
        var context = CreateContext(app, DependencyInjectedHandlerQuery.Configure(), new DependencyInjectedHandlerQuery { Message = "value" }, serviceProvider);

        DependencyInjectedHandlerQuery.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("formatted:value");
        DependencyInjectedHandlerQuery.HandlerCallCount.Should().Be(1);
        formatter.CallCount.Should().Be(1);
        formatter.LastValue.Should().Be("value");
    }

    [Fact]
    public async Task When_synchronous_handler_returns_result_then_generated_pipeline_returns_it()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(SynchronousHandlerQuery));
        var context = CreateContext(app, SynchronousHandlerQuery.Configure(), new SynchronousHandlerQuery { Message = "sync-result" });

        SynchronousHandlerQuery.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("sync-result");
        SynchronousHandlerQuery.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_handler_returns_task_then_generated_pipeline_completes_successfully()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(TaskOnlyHandlerCommand));
        var context = CreateContext(app, TaskOnlyHandlerCommand.Configure(), new TaskOnlyHandlerCommand());

        TaskOnlyHandlerCommand.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<NoResultOperationResult>();
        TaskOnlyHandlerCommand.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_handler_returns_ok_result_then_generated_pipeline_preserves_wrapped_result()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(ExplicitOkResultHandlerQuery));
        var context = CreateContext(app, ExplicitOkResultHandlerQuery.Configure(), new ExplicitOkResultHandlerQuery());

        ExplicitOkResultHandlerQuery.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("ok-result");
        ExplicitOkResultHandlerQuery.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_handler_accepts_api_operation_context_then_generated_pipeline_passes_current_context()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(ContextAwareHandlerQuery));
        var context = CreateContext(app, ContextAwareHandlerQuery.Configure(), new ContextAwareHandlerQuery { Message = "context-result" });

        ContextAwareHandlerQuery.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("context-result");
        ContextAwareHandlerQuery.HandlerCallCount.Should().Be(1);
        ContextAwareHandlerQuery.LastContext.Should().BeSameAs(context);
        ContextAwareHandlerQuery.LastOperation.Should().BeSameAs(context.Operation);
    }

    [Fact]
    public async Task When_handler_accepts_HttpRequest_parameter_then_generated_pipeline_injects_from_context()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(HttpRequestHandlerCommand));
        var context = CreateContext(app, HttpRequestHandlerCommand.Configure(), new HttpRequestHandlerCommand());

        // Set a custom header on the HttpRequest to prove it's the real one from context
        context.HttpContext!.Request.Headers["X-Webhook-Signature"] = "test-sig-123";

        HttpRequestHandlerCommand.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("test-sig-123");
        HttpRequestHandlerCommand.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_handler_accepts_ClaimsPrincipal_parameter_then_generated_pipeline_injects_from_context()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(ClaimsPrincipalHandlerQuery));

        var identity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("sub", "user-42")],
            "TestAuth");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        var context = CreateContext(app, ClaimsPrincipalHandlerQuery.Configure(), new ClaimsPrincipalHandlerQuery());
        context.UserPrincipal = principal;

        ClaimsPrincipalHandlerQuery.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("user-42");
        ClaimsPrincipalHandlerQuery.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_external_handler_has_base_class_properties_then_generated_pipeline_injects_them()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(PropertyInjectedHandlerQuery));
        var context = CreateContext(app, PropertyInjectedHandlerQuery.Configure(), new PropertyInjectedHandlerQuery { Value = "injected" });

        PropertyInjectedHandlerQueryHandler.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("injected");
        PropertyInjectedHandlerQueryHandler.LastContext.Should().BeSameAs(context);
        PropertyInjectedHandlerQueryHandler.LastCancellationToken.Should().Be(CancellationToken.None);
        PropertyInjectedHandlerQueryHandler.LastLogger.Should().NotBeNull();
    }

    private static ApiOperationContext CreateContext(BlueprintApp app, ApiOperationDescriptor descriptor, object operation, IServiceProvider? serviceProvider = null)
    {
        var effectiveServiceProvider = serviceProvider ?? EmptyServiceProvider.Instance;
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            RequestServices = effectiveServiceProvider,
        };

        return new ApiOperationContext(effectiveServiceProvider, app, descriptor, CancellationToken.None)
        {
            Operation = operation,
            HttpContext = httpContext,
            UserPrincipal = httpContext.User,
        };
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}

[AllowAnonymous]
[RootLink("/handler/static-typed")]
public partial class StaticTypedHandlerQuery : IQuery<string>
{
    public string Message { get; set; } = string.Empty;

    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(StaticTypedHandlerQuery operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Message);
    }
}

[AllowAnonymous]
[RootLink("/handler/di")]
public partial class DependencyInjectedHandlerQuery : IQuery<string>
{
    public string Message { get; set; } = string.Empty;

    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(DependencyInjectedHandlerQuery operation, IHandlerExecutionFormatter formatter)
    {
        HandlerCallCount++;
        return Task.FromResult(formatter.Format(operation.Message));
    }
}

[AllowAnonymous]
[RootLink("/handler/synchronous")]
public partial class SynchronousHandlerQuery : IQuery<string>
{
    public string Message { get; set; } = string.Empty;

    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static string Handle(SynchronousHandlerQuery operation)
    {
        HandlerCallCount++;
        return operation.Message;
    }
}

[AllowAnonymous]
[RootLink("/handler/task-only")]
public partial class TaskOnlyHandlerCommand : ICommand
{
    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task HandleAsync(TaskOnlyHandlerCommand operation)
    {
        HandlerCallCount++;
        return Task.CompletedTask;
    }
}

[AllowAnonymous]
[RootLink("/handler/ok-result")]
public partial class ExplicitOkResultHandlerQuery : IQuery<string>
{
    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<OkResult<string>> HandleAsync(ExplicitOkResultHandlerQuery operation)
    {
        HandlerCallCount++;
        return Task.FromResult(new OkResult<string>("ok-result"));
    }
}

[AllowAnonymous]
[RootLink("/handler/context")]
public partial class ContextAwareHandlerQuery : IQuery<string>
{
    public string Message { get; set; } = string.Empty;

    public static int HandlerCallCount { get; private set; }

    public static ApiOperationContext? LastContext { get; private set; }

    public static object? LastOperation { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
        LastContext = null;
        LastOperation = null;
    }

    public static Task<string> HandleAsync(ContextAwareHandlerQuery operation, ApiOperationContext context)
    {
        HandlerCallCount++;
        LastContext = context;
        LastOperation = context.Operation;
        return Task.FromResult(operation.Message);
    }
}

public interface IHandlerExecutionFormatter
{
    string Format(string value);
}

public sealed class RecordingFormatter(string prefix) : IHandlerExecutionFormatter
{
    public int CallCount { get; private set; }

    public string? LastValue { get; private set; }

    public string Format(string value)
    {
        this.CallCount++;
        this.LastValue = value;
        return prefix + value;
    }
}

/// <summary>
/// Base class that mirrors the S365 <c>ApiOperations</c> pattern — properties are set by
/// the generated pipeline after handler instantiation.
/// </summary>
public abstract class TestApiOperationsBase
{
    public ApiOperationContext Context { get; set; } = default!;

    public IUserAuthorisationContext Identity { get; set; } = default!;

    public ILogger Logger { get; set; } = default!;

    public CancellationToken CancellationToken { get; set; }
}

[AllowAnonymous]
[RootLink("/handler/property-injected")]
public partial class PropertyInjectedHandlerQuery : IQuery<string>
{
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// External handler that reads injected base-class properties, proving the pipeline
/// sets <c>Context</c>, <c>Logger</c>, and <c>CancellationToken</c> at runtime.
/// </summary>
public class PropertyInjectedHandlerQueryHandler : TestApiOperationsBase
{
    public static ApiOperationContext? LastContext { get; private set; }

    public static CancellationToken LastCancellationToken { get; private set; }

    public static ILogger? LastLogger { get; private set; }

    public static void Reset()
    {
        LastContext = null;
        LastCancellationToken = default;
        LastLogger = null;
    }

    public Task<string> HandleAsync(PropertyInjectedHandlerQuery query)
    {
        LastContext = this.Context;
        LastCancellationToken = this.CancellationToken;
        LastLogger = this.Logger;
        return Task.FromResult(query.Value);
    }
}

/// <summary>
/// Proves <c>HttpRequest</c> is injected from <c>context.HttpContext.Request</c>,
/// not from DI — matching the S365 Stripe webhook handler pattern.
/// </summary>
[AllowAnonymous]
[RootLink("/handler/http-request")]
public partial class HttpRequestHandlerCommand : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> InvokeAsync(Microsoft.AspNetCore.Http.HttpRequest httpRequest)
    {
        HandlerCallCount++;
        var signature = httpRequest.Headers["X-Webhook-Signature"].ToString();
        return Task.FromResult(signature);
    }
}

/// <summary>
/// Proves <c>ClaimsPrincipal</c> is injected from <c>context.UserPrincipal</c>,
/// not from DI.
/// </summary>
[AllowAnonymous]
[RootLink("/handler/claims-principal")]
public partial class ClaimsPrincipalHandlerQuery : IQuery<string>
{
    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(System.Security.Claims.ClaimsPrincipal principal)
    {
        HandlerCallCount++;
        var sub = principal.FindFirst("sub")?.Value ?? "unknown";
        return Task.FromResult(sub);
    }
}
