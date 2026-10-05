using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Proves that a route placeholder finds its operation (and resource) property ignoring case, and that the route is
/// registered under the property's own name, as legacy Blueprint did.
/// </summary>
/// <remarks>
/// Legacy (Signature365 <c>origin/master</c>, <c>backend/src/Blueprint/Blueprint/ApiOperationLink.cs</c>) found the
/// operation's property with <c>BindingFlags.IgnoreCase</c> (:59-61), and the resource's the same way (:110, :123-125),
/// and routed the link at <c>"{" + property.Name + "}"</c> (:76). Signature365 Analytics'
/// <c>IngestCampaignEventCommand</c> is <c>[RootLink("/track/{type}")]</c> on a property named <c>Type</c>.
/// </remarks>
public class RoutePlaceholderCaseTests
{
    [Fact]
    public void A_root_links_placeholder_finds_its_property_ignoring_case_and_the_route_is_registered_under_the_propertys_name()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/track/{type}")]
            public partial class IngestCampaignEventCommand : ICommand<string>
            {
                public string Type { get; set; }

                public string Payload { get; set; }

                public static Task<string> HandleAsync(IngestCampaignEventCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = Descriptor(result);

        source.Should().Contain("new global::Blueprint.Model.ApiOperationUrl(\"/track/{Type}\")");
        source.Should().NotContain("{type}");
        source.Should().Contain("new global::Blueprint.Model.OperationRootLink(\"IngestCampaignEvent\", \"/track/{Type}\")");
        source.Should().Contain("new global::Blueprint.Model.OperationPropertyBindingEntry(\"Type\", global::Blueprint.Model.PropertyBindingSource.Route, \"Type\"");
        source.Should().Contain("new global::Blueprint.Model.OperationPropertyBindingEntry(\"Payload\", global::Blueprint.Model.PropertyBindingSource.Body");
    }

