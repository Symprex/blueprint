using Blueprint.SourceGenerator.Generators.ApiResources;
using Blueprint.SourceGenerator.Generators.App;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Blueprint.SourceGenerator.Tests.Generators;

/// <summary>
/// Proves a consumer can suppress one occurrence of a generator diagnostic in source, which needs the diagnostic to be
/// reported against the syntax tree it came from rather than a detached file location.
/// </summary>
public class DiagnosticSuppressionTests
{
    // Two operations whose [Resource] fallback names a property neither has, so each reports BPAU010. Only the first is
    // wrapped in a #pragma, so the second shows the suppression is scoped to that occurrence.
    private const string PragmaSource =
        """
        using Blueprint.Model;
        using Blueprint.Model.Attributes;
        using System.Threading.Tasks;

        namespace TestNamespace;

        #pragma warning disable BPAU010
        [RootLink("/suppressed/{TenantId}")]
        [Resource("tenant", "read", "/Tenants/{TenantId:MissingOnSuppressed}")]
        public partial class SuppressedQuery : IQuery<string>
        {
            public string TenantId { get; set; }

            public static Task<string> HandleAsync(SuppressedQuery operation)
            {
                return Task.FromResult("ok");
            }
        }
        #pragma warning restore BPAU010

        [RootLink("/reported/{TenantId}")]
        [Resource("tenant", "read", "/Tenants/{TenantId:MissingOnReported}")]
        public partial class ReportedQuery : IQuery<string>
        {
            public string TenantId { get; set; }

            public static Task<string> HandleAsync(ReportedQuery operation)
            {
                return Task.FromResult("ok");
            }
        }
        """;

