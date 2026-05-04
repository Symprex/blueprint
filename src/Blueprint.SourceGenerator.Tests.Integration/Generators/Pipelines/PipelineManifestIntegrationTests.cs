using Blueprint.Model;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves that generated pipeline classes expose a <c>GetAppliedModules()</c> static method
/// returning the ordered set of contributor IDs that participated in the pipeline plan.
/// </summary>
public class PipelineManifestIntegrationTests
{
    [Fact]
    public void GetAppliedModules_returns_expected_modules_for_standard_query()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);

        var pipeline = executor.GetPipeline(typeof(ManifestTestQuery));
        var pipelineType = pipeline.GetType();

        var method = pipelineType.GetMethod("GetAppliedModules", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull("generated pipeline classes should have a static GetAppliedModules method");

        var modules = (IReadOnlyList<string>)method!.Invoke(null, null)!;

        modules.Should().Contain("HandlerExecutionModule");
        modules.Should().Contain("AuditingModule");
        modules.Should().Contain("BackgroundTaskFlushModule");
        modules.Should().Contain("PipelineReturnModule");
    }

    [Fact]
    public void GetAppliedModules_excludes_AuditingModule_when_DoNotAudit_applied()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);

        var pipeline = executor.GetPipeline(typeof(ManifestNonAuditableQuery));
        var pipelineType = pipeline.GetType();

        var method = pipelineType.GetMethod("GetAppliedModules", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull();

        var modules = (IReadOnlyList<string>)method!.Invoke(null, null)!;

        modules.Should().Contain("HandlerExecutionModule");
        modules.Should().NotContain("AuditingModule", "DoNotAudit should exclude the AuditingModule from the manifest");
        modules.Should().Contain("BackgroundTaskFlushModule");
    }

    [Fact]
    public void GetAppliedModules_preserves_pipeline_contribution_order()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);

        var pipeline = executor.GetPipeline(typeof(ManifestTestQuery));
        var pipelineType = pipeline.GetType();

        var method = pipelineType.GetMethod("GetAppliedModules", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var modules = (IReadOnlyList<string>)method!.Invoke(null, null)!;

        var handlerIdx = modules.ToList().IndexOf("HandlerExecutionModule");
        var auditIdx = modules.ToList().IndexOf("AuditingModule");
        var bgTaskIdx = modules.ToList().IndexOf("BackgroundTaskFlushModule");

        handlerIdx.Should().BeLessThan(auditIdx, "HandlerExecution should come before Auditing");
        auditIdx.Should().BeLessThan(bgTaskIdx, "Auditing should come before BackgroundTaskFlush");
    }
}

/// <summary>
/// Standard query operation for manifest tests.
/// </summary>
public partial class ManifestTestQuery : IQuery<string>
{
    /// <summary>
    /// Handles the query.
    /// </summary>
    /// <param name="query">The query to handle.</param>
    /// <returns>A test result string.</returns>
    public static Task<string> HandleAsync(ManifestTestQuery query)
    {
        return Task.FromResult("manifest-test");
    }
}

/// <summary>
/// Query with <c>[DoNotAudit]</c> for verifying manifest excludes AuditingModule.
/// </summary>
[Blueprint.Auditing.DoNotAudit]
public partial class ManifestNonAuditableQuery : IQuery<string>
{
    /// <summary>
    /// Handles the query.
    /// </summary>
    /// <param name="query">The query to handle.</param>
    /// <returns>A test result string.</returns>
    public static Task<string> HandleAsync(ManifestNonAuditableQuery query)
    {
        return Task.FromResult("non-auditable");
    }
}
