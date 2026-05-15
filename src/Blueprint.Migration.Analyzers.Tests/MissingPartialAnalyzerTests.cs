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
/// Tests for <see cref="Analyzers.MissingPartialAnalyzer"/> (BPMIG400) and its companion
/// <see cref="CodeFixes.MissingPartialCodeFixProvider"/> that auto-adds <c>partial</c> to
/// operation classes.
/// </summary>
public sealed class MissingPartialAnalyzerTests
{
    [Xunit.Fact]
    public async Task When_concrete_class_implements_IQuery_without_partial_then_reports_bpmig400()
    {
        const string source = """
            namespace Blueprint.Model
            {
                public interface IOperation { }
                public interface IQuery<T> : IOperation { }
            }

            namespace TestConsumer
            {
                public class GetUsersQuery : Blueprint.Model.IQuery<string> { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static d => d.Id == "BPMIG400");
        var diagnostic = diagnostics.Single(static d => d.Id == "BPMIG400");
        diagnostic.GetMessage().Should().Contain("GetUsersQuery");
        diagnostic.GetMessage().Should().Contain("IQuery");
    }

    [Xunit.Fact]
    public async Task When_concrete_class_implements_ICommand_without_partial_then_reports_bpmig400()
    {
        const string source = """
            namespace Blueprint.Model
            {
                public interface IOperation { }
                public interface ICommand<T> : IOperation { }
            }

            namespace TestConsumer
            {
                public class CreateUserCommand : Blueprint.Model.ICommand<string> { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static d => d.Id == "BPMIG400");
        diagnostics.Single(static d => d.Id == "BPMIG400")
            .GetMessage().Should().Contain("CreateUserCommand");
    }

    [Xunit.Fact]
    public async Task When_concrete_class_is_already_partial_then_no_bpmig400()
    {
        const string source = """
            namespace Blueprint.Model
            {
                public interface IOperation { }
                public interface IQuery<T> : IOperation { }
            }

            namespace TestConsumer
            {
                public partial class GetUsersQuery : Blueprint.Model.IQuery<string> { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG400").Should().BeEmpty();
    }

    [Xunit.Fact]
    public async Task When_abstract_class_implements_IQuery_without_partial_then_no_bpmig400()
    {
        const string source = """
            namespace Blueprint.Model
            {
                public interface IOperation { }
                public interface IQuery<T> : IOperation { }
            }

            namespace TestConsumer
            {
                public abstract class BaseQuery<T> : Blueprint.Model.IQuery<T> { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        // Abstract classes don't need partial — only concrete operations do
        diagnostics.Where(static d => d.Id == "BPMIG400").Should().BeEmpty();
    }

    [Xunit.Fact]
    public async Task When_concrete_class_inherits_IQuery_from_base_then_reports_bpmig400()
    {
        const string source = """
            namespace Blueprint.Model
            {
                public interface IOperation { }
                public interface IQuery<T> : IOperation { }
            }

            namespace TestConsumer
            {
                public abstract class BaseQuery<T> : Blueprint.Model.IQuery<T> { }
                public class GetUsersQuery : BaseQuery<string> { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        // Concrete subclass inherits IQuery<T> from base — still needs partial
        diagnostics.Should().ContainSingle(static d => d.Id == "BPMIG400");
        diagnostics.Single(static d => d.Id == "BPMIG400")
            .GetMessage().Should().Contain("GetUsersQuery");
    }

    [Xunit.Fact]
    public async Task When_class_implements_unrelated_interface_then_no_bpmig400()
    {
        const string source = """
            namespace TestConsumer
            {
                public interface IMyService<T> { }
                public class MyService : IMyService<string> { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG400").Should().BeEmpty();
    }

    [Xunit.Fact]
    public async Task When_concrete_class_implements_legacy_Blueprint_IQuery_without_partial_then_reports_bpmig400()
    {
        const string source = """
            namespace Blueprint
            {
                public interface IReturn<T> { }
                public interface IQuery<T> : IReturn<T> { }
            }

            namespace TestConsumer
            {
                public class GetUsersQuery : Blueprint.IQuery<string> { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static d => d.Id == "BPMIG400");
        diagnostics.Single(static d => d.Id == "BPMIG400")
            .GetMessage().Should().Contain("GetUsersQuery");
    }

    [Xunit.Fact]
    public async Task When_concrete_class_implements_legacy_Blueprint_ICommand_without_partial_then_reports_bpmig400()
    {
        const string source = """
            namespace Blueprint
            {
                public interface IReturn<T> { }
                public interface ICommand<T> : IReturn<T> { }
            }

            namespace TestConsumer
            {
                public class CreateUserCommand : Blueprint.ICommand<string> { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static d => d.Id == "BPMIG400");
        diagnostics.Single(static d => d.Id == "BPMIG400")
            .GetMessage().Should().Contain("CreateUserCommand");
    }

    [Xunit.Fact]
    public async Task Code_fix_adds_partial_to_class_declaration()
    {
        const string source = """
            namespace Blueprint.Model
            {
                public interface IOperation { }
                public interface IQuery<T> : IOperation { }
            }

            namespace TestConsumer
            {
                public class GetUsersQuery : Blueprint.Model.IQuery<string> { }
            }
            """;

        var fixedSource = await ApplyCodeFixAsync(source, "BPMIG400");

        fixedSource.Should().Contain("public partial class GetUsersQuery");
    }

    [Xunit.Fact]
    public async Task Code_fix_adds_partial_to_sealed_class_declaration()
    {
        const string source = """
            namespace Blueprint.Model
            {
                public interface IOperation { }
                public interface ICommand<T> : IOperation { }
            }

            namespace TestConsumer
            {
                public sealed class CreateUserCommand : Blueprint.Model.ICommand<string> { }
            }
            """;

        var fixedSource = await ApplyCodeFixAsync(source, "BPMIG400");

        fixedSource.Should().Contain("partial class CreateUserCommand");
        fixedSource.Should().Contain("sealed");
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            assemblyName: "MissingPartialTests",
            syntaxTrees: [syntaxTree],
            references: AppDomain.CurrentDomain.GetAssemblies()
                .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location)),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new Analyzers.MissingPartialAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static async Task<string> ApplyCodeFixAsync(string source, string diagnosticId)
    {
        var projectId = ProjectId.CreateNewId("MissingPartialCodeFixTests");
        var documentId = DocumentId.CreateNewId(projectId, "MissingPartialCodeFixTests.cs");
        var solution = new AdhocWorkspace()
            .CurrentSolution
            .AddProject(projectId, "MissingPartialCodeFixTests", "MissingPartialCodeFixTests", LanguageNames.CSharp)
            .WithProjectParseOptions(projectId, new CSharpParseOptions(LanguageVersion.Preview))
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithProjectMetadataReferences(projectId, AppDomain.CurrentDomain.GetAssemblies()
                .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location)))
            .AddDocument(documentId, "MissingPartialCodeFixTests.cs", SourceText.From(source));

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
