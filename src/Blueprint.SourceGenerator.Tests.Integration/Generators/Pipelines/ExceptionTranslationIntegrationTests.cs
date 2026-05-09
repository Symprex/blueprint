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

    [Fact]
    public async Task When_handler_throws_not_found_exception_then_generated_pipeline_returns_unhandled_exception_result_with_404()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(NotFoundExceptionOperation));
        var context = CreateContext(app, NotFoundExceptionOperation.Configure());

        NotFoundExceptionOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<UnhandledExceptionResult>();
        var unhandled = (UnhandledExceptionResult)result;
        unhandled.Exception.Should().BeOfType<NotFoundException>();
        unhandled.Exception.Should().BeSameAs(NotFoundExceptionOperation.LastThrownException);
        ((NotFoundException)unhandled.Exception).HttpStatus.Should().Be(404);
        NotFoundExceptionOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_handler_throws_validation_exception_then_generated_pipeline_returns_validation_failed_result()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(ValidationExceptionOperation));
        var context = CreateContext(app, ValidationExceptionOperation.Configure());

        ValidationExceptionOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        result.WasSuccessful.Should().BeFalse();
        ValidationExceptionOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_handler_throws_forbidden_exception_then_generated_pipeline_returns_unhandled_exception_with_403()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(ForbiddenExceptionOperation));
        var context = CreateContext(app, ForbiddenExceptionOperation.Configure());

        ForbiddenExceptionOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<UnhandledExceptionResult>();
        var unhandled = (UnhandledExceptionResult)result;
        unhandled.Exception.Should().BeOfType<ForbiddenException>();
        ((ForbiddenException)unhandled.Exception).HttpStatus.Should().Be(403);
        ForbiddenExceptionOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_handler_throws_conflict_exception_then_generated_pipeline_returns_unhandled_exception_with_409()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(ConflictExceptionOperation));
        var context = CreateContext(app, ConflictExceptionOperation.Configure());

        ConflictExceptionOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<UnhandledExceptionResult>();
        var unhandled = (UnhandledExceptionResult)result;
        unhandled.Exception.Should().BeOfType<ConflictException>();
        ((ConflictException)unhandled.Exception).HttpStatus.Should().Be(409);
        ConflictExceptionOperation.HandlerCallCount.Should().Be(1);
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

public partial class NotFoundExceptionOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public static NotFoundException? LastThrownException { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
        LastThrownException = null;
    }

    public static Task<string> HandleAsync(NotFoundExceptionOperation operation)
    {
        HandlerCallCount++;
        LastThrownException = new NotFoundException("Template not found");
        throw LastThrownException;
    }
}

public partial class ValidationExceptionOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(ValidationExceptionOperation operation)
    {
        HandlerCallCount++;
        throw new Blueprint.Validation.ValidationException("Name", "Name is required");
    }
}

public partial class ForbiddenExceptionOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public static ForbiddenException? LastThrownException { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
        LastThrownException = null;
    }

    public static Task<string> HandleAsync(ForbiddenExceptionOperation operation)
    {
        HandlerCallCount++;
        LastThrownException = new ForbiddenException("Access denied");
        throw LastThrownException;
    }
}

public partial class ConflictExceptionOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public static ConflictException? LastThrownException { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
        LastThrownException = null;
    }

    public static Task<string> HandleAsync(ConflictExceptionOperation operation)
    {
        HandlerCallCount++;
        LastThrownException = new ConflictException("Resource already exists");
        throw LastThrownException;
    }
}
