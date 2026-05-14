using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Blueprint.SourceGenerator.CodeFixes.Tests;

/// <summary>
/// Tests for <see cref="AddPartialModifierCodeFixProvider"/>.
/// Uses synthetic diagnostics at the type declaration location since the actual diagnostics
/// come from the source generator (which runs in a different assembly context).
/// </summary>
public sealed class AddPartialModifierCodeFixProviderTests
{
    private readonly AddPartialModifierCodeFixProvider _provider = new();

    [Fact]
    public void FixableDiagnosticIds_should_include_all_partial_diagnostics()
    {
        this._provider.FixableDiagnosticIds.Should().BeEquivalentTo("BP001", "BP002", "BP005", "BP104");
    }

    [Fact]
    public void GetFixAllProvider_should_return_batch_fixer()
    {
        this._provider.GetFixAllProvider().Should().Be(WellKnownFixAllProviders.BatchFixer);
    }

    [Theory]
    [InlineData("BP001")]
    [InlineData("BP002")]
    [InlineData("BP005")]
    [InlineData("BP104")]
    public async Task When_class_is_not_partial_then_code_fix_adds_partial_modifier(string diagnosticId)
    {
        const string source = """
            public class MyOperation
            {
            }
            """;

        var fixedSource = await this.ApplyCodeFixAsync(source, diagnosticId, "MyOperation");

        fixedSource.Should().Contain("public partial class MyOperation");
    }

    [Fact]
    public async Task When_class_has_static_modifier_then_partial_is_added_alongside_static()
    {
        const string source = """
            public static class MyApp
            {
            }
            """;

        var fixedSource = await this.ApplyCodeFixAsync(source, "BP002", "MyApp");

        fixedSource.Should().Contain("public static partial class MyApp");
    }

    [Fact]
    public async Task When_class_has_sealed_modifier_then_partial_is_added_alongside_sealed()
    {
        const string source = """
            public sealed class GetUserQuery
            {
            }
            """;

        var fixedSource = await this.ApplyCodeFixAsync(source, "BP104", "GetUserQuery");

        fixedSource.Should().Contain("public sealed partial class GetUserQuery");
    }

    [Fact]
    public async Task When_class_has_internal_modifier_then_partial_is_added()
    {
        const string source = """
            internal class InternalOperation
            {
            }
            """;

        var fixedSource = await this.ApplyCodeFixAsync(source, "BP104", "InternalOperation");

        fixedSource.Should().Contain("internal partial class InternalOperation");
    }

    [Fact]
    public async Task When_record_is_not_partial_then_code_fix_adds_partial_modifier()
    {
        const string source = """
            public record UserCreated
            {
            }
            """;

        var fixedSource = await this.ApplyCodeFixAsync(source, "BP005", "UserCreated");

        fixedSource.Should().Contain("public partial record UserCreated");
    }

    [Fact]
    public async Task When_class_is_already_partial_then_no_code_fix_is_registered()
    {
        const string source = """
            public partial class AlreadyPartial
            {
            }
            """;

        var (document, diagnostic) = CreateDocumentWithDiagnostic(source, "BP001", "AlreadyPartial");
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await this._provider.RegisterCodeFixesAsync(context);

        actions.Should().BeEmpty();
    }

    [Fact]
    public async Task When_class_is_inside_namespace_then_partial_is_added_correctly()
    {
        const string source = """
            namespace MyApp.Operations
            {
                public class CreateTemplateCommand
                {
                }
            }
            """;

        var fixedSource = await this.ApplyCodeFixAsync(source, "BP104", "CreateTemplateCommand");

        fixedSource.Should().Contain("public partial class CreateTemplateCommand");
    }

    [Fact]
    public async Task When_class_is_inside_filescoped_namespace_then_partial_is_added_correctly()
    {
        const string source = """
            namespace MyApp.Operations;

            public class DeleteItemCommand
            {
            }
            """;

        var fixedSource = await this.ApplyCodeFixAsync(source, "BP104", "DeleteItemCommand");

        fixedSource.Should().Contain("public partial class DeleteItemCommand");
    }

    private async Task<string> ApplyCodeFixAsync(string source, string diagnosticId, string typeName)
    {
        var (document, diagnostic) = CreateDocumentWithDiagnostic(source, diagnosticId, typeName);

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await this._provider.RegisterCodeFixesAsync(context);

        actions.Should().NotBeEmpty($"expected a code fix for {diagnosticId} on '{typeName}'");

        var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        var applyChanges = operations.OfType<ApplyChangesOperation>().Single();
        var updatedDocument = applyChanges.ChangedSolution.GetDocument(document.Id)!;
        var updatedText = await updatedDocument.GetTextAsync(CancellationToken.None);
        return updatedText.ToString();
    }

    private static (Document Document, Diagnostic Diagnostic) CreateDocumentWithDiagnostic(
        string source,
        string diagnosticId,
        string typeName)
    {
        var projectId = ProjectId.CreateNewId("TestProject");
        var documentId = DocumentId.CreateNewId(projectId, "Test.cs");

        var solution = new AdhocWorkspace()
            .CurrentSolution
            .AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp)
            .WithProjectParseOptions(projectId, new CSharpParseOptions(LanguageVersion.Preview))
            .WithProjectCompilationOptions(
                projectId,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithProjectMetadataReferences(
                projectId,
                AppDomain.CurrentDomain.GetAssemblies()
                    .Where(static a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
                    .Select(static a => MetadataReference.CreateFromFile(a.Location)))
            .AddDocument(documentId, "Test.cs", SourceText.From(source));

        var document = solution.GetDocument(documentId)!;

        // Find the type declaration span to place the diagnostic on
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var typeDecl = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
            .First(t => t.Identifier.Text == typeName);

        var descriptor = new DiagnosticDescriptor(
            diagnosticId,
            "Class must be partial",
            "The class '{0}' must be partial",
            "Blueprint",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        var diagnostic = Diagnostic.Create(
            descriptor,
            Location.Create(document.FilePath ?? "Test.cs", typeDecl.Identifier.Span, typeDecl.Identifier.GetLocation().GetLineSpan().Span),
            typeName);

        return (document, diagnostic);
    }
}
