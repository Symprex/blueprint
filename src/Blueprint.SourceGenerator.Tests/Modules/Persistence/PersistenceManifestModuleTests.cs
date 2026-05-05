using Blueprint.SourceGenerator.Modules.Persistence;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.Persistence;

/// <summary>
/// Tests that <see cref="PersistenceManifestModule"/> contributes <c>persistence.saveChangesEnabled</c>
/// to the per-operation debug manifest, reflecting whether UnitOfWork save-changes is active.
/// </summary>
public class PersistenceManifestModuleTests
{
    [Fact]
    public void When_command_then_persistence_enabled()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class CreateItemCommand : ICommand<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new PersistenceManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.CreateItemCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("persistence.saveChangesEnabled");
        result.Value.Entries["persistence.saveChangesEnabled"].ToString().Should().Be("True");
    }

    [Fact]
    public void When_query_then_persistence_disabled()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class GetItemQuery : IQuery<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new PersistenceManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.GetItemQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["persistence.saveChangesEnabled"].ToString().Should().Be("False");
    }

    [Fact]
    public void When_command_with_DoNotSaveChanges_then_persistence_disabled()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Persistence;

            namespace TestNamespace;

            [DoNotSaveChanges]
            public partial class FireAndForgetCommand : ICommand<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new PersistenceManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.FireAndForgetCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["persistence.saveChangesEnabled"].ToString().Should().Be("False");
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));

        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
