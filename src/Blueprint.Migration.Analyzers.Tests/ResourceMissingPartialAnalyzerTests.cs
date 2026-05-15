using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Blueprint.Migration.Analyzers.Tests;

/// <summary>
/// Tests for <see cref="Analyzers.ResourceMissingPartialAnalyzer"/> (BPMIG401) and the shared
/// <see cref="CodeFixes.MissingPartialCodeFixProvider"/> that auto-adds <c>partial</c> to
/// API resource classes.
/// </summary>
public sealed class ResourceMissingPartialAnalyzerTests
{
    private const string ApiResourceStub = """
        namespace Blueprint.Model
        {
            public abstract class ApiResource
            {
                public abstract string Object { get; }
            }
        }
        """;

    [Xunit.Fact]
    public async Task When_concrete_class_inherits_ApiResource_without_partial_then_reports_bpmig401()
    {
        var source = ApiResourceStub + """

            namespace TestConsumer
            {
                public class TemplateApiResource : Blueprint.Model.ApiResource
                {
                    public override string Object => "template";
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static d => d.Id == "BPMIG401");
        var diagnostic = diagnostics.Single(static d => d.Id == "BPMIG401");
        diagnostic.GetMessage().Should().Contain("TemplateApiResource");
        diagnostic.GetMessage().Should().Contain("ApiResource");
    }

    [Xunit.Fact]
    public async Task When_concrete_class_is_already_partial_then_no_bpmig401()
    {
        var source = ApiResourceStub + """

            namespace TestConsumer
            {
                public partial class TemplateApiResource : Blueprint.Model.ApiResource
                {
                    public override string Object => "template";
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG401").Should().BeEmpty();
    }

    [Xunit.Fact]
    public async Task When_abstract_class_inherits_ApiResource_then_no_bpmig401()
    {
        var source = ApiResourceStub + """

            namespace TestConsumer
            {
                public abstract class BaseResource : Blueprint.Model.ApiResource { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG401").Should().BeEmpty();
    }

    [Xunit.Fact]
    public async Task When_concrete_class_inherits_ApiResource_indirectly_without_partial_then_reports_bpmig401()
    {
        var source = ApiResourceStub + """

            namespace TestConsumer
            {
                public abstract class BaseResource : Blueprint.Model.ApiResource { }
                public class UserApiResource : BaseResource
                {
                    public override string Object => "user";
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static d => d.Id == "BPMIG401");
        diagnostics.Single(static d => d.Id == "BPMIG401")
            .GetMessage().Should().Contain("UserApiResource");
    }

    [Xunit.Fact]
    public async Task When_class_does_not_inherit_ApiResource_then_no_bpmig401()
    {
        var source = """
            namespace TestConsumer
            {
                public class SomeDto
                {
                    public string Name { get; set; }
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG401").Should().BeEmpty();
    }

    [Xunit.Fact]
    public async Task When_concrete_class_inherits_legacy_Blueprint_Http_ApiResource_without_partial_then_reports_bpmig401()
    {
        const string legacyStub = """
            namespace Blueprint.Http
            {
                public abstract class ApiResource { }
            }
            """;

        var source = legacyStub + """

            namespace TestConsumer
            {
                public class SentEmailApiResource : Blueprint.Http.ApiResource { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static d => d.Id == "BPMIG401");
        diagnostics.Single(static d => d.Id == "BPMIG401")
            .GetMessage().Should().Contain("SentEmailApiResource");
    }

    [Xunit.Fact]
    public async Task When_concrete_class_inherits_legacy_root_Blueprint_ApiResource_without_partial_then_reports_bpmig401()
    {
        const string legacyStub = """
            namespace Blueprint
            {
                public abstract class ApiResource { }
            }
            """;

        var source = legacyStub + """

            namespace TestConsumer
            {
                public class UserApiResource : Blueprint.ApiResource { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static d => d.Id == "BPMIG401");
        diagnostics.Single(static d => d.Id == "BPMIG401")
            .GetMessage().Should().Contain("UserApiResource");
    }

    [Xunit.Fact]
    public async Task Code_fix_adds_partial_to_resource_class()
    {
        var source = ApiResourceStub + """

            namespace TestConsumer
            {
                public class TemplateApiResource : Blueprint.Model.ApiResource
                {
                    public override string Object => "template";
                }
            }
            """;

        var fixedSource = await ApplyCodeFixAsync(source, "BPMIG401");

        fixedSource.Should().Contain("public partial class TemplateApiResource");
    }

    [Xunit.Fact]
    public async Task Code_fix_adds_partial_to_sealed_resource_class()
    {
        var source = ApiResourceStub + """

            namespace TestConsumer
            {
                public sealed class ReadOnlyApiResource : Blueprint.Model.ApiResource
                {
                    public override string Object => "readonly";
                }
            }
            """;

        var fixedSource = await ApplyCodeFixAsync(source, "BPMIG401");

        fixedSource.Should().Contain("partial class ReadOnlyApiResource");
        fixedSource.Should().Contain("sealed");
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            assemblyName: "ResourceMissingPartialTests",
            syntaxTrees: [syntaxTree],
            references: AppDomain.CurrentDomain.GetAssemblies()
                .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location)),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new Analyzers.ResourceMissingPartialAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static async Task<string> ApplyCodeFixAsync(string source, string diagnosticId)
    {
        var projectId = ProjectId.CreateNewId("ResourceMissingPartialCodeFixTests");
        var documentId = DocumentId.CreateNewId(projectId, "ResourceMissingPartialCodeFixTests.cs");
        var solution = new AdhocWorkspace()
            .CurrentSolution
            .AddProject(projectId, "ResourceMissingPartialCodeFixTests", "ResourceMissingPartialCodeFixTests", LanguageNames.CSharp)
            .WithProjectParseOptions(projectId, new CSharpParseOptions(LanguageVersion.Preview))
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithProjectMetadataReferences(projectId, AppDomain.CurrentDomain.GetAssemblies()
                .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location)))
            .AddDocument(documentId, "ResourceMissingPartialCodeFixTests.cs", SourceText.From(source));

        var document = solution.GetDocument(documentId)!;
        var diagnostics = await GetWorkspaceDiagnosticsAsync(document);
        var diagnostic = diagnostics.Single(candidate => candidate.Id == diagnosticId);

        var codeFixProvider = typeof(Blueprint.Migration.Analyzers.CodeFixes.MissingPartialCodeFixProvider).Assembly
            .GetTypes()
            .Where(static type => !type.IsAbstract && typeof(CodeFixProvider).IsAssignableFrom(type))
            .Select(static type => (CodeFixProvider)Activator.CreateInstance(type)!)
            .SingleOrDefault(provider => provider.FixableDiagnosticIds.Contains(diagnosticId));

        if (codeFixProvider is null)
        {
            return source;
        }

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
        await codeFixProvider.RegisterCodeFixesAsync(context);

        if (actions.Count == 0)
        {
            return source;
        }

        var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        var applyChangesOperation = operations.OfType<ApplyChangesOperation>().Single();
        var updatedDocument = applyChangesOperation.ChangedSolution.GetDocument(document.Id)!;
        var updatedText = await updatedDocument.GetTextAsync(CancellationToken.None);
        return updatedText.ToString();
    }

    private static async Task<ImmutableArray<Diagnostic>> GetWorkspaceDiagnosticsAsync(Document document)
    {
        var compilation = await document.Project.GetCompilationAsync(CancellationToken.None);
        compilation.Should().NotBeNull();

        var analyzers = typeof(Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors).Assembly
            .GetTypes()
            .Where(static type => !type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            .Select(static type => (DiagnosticAnalyzer)Activator.CreateInstance(type)!)
            .ToImmutableArray();

        var compilationWithAnalyzers = compilation!.WithAnalyzers(analyzers);
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