    [Fact]
    public void A_self_links_placeholder_finds_its_operation_and_resource_properties_ignoring_case_and_links_by_their_names()
    {
        var result = CompileDescriptor(
            """
            [SelfLink(typeof(CampaignApiResource), "/campaigns/{id}")]
            public partial class GetCampaignQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetCampaignQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }

            public partial class CampaignApiResource : ApiResource
            {
                public string Id { get; set; }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = Descriptor(result);

        source.Should().Contain("new global::Blueprint.Model.ApiOperationUrl(\"/campaigns/{Id}\")");
        source.Should().Contain("\"/campaigns/{Id}\", \"/campaigns/{Id}\", \"self\", typeof(global::TestNamespace.CampaignApiResource)");
        source.Should().Contain("r.Id");
        source.Should().NotContain("{id}");
        source.Should().Contain("new global::Blueprint.Model.OperationPropertyBindingEntry(\"Id\", global::Blueprint.Model.PropertyBindingSource.Route, \"Id\"");
    }

    [Fact]
    public void A_self_links_placeholder_reads_the_resource_property_that_differs_from_it_only_in_case_by_its_own_name()
    {
        var result = CompileDescriptor(
            """
            [SelfLink(typeof(CampaignApiResource), "/campaigns/{code}")]
            public partial class GetCampaignByCodeQuery : IQuery<string>
            {
                public string Code { get; set; }

                public static Task<string> HandleAsync(GetCampaignByCodeQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }

            public partial class CampaignApiResource : ApiResource
            {
                public string CODE { get; set; }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = Descriptor(result);

        source.Should().Contain("new global::Blueprint.Model.ApiOperationUrl(\"/campaigns/{Code}\")");

        // The URL builder reads the resource's property as the resource spells it.
        source.Should().Contain("r.CODE");
        source.Should().NotContain("r.Code");
    }

    [Fact]
    public void A_links_placeholder_finds_its_property_ignoring_case_and_keeps_its_alternate_resource_property()
    {
        var result = CompileDescriptor(
            """
            [Link(typeof(CampaignApiResource), "/campaigns/{campaignid:id}/pause", Rel = "pause")]
            public partial class PauseCampaignCommand : ICommand<string>
            {
                public string CampaignId { get; set; }

                public static Task<string> HandleAsync(PauseCampaignCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }

            public partial class CampaignApiResource : ApiResource
            {
                public string Id { get; set; }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = Descriptor(result);

        source.Should().Contain("new global::Blueprint.Model.ApiOperationUrl(\"/campaigns/{CampaignId}/pause\")");
        source.Should().Contain("r.Id");
        source.Should().Contain("new global::Blueprint.Model.OperationPropertyBindingEntry(\"CampaignId\", global::Blueprint.Model.PropertyBindingSource.Route, \"CampaignId\"");
    }

    [Fact]
    public void A_links_placeholder_reads_the_resource_property_that_differs_from_it_only_in_case_by_its_own_name()
    {
        var result = CompileDescriptor(
            """
            [Link(typeof(CampaignApiResource), "/campaigns/{code}/pause", Rel = "pause")]
            public partial class PauseCampaignByCodeCommand : ICommand<string>
            {
                public string Code { get; set; }

                public static Task<string> HandleAsync(PauseCampaignByCodeCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }

            public partial class CampaignApiResource : ApiResource
            {
                public string CODE { get; set; }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = Descriptor(result);

        source.Should().Contain("new global::Blueprint.Model.ApiOperationUrl(\"/campaigns/{Code}/pause\")");
        source.Should().Contain("r.CODE");
        source.Should().NotContain("r.Code");
    }

    [Fact]
    public void A_self_links_alternate_resource_property_is_found_ignoring_case_and_read_by_its_own_name()
    {
        var result = CompileDescriptor(
            """
            [SelfLink(typeof(CampaignApiResource), "/campaigns/{campaignid:id}")]
            public partial class GetCampaignByAlternateQuery : IQuery<string>
            {
                public string CampaignId { get; set; }

                public static Task<string> HandleAsync(GetCampaignByAlternateQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }

            public partial class CampaignApiResource : ApiResource
            {
                public string Id { get; set; }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = Descriptor(result);

        source.Should().Contain("new global::Blueprint.Model.ApiOperationUrl(\"/campaigns/{CampaignId}\")");
        source.Should().Contain("r.Id");
        source.Should().NotContain("r.id");
    }

    [Fact]
    public void A_links_placeholder_without_a_resource_type_finds_its_property_ignoring_case()
    {
        var result = CompileDescriptor(
            """
            [Link(null, "/campaigns/{campaignid}/export", Rel = "export")]
            public partial class ExportCampaignQuery : IQuery<string>
            {
                public string CampaignId { get; set; }

                public static Task<string> HandleAsync(ExportCampaignQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        Descriptor(result).Should().Contain("new global::Blueprint.Model.ApiOperationUrl(\"/campaigns/{CampaignId}/export\")");
    }

    [Fact]
    public void A_placeholder_that_matches_a_property_exactly_is_not_taken_by_another_that_differs_only_in_case()
    {
        // Legacy's GetProperty(IgnoreCase) threw on such a pair (AmbiguousMatchException), so any deterministic choice is
        // an improvement; the exact spelling is the one the author wrote.
        var result = CompileDescriptor(
            """
            [RootLink("/kinds/{Kind}")]
            public partial class GetKindQuery : IQuery<string>
            {
                public string kind { get; set; }

                public string Kind { get; set; }

                public static Task<string> HandleAsync(GetKindQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = Descriptor(result);

        source.Should().Contain("new global::Blueprint.Model.ApiOperationUrl(\"/kinds/{Kind}\")");
        source.Should().Contain("new global::Blueprint.Model.OperationPropertyBindingEntry(\"Kind\", global::Blueprint.Model.PropertyBindingSource.Route, \"Kind\"");
    }

    [Fact]
    public void A_placeholder_that_matches_no_property_even_ignoring_case_is_still_reported()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/track/{kind}")]
            public partial class TrackQuery : IQuery<string>
            {
                public string Type { get; set; }

                public static Task<string> HandleAsync(TrackQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Select(static d => d.Id).Should().Contain("BPPO012").And.Contain("BPLG001");
    }

    [Fact]
    public void A_TenantId_placeholder_spelled_in_another_case_is_route_owned_so_the_pipeline_does_not_fill_it_from_the_accessor()
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

                [RootLink("/tenants/{tenantid}/items")]
                public partial class CreateItemCommand : ICommand<string>
                {
                    public string TenantId { get; set; }

                    public static Task<string> HandleAsync(CreateItemCommand operation)
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

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSource = string.Join("\n", result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator)).Select(static s => s.SourceText));

        pipelineSource.Should().NotContain("operation.TenantId = this.TenantAccessor.Current");
    }

    private static string Descriptor(GenerationResult result)
    {
        return string.Join("\n", result.Output.Select(static t => t.ToString()));
    }

    private static GenerationResult CompileDescriptor(string source)
    {
        return TestHelpers.Compile<OperationDescriptorSourceGenerator>(
            "using Blueprint.Model;\nusing Blueprint.Model.Attributes;\nusing System.Threading.Tasks;\n\n"
            + "namespace TestNamespace;\n\n" + source);
    }
}
