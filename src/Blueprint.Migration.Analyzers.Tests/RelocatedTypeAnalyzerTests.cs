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
/// Tests for <see cref="Analyzers.RelocatedTypeAnalyzer"/> (BPMIG402) and its companion
/// <see cref="CodeFixes.RelocatedTypeCodeFixProvider"/> that adds missing using directives
/// for Blueprint types that moved from the root namespace to sub-namespaces.
/// </summary>
public sealed class RelocatedTypeAnalyzerTests
{
    [Xunit.Fact]
    public async Task When_file_uses_ApiOperationContext_with_only_root_using_then_reports_bpmig402()
    {
        const string source = """
            namespace Blueprint
            {
                public class ApiOperationContext { }
            }

            namespace TestConsumer
            {
                using Blueprint;

                public class MyHandler
                {
                    public void Handle(ApiOperationContext context) { }
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static d => d.Id == "BPMIG402");
        diagnostics.Single(static d => d.Id == "BPMIG402")
            .GetMessage().Should().Contain("Blueprint.Runtime.Exec");
    }

    [Xunit.Fact]
    public async Task When_file_uses_ApiOperationDescriptor_with_only_root_using_then_reports_bpmig402()
    {
        const string source = """
            namespace Blueprint
            {
                public class ApiOperationDescriptor { }
            }

            namespace TestConsumer
            {
                using Blueprint;

                public class MyHandler
                {
                    public void Handle(ApiOperationDescriptor descriptor) { }
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static d => d.Id == "BPMIG402");
        diagnostics.Single(static d => d.Id == "BPMIG402")
            .GetMessage().Should().Contain("Blueprint.Model");
    }

    [Xunit.Fact]
    public async Task When_file_uses_IApiAuthoriser_with_only_root_using_then_reports_bpmig402_for_authorization()
    {
        const string source = """
            namespace Blueprint
            {
                public interface IApiAuthoriser { }
            }

            namespace TestConsumer
            {
                using Blueprint;

                public class MyAuthoriser : IApiAuthoriser { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static d => d.Id == "BPMIG402");
        diagnostics.Single(static d => d.Id == "BPMIG402")
            .GetMessage().Should().Contain("Blueprint.Authorization");
    }

    [Xunit.Fact]
    public async Task When_file_already_has_correct_using_then_no_diagnostic()
    {
        const string source = """
            namespace Blueprint
            {
                public class ApiOperationContext { }
            }

            namespace Blueprint.Runtime.Exec
            {
                public class ApiOperationContext { }
            }

            namespace TestConsumer
            {
                using Blueprint;
                using Blueprint.Runtime.Exec;

                public class MyHandler
                {
                    public void Handle(ApiOperationContext context) { }
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG402").Should().BeEmpty();
    }

    [Xunit.Fact]
    public async Task When_file_has_no_blueprint_using_at_all_then_no_diagnostic()
    {
        const string source = """
            namespace TestConsumer
            {
                public class ApiOperationContext { }

                public class MyHandler
                {
                    public void Handle(ApiOperationContext context) { }
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG402").Should().BeEmpty();
    }

    [Xunit.Fact]
    public async Task When_file_uses_multiple_relocated_types_from_same_namespace_then_reports_one_diagnostic()
    {
        const string source = """
            namespace Blueprint
            {
                public interface IApiAuthoriser { }
                public class ExecutionAllowed { }
                public interface IUserAuthorisationContext { }
            }

            namespace TestConsumer
            {
                using Blueprint;

                public class MyAuth
                {
                    public IApiAuthoriser Authoriser { get; set; }
                    public ExecutionAllowed Allowed { get; set; }
                    public IUserAuthorisationContext Context { get; set; }
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        // All three types map to Blueprint.Authorization — should be one diagnostic
        diagnostics.Where(static d => d.Id == "BPMIG402").Should().HaveCount(1);
        diagnostics.Single(static d => d.Id == "BPMIG402")
            .GetMessage().Should().Contain("Blueprint.Authorization");
    }

    [Xunit.Fact]
    public async Task When_file_uses_types_from_multiple_namespaces_then_reports_one_per_namespace()
    {
        const string source = """
            namespace Blueprint
            {
                public class ApiOperationContext { }
                public class ApiOperationDescriptor { }
                public interface IApiAuthoriser { }
            }

            namespace TestConsumer
            {
                using Blueprint;

                public class MyHandler
                {
                    public void Handle(ApiOperationContext ctx, ApiOperationDescriptor desc, IApiAuthoriser auth) { }
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        var bpmig402 = diagnostics.Where(static d => d.Id == "BPMIG402").ToList();
        bpmig402.Should().HaveCount(3); // Runtime.Exec, Model, Authorization
        bpmig402.Select(static d => d.GetMessage())
            .Should().Contain(static m => m.Contains("Blueprint.Runtime.Exec"))
            .And.Contain(static m => m.Contains("Blueprint.Model"))
            .And.Contain(static m => m.Contains("Blueprint.Authorization"));
    }

    [Xunit.Fact]
    public async Task When_identifier_matches_but_is_local_type_then_no_diagnostic()
    {
        const string source = """
            namespace Blueprint
            {
            }

            namespace TestConsumer
            {
                using Blueprint;

                public class ApiOperationContext { }

                public class MyHandler
                {
                    public void Handle(ApiOperationContext context) { }
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        // The identifier resolves to the local type, not to Blueprint namespace
        diagnostics.Where(static d => d.Id == "BPMIG402").Should().BeEmpty();
    }

    [Xunit.Fact]
    public async Task CodeFix_adds_using_directive_for_relocated_type()
    {
        const string source = """
            using Blueprint;

            namespace Blueprint
            {
                public class ApiOperationContext { }
            }

            namespace TestConsumer
            {
                public class MyHandler
                {
                    public void Handle(ApiOperationContext context) { }
                }
            }
            """;

        var fixedSource = await ApplyCodeFixAsync(source, "BPMIG402");

        fixedSource.Should().Contain("using Blueprint.Runtime.Exec;");
    }

    [Xunit.Fact]
    public async Task CodeFix_inserts_using_after_existing_blueprint_usings()
    {
        const string source = """
            using System;
            using Blueprint;
            using Blueprint.Model;

            namespace Blueprint
            {
                public class ApiOperationContext { }
            }

            namespace Blueprint.Model
            {
                public class Foo { }
            }

            namespace TestConsumer
            {
                public class MyHandler
                {
                    public void Handle(ApiOperationContext context) { }
                }
            }
            """;

        var fixedSource = await ApplyCodeFixAsync(source, "BPMIG402");

        fixedSource.Should().Contain("using Blueprint.Runtime.Exec;");

        // The new using should appear after the existing Blueprint.Model using
        var modelIndex = fixedSource.IndexOf("using Blueprint.Model;", StringComparison.Ordinal);
        var runtimeExecIndex = fixedSource.IndexOf("using Blueprint.Runtime.Exec;", StringComparison.Ordinal);
        runtimeExecIndex.Should().BeGreaterThan(modelIndex);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            assemblyName: "RelocatedTypeTests",
            syntaxTrees: [syntaxTree],
            references: AppDomain.CurrentDomain.GetAssemblies()
                .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location)),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new Analyzers.RelocatedTypeAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static async Task<string> ApplyCodeFixAsync(string source, string diagnosticId)
    {
        var projectId = ProjectId.CreateNewId("RelocatedTypeCodeFixTests");
        var documentId = DocumentId.CreateNewId(projectId, "RelocatedTypeCodeFixTests.cs");
        var solution = new AdhocWorkspace()
            .CurrentSolution
            .AddProject(projectId, "RelocatedTypeCodeFixTests", "RelocatedTypeCodeFixTests", LanguageNames.CSharp)
            .WithProjectParseOptions(projectId, new CSharpParseOptions(LanguageVersion.Preview))
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithProjectMetadataReferences(projectId, AppDomain.CurrentDomain.GetAssemblies()
                .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location)))
            .AddDocument(documentId, "RelocatedTypeCodeFixTests.cs", SourceText.From(source));

        var document = solution.GetDocument(documentId)!;
        var diagnostics = await GetWorkspaceDiagnosticsAsync(document);
        var diagnostic = diagnostics.Single(candidate => candidate.Id == diagnosticId);

        var codeFixProvider = typeof(Blueprint.Migration.Analyzers.CodeFixes.RelocatedTypeCodeFixProvider).Assembly
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
