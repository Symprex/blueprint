using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules;

public class DebugManifestContributorCompatibilityTests
{
    [Fact]
    public void When_multiple_debug_manifest_contributors_write_same_key_then_public_api_returns_diagnostic()
    {
        var compilation = CreateCompilation();
        var config = new BlueprintSourceGenerationConfig(
        [
            new TestDebugManifestModule("first-module", "first", "shared", "alpha"),
            new TestDebugManifestModule("second-module", "second", "shared", "beta"),
        ]);

        var result = config.BuildDebugManifest("TestNamespace.ListUsersQuery", compilation);

        result.Value.Should().BeNull();
        result.Diagnostics.Should().ContainSingle();
        var diagnostic = result.Diagnostics[0].ToDiagnostic(null);
        diagnostic.Id.Should().Be("BPMOD006");
        diagnostic.GetMessage().Should().ContainAll("shared", "first", "second", "TestNamespace.ListUsersQuery");
    }

    [Fact]
    public void When_multiple_debug_manifest_contributors_write_distinct_keys_then_public_api_composes_entries()
    {
        var compilation = CreateCompilation();
        var config = new BlueprintSourceGenerationConfig(
        [
            new TestDebugManifestModule("first-module", "first", "first-key", "alpha"),
            new TestDebugManifestModule("second-module", "second", "second-key", "beta"),
        ]);

        var result = config.BuildDebugManifest("TestNamespace.ListUsersQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["first-key"].ToString().Should().Be("alpha");
        result.Value.Entries["second-key"].ToString().Should().Be("beta");
    }

    private static CSharpCompilation CreateCompilation(string source = """
                                                             using Blueprint.Model;

                                                             namespace TestNamespace;

                                                             public partial class ListUsersQuery : IQuery<string>
                                                             {
                                                             }
                                                             """)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IQuery<>).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "DebugManifestContributorCompatibilityTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private sealed class TestDebugManifestModule(
        string moduleId,
        string contributorId,
        string key,
        string value,
        int order = 0)
        : IBlueprintSourceGeneratorModule, IDebugManifestContributor
    {
        public string ModuleId { get; } = moduleId;

        public BlueprintModuleCategory Category { get; } = BlueprintModuleCategory.Core;

        public string ContributorId { get; } = contributorId;

        public int Order { get; } = order;

        public bool IsUnique { get; } = false;

        public IReadOnlyList<string> DependsOn { get; } = [];

        public void ContributeToManifest(DebugManifestContributorContext context)
        {
            context.AddEntry(key, value);
        }
    }
}
