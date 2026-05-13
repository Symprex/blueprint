using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules.Tenancy;

/// <summary>
/// Proves the <c>TenantModule</c> generates compile-time tenant resolution code
/// that sets <c>ITenantAccessor.Current</c> from <c>ITenantResolver</c> and
/// directly assigns the <c>TenantId</c> property without reflection when present.
/// </summary>
public class TenantModuleTests
{
    [Fact]
    public void When_operation_has_TenantId_property_then_pipeline_assigns_directly()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class TenantCommand : ICommand<string>
                {
                    public string TenantId { get; set; }
                    public string Name { get; set; }

                    public static Task<string> HandleAsync(TenantCommand operation)
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
        pipelineSources.Should().NotBeEmpty();

        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // Module caches both services at construction time
        pipelineSource.Should().Contain("ITenantAccessor _tenantAccessor");
        pipelineSource.Should().Contain("ITenantResolver _tenantResolver");

        // Uses shared helper to resolve and set tenant
        pipelineSource.Should().Contain("PipelineHelpers.ResolveTenant(");

        // Direct property assignment (no reflection, no IPropertyPopulator)
        pipelineSource.Should().Contain("operation.TenantId = this._tenantAccessor.Current");
    }

    [Fact]
    public void When_operation_has_no_TenantId_property_then_pipeline_only_sets_accessor()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class NoTenantQuery : IQuery<string>
                {
                    public string Filter { get; set; }

                    public static Task<string> HandleAsync(NoTenantQuery operation)
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
        pipelineSources.Should().NotBeEmpty();

        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // Still resolves and sets accessor (all operations get tenant context)
        pipelineSource.Should().Contain("PipelineHelpers.ResolveTenant(");

        // Should NOT attempt direct property assignment
        pipelineSource.Should().NotContain("operation.TenantId");
    }

    [Fact]
    public void When_TenantId_is_inherited_from_base_then_pipeline_still_assigns()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public abstract class TenantAwareBase
                {
                    public string TenantId { get; set; }
                }

                public partial class InheritedTenantCommand : TenantAwareBase, ICommand<string>
                {
                    public string Data { get; set; }

                    public static Task<string> HandleAsync(InheritedTenantCommand operation)
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

        // Should detect TenantId from the base class and assign directly
        pipelineSource.Should().Contain("operation.TenantId = this._tenantAccessor.Current");
    }

    [Fact]
    public void When_TenantId_is_readonly_then_pipeline_skips_assignment()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class ReadonlyTenantQuery : IQuery<string>
                {
                    public string TenantId { get; }

                    public static Task<string> HandleAsync(ReadonlyTenantQuery operation)
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

        // Still resolves and sets accessor
        pipelineSource.Should().Contain("PipelineHelpers.ResolveTenant(");

        // Should NOT assign to readonly property
        pipelineSource.Should().NotContain("operation.TenantId");
    }
}
