using System.Text;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Modules.Authentication;
using Blueprint.SourceGenerator.Modules.Authorization;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Tests the compile-time authorization analysis and descriptor contributions
/// produced by <see cref="AuthorizationModule"/>.
/// </summary>
public class AuthorizationModuleTests
{
    // ── Descriptor projection tests ──────────────────────────────────

    [Fact]
    public void When_operation_has_single_Resource_attribute_then_descriptor_projects_scope_pair()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/accounts/{Id}")]
            [Resource("account", "read")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("GetAccountQueryDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("ScopePair");
        descriptorSource.Should().Contain("\"account\"");
        descriptorSource.Should().Contain("\"read\"");
    }

    [Fact]
    public void When_operation_has_multiple_Resource_attributes_then_descriptor_projects_all_scope_pairs()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/accounts/{Id}")]
            [Resource("account", "read")]
            [Resource("tenant", "manage")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("GetAccountQueryDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("\"account\"");
        descriptorSource.Should().Contain("\"read\"");
        descriptorSource.Should().Contain("\"tenant\"");
        descriptorSource.Should().Contain("\"manage\"");
    }

    [Fact]
    public void When_operation_has_Resource_with_template_then_descriptor_projects_scope_pair_with_template()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/tenants/{TenantId}/templates/{Id}")]
            [Resource("template", "write", "/Tenants/{Auth.AccountId}/Templates/{Id}")]
            public partial class UpdateTemplateCommand : ICommand<string>
            {
                public string TenantId { get; set; }
                public string Id { get; set; }

                public static Task<string> HandleAsync(UpdateTemplateCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("UpdateTemplateCommandDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("\"template\"");
        descriptorSource.Should().Contain("\"write\"");
        descriptorSource.Should().Contain("/Tenants/{Auth.AccountId}/Templates/{Id}");
    }

    [Fact]
    public void When_Resource_with_AllowAnonymous_then_reports_BPAU003_diagnostic()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/items")]
            [AllowAnonymous]
            [Resource("item", "read")]
            public partial class GetItemsQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().Contain(
            static d => d.Id == "BPAU003",
            "[Resource] combined with [AllowAnonymous] should report BPAU003");
    }

    [Fact]
    public void When_Resource_without_explicit_access_mode_then_descriptor_implies_permissioned_access()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/accounts/{Id}")]
            [Resource("account", "read")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        // Permissioned access means RequiresAuthentication feature data should be set
        // (permissioned implies authenticated)
        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("GetAccountQueryDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("WellKnownFeatureData.RequiresAuthentication");
    }

    [Fact]
    public void When_Resource_with_MustBeAuthenticated_then_both_are_valid_and_permissioned()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/accounts/{Id}")]
            [MustBeAuthenticated]
            [Resource("account", "read")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        // [MustBeAuthenticated] + [Resource] is redundant but valid (both require auth)
        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("GetAccountQueryDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("ScopePair");
    }

    [Fact]
    public void When_Resource_has_empty_noun_then_reports_BPAU004_diagnostic()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/items")]
            [Resource("", "read")]
            public partial class GetItemsQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().Contain(
            static d => d.Id == "BPAU004",
            "empty noun in [Resource] should report BPAU004");
    }

    [Fact]
    public void When_Resource_has_empty_verb_then_reports_BPAU004_diagnostic()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/items")]
            [Resource("item", "")]
            public partial class GetItemsQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().Contain(
            static d => d.Id == "BPAU004",
            "empty verb in [Resource] should report BPAU004");
    }

    [Fact]
    public void When_Resource_has_whitespace_noun_then_reports_BPAU004_diagnostic()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/items")]
            [Resource("  ", "read")]
            public partial class GetItemsQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().Contain(
            static d => d.Id == "BPAU004",
            "whitespace-only noun in [Resource] should report BPAU004");
    }

