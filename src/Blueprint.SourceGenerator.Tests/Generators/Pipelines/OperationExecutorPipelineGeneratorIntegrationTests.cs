using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Generators.Pipelines;

/// <summary>
/// Integration tests for the <see cref="OperationExecutorPipelineSourceGenerator"/> that verify
/// end-to-end code generation with actual compilation.
/// </summary>
public class OperationExecutorPipelineGeneratorIntegrationTests
{
    private const string SnapshotsFolder = "Generators/Pipelines/Snapshots/Integration/";

    [Fact]
    public void Pipeline_contributor_context_exposes_readonly_operation_and_handler_metadata()
    {
        const string source = """
                              using Blueprint.Model;
                              using System.Threading.Tasks;

                              namespace TestNamespace;

                              public partial class TestQuery : IQuery<string>
                              {
                                  public static Task<string> InvokeAsync()
                                  {
                                      return Task.FromResult("Hello World");
                                  }
                              }
                              """;

        var (compilation, operationType) = CompileAndGetOperationType(source, "TestQuery");
        var handlerResult = HandlerDiscovery.FindHandler(operationType, compilation);
        var operation = OperationModel.Create(operationType);
        var handler = handlerResult.Value!;
        var context = new PipelineContributorContext(
            nameof(HandlerExecutionModule),
            operation,
            new PipelinePlan(operation, new VariableRegistry()),
            new CompilationOperationLookup(compilation),
            handler);

        context.Operation.Should().Be(operation);
        context.Handler.Should().Be(handler);
    }

