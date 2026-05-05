using Blueprint.SourceGenerator.Modules.BackgroundTaskFlush;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.BackgroundTaskFlush;

/// <summary>
/// Tests that <see cref="BackgroundTaskFlushManifestModule"/> contributes
/// <c>backgroundTasks.flushEnabled</c> to the per-operation debug manifest.
/// </summary>
public class BackgroundTaskFlushManifestModuleTests
{
    [Fact]
    public void When_any_operation_then_flush_enabled_is_true()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class GetItemQuery : IQuery<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new BackgroundTaskFlushManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.GetItemQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("backgroundTasks.flushEnabled");
        result.Value.Entries["backgroundTasks.flushEnabled"].ToString().Should().Be("True");
    }

    [Fact]
    public void When_command_then_flush_enabled_is_true()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class CreateItemCommand : ICommand<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new BackgroundTaskFlushManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.CreateItemCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["backgroundTasks.flushEnabled"].ToString().Should().Be("True");
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
