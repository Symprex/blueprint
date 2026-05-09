using System.Text;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Modules.Authentication;
using Blueprint.SourceGenerator.Modules.Authorization;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.Authorization;

/// <summary>
/// Tests the compile-time claims authorization checks produced by
/// <see cref="ClaimsAuthorizationModule"/>. Covers descriptor metadata,
/// pipeline plan contributions, diagnostic reports, and full pipeline snapshots.
/// </summary>
public class ClaimsAuthorizationModuleTests
{
    // ── Using directives shared across tests ─────────────────────────

    private const string Usings = """
        using Blueprint;
        using Blueprint.Model;
        using Blueprint.Model.Attributes;
        using Blueprint.Authorization;
        using System.Threading.Tasks;

        namespace TestNamespace;

        """;

    private const string AppClass = """

        [BlueprintAppScan]
        public partial class TestApp : BlueprintApp { }

        """;

    // ── Descriptor tests ─────────────────────────────────────────────

    [Fact]
    public void When_operation_has_Permission_then_descriptor_sets_RequiresAuthentication()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/users")]
            [Permission("ManageUsers")]
            public partial class ListUsersQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(ListUsersQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("ListUsersQueryDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("WellKnownFeatureData.RequiresAuthentication");
    }

    [Fact]
    public void When_operation_has_PermissionFor_then_descriptor_sets_RequiresAuthentication()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/tenants/{TenantId}/docs")]
            [PermissionFor("Edit", "Tenant/{TenantId}")]
            public partial class EditDocCommand : ICommand<string>
            {
                public string TenantId { get; set; }

                public static Task<string> HandleAsync(EditDocCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("EditDocCommandDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("WellKnownFeatureData.RequiresAuthentication");
    }

    [Fact]
    public void When_Permission_combined_with_AllowAnonymous_then_reports_BPAU008()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/public")]
            [AllowAnonymous]
            [Permission("ManageUsers")]
            public partial class PublicQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(PublicQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().Contain(
            static d => d.Id == "BPAU008",
            "[Permission] combined with [AllowAnonymous] should report BPAU008");
    }

    [Fact]
    public void When_no_claims_attributes_then_no_descriptor_contribution()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/items")]
            public partial class GetItemsQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("GetItemsQueryDescriptor"));

        descriptorSource.Should().NotBeNull();

        // No claims-related authentication requirement
        descriptorSource!.Should().NotContain("ClaimsAuthorization");
    }

    // ── Pipeline plan tests ──────────────────────────────────────────

    [Fact]
    public void When_Permission_on_http_operation_then_pipeline_has_authorisation_stage()
    {
        var compilation = CreateCompilation(
            Usings + """
            [RootLink("/users")]
            [Permission("ManageUsers")]
            public partial class ListUsersQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(ListUsersQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([
            new AuthenticationModule(),
            new AuthorizationModule(),
            new ClaimsAuthorizationModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.ListUsersQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.Authorisation, nameof(ClaimsAuthorizationModule)),
            "[Permission] operations get claims authorization check in the pipeline");
    }

    [Fact]
    public void When_PermissionFor_on_http_operation_then_pipeline_has_authorisation_stage()
    {
        var compilation = CreateCompilation(
            Usings + """
            [RootLink("/tenants/{TenantId}/docs")]
            [PermissionFor("Edit", "Tenant/{TenantId}")]
            public partial class EditDocCommand : ICommand<string>
            {
                public string TenantId { get; set; }

                public static Task<string> HandleAsync(EditDocCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([
            new AuthenticationModule(),
            new AuthorizationModule(),
            new ClaimsAuthorizationModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.EditDocCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.Authorisation, nameof(ClaimsAuthorizationModule)));
    }

    [Fact]
    public void When_Permission_on_non_http_operation_then_no_pipeline_contribution()
    {
        var compilation = CreateCompilation(
            Usings + """
            [Permission("ManageUsers")]
            public partial class ProcessCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(ProcessCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([
            new AuthenticationModule(),
            new AuthorizationModule(),
            new ClaimsAuthorizationModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.ProcessCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        result.Value!.Contributions.Should().NotContain(
            static c => c.ContributorId == nameof(ClaimsAuthorizationModule),
            "non-HTTP operations should not get inline claims checks");
    }

    [Fact]
    public void When_PermissionFor_template_references_missing_property_then_reports_BPAU009()
    {
        var compilation = CreateCompilation(
            Usings + """
            [RootLink("/tenants/{TenantId}")]
            [PermissionFor("Edit", "Tenant/{NonExistentProp}")]
            public partial class BadTemplateCommand : ICommand<string>
            {
                public string TenantId { get; set; }

                public static Task<string> HandleAsync(BadTemplateCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([
            new AuthenticationModule(),
            new AuthorizationModule(),
            new ClaimsAuthorizationModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.BadTemplateCommand", compilation);

        result.Diagnostics.Should().Contain(
            static d => d.Descriptor.Id == "BPAU009",
            "template token referencing missing property should report BPAU009");
    }

    [Fact]
    public void When_Permission_with_AllowAnonymous_then_no_pipeline_contribution()
    {
        var compilation = CreateCompilation(
            Usings + """
            [RootLink("/public")]
            [AllowAnonymous]
            [Permission("ManageUsers")]
            public partial class PublicQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(PublicQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([
            new AuthenticationModule(),
            new AuthorizationModule(),
            new ClaimsAuthorizationModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.PublicQuery", compilation);

        // No contributions from ClaimsAuthorizationModule
        result.Value?.Contributions.Should().NotContain(
                static c => c.ContributorId == nameof(ClaimsAuthorizationModule),
                "[AllowAnonymous] + [Permission] should suppress claims authorization");
    }

    [Fact]
    public void When_PermissionFor_with_Auth_token_then_pipeline_contribution_succeeds()
    {
        var compilation = CreateCompilation(
            Usings + """
            [RootLink("/tenants/{TenantId}/accounts/{Id}")]
            [PermissionFor("Edit", "Tenant/{Auth:AccountId}/Account/{Id}")]
            public partial class EditAccountCommand : ICommand<string>
            {
                public string TenantId { get; set; }
                public string Id { get; set; }

                public static Task<string> HandleAsync(EditAccountCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([
            new AuthenticationModule(),
            new AuthorizationModule(),
            new ClaimsAuthorizationModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.EditAccountCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        // {Auth:AccountId} is a claim token, not a property — should not cause BPAU009
        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.Authorisation, nameof(ClaimsAuthorizationModule)));
    }

    // ── Pipeline snapshot test ───────────────────────────────────────

    [Fact]
    public async Task When_Permission_then_pipeline_snapshot_matches()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Authorization;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/users")]
                [Permission("ManageUsers")]
                public partial class ListUsersQuery : IQuery<string>
                {
                    public static Task<string> HandleAsync(ListUsersQuery operation)
                    {
                        return Task.FromResult("ok");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(
            generators,
            [source],
            ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty("the compilation should have no diagnostics");

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        pipelineSources.Should().HaveCountGreaterOrEqualTo(1, "pipeline should be generated");

        var outputText = new StringBuilder();
        foreach (var s in pipelineSources.OrderBy(static s => s.HintName))
        {
            outputText.AppendLine(s.HintName + ":");
            outputText.AppendLine();
            outputText.AppendLine(s.SourceText);
            outputText.AppendLine();
        }

        await Verify(outputText)
            .UseDirectory("Snapshots/ClaimsAuthorization/");
    }

    [Fact]
    public async Task When_PermissionFor_with_template_then_pipeline_snapshot_matches()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Authorization;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/tenants/{TenantId}/docs/{DocId}")]
                [PermissionFor("Edit", "Tenant/{TenantId}/Document/{DocId}")]
                public partial class EditDocCommand : ICommand<string>
                {
                    public string TenantId { get; set; }
                    public string DocId { get; set; }

                    public static Task<string> HandleAsync(EditDocCommand operation)
                    {
                        return Task.FromResult("ok");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(
            generators,
            [source],
            ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty("the compilation should have no diagnostics");

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        pipelineSources.Should().HaveCountGreaterOrEqualTo(1, "pipeline should be generated");

        var outputText = new StringBuilder();
        foreach (var s in pipelineSources.OrderBy(static s => s.HintName))
        {
            outputText.AppendLine(s.HintName + ":");
            outputText.AppendLine();
            outputText.AppendLine(s.SourceText);
            outputText.AppendLine();
        }

        await Verify(outputText)
            .UseDirectory("Snapshots/ClaimsAuthorization/");
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static GenerationResult CompileDescriptor(string source)
    {
        return TestHelpers.Compile<OperationDescriptorSourceGenerator>(
            Usings + source);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Authorization.PermissionAttribute).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "ClaimsAuthorizationModuleTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
