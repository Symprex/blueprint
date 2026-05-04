using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules.BackgroundTaskFlush;

/// <summary>
/// Tests that compile-time diagnostics are emitted for invalid background task handler return contracts.
/// </summary>
public class BackgroundTaskReturnDiagnosticTests
{
    [Fact]
    public void When_background_task_handler_returns_plain_value_then_emits_BPT001_warning()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Tasks;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(MyTask.Configure());
                         }
                     }

                     public class MyTask : IBackgroundTask
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(MyTask), nameof(MyTask), "Tasks");
                         }

                         public static Task<string> InvokeAsync()
                         {
                             return Task.FromResult("should not return a value");
                         }
                     }
                     """;

        var result = TestHelpers.Compile<OperationExecutorPipelineSourceGenerator>(source, assertCacheability: false);

        result.Diagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "BPT001");
    }

    [Fact]
    public void When_background_task_handler_returns_void_then_no_BPT001_diagnostic()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Tasks;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(MyTask.Configure());
                         }
                     }

                     public class MyTask : IBackgroundTask
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(MyTask), nameof(MyTask), "Tasks");
                         }

                         public static Task InvokeAsync()
                         {
                             return Task.CompletedTask;
                         }
                     }
                     """;

        var result = TestHelpers.Compile<OperationExecutorPipelineSourceGenerator>(source, assertCacheability: false);

        result.Diagnostics.Where(static d => d.Id == "BPT001").Should().BeEmpty();
    }

    [Fact]
    public void When_background_task_handler_returns_operation_result_then_no_BPT001_diagnostic()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Tasks;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(MyTask.Configure());
                         }
                     }

                     public class MyTask : IBackgroundTask
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(MyTask), nameof(MyTask), "Tasks");
                         }

                         public static Task<OperationResult> InvokeAsync()
                         {
                             return Task.FromResult<OperationResult>(new Blueprint.Runtime.Exec.NoResultOperationResult());
                         }
                     }
                     """;

        var result = TestHelpers.Compile<OperationExecutorPipelineSourceGenerator>(source, assertCacheability: false);

        result.Diagnostics.Where(static d => d.Id == "BPT001").Should().BeEmpty();
    }

    [Fact]
    public void When_non_task_operation_handler_returns_plain_value_then_no_BPT001_diagnostic()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(TestQuery.Configure());
                         }
                     }

                     public class TestQuery : IQuery<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestQuery), nameof(TestQuery), "Tests");
                         }

                         public static Task<string> InvokeAsync()
                         {
                             return Task.FromResult("hello");
                         }
                     }
                     """;

        var result = TestHelpers.Compile<OperationExecutorPipelineSourceGenerator>(source, assertCacheability: false);

        result.Diagnostics.Where(static d => d.Id == "BPT001").Should().BeEmpty();
    }
}
