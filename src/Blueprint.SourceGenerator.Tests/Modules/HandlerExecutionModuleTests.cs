using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Proves the <c>HandlerExecutionModule</c> generates the handler invocation code in the pipeline,
/// including correct async pattern selection, return type normalization to <c>OperationResult</c>,
/// and <c>handlerResult</c> variable declaration for downstream consumers.
/// </summary>
public class HandlerExecutionModuleTests
{
    [Fact]
    public void When_command_with_async_handler_then_pipeline_calls_handler()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Runtime.Exec;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class SimpleCommand : ICommand
                {
                    public string Name { get; set; }

                    public static Task HandleAsync(SimpleCommand operation) => Task.CompletedTask;
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // Handler is called via the generated static method
        pipelineSource.Should().Contain("HandleAsync");

        // Result is normalized to OperationResult for downstream modules
        pipelineSource.Should().Contain("handlerResult");
    }

    [Fact]
    public void When_query_with_typed_return_then_pipeline_wraps_result()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Runtime.Exec;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class TypedQuery : IQuery<string>
                {
                    public string Id { get; set; }

                    public static Task<string> HandleAsync(TypedQuery operation) => Task.FromResult("result");
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        var allPipelineText = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // Query handler returns typed result that gets wrapped
        allPipelineText.Should().Contain("HandleAsync");
        allPipelineText.Should().Contain("handlerResult");

        // OperationResult normalization for downstream consumers
        allPipelineText.Should().Contain("OperationResult");
    }

    [Fact]
    public void When_handler_has_di_parameters_then_pipeline_resolves_services()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Runtime.Exec;
            using System.Threading.Tasks;
            using Microsoft.Extensions.Logging;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class DiCommand : ICommand
                {
                    public string Name { get; set; }

                    public static Task HandleAsync(DiCommand operation, ILogger<DiCommand> logger)
                    {
                        return Task.CompletedTask;
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // ILogger<T> cached via loggerFactory in constructor, used from field in method body
        pipelineSource.Should().Contain("ILogger");
        pipelineSource.Should().Contain("CreateLogger");
        pipelineSource.Should().Contain("_cached_ILogger");
    }

    [Fact]
    public void When_handler_takes_context_parameter_then_pipeline_passes_context()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Runtime.Exec;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class ContextCommand : ICommand
                {
                    public string Name { get; set; }

                    public static Task HandleAsync(ContextCommand operation, ApiOperationContext context)
                    {
                        return Task.CompletedTask;
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // Context parameter passed directly — not resolved from DI
        pipelineSource.Should().Contain("HandleAsync");
        pipelineSource.Should().Contain("context");
    }

    [Fact]
    public void When_handler_takes_HttpRequest_parameter_then_pipeline_resolves_from_context()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;
            using Microsoft.AspNetCore.Http;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [AllowAnonymous]
                [RootLink("/stripe/webhooks")]
                public partial class HandleWebhookCommand : ICommand<string>
                {
                    public static Task<string> InvokeAsync(HttpRequest httpRequest)
                    {
                        return Task.FromResult("ok");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // HttpRequest resolved from context.HttpContext.Request, not from DI
        pipelineSource.Should().Contain("context.HttpContext!.Request");
        pipelineSource.Should().NotContain("GetRequiredService<Microsoft.AspNetCore.Http.HttpRequest>");
    }

    [Fact]
    public void When_handler_takes_ClaimsPrincipal_parameter_then_pipeline_resolves_from_context()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;
            using System.Security.Claims;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [AllowAnonymous]
                [RootLink("/test/principal")]
                public partial class PrincipalQuery : IQuery<string>
                {
                    public static Task<string> HandleAsync(ClaimsPrincipal principal)
                    {
                        return Task.FromResult(principal.Identity?.Name ?? "anonymous");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // ClaimsPrincipal resolved from context.UserPrincipal, not from DI
        pipelineSource.Should().Contain("context.UserPrincipal!");
        pipelineSource.Should().NotContain("GetRequiredService<System.Security.Claims.ClaimsPrincipal>");
    }
}
