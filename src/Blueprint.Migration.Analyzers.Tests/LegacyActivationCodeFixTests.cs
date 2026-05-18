using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Blueprint.Migration.Analyzers.Tests;

public sealed class LegacyActivationCodeFixTests
{
    [Xunit.Fact]
    public async Task When_bpmig001_is_reported_then_code_fix_removes_parameterless_addopenapi_call_from_the_chain()
    {
        const string source = """
            namespace Legacy.Blueprint;

            public sealed class BlueprintApiBuilder
            {
            }

            public static class BlueprintApiBuilderExtensions
            {
                public static BlueprintApiBuilder AddOpenApi(this BlueprintApiBuilder pipelineBuilder)
                    => pipelineBuilder;

                public static BlueprintApiBuilder AddHateoasLinks(this BlueprintApiBuilder pipelineBuilder)
                    => pipelineBuilder;
            }

            public static class ConsumerConfiguration
            {
                public static BlueprintApiBuilder Configure(BlueprintApiBuilder builder)
                {
                    return builder
                        .AddOpenApi()
                        .AddHateoasLinks();
                }
            }
            """;

        var fixedSource = await ApplyCodeFixAsync(source, "BPMIG001");

        fixedSource.Should().NotContain("AddOpenApi()");
        fixedSource.Should().Contain("return builder");
        fixedSource.Should().Contain(".AddHateoasLinks();");
    }

    [Xunit.Fact]
    public async Task When_bpmig001_is_reported_on_a_standalone_statement_then_code_fix_removes_the_statement_instead_of_leaving_a_bare_expression()
    {
        const string source = """
            namespace Legacy.Blueprint;

            public sealed class BlueprintApiBuilder
            {
            }

            public static class BlueprintApiBuilderExtensions
            {
                public static BlueprintApiBuilder AddOpenApi(this BlueprintApiBuilder pipelineBuilder)
                    => pipelineBuilder;
            }

            public static class ConsumerConfiguration
            {
                public static void Configure(BlueprintApiBuilder builder)
                {
                    builder.AddOpenApi();
                }
            }
            """;

        var fixedSource = await ApplyCodeFixAsync(source, "BPMIG001");

        fixedSource.Should().NotContain("AddOpenApi()");
        fixedSource.Should().NotContain("builder;");
    }

    [Xunit.Fact]
    public async Task When_bpmig002_is_reported_then_code_fix_removes_parameterless_addhateoaslinks_call_from_the_chain()
    {
        const string source = """
            namespace Legacy.Blueprint;

            public sealed class BlueprintApiBuilder
            {
            }

            public static class BlueprintApiBuilderExtensions
            {
                public static BlueprintApiBuilder AddOpenApi(this BlueprintApiBuilder pipelineBuilder)
                    => pipelineBuilder;

                public static BlueprintApiBuilder AddHateoasLinks(this BlueprintApiBuilder pipelineBuilder)
                    => pipelineBuilder;
            }

            public static class ConsumerConfiguration
            {
                public static BlueprintApiBuilder Configure(BlueprintApiBuilder builder)
                {
                    return builder
                        .AddOpenApi()
                        .AddHateoasLinks();
                }
            }
            """;

        var fixedSource = await ApplyCodeFixAsync(source, "BPMIG002");

        fixedSource.Should().NotContain("AddHateoasLinks()");
        fixedSource.Should().Contain("return builder");
        fixedSource.Should().Contain(".AddOpenApi()");
    }

    [Xunit.Fact]
    public async Task When_bpmig002_is_reported_on_a_standalone_statement_then_code_fix_removes_the_statement()
    {
        const string source = """
            namespace Legacy.Blueprint;

            public sealed class BlueprintApiBuilder
            {
            }

            public static class BlueprintApiBuilderExtensions
            {
                public static BlueprintApiBuilder AddHateoasLinks(this BlueprintApiBuilder pipelineBuilder)
                    => pipelineBuilder;
            }

            public static class ConsumerConfiguration
            {
                public static void Configure(BlueprintApiBuilder builder)
                {
                    builder.AddHateoasLinks();
                }
            }
            """;

        var fixedSource = await ApplyCodeFixAsync(source, "BPMIG002");

        fixedSource.Should().NotContain("AddHateoasLinks()");
        fixedSource.Should().NotContain("builder;");
    }