    private static readonly DiagnosticDescriptor Descriptor = new(
        "TEST001",
        "Test",
        "Test",
        "Test",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    [Fact]
    public void When_a_pragma_disables_a_generator_warning_then_only_that_occurrence_is_suppressed()
    {
        // The pipeline generator reports BPAU010, and its output already reads the compilation.
        var result = TestHelpers.Compile<OperationExecutorPipelineSourceGenerator>(
            PragmaSource + "\n\n[BlueprintAppScan]\npublic partial class TestBlueprintApp : BlueprintApp\n{\n}\n",
            assertCacheability: false);

        ReportedMessages(result.Diagnostics, "BPAU010")
            .Should().ContainSingle()
            .Which.Should().Contain("MissingOnReported");
    }

    [Fact]
    public void When_a_pragma_disables_a_warning_from_a_cached_generator_then_only_that_occurrence_is_suppressed()
    {
        // The API resource generator reads syntax only, so its diagnostics are bound to the tree by a separate output;
        // the harness's second run proves that output still replays from cache.
        var result = TestHelpers.Compile<ApiResourceSourceGenerator>(
            """
            using Blueprint.Model;

            #pragma warning disable BP001
            public class SuppressedResource : ApiResource
            {
            }
            #pragma warning restore BP001

            public class ReportedResource : ApiResource
            {
            }
            """);

        ReportedMessages(result.Diagnostics, "BP001")
            .Should().ContainSingle()
            .Which.Should().Contain("ReportedResource");
    }

    [Fact]
    public void When_a_pragma_is_added_without_moving_the_diagnostic_then_the_cached_diagnostic_is_not_replayed()
    {
        // The pragma replaces a comment of the same length, so the model and its spans are unchanged: only the tree
        // differs, and the diagnostic must be reported against the new one.
        const string Pragma = "#pragma warning disable BP001";
        const string Template =
            """
            using Blueprint.Model;

            {0}
            public class ReportedResource : ApiResource
            {{
            }}
            """;

        var result = TestHelpers.CompileWithTrackedGeneratorsAfterEdit(
            [TestHelpers.CreateTrackedGenerator<ApiResourceSourceGenerator>()],
            [string.Format(Template, "//" + new string('x', Pragma.Length - 2))],
            [string.Format(Template, Pragma)],
            ["Resources.cs"]);

        ReportedMessages(result.Initial.Diagnostics, "BP001").Should().ContainSingle();
        ReportedMessages(result.Updated.Diagnostics, "BP001").Should().BeEmpty();
    }

    [Fact]
    public void When_a_pragma_disables_a_generator_warning_then_every_generator_reporting_it_respects_the_pragma()
    {
        // As a consumer's build runs them, with every generator that runs the authorisation module.
        var result = TestHelpers.CompileWithTrackedGenerators(
            [
                TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<AppDataModelSourceGenerator>(),
            ],
            [
                PragmaSource,
                """
                using Blueprint.Model;

                namespace TestNamespace;

                [BlueprintAppScan]
                public partial class TestBlueprintApp : BlueprintApp
                {
                }
                """,
            ],
            ["Operations.cs", "App.cs"]);

        ReportedMessages(result.Diagnostics, "BPAU010")
            .Should().NotBeEmpty()
            .And.OnlyContain(static message => message.Contains("MissingOnReported"));
    }

    [Fact]
    public void When_a_diagnostic_location_matches_a_syntax_tree_then_it_is_reported_against_that_tree()
    {
        var tree = CSharpSyntaxTree.ParseText("public class Foo { }", path: "Foo.cs");
        var compilation = CSharpCompilation.Create("Tests", [tree]);
        var span = new TextSpan(13, 3);
        var diagnosticInfo = new DiagnosticInfo(Descriptor, new LocationInfo("Foo.cs", span, tree.GetLineSpan(span).Span));

        var diagnostic = diagnosticInfo.ToDiagnostic(compilation);

        diagnostic.Location.SourceTree.Should().BeSameAs(tree);
        diagnostic.Location.SourceSpan.Should().Be(span);
    }

    [Fact]
    public void When_a_diagnostic_location_matches_no_syntax_tree_then_it_is_reported_as_an_external_location()
    {
        // A metadata location keeps the module name as its path, which no tree in the compilation has.
        var compilation = CSharpCompilation.Create("Tests", [CSharpSyntaxTree.ParseText("public class Foo { }", path: "Foo.cs")]);
        var lineSpan = new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 3));
        var diagnosticInfo = new DiagnosticInfo(Descriptor, new LocationInfo("Contract.dll", new TextSpan(0, 3), lineSpan));

        var diagnostic = diagnosticInfo.ToDiagnostic(compilation);

        diagnostic.Location.Kind.Should().Be(LocationKind.ExternalFile);
        diagnostic.Location.GetLineSpan().Path.Should().Be("Contract.dll");
        diagnostic.Location.GetLineSpan().Span.Should().Be(lineSpan);
    }

    [Fact]
    public void When_a_diagnostic_span_runs_past_the_matching_tree_then_it_is_reported_as_an_external_location()
    {
        // The tree changed under a cached location: binding it would point past the end of the file.
        var compilation = CSharpCompilation.Create("Tests", [CSharpSyntaxTree.ParseText("class A { }", path: "Foo.cs")]);
        var lineSpan = new LinePositionSpan(new LinePosition(0, 40), new LinePosition(0, 45));
        var diagnosticInfo = new DiagnosticInfo(Descriptor, new LocationInfo("Foo.cs", new TextSpan(40, 5), lineSpan));

        var diagnostic = diagnosticInfo.ToDiagnostic(compilation);

        diagnostic.Location.Kind.Should().Be(LocationKind.ExternalFile);
        diagnostic.Location.GetLineSpan().Span.Should().Be(lineSpan);
    }

    private static IEnumerable<string> ReportedMessages(IEnumerable<Diagnostic> diagnostics, string id)
    {
        // A pragma may remove the diagnostic or keep it marked as suppressed; either way it must not be reported.
        return diagnostics
            .Where(d => d.Id == id && !d.IsSuppressed)
            .Select(static d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
    }
}
