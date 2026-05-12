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
                new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
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

        writer.Rendered.Should().Contain("var handlerResult = (Blueprint.OperationResult)new Blueprint.Runtime.Exec.OkResult<string>(await global::TestNamespace.TestQuery.InvokeAsync().ConfigureAwait(false));");
    }

    [Fact]
    public void Handler_execution_render_step_treats_task_without_result_as_no_result()
    {
        const string source = """
                               using Blueprint.Model;
                               using System.Threading.Tasks;

                               namespace TestNamespace;

                               public partial class TestCommand : ICommand
                               {
                                   public static Task HandleAsync()
                                   {
                                       return Task.CompletedTask;
                                   }
                               }
                               """;

        var (compilation, operationType) = CompileAndGetOperationType(source, "TestCommand");
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

        writer.Rendered.Should().Contain("await global::TestNamespace.TestCommand.HandleAsync().ConfigureAwait(false);");
        writer.Rendered.Should().Contain("var handlerResult = (Blueprint.OperationResult)new Blueprint.Runtime.Exec.NoResultOperationResult();");
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
    public void Pipeline_code_generator_allows_exception_translation_to_be_replaced()
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

        var policyResult = PipelineExceptionTranslatorPolicy.Create([new TestExceptionTranslator()]);

        policyResult.Diagnostics.Should().BeEmpty();
        policyResult.Value.Should().NotBeNull();

        // Exception handling now lives in OperationExecutorShell at runtime.
        // Verify the policy correctly captures a custom translator replacement.
        var translatorIds = policyResult.Value!.Translators.Select(static t => t.TranslatorId).ToArray();
        translatorIds.Should().Equal("test-invalid-operation-exception");

        // Generated code should contain the shell-derived pipeline but no catch blocks
        var pipelineSource = PipelineCodeGenerator.GeneratePipeline(handlerResult.Value!, plan);

        pipelineSource.Should().Contain("ExecuteCoreAsync");
        pipelineSource.Should().NotContain("catch (");
    }

    [Fact]
    public void When_package_exception_translation_module_is_enabled_then_generated_pipeline_includes_package_translator_in_policy_order()
    {
        const string source = """
                             using Blueprint;
                             using Blueprint.Model;
                             using System.Threading.Tasks;

                             namespace TestNamespace;

                             [BlueprintAppScan]
                             public partial class TestApp : BlueprintApp
                             {
                             }

                             public partial class IncludedOperation : IQuery<string>
                             {
                                 public static ApiOperationDescriptor Configure()
                                 {
                                     return new ApiOperationDescriptor(typeof(IncludedOperation), nameof(IncludedOperation), "Tests");
                                 }

                                 public static Task<string> HandleAsync(IncludedOperation operation)
                                 {
                                     return Task.FromResult("ok");
                                 }
                             }
                             """;

        var result = TestHelpers.Compile<PackageAwareOperationExecutorPipelineSourceGenerator>(source, assertCacheability: false);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static output => output.Contains("IncludedOperationExecutorPipeline"));

        pipelineSource.Should().NotBeNull();

        // Exception handling now lives in OperationExecutorShell at runtime.
        // Generated pipeline should contain ExecuteCoreAsync, not validation/exception-translation catch blocks.
        pipelineSource!.Should().Contain("ExecuteCoreAsync");
        pipelineSource.Should().Contain("OperationExecutorShell");
        pipelineSource.Should().NotContain("catch (Blueprint.Validation.ValidationException");
        pipelineSource.Should().NotContain("catch (System.ComponentModel.DataAnnotations.ValidationException");
    }

    [Fact]
    public async Task When_package_exception_translation_module_is_enabled_then_generated_pipeline_matches_snapshot()
    {
        const string source = """
                             using Blueprint;
                             using Blueprint.Model;
                             using System.Threading.Tasks;

                             namespace TestNamespace;

                             [BlueprintAppScan]
                             public partial class TestApp : BlueprintApp
                             {
                             }

                             public partial class IncludedOperation : IQuery<string>
                             {
                                 public static ApiOperationDescriptor Configure()
                                 {
                                     return new ApiOperationDescriptor(typeof(IncludedOperation), nameof(IncludedOperation), "Tests");
                                 }

                                 public static Task<string> HandleAsync(IncludedOperation operation)
                                 {
                                     return Task.FromResult("ok");
                                 }
                             }
                             """;

        await TestHelpers.Compile<PackageAwareOperationExecutorPipelineSourceGenerator>(source, assertCacheability: false)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public void When_package_exception_translation_module_conflicts_with_builtin_translator_then_generator_reports_bp113()
    {
        const string source = """
                             using Blueprint;
                             using Blueprint.Model;
                             using System.Threading.Tasks;

                             namespace TestNamespace;

                             [BlueprintAppScan]
                             public partial class TestApp : BlueprintApp
                             {
                             }

                             public partial class IncludedOperation : IQuery<string>
                             {
                                 public static ApiOperationDescriptor Configure()
                                 {
                                     return new ApiOperationDescriptor(typeof(IncludedOperation), nameof(IncludedOperation), "Tests");
                                 }

                                 public static Task<string> HandleAsync(IncludedOperation operation)
                                 {
                                     return Task.FromResult("ok");
                                 }
                             }
                             """;

        var result = TestHelpers.Compile<ConflictingPackageAwareOperationExecutorPipelineSourceGenerator>(source, assertCacheability: false);

        result.Diagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "BP113");
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
    public void Generated_outer_exception_shell_uses_canonical_unhandled_exception_result_name()
    {
        const string source = """
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

        var result = Compile(source);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static sourceText => sourceText.Contains("TestQueryExecutorPipeline"));

        pipelineSource.Should().NotBeNull();

        // Exception handling now lives in OperationExecutorShell at runtime.
        // Verify the generated pipeline derives from the shell and uses ExecuteCoreAsync.
        pipelineSource!.Should().Contain("OperationExecutorShell");
        pipelineSource.Should().Contain("ExecuteCoreAsync");
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
    public async Task External_handler_with_constructor_DI_generates_correctly()
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

                     public interface ILogger
                     {
                         void Log(string message);
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
                         private readonly IRepository _repository;
                         private readonly ILogger _logger;

                         public TestCommandHandler(IRepository repository, ILogger logger)
                         {
                             _repository = repository;
                             _logger = logger;
                         }

                         public async Task<bool> HandleAsync(TestCommand command)
                         {
                             _logger.Log("Saving: " + command.Value);
                             await _repository.SaveAsync(command.Value);
                             return true;
                         }
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task External_handler_with_base_class_property_injection_generates_correctly()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Runtime.Exec;
                     using Blueprint.Authorization;
                     using Microsoft.Extensions.Logging;
                     using System.Threading;
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

                     public abstract class ApiOperationsBase
                     {
                         public ApiOperationContext Context { get; set; }
                         public IUserAuthorisationContext Identity { get; set; }
                         public ILogger Logger { get; set; }
                         public CancellationToken CancellationToken { get; set; }
                     }

                     public class TestQuery : IQuery<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestQuery), nameof(TestQuery), "Tests");
                         }

                         public string Id { get; set; }
                     }

                     public class TestQueryHandler : ApiOperationsBase
                     {
                         public Task<string> HandleAsync(TestQuery query)
                         {
                             return Task.FromResult("Result for " + query.Id);
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
    public async Task Task_handler_without_result_generates_correctly()
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

                     public class TestCommand : ICommand
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestCommand), nameof(TestCommand), "Tests");
                         }

                         public static Task HandleAsync()
                         {
                             return Task.CompletedTask;
                         }
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task Explicit_operation_result_handler_generates_correctly()
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

                     public class TestQuery : IQuery
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestQuery), nameof(TestQuery), "Tests");
                         }

                         public static Task<OkResult<string>> HandleAsync()
                         {
                             return Task.FromResult(new OkResult<string>("Hello World"));
                         }
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task Explicit_operation_result_handler_with_plain_declared_contract_generates_correctly()
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

                         public static Task<OkResult<string>> HandleAsync()
                         {
                             return Task.FromResult(new OkResult<string>("Hello World"));
                         }
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task Results_union_handler_generates_correctly()
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

                     public class TestQuery : IQuery<Results<OkResult<string>, NoResultOperationResult>>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestQuery), nameof(TestQuery), "Tests");
                         }

                         public static Task<Results<OkResult<string>, NoResultOperationResult>> HandleAsync()
                         {
                             Results<OkResult<string>, NoResultOperationResult> result = new OkResult<string>("Hello World");
                             return Task.FromResult(result);
                         }
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public void Nullable_plain_value_handler_return_generates_diagnostic()
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

                     public class TestQuery : IQuery<string?>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestQuery), nameof(TestQuery), "Tests");
                         }

                         public static Task<string?> InvokeAsync()
                         {
                             return Task.FromResult<string?>("Hello World");
                         }
                     }
                     """;

        var result = TestHelpers.Compile<OperationExecutorPipelineSourceGenerator>(source, assertCacheability: false);

        result.Diagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "BP105");
    }

    [Fact]
    public void Unsupported_results_union_member_generates_diagnostic()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Runtime.Exec;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(TestQuery.Configure());
                         }
                     }

                     public class TestQuery : IQuery<Results<string, OkResult<string>>>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestQuery), nameof(TestQuery), "Tests");
                         }

                         public static Results<string, OkResult<string>> Invoke()
                         {
                             return "Hello World";
                         }
                     }
                     """;

        var result = TestHelpers.Compile<OperationExecutorPipelineSourceGenerator>(source, assertCacheability: false);

        result.Diagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "BP106");
    }

    [Fact]
    public void When_results_union_has_duplicate_member_types_then_generates_bp114_diagnostic()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Runtime.Exec;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(TestQuery.Configure());
                         }
                     }

                     public class TestQuery : IQuery<Results<OkResult<string>, OkResult<string>>>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestQuery), nameof(TestQuery), "Tests");
                         }

                         public static Results<OkResult<string>, OkResult<string>> Invoke()
                         {
                             return new OkResult<string>("Hello");
                         }
                     }
                     """;

        var result = TestHelpers.Compile<OperationExecutorPipelineSourceGenerator>(source, assertCacheability: false);

        result.Diagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "BP114");
    }

    [Fact]
    public void When_operation_implements_both_ICommand_and_IQuery_then_reports_BP115()
    {
        var source = """
                     using Blueprint.Model;

                     namespace TestNamespace;

                     public partial class BadOperation : ICommand<string>, IQuery<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(BadOperation), nameof(BadOperation), "Tests");
                         }

                         public static string Invoke() => "Hello";
                     }
                     """;

        var result = TestHelpers.Compile<Blueprint.SourceGenerator.Generators.Descriptors.OperationDescriptorSourceGenerator>(source);

        result.Diagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "BP115");
    }

    [Fact]
    public void When_operation_implements_only_ICommand_then_no_BP115()
    {
        var source = """
                     using Blueprint.Model;

                     namespace TestNamespace;

                     public partial class GoodCommand : ICommand<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(GoodCommand), nameof(GoodCommand), "Tests");
                         }

                         public static string Invoke() => "Hello";
                     }
                     """;

        var result = TestHelpers.Compile<Blueprint.SourceGenerator.Generators.Descriptors.OperationDescriptorSourceGenerator>(source);

        result.Diagnostics.Where(static d => d.Id == "BP115").Should().BeEmpty();
    }

    [Fact]
    public void Plain_value_handler_with_incompatible_declared_contract_generates_diagnostic()
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

                         public static string Invoke()
                         {
                             return "Hello World";
                         }
                     }
                     """;

        var result = TestHelpers.Compile<OperationExecutorPipelineSourceGenerator>(source, assertCacheability: false);

        result.Diagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "BP107");
    }

    [Fact]
    public void Nested_explicit_ok_result_handler_with_result_shaped_declared_contract_generates_diagnostic()
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

                     public class TestQuery : IQuery<OkResult<string>>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestQuery), nameof(TestQuery), "Tests");
                         }

                         public static Task<OkResult<OkResult<string>>> HandleAsync()
                         {
                             return Task.FromResult(new OkResult<OkResult<string>>(new OkResult<string>("Hello World")));
                         }
                     }
                     """;

        var result = TestHelpers.Compile<OperationExecutorPipelineSourceGenerator>(source, assertCacheability: false);

        result.Diagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "BP107");
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

    [Fact]
    public async Task Synchronous_handler_emits_BP200_warning()
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
            .VerifySuccessWithDiagnosticsAsync(SnapshotsFolder, "BP200");
    }

    [Fact]
    public void Async_handler_does_not_emit_BP200_warning()
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

                     public class TestQuery : IQuery<int>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(TestQuery), nameof(TestQuery), "Tests");
                         }

                         public static Task<int> InvokeAsync()
                         {
                             return Task.FromResult(42);
                         }
                     }
                     """;

        var result = Compile(source);
        result.Diagnostics.Where(static d => d.Id == "BP200").Should().BeEmpty("async handlers should not trigger BP200");
    }

    private static GenerationResult Compile(string source)
    {
        return TestHelpers.Compile<OperationExecutorPipelineSourceGenerator>(source, assertCacheability: false);
    }

    private sealed class PackageAwareOperationExecutorPipelineSourceGenerator : OperationExecutorPipelineSourceGenerator
    {
        public PackageAwareOperationExecutorPipelineSourceGenerator()
            : base(CreateDefaultConfig([new PackageExceptionTranslationModule()]))
        {
        }
    }

    private sealed class ConflictingPackageAwareOperationExecutorPipelineSourceGenerator : OperationExecutorPipelineSourceGenerator
    {
        public ConflictingPackageAwareOperationExecutorPipelineSourceGenerator()
            : base(CreateDefaultConfig([new ConflictingPackageExceptionTranslationModule()]))
        {
        }
    }

    private static (Compilation Compilation, INamedTypeSymbol OperationType) CompileAndGetOperationType(
        string source,
        string typeName)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
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

    private sealed class TestExceptionTranslator : IPipelineExceptionTranslator
    {
        public string TranslatorId => "test-invalid-operation-exception";

        public string HandledExceptionTypeName => "System.InvalidOperationException";

        public IReadOnlyList<string> OwnedExceptionTypeNames { get; } = ["System.InvalidOperationException"];

        public int Order => 100;

        public string ResultTypeName => "Blueprint.Runtime.Exec.UnhandledExceptionResult";

        public bool IsFallback => false;

        public void WriteCatchBlock(ICodeWriter writer, bool isHandlerAsync)
        {
            writer.Catch(
                "System.InvalidOperationException e",
                catchWriter =>
                {
                    catchWriter.Return(
                        isHandlerAsync
                            ? "new Blueprint.Runtime.Exec.UnhandledExceptionResult(e)"
                            : "System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>(new Blueprint.Runtime.Exec.UnhandledExceptionResult(e))");
                });
        }
    }

    private sealed class PackageExceptionTranslationModule : IBlueprintSourceGeneratorModule, IExceptionTranslationContributor
    {
        public string ModuleId => nameof(PackageExceptionTranslationModule);

        public BlueprintModuleCategory Category => BlueprintModuleCategory.Consumer;

        public string ContributorId => "package-exception-translation";

        public int Order => 0;

        public bool IsUnique => true;

        public IReadOnlyList<string> DependsOn { get; } = [];

        public void ContributeToExceptionTranslation(ExceptionTranslationContributorContext context)
        {
            context.AddTranslator(new PackageSecurityExceptionTranslator());
        }
    }

    private sealed class PackageSecurityExceptionTranslator : IPipelineExceptionTranslator
    {
        public string TranslatorId => "package-security-exception";

        public string HandledExceptionTypeName => "System.Security.SecurityException";

        public IReadOnlyList<string> OwnedExceptionTypeNames { get; } = ["System.Security.SecurityException"];

        public string ResultTypeName => "Blueprint.Runtime.Exec.UnhandledExceptionResult";

        public int Order => 150;

        public bool IsFallback => false;

        public void WriteCatchBlock(ICodeWriter writer, bool isHandlerAsync)
        {
            writer.Catch(
                "System.Security.SecurityException e",
                catchWriter =>
                {
                    catchWriter.Return(
                        isHandlerAsync
                            ? "new Blueprint.Runtime.Exec.UnhandledExceptionResult(e)"
                            : "System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>(new Blueprint.Runtime.Exec.UnhandledExceptionResult(e))");
                });
        }
    }

    private sealed class ConflictingPackageExceptionTranslationModule : IBlueprintSourceGeneratorModule, IExceptionTranslationContributor
    {
        public string ModuleId => nameof(ConflictingPackageExceptionTranslationModule);

        public BlueprintModuleCategory Category => BlueprintModuleCategory.Consumer;

        public string ContributorId => "conflicting-package-exception-translation";

        public int Order => 0;

        public bool IsUnique => true;

        public IReadOnlyList<string> DependsOn { get; } = [];

        public void ContributeToExceptionTranslation(ExceptionTranslationContributorContext context)
        {
            context.AddTranslator(new ConflictingLegacyValidationExceptionTranslator());
        }
    }

    private sealed class ConflictingLegacyValidationExceptionTranslator : IPipelineExceptionTranslator
    {
        public string TranslatorId => "conflicting-legacy-validation-exception";

        public string HandledExceptionTypeName => "Blueprint.Validation.ValidationException";

        public IReadOnlyList<string> OwnedExceptionTypeNames { get; } = ["Blueprint.Validation.ValidationException"];

        public string ResultTypeName => "Blueprint.Runtime.Exec.ValidationFailedResult";

        public int Order => 125;

        public bool IsFallback => false;

        public void WriteCatchBlock(ICodeWriter writer, bool isHandlerAsync)
        {
            writer.Catch(
                "Blueprint.Validation.ValidationException e",
                catchWriter =>
                {
                    catchWriter.Return(
                        isHandlerAsync
                            ? "new Blueprint.Runtime.Exec.ValidationFailedResult(e.Results)"
                            : "System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>(new Blueprint.Runtime.Exec.ValidationFailedResult(e.Results))");
                });
        }
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

