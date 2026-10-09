using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

public class HandlerReturnNormalizationIntegrationTests
{
    [Fact]
    public async Task When_handler_returns_plain_value_then_generated_pipeline_returns_typed_ok_result()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(PlainValueHandlerOperation));
        var context = CreateContext(app, PlainValueHandlerOperation.Configure());

        PlainValueHandlerOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("Blueprint");
        PlainValueHandlerOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_handler_returns_task_without_payload_then_generated_pipeline_returns_no_result()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NoResultHandlerOperation));
        var context = CreateContext(app, NoResultHandlerOperation.Configure());

        NoResultHandlerOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<NoResultOperationResult>();
        NoResultHandlerOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_handler_returns_results_union_then_generated_pipeline_returns_selected_union_member()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(UnionHandlerOperation));
        var context = CreateContext(app, UnionHandlerOperation.Configure());

        UnionHandlerOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("Union");
        UnionHandlerOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_handler_returns_explicit_ok_result_with_plain_contract_then_generated_pipeline_returns_same_result()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(ExplicitResultPlainContractOperation));
        var context = CreateContext(app, ExplicitResultPlainContractOperation.Configure());

        ExplicitResultPlainContractOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("Explicit");
        ExplicitResultPlainContractOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_handler_throws_then_generated_pipeline_returns_canonical_unhandled_exception_result()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(ThrowingHandlerOperation));
        var context = CreateContext(app, ThrowingHandlerOperation.Configure());

        ThrowingHandlerOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<UnhandledExceptionResult>();
        ((UnhandledExceptionResult)result).Exception.Should().BeSameAs(ThrowingHandlerOperation.LastThrownException);
        ThrowingHandlerOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_handler_returns_status_code_result_accepted_then_pipeline_returns_it_directly()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(AcceptedStatusCodeOperation));
        var context = CreateContext(app, AcceptedStatusCodeOperation.Configure());

        AcceptedStatusCodeOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeSameAs(StatusCodeResult.Accepted.Instance);
        AcceptedStatusCodeOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_handler_returns_status_code_result_no_content_then_pipeline_returns_it_directly()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NoContentStatusCodeOperation));
        var context = CreateContext(app, NoContentStatusCodeOperation.Configure());

        NoContentStatusCodeOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeSameAs(StatusCodeResult.NoContent.Instance);
        NoContentStatusCodeOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_handler_returns_list_then_generated_pipeline_wraps_in_ok_result()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(ListReturnQuery));
        var context = CreateContext(app, ListReturnQuery.Configure());

        ListReturnQuery.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<List<string>>>();
        var content = ((OkResult<List<string>>)result).Content;
        content.Should().HaveCount(3);
        content.Should().ContainInOrder("alpha", "beta", "gamma");
        ListReturnQuery.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_handler_returns_array_then_generated_pipeline_wraps_in_ok_result()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(ArrayReturnQuery));
        var context = CreateContext(app, ArrayReturnQuery.Configure());

        ArrayReturnQuery.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string[]>>();
        var content = ((OkResult<string[]>)result).Content;
        content.Should().HaveCount(3);
        content.Should().ContainInOrder("one", "two", "three");
        ArrayReturnQuery.HandlerCallCount.Should().Be(1);
    }

    private static ApiOperationContext CreateContext(BlueprintApp app, ApiOperationDescriptor descriptor)
    {
        return new ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            SkipAuthorisation = true,
        };
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}

public partial class PlainValueHandlerOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(PlainValueHandlerOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult("Blueprint");
    }
}

public partial class NoResultHandlerOperation : ICommand
{
    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task HandleAsync(NoResultHandlerOperation operation)
    {
        HandlerCallCount++;
        return Task.CompletedTask;
    }
}

public partial class UnionHandlerOperation : ICommand<Results<OkResult<string>, NoResultOperationResult>>
{
    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<Results<OkResult<string>, NoResultOperationResult>> HandleAsync(UnionHandlerOperation operation)
    {
        HandlerCallCount++;
        Results<OkResult<string>, NoResultOperationResult> result = new OkResult<string>("Union");
        return Task.FromResult(result);
    }
}

public partial class ExplicitResultPlainContractOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<OkResult<string>> HandleAsync(ExplicitResultPlainContractOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(new OkResult<string>("Explicit"));
    }
}

public partial class ThrowingHandlerOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public static Exception? LastThrownException { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
        LastThrownException = null;
    }

    public static Task<string> HandleAsync(ThrowingHandlerOperation operation)
    {
        HandlerCallCount++;
        LastThrownException = new InvalidOperationException("Boom");
        return Task.FromException<string>(LastThrownException);
    }
}

/// <summary>
/// Proves the S365 pattern where a command handler returns <c>StatusCodeResult.Accepted</c> —
/// the most common return type in S365 with 85 usages.
/// </summary>
[AllowAnonymous]
public partial class AcceptedStatusCodeOperation : ICommand<StatusCodeResult.Accepted>
{
    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<StatusCodeResult.Accepted> HandleAsync(AcceptedStatusCodeOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(StatusCodeResult.Accepted.Instance);
    }
}

/// <summary>
/// Proves the S365 pattern for <c>StatusCodeResult.NoContent</c> returns (10 usages in S365).
/// </summary>
[AllowAnonymous]
public partial class NoContentStatusCodeOperation : ICommand<StatusCodeResult.NoContent>
{
    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<StatusCodeResult.NoContent> HandleAsync(NoContentStatusCodeOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(StatusCodeResult.NoContent.Instance);
    }
}

/// <summary>
/// Proves the S365 pattern where a query returns <c>List&lt;T&gt;</c> directly — Blueprint wraps
/// it in <c>OkResult&lt;List&lt;T&gt;&gt;</c>. Used by ~20 operations in S365 (e.g. AdminPortal).
/// </summary>
[AllowAnonymous]
public partial class ListReturnQuery : IQuery<List<string>>
{
    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<List<string>> HandleAsync(ListReturnQuery operation)
    {
        HandlerCallCount++;
        return Task.FromResult(new List<string> { "alpha", "beta", "gamma" });
    }
}

/// <summary>
/// Proves the S365 pattern where a query returns <c>T[]</c> directly — Blueprint wraps
/// it in <c>OkResult&lt;T[]&gt;</c>. Used by Templates service in S365.
/// </summary>
[AllowAnonymous]
public partial class ArrayReturnQuery : IQuery<string[]>
{
    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string[]> HandleAsync(ArrayReturnQuery operation)
    {
        HandlerCallCount++;
        return Task.FromResult(new[] { "one", "two", "three" });
    }
}