    [Fact]
    public void Handler_execution_module_registers_implicit_execution_stage_and_handler_result_variable()
    {
        const string source = """
                              using Blueprint.Model;
                              using System.Threading.Tasks;

                              namespace TestNamespace;

                              public partial class TestQuery : IQuery<string>
                              {
                                  public static Task<string> InvokeAsync()
                                  {
                                      return Task.FromResult("Hello World");
                                  }
                              }
                              """;

        var (compilation, operationType) = CompileAndGetOperationType(source, "TestQuery");
        var handlerResult = HandlerDiscovery.FindHandler(operationType, compilation);
        var operation = OperationModel.Create(operationType);
        var plan = new PipelinePlan(operation, new VariableRegistry());
        var module = new HandlerExecutionModule();
        var context = new PipelineContributorContext(module.ContributorId, operation, plan, new CompilationOperationLookup(compilation), handlerResult.Value!);

        handlerResult.Diagnostics.Should().BeEmpty();
        handlerResult.Value.Should().NotBeNull();
        context.FindOperationByMetadataName("TestNamespace.TestQuery")!.TypeName.Should().Be("TestQuery");

        module.ContributeToPipeline(context);

        plan.Diagnostics.Should().BeEmpty();
        plan.Contributions.Should().ContainSingle().Which.Should().Be(
            new PipelineContribution(PipelineStage.Execution, module.ContributorId));
        var renderStep = plan.RenderSteps.Should().ContainSingle().Subject;
        renderStep.Stage.Should().Be(PipelineStage.Execution);
        renderStep.ContributorId.Should().Be(module.ContributorId);
        plan.VariableRegistry.Registrations.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new VariableRegistration(
                new VariableKey("string", HandlerExecutionModule.HandlerResultPurpose),
                module.ContributorId,
                PipelineStage.Execution,
                VariableSourceKind.ProducedValue,
                VariableLifetime.Local));
    }

    [Fact]
    public void Handler_execution_render_step_owns_handler_invocation_rendering()
    {
        const string source = """
                              using Blueprint.Model;
                              using System.Threading.Tasks;

                              namespace TestNamespace;

                              public partial class TestQuery : IQuery<string>
                              {
                                  public static Task<string> InvokeAsync()
                                  {
                                      return Task.FromResult("Hello World");
                                  }
                              }
                              """;

        var (compilation, operationType) = CompileAndGetOperationType(source, "TestQuery");
        var handlerResult = HandlerDiscovery.FindHandler(operationType, compilation);
        var operation = OperationModel.Create(operationType);
        var plan = new PipelinePlan(operation, new VariableRegistry());
        var module = new HandlerExecutionModule();

        handlerResult.Diagnostics.Should().BeEmpty();
        handlerResult.Value.Should().NotBeNull();

        module.ContributeToPipeline(new PipelineContributorContext(module.ContributorId, operation, plan, new CompilationOperationLookup(compilation), handlerResult.Value!));

        var renderStep = plan.RenderSteps.Should().ContainSingle().Subject;
        var writer = new RecordingCodeWriter();

        renderStep.Render(new PipelineRenderContext(writer, handlerResult.Value!, plan));

        writer.Rendered.Should().Contain("var handlerResult = await global::TestNamespace.TestQuery.InvokeAsync();");
        writer.Rendered.Should().Contain("return new Blueprint.Runtime.Exec.OkResult(handlerResult);");
    }

    [Fact]
    public void Pipeline_render_context_helper_methods_render_without_exposing_writer_or_handler_properties()
    {
        const string source = """
                              using Blueprint.Model;
                              using System.Threading.Tasks;

                              namespace TestNamespace;

                              public partial class TestQuery : IQuery<string>
                              {
                                  public static Task<string> InvokeAsync(ApiOperationContext context)
                                  {
                                      return Task.FromResult("Hello World");
                                  }
                              }
                              """;

        var (compilation, operationType) = CompileAndGetOperationType(source, "TestQuery");
        var handler = HandlerDiscovery.FindHandler(operationType, compilation).Value!;
        var plan = new PipelinePlan(OperationModel.Create(operationType), new VariableRegistry());
        var writer = new RecordingCodeWriter();
        var context = new PipelineRenderContext(writer, handler, plan);

        context.DeclareVariable("contextCopy", context.GetContextExpression());
        context.WriteBlankLine();
        context.Return("new Blueprint.Runtime.Exec.OkResult(contextCopy)");

        context.GetHandlerParameters().Should().ContainSingle();
        context.IsHandlerAsync().Should().BeTrue();
        writer.Rendered.Should().Contain("var contextCopy = context;");
        writer.Rendered.Should().Contain("return new Blueprint.Runtime.Exec.OkResult(contextCopy);");
    }

    [Fact]
    public void Inline_handler_with_validation_attributes_generates_validation_before_handler_invocation()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using System.ComponentModel.DataAnnotations;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(CreateUserCommand.Configure());
                         }
                     }

                     public class CreateUserCommand : ICommand<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(CreateUserCommand), nameof(CreateUserCommand), "Tests");
                         }

                         [Required]
                         public string? Name { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Name ?? string.Empty);
                         }
                     }
                     """;

        var result = Compile(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().Contain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("throw new global::Blueprint.Validation.ValidationException(validationResults);");
        pipelineSource.IndexOf("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

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
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestQuery), nameof(TestQuery), "Tests");
                         }

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
                             this.Register(TestQuery.Configure());
                         }
                     }

                     public class TestQuery : IQuery<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestQuery), nameof(TestQuery), "Tests");
                         }

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
                             this.Register(TestQuery.Configure());
                         }
                     }

                     public class TestQuery : IQuery<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestQuery), nameof(TestQuery), "Tests");
                         }

                         public static Task<string> InvokeAsync(ApiOperationContext context)
                         {
                             return Task.FromResult("Context: " + context.App.GetType().Name);
                         }
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task Inline_handler_with_validation_attributes_generates_correctly()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using System.ComponentModel.DataAnnotations;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(CreateUserCommand.Configure());
                         }
                     }

                     public class CreateUserCommand : ICommand<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(CreateUserCommand), nameof(CreateUserCommand), "Tests");
                         }

                         [Required]
                         public string? Name { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Name ?? string.Empty);
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
                             this.Register(TestQuery.Configure());
                         }
                     }

                     public interface IMyService
                     {
                         string GetValue();
                     }

                     public class TestQuery : IQuery<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestQuery), nameof(TestQuery), "Tests");
                         }

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
                             this.Register(TestCommand.Configure());
                         }
                     }

                     public class TestCommand : ICommand<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestCommand), nameof(TestCommand), "Tests");
                         }

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
                             this.Register(TestCommand.Configure());
                         }
                     }

                     public interface IRepository
                     {
                         Task SaveAsync(string value);
                     }

                     public class TestCommand : ICommand<bool>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestCommand), nameof(TestCommand), "Tests");
                         }

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
                             this.Register(TestQuery.Configure());
                         }
                     }

                     public class TestQuery : IQuery<int>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestQuery), nameof(TestQuery), "Tests");
                         }

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
                             this.Register(GetUserQuery.Configure());
                             this.Register(CreateUserCommand.Configure());
                         }
                     }

                     public class GetUserQuery : IQuery<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(GetUserQuery), nameof(GetUserQuery), "Tests");
                         }

                         public static Task<string> InvokeAsync()
                         {
                             return Task.FromResult("User");
                         }
                     }

                     public class CreateUserCommand : ICommand<bool>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(CreateUserCommand), nameof(CreateUserCommand), "Tests");
                         }

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
                             this.Register(TestQuery.Configure());
                         }
                     }

                     public class TestQuery : IQuery<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestQuery), nameof(TestQuery), "Tests");
                         }

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
                             this.Register(TestQuery.Configure());
                         }
                     }

                     public class TestQuery : IQuery<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestQuery), nameof(TestQuery), "Tests");
                         }

                         public static string Invoke() => "test";
                     }
                     """;

        await Compile(source)
            .VerifyCompilationFailedAsync(SnapshotsFolder);
    }

    private static GenerationResult Compile(string source)
    {
        return TestHelpers.Compile<OperationExecutorPipelineSourceGenerator>(source, assertCacheability: false);
    }

    private static (Compilation Compilation, INamedTypeSymbol OperationType) CompileAndGetOperationType(
        string source,
        string typeName)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.ICommand).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "OperationExecutorPipelineContributorTests",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var operationType = compilation.GetTypeByMetadataName($"TestNamespace.{typeName}");

        operationType.Should().NotBeNull($"Type {typeName} should exist in compilation");

        return (compilation, operationType!);
    }

    private sealed class RecordingCodeWriter : ICodeWriter
    {
        private readonly List<string> _lines = [];

        public string Rendered => string.Join(Environment.NewLine, this._lines);

        public void Append(string value)
        {
            this._lines.Add(value);
        }

        public void AppendUnindented(string value)
        {
            this._lines.Add(value);
        }

        public void NewLine()
        {
            this._lines.Add(string.Empty);
        }

        public void AppendLine(string value)
        {
            this._lines.Add(value);
        }

        public void AppendUnindentedLine(string value)
        {
            this._lines.Add(value);
        }

        public IDisposable Block(string value, params string[] constraints)
        {
            throw new NotSupportedException();
        }

        public ICodeWriter BlockWriter(string originalLine)
        {
            throw new NotSupportedException();
        }
    }
}

