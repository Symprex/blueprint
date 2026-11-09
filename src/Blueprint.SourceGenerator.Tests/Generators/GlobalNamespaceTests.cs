using Blueprint.SourceGenerator.Generators.ApiExceptionStatusIndex;
using Blueprint.SourceGenerator.Generators.ApiResources;
using Blueprint.SourceGenerator.Generators.App;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Generators.ResourceEvents;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Tests.Generators;

/// <summary>
/// Proves that types declared in the global namespace (no <c>namespace</c> declaration) generate valid source. Roslyn
/// renders their containing namespace as <c>&lt;global namespace&gt;</c>, which used to reach hint names, so the
/// descriptor generator threw, and <c>namespace</c> declarations.
/// </summary>
public class GlobalNamespaceTests
{
    private const string GlobalTypes = """
        using Blueprint;
        using Blueprint.Model;
        using Blueprint.Model.Attributes;
        using System.Threading.Tasks;

        public partial class GlobalResource : ApiResource
        {
            public string Id { get; set; } = "";
        }

        [ResourceEventFamily(typeof(GlobalResource), ResourceObject = "global")]
        public static partial class GlobalResourceEvents
        {
            [ResourceEventReason(ChangeType.Created, "created")]
            public sealed partial record Created(GlobalResource Resource);
        }

        public partial class PingQuery : IQuery<string>
        {
            public static Task<string> InvokeAsync()
            {
                return Task.FromResult("global");
            }
        }
        """;

    private const string NamespacedApp = """
        using Blueprint.Model;
        using Blueprint.Model.Attributes;

        namespace TestNamespace
        {
            [BlueprintAppScan]
            public partial class TestBlueprintApp : BlueprintApp
            {
            }
        }
        """;

    private const string GlobalApp = """
        using Blueprint.Model;
        using Blueprint.Model.Attributes;

        [BlueprintAppScan]
        public partial class TestBlueprintApp : BlueprintApp
        {
        }
        """;

    [Fact]
    public void When_operation_is_in_the_global_namespace_then_descriptor_generator_compiles_and_is_cacheable()
    {
        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>("""
            using Blueprint.Model;

            public partial class PingQuery : IQuery<string> {}
            """);

        result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var descriptor = result.Output.Select(static t => t.ToString()).Should().ContainSingle(static s => s.Contains("class PingQueryDescriptor")).Which;
        descriptor.Should().NotContain("namespace");
        descriptor.Should().Contain("""new global::Blueprint.Model.OperationDiscoveryFeatureData("query", "", "PingQuery")""");
    }

    [Fact]
    public void When_types_are_in_the_global_namespace_then_every_generator_compiles()
    {
        var result = CompileWithEveryGenerator(GlobalTypes, NamespacedApp);

        ErrorsOf(result).Should().BeEmpty();
        result.GetGeneratedSources(nameof(AppDataModelSourceGenerator))
            .Select(static s => s.SourceText)
            .Should().Contain(static s => s.Contains("PingQuery.Configure()"));
    }

    [Fact]
    public void When_the_app_is_in_the_global_namespace_too_then_every_generator_compiles()
    {
        var result = CompileWithEveryGenerator(GlobalTypes, GlobalApp);

        ErrorsOf(result).Should().BeEmpty();
    }

    [Fact]
    public void When_a_global_and_a_namespaced_operation_share_a_name_then_their_hint_names_differ()
    {
        // Hint names only: a hint name used twice throws. The generated code does not compile, and not because of the
        // global namespace: pipeline classes and typed-client methods are named after the operation alone, so two
        // same-named operations collide there whatever their namespaces.
        var result = TestHelpers.CompileWithTrackedGenerators(
            [
                TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<AppDataModelSourceGenerator>(),
            ],
            [
                GlobalTypes,
                NamespacedApp,
                """
                using Blueprint.Model;
                using System.Threading.Tasks;

                namespace TestNamespace;

                public partial class PingQuery : IQuery<string>
                {
                    public static Task<string> InvokeAsync()
                    {
                        return Task.FromResult("namespaced");
                    }
                }
                """,
            ],
            ["Input0.cs", "Input1.cs", "Input2.cs"]);

        result.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator))
            .Select(static s => s.HintName)
            .Should().Contain(["PingQuery_Descriptor.g.cs", "TestNamespace.PingQuery_Descriptor.g.cs"]);

        result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator))
            .Select(static s => s.HintName)
            .Should().Contain(["PingQueryExecutorPipeline.g.cs", "TestNamespace.PingQueryExecutorPipeline.g.cs"]);
    }

    private static MultiGeneratorGenerationResult CompileWithEveryGenerator(params string[] sources)
    {
        // As a consumer's build runs them: the package brings every generator.
        return TestHelpers.CompileWithTrackedGenerators(
            [
                TestHelpers.CreateTrackedGenerator<ApiResourceSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<ApiExceptionStatusIndexGenerator>(),
                TestHelpers.CreateTrackedGenerator<ResourceEventFamilySourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<AppDataModelSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<TypedHttpClientExtensionsGenerator>(),
            ],
            sources,
            sources.Select(static (_, i) => $"Input{i}.cs").ToArray());
    }

    private static IEnumerable<string> ErrorsOf(MultiGeneratorGenerationResult result)
    {
        return result.Diagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => $"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}");
    }
}
