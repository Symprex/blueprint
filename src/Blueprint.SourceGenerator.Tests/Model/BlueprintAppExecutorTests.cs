using Blueprint.Model;
using Blueprint.Runtime.Exec;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Model;

public class BlueprintAppExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_Should_Invoke_Registered_Pipeline_For_Context_Descriptor()
    {
        var app = new BlueprintApp();
        var descriptor = new ApiOperationDescriptor(typeof(TestOperation), "TestOperation", "TestSource");
        app.Register(descriptor);

        var executor = new BlueprintAppExecutor(app);
        var pipeline = new CapturingPipeline();
        executor.Register(typeof(TestOperation), pipeline);

        var context = new ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None);

        var result = await executor.ExecuteAsync(context);

        result.Should().BeSameAs(pipeline.Result);
        pipeline.ExecutedContext.Should().BeSameAs(context);
    }

    private sealed class TestOperation;

    private sealed class CapturingPipeline : IOperationExecutorPipeline
    {
        public OperationResult Result { get; } = new StubResult();

        public ApiOperationContext? ExecutedContext { get; private set; }

        public Task<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            this.ExecutedContext = context;
            return Task.FromResult(this.Result);
        }
    }

    private sealed class StubResult : OperationResult
    {
        public override Task ExecuteAsync(ApiOperationContext context)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
