using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

public class ExceptionTranslationIntegrationTests
{
    [Fact]
    public async Task When_handler_throws_non_server_api_exception_then_generated_pipeline_returns_unhandled_exception_result()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(NonServerApiExceptionOperation));
        var context = CreateContext(app, NonServerApiExceptionOperation.Configure());

        NonServerApiExceptionOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<UnhandledExceptionResult>();
        ((UnhandledExceptionResult)result).Exception.Should().BeSameAs(NonServerApiExceptionOperation.LastThrownException);
        NonServerApiExceptionOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_handler_throws_server_api_exception_then_generated_pipeline_returns_unhandled_exception_result()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(ServerApiExceptionOperation));
        var context = CreateContext(app, ServerApiExceptionOperation.Configure());

        ServerApiExceptionOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<UnhandledExceptionResult>();
        ((UnhandledExceptionResult)result).Exception.Should().BeSameAs(ServerApiExceptionOperation.LastThrownException);
        ServerApiExceptionOperation.HandlerCallCount.Should().Be(1);
    }

    private static ApiOperationContext CreateContext(BlueprintApp app, ApiOperationDescriptor descriptor)
    {
        return new ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}

public partial class NonServerApiExceptionOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public static ApiException? LastThrownException { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
        LastThrownException = null;
    }

    public static Task<string> HandleAsync(NonServerApiExceptionOperation operation)
    {
        HandlerCallCount++;
        LastThrownException = new ApiException("Client failure", 400);
        throw LastThrownException;
    }
}

public partial class ServerApiExceptionOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public static ApiException? LastThrownException { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
        LastThrownException = null;
    }

    public static Task<string> HandleAsync(ServerApiExceptionOperation operation)
    {
        HandlerCallCount++;
        LastThrownException = new ApiException("Server failure", 500);
        throw LastThrownException;
    }
}