    [Xunit.Fact]
    public async Task When_bpmig003_is_reported_then_code_fix_removes_parameterless_addauthorisation_call_from_the_chain()
    {
        const string source = """
            namespace Legacy.Blueprint;

            public sealed class BlueprintApiBuilder
            {
            }

            public static class BlueprintApiBuilderExtensions
            {
                public static BlueprintApiBuilder AddAuthorisation(this BlueprintApiBuilder pipelineBuilder)
                    => pipelineBuilder;

                public static BlueprintApiBuilder AddOpenApi(this BlueprintApiBuilder pipelineBuilder)
                    => pipelineBuilder;
            }

            public static class ConsumerConfiguration
            {
                public static BlueprintApiBuilder Configure(BlueprintApiBuilder builder)
                {
                    return builder
                        .AddAuthorisation()
                        .AddOpenApi();
                }
            }
            """;

        var fixedSource = await ApplyCodeFixAsync(source, "BPMIG003");

        fixedSource.Should().NotContain("AddAuthorisation()");
        fixedSource.Should().Contain("return builder");
        fixedSource.Should().Contain(".AddOpenApi()");
    }

    [Xunit.Fact]
    public async Task When_bpmig003_is_reported_on_a_standalone_statement_then_code_fix_removes_the_statement()
    {
        const string source = """
            namespace Legacy.Blueprint;

            public sealed class BlueprintApiBuilder
            {
            }

            public static class BlueprintApiBuilderExtensions
            {
                public static BlueprintApiBuilder AddAuthorisation(this BlueprintApiBuilder pipelineBuilder)
                    => pipelineBuilder;
            }

            public static class ConsumerConfiguration
            {
                public static void Configure(BlueprintApiBuilder builder)
                {
                    builder.AddAuthorisation();
                }
            }
            """;

        var fixedSource = await ApplyCodeFixAsync(source, "BPMIG003");

        fixedSource.Should().NotContain("AddAuthorisation()");
        fixedSource.Should().NotContain("builder;");
    }