    [Fact]
    public void When_no_Resource_attribute_then_no_authorization_metadata()
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
        descriptorSource!.Should().NotContain("ScopePair");
    }

    [Fact]
    public void When_Resource_on_http_operation_then_401_response_is_generated()
    {
        // When an operation has [Resource], it implicitly requires auth,
        // so the 401 response metadata should be projected
        var result = CompileDescriptor(
            """
            [RootLink("/accounts/{Id}")]
            [Resource("account", "read")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("GetAccountQueryDescriptor"));

        descriptorSource.Should().NotBeNull();

        // With [Resource], both 401 and 403 are projected via the authorized failure tail constant
        descriptorSource!.Should().Contain("AuthorizedFailureResponses");
    }

    // ── Pipeline plan tests ──────────────────────────────────────────

    [Fact]
    public void When_Resource_on_http_operation_then_pipeline_has_authorisation_stage()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [RootLink("/accounts/{Id}")]
            [Resource("account", "read")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new AuthenticationModule(), new AuthorizationModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.GetAccountQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        // AuthorizationModule contributes at the Authorisation stage (not Authentication —
        // AuthenticationModule handles identity checks for all HTTP operations)
        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.Authorisation, nameof(AuthorizationModule)),
            "[Resource] operations get authorization check in the pipeline");

        // Identity check comes from AuthenticationModule, not AuthorizationModule
        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.Authentication, nameof(AuthenticationModule)),
            "AuthenticationModule handles identity for all HTTP operations");
    }

    [Fact]
    public void When_Resource_on_non_http_operation_then_no_authentication_stage_and_warns_BPAU008()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [Resource("account", "read")]
            public partial class ProcessAccountCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(ProcessAccountCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new AuthenticationModule(), new AuthorizationModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.ProcessAccountCommand", compilation);

        result.Diagnostics.Should().ContainSingle(
            static d => d.Descriptor.Id == "BPAU008",
            "resource authorization on a non-HTTP operation should warn");
        result.Value.Should().NotBeNull();

        // Non-HTTP operations don't get pipeline checks
        result.Value!.Contributions.Should().NotContain(
            static c => c.Stage == PipelineStage.Authentication,
            "non-HTTP [Resource] operations should not have authentication pipeline checks");
    }

    [Fact]
    public void When_Resource_with_MustBeAuthenticated_then_only_AuthenticationModule_adds_pipeline_check()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [RootLink("/accounts/{Id}")]
            [MustBeAuthenticated]
            [Resource("account", "read")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new AuthenticationModule(), new AuthorizationModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.GetAccountQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        // AuthenticationModule handles the identity check; AuthorizationModule defers
        var authContributions = result.Value!.Contributions
            .Where(static c => c.Stage == PipelineStage.Authentication)
            .ToList();

        authContributions.Should().HaveCount(1, "only AuthenticationModule should contribute when [MustBeAuthenticated] is present");
        authContributions.Should().Contain(
            new PipelineContribution(PipelineStage.Authentication, nameof(AuthenticationModule)));
    }

    // ── [FromResource] inheritance tests ────────────────────────────────

    [Fact]
    public void When_FromResource_references_operation_with_Resource_then_descriptor_inherits_scope_pairs()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/accounts/{Id}")]
            [Resource("account", "read")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }

            [RootLink("/accounts/{Id}/details")]
            [FromResource(typeof(GetAccountQuery))]
            public partial class GetAccountDetailsQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountDetailsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("GetAccountDetailsQueryDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("ScopePair");
        descriptorSource.Should().Contain("\"account\"");
        descriptorSource.Should().Contain("\"read\"");
    }

    [Fact]
    public void When_FromResource_references_operation_with_multiple_Resources_then_descriptor_inherits_all()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/accounts/{Id}")]
            [Resource("account", "read")]
            [Resource("tenant", "manage")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }

            [RootLink("/accounts/{Id}/details")]
            [FromResource(typeof(GetAccountQuery))]
            public partial class GetAccountDetailsQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountDetailsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("GetAccountDetailsQueryDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("\"account\"");
        descriptorSource.Should().Contain("\"read\"");
        descriptorSource.Should().Contain("\"tenant\"");
        descriptorSource.Should().Contain("\"manage\"");
    }

    [Fact]
    public void When_FromResource_with_AllowAnonymous_then_reports_BPAU003()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/accounts/{Id}")]
            [Resource("account", "read")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }

            [RootLink("/accounts/{Id}/details")]
            [AllowAnonymous]
            [FromResource(typeof(GetAccountQuery))]
            public partial class GetAccountDetailsQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountDetailsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().Contain(
            static d => d.Id == "BPAU003",
            "[FromResource] combined with [AllowAnonymous] should report BPAU003");
    }

    [Fact]
    public void When_FromResource_combined_with_Resource_then_reports_BPAU005()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/accounts/{Id}")]
            [Resource("account", "read")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }

            [RootLink("/accounts/{Id}/details")]
            [Resource("account", "write")]
            [FromResource(typeof(GetAccountQuery))]
            public partial class UpdateAccountCommand : ICommand<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(UpdateAccountCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().Contain(
            static d => d.Id == "BPAU005",
            "combining [FromResource] with [Resource] should report BPAU005");
    }

    [Fact]
    public void When_FromResource_target_has_no_Resource_then_reports_BPAU006()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/accounts/{Id}")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }

            [RootLink("/accounts/{Id}/details")]
            [FromResource(typeof(GetAccountQuery))]
            public partial class GetAccountDetailsQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountDetailsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().Contain(
            static d => d.Id == "BPAU006",
            "[FromResource] referencing a type with no [Resource] should report BPAU006");
    }

    [Fact]
    public void When_FromResource_on_http_operation_then_pipeline_has_authentication_stage()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [RootLink("/accounts/{Id}")]
            [Resource("account", "read")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }

            [RootLink("/accounts/{Id}/details")]
            [FromResource(typeof(GetAccountQuery))]
            public partial class GetAccountDetailsQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountDetailsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new AuthenticationModule(), new AuthorizationModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.GetAccountDetailsQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.Authorisation, nameof(AuthorizationModule)),
            "[FromResource] operations get authorization check in the pipeline");

        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.Authentication, nameof(AuthenticationModule)),
            "AuthenticationModule handles identity for all HTTP operations");
    }

    [Fact]
    public void When_FromResource_is_not_transitive_then_only_direct_Resource_attributes_are_inherited()
    {
        // GetAccountQuery has [Resource], GetAccountDetailsQuery has [FromResource(GetAccountQuery)],
        // GetAccountSubDetailsQuery has [FromResource(GetAccountDetailsQuery)] — should get BPAU006
        // because GetAccountDetailsQuery does NOT have [Resource] (only [FromResource])
        var result = CompileDescriptor(
            """
            [RootLink("/accounts/{Id}")]
            [Resource("account", "read")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }

            [RootLink("/accounts/{Id}/details")]
            [FromResource(typeof(GetAccountQuery))]
            public partial class GetAccountDetailsQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountDetailsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }

            [RootLink("/accounts/{Id}/sub-details")]
            [FromResource(typeof(GetAccountDetailsQuery))]
            public partial class GetAccountSubDetailsQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountSubDetailsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        // Transitive inheritance is NOT supported — GetAccountDetailsQuery has no [Resource] directly
        result.Diagnostics.Should().Contain(
            static d => d.Id == "BPAU006",
            "[FromResource] should not follow transitive chains");
    }

    // ── [FromResource] template override tests ─────────────────────

    [Fact]
    public void When_FromResource_has_template_override_then_descriptor_uses_override_template()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/accounts/{Id}")]
            [Resource("account", "read", "/tenants/{TenantId}/accounts/{Id}")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }
                public string TenantId { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }

            [RootLink("/accounts/{AccountId}/details")]
            [FromResource(typeof(GetAccountQuery), "/partners/{PartnerId}/accounts/{AccountId}")]
            public partial class GetAccountDetailsQuery : IQuery<string>
            {
                public string AccountId { get; set; }
                public string PartnerId { get; set; }

                public static Task<string> HandleAsync(GetAccountDetailsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("GetAccountDetailsQueryDescriptor"));

        descriptorSource.Should().NotBeNull();

        // Should inherit noun+verb from source
        descriptorSource!.Should().Contain("\"account\"");
        descriptorSource.Should().Contain("\"read\"");

        // Should use the OVERRIDE template, not the source template
        descriptorSource.Should().Contain("/partners/{PartnerId}/accounts/{AccountId}");
        descriptorSource.Should().NotContain("/tenants/{TenantId}");
    }

    [Fact]
    public void When_FromResource_has_template_override_with_multiple_Resources_then_all_use_override()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/accounts/{Id}")]
            [Resource("account", "read", "/tenants/{TenantId}/accounts/{Id}")]
            [Resource("tenant", "manage", "/tenants/{TenantId}")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }
                public string TenantId { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }

            [RootLink("/accounts/{AccountId}/details")]
            [FromResource(typeof(GetAccountQuery), "/partners/{PartnerId}/accounts/{AccountId}")]
            public partial class GetAccountDetailsQuery : IQuery<string>
            {
                public string AccountId { get; set; }
                public string PartnerId { get; set; }

                public static Task<string> HandleAsync(GetAccountDetailsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("GetAccountDetailsQueryDescriptor"));

        descriptorSource.Should().NotBeNull();

        // Should inherit both noun+verb pairs
        descriptorSource!.Should().Contain("\"account\"");
        descriptorSource.Should().Contain("\"read\"");
        descriptorSource.Should().Contain("\"tenant\"");
        descriptorSource.Should().Contain("\"manage\"");

        // ALL scope pairs should use override template, none of the originals
        descriptorSource.Should().Contain("/partners/{PartnerId}/accounts/{AccountId}");
        descriptorSource.Should().NotContain("/tenants/{TenantId}");
    }

    [Fact]
    public void When_FromResource_without_template_override_then_descriptor_keeps_source_templates()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/accounts/{Id}")]
            [Resource("account", "read", "/tenants/{TenantId}/accounts/{Id}")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }
                public string TenantId { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }

            [RootLink("/accounts/{Id}/details")]
            [FromResource(typeof(GetAccountQuery))]
            public partial class GetAccountDetailsQuery : IQuery<string>
            {
                public string Id { get; set; }
                public string TenantId { get; set; }

                public static Task<string> HandleAsync(GetAccountDetailsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("GetAccountDetailsQueryDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("\"account\"");
        descriptorSource.Should().Contain("\"read\"");

        // Without override, it keeps the original template
        descriptorSource.Should().Contain("/tenants/{TenantId}/accounts/{Id}");
    }

    // ── Resource key template validation tests ──────────────────────

    [Fact]
    public void When_Resource_on_non_HTTP_operation_then_reports_BPAU008()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [Resource("account", "read")]
            public partial class ProcessAccountCommand : ICommand<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(ProcessAccountCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new AuthenticationModule(), new AuthorizationModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.ProcessAccountCommand", compilation);

        result.Diagnostics.Should().Contain(
            static d => d.Descriptor.Id == "BPAU008",
            "resource authorization on a non-HTTP operation should warn because the runtime check cannot be generated");
    }

    [Fact]
    public void When_Resource_template_token_references_missing_property_then_reports_BPAU007()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [RootLink("/accounts/{Id}")]
            [Resource("account", "read", "/tenants/{TenantId}/accounts/{NonExistentProp}")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }
                public string TenantId { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new AuthenticationModule(), new AuthorizationModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.GetAccountQuery", compilation);

        result.Diagnostics.Should().Contain(
            static d => d.Descriptor.Id == "BPAU007",
            "template token referencing a non-existent property should report BPAU007");
    }

    [Fact]
    public void When_Resource_template_token_references_existing_property_then_no_diagnostic()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [RootLink("/accounts/{Id}")]
            [Resource("account", "read", "/tenants/{TenantId}/accounts/{Id}")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }
                public string TenantId { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new AuthenticationModule(), new AuthorizationModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.GetAccountQuery", compilation);

        result.Diagnostics.Should().NotContain(
            static d => d.Descriptor.Id == "BPAU007",
            "all template tokens resolve to existing properties");
    }

    [Fact]
    public void When_Resource_template_has_Auth_claim_token_then_no_diagnostic()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [RootLink("/accounts/{Id}")]
            [Resource("account", "read", "/tenants/{Auth.AccountId}/accounts/{Id}")]
            public partial class GetAccountQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetAccountQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new AuthenticationModule(), new AuthorizationModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.GetAccountQuery", compilation);

        result.Diagnostics.Should().NotContain(
            static d => d.Descriptor.Id == "BPAU007",
            "Auth:ClaimType tokens should not be validated as operation properties");
    }

    [Fact]
    public void When_Resource_template_has_missing_primary_but_valid_auth_fallback_then_no_BPAU007()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [RootLink("/billing/reset")]
            [Resource("billing", "update", "/Tenants/{TenantId:Auth.AccountId}")]
            public partial class PortalResetCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(PortalResetCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new AuthenticationModule(), new AuthorizationModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.PortalResetCommand", compilation);

        result.Diagnostics.Should().NotContain(
            static d => d.Descriptor.Id == "BPAU007",
            "a missing primary path with a valid Auth fallback should not fire BPAU007");

        // Verify the pipeline still generates authorization code (not skipped)
        result.Value!.RenderSteps.Should().Contain(
            static s => s.Stage == PipelineStage.Authorisation,
            "authorization check should still be generated when fallback is valid");
    }

    [Fact]
    public void When_Resource_template_has_missing_primary_and_no_fallback_then_still_reports_BPAU007()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [RootLink("/billing/reset")]
            [Resource("billing", "update", "/Tenants/{TenantId}")]
            public partial class PortalResetCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(PortalResetCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new AuthenticationModule(), new AuthorizationModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.PortalResetCommand", compilation);

        result.Diagnostics.Should().Contain(
            static d => d.Descriptor.Id == "BPAU007",
            "a missing primary path without fallback should still fire BPAU007");
    }

    // ── Duplicate scope pair detection ─────────────────────────────

    [Fact]
    public void When_Resource_has_duplicate_noun_verb_pair_then_reports_BPAU009_diagnostic()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/items/{Id}")]
            [Resource("item", "read")]
            [Resource("item", "read")]
            public partial class GetItemQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetItemQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().Contain(
            static d => d.Id == "BPAU009",
            "duplicate [Resource(\"item\", \"read\")] should report BPAU009");
    }

    [Fact]
    public void When_Resource_has_different_noun_verb_pairs_then_no_BPAU009_diagnostic()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/items/{Id}")]
            [Resource("item", "read")]
            [Resource("item", "write")]
            public partial class GetItemQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetItemQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().NotContain(
            static d => d.Id == "BPAU009",
            "different noun/verb pairs should not trigger BPAU009");
    }

    // ── Pipeline snapshot test ──────────────────────────────────────

    [Fact]
    public async Task When_Resource_with_template_then_pipeline_snapshot_matches()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/tenants/{TenantId}/accounts/{Id}")]
                [Resource("account", "read", "/tenants/{Auth.AccountId}/accounts/{Id}")]
                public partial class GetAccountQuery : IQuery<string>
                {
                    public string TenantId { get; set; }
                    public string Id { get; set; }

                    public static Task<string> HandleAsync(GetAccountQuery operation)
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
            .UseDirectory("Generators/Pipelines/Snapshots/Integration/");
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static GenerationResult CompileDescriptor(string source)
    {
        return TestHelpers.Compile<OperationDescriptorSourceGenerator>(
            "using Blueprint.Model;\nusing Blueprint.Model.Attributes;\nusing System.Threading.Tasks;\n\n"
            + "namespace TestNamespace;\n\n" + source);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "AuthorizationModuleTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
