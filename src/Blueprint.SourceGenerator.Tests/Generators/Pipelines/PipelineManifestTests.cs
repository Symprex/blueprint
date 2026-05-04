using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.Pipelines;

/// <summary>
/// Tests that generated pipeline classes include a <c>GetAppliedModules()</c>
/// static method returning the ordered set of contributor IDs that participated
/// in the pipeline plan. This is the compile-time inspection surface for
/// debugging and AI-friendly manifest generation.
/// </summary>
public class PipelineManifestTests
{
    [Fact]
    public void Generated_pipeline_includes_GetAppliedModules_method_returning_contributor_ids()
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

                             public partial class GetOrderQuery : IQuery<string>
                             {
                                 public static ApiOperationDescriptor Configure()
                                 {
                                     return new ApiOperationDescriptor(typeof(GetOrderQuery), nameof(GetOrderQuery), "Tests");
                                 }

                                 public static Task<string> HandleAsync(GetOrderQuery query)
                                 {
                                     return Task.FromResult("ok");
                                 }
                             }
                             """;

        var result = TestHelpers.Compile<OperationExecutorPipelineSourceGenerator>(source, assertCacheability: false);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSource = result.Output
            .Single(static tree => tree.FilePath.EndsWith("GetOrderQueryExecutorPipeline.g.cs", System.StringComparison.Ordinal))
            .ToString();

        // The method must exist
        pipelineSource.Should().Contain("public static System.Collections.Generic.IReadOnlyList<string> GetAppliedModules()");

        // It must contain the handler execution module (always present)
        pipelineSource.Should().Contain("\"HandlerExecutionModule\"");

        // It must contain the auditing module (default config)
        pipelineSource.Should().Contain("\"AuditingModule\"");

        // It must contain the background task flush module (default config)
        pipelineSource.Should().Contain("\"BackgroundTaskFlushModule\"");
    }

    [Fact]
    public void GetAppliedModules_preserves_contribution_order()
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

                             public partial class GetOrderQuery : IQuery<string>
                             {
                                 public static ApiOperationDescriptor Configure()
                                 {
                                     return new ApiOperationDescriptor(typeof(GetOrderQuery), nameof(GetOrderQuery), "Tests");
                                 }

                                 public static Task<string> HandleAsync(GetOrderQuery query)
                                 {
                                     return Task.FromResult("ok");
                                 }
                             }
                             """;

        var result = TestHelpers.Compile<OperationExecutorPipelineSourceGenerator>(source, assertCacheability: false);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSource = result.Output
            .Single(static tree => tree.FilePath.EndsWith("GetOrderQueryExecutorPipeline.g.cs", System.StringComparison.Ordinal))
            .ToString();

        // Handler execution must come before auditing which must come before background task flush
        var handlerIdx = pipelineSource.IndexOf("\"HandlerExecutionModule\"", System.StringComparison.Ordinal);
        var auditIdx = pipelineSource.IndexOf("\"AuditingModule\"", System.StringComparison.Ordinal);
        var bgTaskIdx = pipelineSource.IndexOf("\"BackgroundTaskFlushModule\"", System.StringComparison.Ordinal);

        handlerIdx.Should().BeGreaterThan(-1);
        auditIdx.Should().BeGreaterThan(handlerIdx, "AuditingModule should come after HandlerExecutionModule");
        bgTaskIdx.Should().BeGreaterThan(auditIdx, "BackgroundTaskFlushModule should come after AuditingModule");
    }

    [Fact]
    public void When_module_does_not_contribute_then_it_is_not_in_manifest()
    {
        const string source = """
                             using Blueprint;
                             using Blueprint.Auditing;
                             using Blueprint.Model;
                             using System.Threading.Tasks;

                             namespace TestNamespace;

                             [BlueprintAppScan]
                             public partial class TestApp : BlueprintApp
                             {
                             }

                             [DoNotAudit]
                             public partial class GetOrderQuery : IQuery<string>
                             {
                                 public static ApiOperationDescriptor Configure()
                                 {
                                     return new ApiOperationDescriptor(typeof(GetOrderQuery), nameof(GetOrderQuery), "Tests");
                                 }

                                 public static Task<string> HandleAsync(GetOrderQuery query)
                                 {
                                     return Task.FromResult("ok");
                                 }
                             }
                             """;

        var result = TestHelpers.Compile<OperationExecutorPipelineSourceGenerator>(source, assertCacheability: false);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSource = result.Output
            .Single(static tree => tree.FilePath.EndsWith("GetOrderQueryExecutorPipeline.g.cs", System.StringComparison.Ordinal))
            .ToString();

        // AuditingModule should NOT appear because [DoNotAudit] excludes it
        pipelineSource.Should().Contain("public static System.Collections.Generic.IReadOnlyList<string> GetAppliedModules()");
        pipelineSource.Should().NotContain("\"AuditingModule\"");

        // But handler execution and background task flush should still be present
        pipelineSource.Should().Contain("\"HandlerExecutionModule\"");
        pipelineSource.Should().Contain("\"BackgroundTaskFlushModule\"");
    }
}
