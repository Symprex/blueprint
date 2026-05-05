using Blueprint.SourceGenerator.Modules.Auditing;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.Auditing;

/// <summary>
/// Tests that <see cref="AuditingManifestModule"/> contributes <c>auditing.enabled</c> to the per-operation debug
/// manifest, reflecting whether the operation has opted out of audit recording via [DoNotAudit].
/// </summary>
public class AuditingManifestTests
{
    [Fact]
    public void When_operation_has_DoNotAudit_then_manifest_entry_is_false()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Auditing;
            using Blueprint.Model;

            namespace TestNamespace;

            [DoNotAudit]
            public partial class InternalQuery : IQuery<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new AuditingManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.InternalQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("auditing.enabled");
        result.Value.Entries["auditing.enabled"].ToString().Should().Be("False");
    }

    [Fact]
    public void When_normal_operation_then_manifest_entry_is_true()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class NormalQuery : IQuery<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new AuditingManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.NormalQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("auditing.enabled");
        result.Value.Entries["auditing.enabled"].ToString().Should().Be("True");
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
