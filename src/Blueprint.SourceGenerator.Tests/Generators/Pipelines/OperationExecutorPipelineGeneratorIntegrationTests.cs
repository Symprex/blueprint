using Blueprint.SourceGenerator.Generators.Pipelines;

namespace Blueprint.SourceGenerator.Tests.Generators.Pipelines;

/// <summary>
/// Integration tests for the <see cref="OperationExecutorPipelineSourceGenerator"/> that verify
/// end-to-end code generation with actual compilation.
/// </summary>
public class OperationExecutorPipelineGeneratorIntegrationTests
{
    private const string SnapshotsFolder = "Generators/Pipelines/Snapshots/Integration/";

    [Fact]
    public async Task Inline_static_handler_generates_correctly()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                     }

                     public class TestQuery : IQuery<string>
                     {
                         public static Task<string> InvokeAsync()
                         {
                             return Task.FromResult("Hello World");
                         }
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task Inline_instance_handler_generates_correctly()
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
                             this.RegisterOperation<TestQuery>();
                         }
                     }

                     public class TestQuery : IQuery<string>
                     {
                         public string Value { get; set; }

                         public Task<string> HandleAsync()
                         {
                             return Task.FromResult(this.Value);
                         }
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task Inline_handler_with_context_parameter_generates_correctly()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Runtime.Exec;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.RegisterOperation<TestQuery>();
                         }
                     }

                     public class TestQuery : IQuery<string>
                     {
                         public static Task<string> InvokeAsync(ApiOperationContext context)
                         {
                             return Task.FromResult("Context: " + context.DataModel.Name);
                         }
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task Inline_handler_with_DI_parameters_generates_correctly()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using System.Threading.Tasks;
                     using Microsoft.Extensions.Logging;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.RegisterOperation<TestQuery>();
                         }
                     }

                     public interface IMyService
                     {
                         string GetValue();
                     }

                     public class TestQuery : IQuery<string>
                     {
                         public static Task<string> InvokeAsync(IMyService service, ILogger<TestQuery> logger)
                         {
                             logger.LogInformation("Getting value");
                             return Task.FromResult(service.GetValue());
                         }
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task External_handler_generates_correctly()
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
                             this.RegisterOperation<TestCommand>();
                         }
                     }

                     public class TestCommand : ICommand<string>
                     {
                         public string Name { get; set; }
                     }

                     public class TestCommandHandler
                     {
                         public Task<string> HandleAsync(TestCommand command)
                         {
                             return Task.FromResult("Handled: " + command.Name);
                         }
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task External_handler_with_DI_generates_correctly()
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
                             this.RegisterOperation<TestCommand>();
                         }
                     }

                     public interface IRepository
                     {
                         Task SaveAsync(string value);
                     }

                     public class TestCommand : ICommand<bool>
                     {
                         public string Value { get; set; }
                     }

                     public class TestCommandHandler
                     {
                         public async Task<bool> HandleAsync(TestCommand command, IRepository repository)
                         {
                             await repository.SaveAsync(command.Value);
                             return true;
                         }
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task Synchronous_handler_generates_correctly()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.RegisterOperation<TestQuery>();
                         }
                     }

                     public class TestQuery : IQuery<int>
                     {
                         public static int Invoke()
                         {
                             return 42;
                         }
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task Multiple_operations_generate_correctly()
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
                             this.RegisterOperation<GetUserQuery>();
                             this.RegisterOperation<CreateUserCommand>();
                         }
                     }

                     public class GetUserQuery : IQuery<string>
                     {
                         public static Task<string> InvokeAsync()
                         {
                             return Task.FromResult("User");
                         }
                     }

                     public class CreateUserCommand : ICommand<bool>
                     {
                         public string Name { get; set; }
                         
                         public static Task<bool> HandleAsync()
                         {
                             return Task.FromResult(true);
                         }
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task Operation_without_handler_generates_diagnostic()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.RegisterOperation<TestQuery>();
                         }
                     }

                     public class TestQuery : IQuery<string>
                     {
                         // No handler method
                     }
                     """;

        await Compile(source)
            .VerifyCompilationFailedAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task BlueprintApp_not_partial_generates_diagnostic()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.RegisterOperation<TestQuery>();
                         }
                     }

                     public class TestQuery : IQuery<string>
                     {
                         public static string Invoke() => "test";
                     }
                     """;

        await Compile(source)
            .VerifyCompilationFailedAsync(SnapshotsFolder);
    }

    private static GenerationResult Compile(string source)
    {
        return TestHelpers.Compile<OperationExecutorPipelineSourceGenerator>(source);
    }
}