    private static async Task<string> ApplyCodeFixAsync(string source, string diagnosticId)
    {
        var document = CreateDocument(source);
        var diagnostics = await GetDiagnosticsAsync(document);
        var diagnostic = diagnostics.Single(candidate => candidate.Id == diagnosticId);

        var codeFixProvider = typeof(Blueprint.Migration.Analyzers.CodeFixes.LegacyActivationCodeFixProvider).Assembly
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

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(Document document)
    {
        var compilation = await document.Project.GetCompilationAsync(CancellationToken.None);
        compilation.Should().NotBeNull();

        var analyzers = typeof(Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors).Assembly
            .GetTypes()
            .Where(static type => !type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            .Select(static type => (DiagnosticAnalyzer)Activator.CreateInstance(type)!)
            .ToImmutableArray();

        analyzers.Should().NotBeEmpty();

        var compilationWithAnalyzers = compilation!.WithAnalyzers(analyzers);
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    [Xunit.Fact]
    public async Task When_bpmig200_is_reported_then_code_fix_removes_the_resource_key_template_argument()
    {
        const string source = """
            namespace Legacy.Blueprint;

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class FromResourceAttribute : System.Attribute
            {
                public FromResourceAttribute(System.Type operationType, string resourceKeyTemplate) { }
            }

            public sealed class AccountLookupQuery { }

            [FromResource(typeof(AccountLookupQuery), "/Tenants/{Auth:AccountId}/Accounts")]
            public sealed class AccountCommand { }
            """;

        var fixedSource = await ApplyCodeFixAsync(source, "BPMIG200");

        fixedSource.Should().Contain("[FromResource(typeof(AccountLookupQuery))]");
        fixedSource.Should().NotContain("{Auth:AccountId}");
        fixedSource.Should().NotContain("/Tenants/");
    }

    [Xunit.Fact]
    public async Task When_bpmig200_is_reported_on_fromresource_with_only_one_argument_then_code_fix_does_not_change_the_attribute()
    {
        const string source = """
            namespace Legacy.Blueprint;

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class FromResourceAttribute : System.Attribute
            {
                public FromResourceAttribute(System.Type operationType, string resourceKeyTemplate) { }
                public FromResourceAttribute(System.Type operationType) { }
            }

            public sealed class AccountLookupQuery { }

            [FromResource(typeof(AccountLookupQuery))]
            public sealed class AccountCommand { }
            """;

        // The diagnostic should not even be raised by the code fix for single-arg usage,
        // but if it is, the fix provider requires ≥2 args, so nothing should change.
        var diagnostics = await GetDiagnosticsAsync(CreateDocument(source));
        diagnostics.Any(static d => d.Id == "BPMIG200").Should().BeTrue("the analyzer detects FromResource by name");

        var fixedSource = await ApplyCodeFixAsync(source, "BPMIG200");
        fixedSource.Should().Contain("[FromResource(typeof(AccountLookupQuery))]");
    }

    [Xunit.Fact]
    public async Task When_bpmig201_is_reported_on_resource_attribute_then_code_fix_migrates_auth_colon_to_dot()
    {
        const string source = """
            namespace Legacy.Blueprint;

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ResourceAttribute : System.Attribute
            {
                public ResourceAttribute(string noun, string verb, string resourceKeyTemplate) { }
            }

            [Resource("Account", "Read", "/Tenants/{Auth:AccountId}/Accounts")]
            public sealed class GetAccountQuery { }
            """;

        var fixedSource = await ApplyCodeFixAsync(source, "BPMIG201");

        fixedSource.Should().Contain("/Tenants/{Auth.AccountId}/Accounts");
        fixedSource.Should().NotContain("{Auth:AccountId}");
    }

    [Xunit.Fact]
    public async Task When_bpmig201_is_reported_on_fromresource_attribute_then_code_fix_migrates_auth_colon_to_dot()
    {
        const string source = """
            namespace Legacy.Blueprint;

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class FromResourceAttribute : System.Attribute
            {
                public FromResourceAttribute(System.Type operationType, string resourceKeyTemplate) { }
            }

            public sealed class AccountLookupQuery { }

            [FromResource(typeof(AccountLookupQuery), "/Tenants/{Auth:AccountId}/Accounts")]
            public sealed class AccountCommand { }
            """;

        var fixedSource = await ApplyCodeFixAsync(source, "BPMIG201");

        fixedSource.Should().Contain("/Tenants/{Auth.AccountId}/Accounts");
        fixedSource.Should().NotContain("{Auth:AccountId}");
    }

    private static Document CreateDocument(string source)
    {
        var projectId = ProjectId.CreateNewId("LegacyActivationCodeFixTests");
        var documentId = DocumentId.CreateNewId(projectId, "LegacyActivationCodeFixTests.cs");
        var solution = new AdhocWorkspace()
            .CurrentSolution
            .AddProject(projectId, "LegacyActivationCodeFixTests", "LegacyActivationCodeFixTests", LanguageNames.CSharp)
            .WithProjectParseOptions(projectId, new CSharpParseOptions(LanguageVersion.Preview))
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithProjectMetadataReferences(projectId, AppDomain.CurrentDomain.GetAssemblies()
                .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location)))
            .AddDocument(documentId, "LegacyActivationCodeFixTests.cs", SourceText.From(source));

        return solution.GetDocument(documentId)!;
    }
}
