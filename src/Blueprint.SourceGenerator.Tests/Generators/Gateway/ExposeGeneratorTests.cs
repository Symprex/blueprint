using System.Collections.Immutable;
using Blueprint.SourceGenerator.Generators.App;
using Blueprint.SourceGenerator.Generators.Clients;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Generators.Gateway;

/// <summary>
/// <c>[Expose&lt;TInner&gt;]</c> (Symprex/blueprint#65, D9, D12 and D25): an operation declared as
/// <c>[Expose&lt;Inner.X&gt;] partial class X : IQuery&lt;R&gt;;</c> gets its inner operation's properties from the shared
/// operation property model, which every generator builds from its own compilation and the descriptor generator emits into
/// the partial, and a handler the pipeline generator synthesises to forward a fresh inner operation through the
/// <c>[BlueprintClient]</c> that includes it.
/// </summary>
public class ExposeGeneratorTests
{
    private const string InnerSource = """
        namespace Inner
        {
            using System.ComponentModel.DataAnnotations;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            public class WidgetResult : ApiResource
            {
                public string Id { get; set; } = "";
            }

            [RootLink("/widgets/{Id}")]
            [HttpGet]
            public partial class GetWidgetQuery : IQuery<WidgetResult>
            {
                /// <summary>The widget to get.</summary>
                [Required]
                public string Id { get; set; } = "";

                /// <summary>How many revisions to include.</summary>
                [Range(1, 10)]
                public int Revisions { get; set; } = 1;

                /// <summary>The region to read from.</summary>
                [FromHeader("X-Region")]
                public string? Region { get; set; }

                [System.Obsolete("Use Region.")]
                public string? Zone { get; set; }

                public string ComputedOnly => this.Id + this.Region;
            }
        }
        """;

    private const string ClientSource = """
        namespace Gateway
        {
            using Blueprint.HttpClient;

            [BlueprintClient]
            [BlueprintClientInclude(typeof(Inner.GetWidgetQuery))]
            public partial class WidgetApi;
        }
        """;

    private const string GatewaySource = """
        namespace Gateway
        {
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            [Expose<Inner.GetWidgetQuery>]
            [AllowAnonymous]
            [RootLink("/gateway/widgets/{Id}")]
            [HttpGet]
            public partial class GetGatewayWidgetQuery : IQuery<Inner.WidgetResult>;
        }
        """;

    private const string AppSource = """
        namespace Gateway
        {
            using Blueprint.Model;

            [BlueprintAppScan]
            public partial class GatewayApp : BlueprintApp;
        }
        """;

    private const string CredentialSource = """
        namespace Gateway
        {
            using Blueprint.HttpClient;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;

            public sealed class TenantTokenStrategy : IUpstreamCredentialStrategy
            {
                public IUpstreamCredential CreateCredential(ApiOperationContext context, IOperation inner, UpstreamCredentialAttribute attribute)
                {
                    return new BearerTokenCredential("exchanged");
                }
            }

            public sealed class TenantTokenAttribute : UpstreamCredentialAttribute<TenantTokenStrategy>
            {
                public string? TenantIdProperty { get; set; }
            }
        }
        """;

    private const string CredentialGatewaySource = """
        namespace Gateway
        {
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            [Expose<Inner.GetWidgetQuery>]
            [AllowAnonymous]
            [RootLink("/gateway/widgets/{Id}")]
            [HttpGet]
            [TenantToken(TenantIdProperty = nameof(Inner.GetWidgetQuery.Region))]
            [TranslateError("widget_gone", nameof(WidgetMissing))]
            public partial class GetGatewayWidgetQuery : IQuery<Inner.WidgetResult>
            {
                public static readonly Blueprint.ApiExceptionFactory WidgetMissing = new("Widget missing", "widget_missing", 404);

                public static readonly string NotAFactory = "";

                public Blueprint.ApiExceptionFactory InstanceFactory => WidgetMissing;
            }
        }
        """;

    private static readonly Lazy<MetadataReference> _innerContract = new(
        static () => TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(InnerSource, "InnerContract"));

    [Fact]
    public void The_model_of_an_exposed_operation_is_its_inner_operations_public_settable_properties_that_are_not_obsolete()
    {
        var gateway = CreateCompilation([GatewaySource]).GetTypeByMetadataName("Gateway.GetGatewayWidgetQuery")!;

        var model = OperationPropertyModelBuilder.Build(gateway);

        model.Exposed!.MetadataName.Should().Be("Inner.GetWidgetQuery");
        model.Properties.Select(static p => (p.Name, p.TypeName, p.Origin)).Should().Equal(
            ("Id", "string", OperationPropertyOrigin.Contributed),
            ("Revisions", "int", OperationPropertyOrigin.Contributed),
            ("Region", "string?", OperationPropertyOrigin.Contributed));
        model.Properties[0].Attributes.Should().Equal("global::System.ComponentModel.DataAnnotations.RequiredAttribute");
        model.Properties[1].Attributes.Should().Equal("global::System.ComponentModel.DataAnnotations.RangeAttribute(1, 10)");
        model.Properties[2].Attributes.Should().Equal("global::Blueprint.Model.Attributes.FromHeaderAttribute(\"X-Region\")");
        model.Properties.Should().OnlyContain(static p => p.Location != null && p.Location.FilePath == "Gateway.cs");
    }

    [Fact]
    public void The_model_is_equal_when_built_again_from_a_new_compilation_of_the_same_source()
    {
        var first = OperationPropertyModelBuilder.Build(CreateCompilation([GatewaySource]).GetTypeByMetadataName("Gateway.GetGatewayWidgetQuery")!);
        var second = OperationPropertyModelBuilder.Build(CreateCompilation([GatewaySource]).GetTypeByMetadataName("Gateway.GetGatewayWidgetQuery")!);

        second.Should().Be(first);
        second.GetHashCode().Should().Be(first.GetHashCode());
    }

    [Fact]
    public void A_property_the_exposing_operation_declares_wins_over_the_contributed_one()
    {
        var gatewaySource = GatewaySource.Replace(
            "public partial class GetGatewayWidgetQuery : IQuery<Inner.WidgetResult>;",
            "public partial class GetGatewayWidgetQuery : IQuery<Inner.WidgetResult> { public int Revisions { get; set; } = 3; }",
            StringComparison.Ordinal);
        var gateway = CreateCompilation([gatewaySource]).GetTypeByMetadataName("Gateway.GetGatewayWidgetQuery")!;

        OperationPropertyModelBuilder.Build(gateway).Properties.Select(static p => (p.Name, p.Origin)).Should().Equal(
            ("Revisions", OperationPropertyOrigin.Declared),
            ("Id", OperationPropertyOrigin.Contributed),
            ("Region", OperationPropertyOrigin.Contributed));
    }

    [Fact]
    public void Every_generator_together_compiles_the_exposed_operation_and_its_forward_and_a_second_run_is_cached()
    {
        // The pipeline generator binds and forwards the contributed properties without seeing the descriptor generator's
        // partial, so its output compiles only because both read the same model.
        var run = RunAllGenerators([ClientSource, GatewaySource, AppSource]);

        run.EmitErrors.Should().BeEmpty();
        run.GeneratorErrors.Should().BeEmpty();

        var properties = run.Source("Gateway.GetGatewayWidgetQuery_ExposedProperties.g.cs");
        properties.Should().Contain("[global::System.ComponentModel.DataAnnotations.RequiredAttribute]")
            .And.Contain("public string Id { get; set; } = __blueprintExposedDefaults.Id;")
            .And.Contain("public int Revisions { get; set; } = __blueprintExposedDefaults.Revisions;")
            .And.Contain("[global::Blueprint.Model.Attributes.FromHeaderAttribute(\"X-Region\")]")
            .And.Contain("/// <inheritdoc cref=\"global::Inner.GetWidgetQuery.Id\"/>")
            .And.NotContain("Zone")
            .And.NotContain("ComputedOnly");

        var descriptor = run.Source("Gateway.GetGatewayWidgetQuery_Descriptor.g.cs");
        descriptor.Should().Contain("new global::Blueprint.Model.OperationPropertyBindingEntry(\"Id\", global::Blueprint.Model.PropertyBindingSource.Route")
            .And.Contain("new global::Blueprint.Model.OperationPropertyBindingEntry(\"Revisions\", global::Blueprint.Model.PropertyBindingSource.Query")
            .And.Contain("new global::Blueprint.Model.OperationPropertyBindingEntry(\"Region\", global::Blueprint.Model.PropertyBindingSource.Header");

        // Op.Http: the route value and the GET's query string both come from the contributed properties.
        descriptor.Should().Contain("global::System.Uri.EscapeDataString(typed.Id)").And.Contain("\"revisions\"");

        var pipeline = run.Source("Gateway.GetGatewayWidgetQueryExecutorPipeline.g.cs");
        pipeline.Should().Contain("new global::Inner.GetWidgetQuery")
            .And.Contain("Id = operation.Id")
            .And.Contain("Revisions = operation.Revisions")
            .And.Contain("Region = operation.Region")
            .And.Contain("ForwardAsync<global::Inner.GetWidgetQuery, global::Inner.WidgetResult>")
            .And.Contain("global::Blueprint.HttpClient.BlueprintClientTransport.Create<global::Gateway.WidgetApi>")
            .And.NotContain("Zone =");

        run.DescriptorAndAppReplayedFromCache.Should().BeTrue();
    }

    [Fact]
    public async Task The_generated_forward_and_emitted_properties_match_the_snapshot()
    {
        var run = RunAllGenerators([ClientSource, GatewaySource, AppSource]);

        run.EmitErrors.Should().BeEmpty();

        await Verify(string.Join(
                "\n",
                new[] { "Gateway.GetGatewayWidgetQuery_ExposedProperties.g.cs", "Gateway.GetGatewayWidgetQueryExecutorPipeline.g.cs" }
                    .Select(hint => hint + ":\n\n" + run.Source(hint))))
            .UseDirectory("Snapshots");
    }

    [Fact]
    public void The_descriptor_and_emitted_properties_follow_an_edit_to_an_inner_operation_in_another_file()
    {
        // An edit to the inner operation leaves the exposing operation's own file untouched, so a step keyed on that file
        // alone would keep a stale descriptor (the property model is part of WithDeclarationChecksums' key).
        var innerInSource = InnerSource;
        var updatedInner = InnerSource.Replace(
            "public string ComputedOnly",
            "/// <summary>The colour.</summary>\n            public string? Colour { get; set; }\n\n            public string ComputedOnly",
            StringComparison.Ordinal);

        var result = TestHelpers.CompileWithTrackedGeneratorsAfterEdit(
            [TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>()],
            [innerInSource, GatewaySource],
            [updatedInner, GatewaySource],
            ["Inner.cs", "Gateway.cs"]);

        result.Updated.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.GetChangedGeneratedSourceHintNames(nameof(OperationDescriptorSourceGenerator)).Should().Contain(
        [
            "Gateway.GetGatewayWidgetQuery_Descriptor.g.cs",
            "Gateway.GetGatewayWidgetQuery_ExposedProperties.g.cs",
        ]);
        Updated(result, "Gateway.GetGatewayWidgetQuery_Descriptor.g.cs").Should().Contain("OperationPropertyBindingEntry(\"Colour\"");
        Updated(result, "Gateway.GetGatewayWidgetQuery_ExposedProperties.g.cs").Should().Contain("public string? Colour { get; set; }");
    }

    [Fact]
    public void A_required_inner_property_is_emitted_required_and_the_inner_defaults_are_made_without_setting_it()
    {
        var inner = InnerSource.Replace("public string Id { get; set; } = \"\";", "public required string Id { get; set; }", StringComparison.Ordinal);
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerContract");

        var run = RunAllGenerators([ClientSource, GatewaySource, AppSource], contract);

        run.EmitErrors.Should().BeEmpty();
        var properties = run.Source("Gateway.GetGatewayWidgetQuery_ExposedProperties.g.cs");
        properties.Should().Contain("public required string Id { get; set; } = __blueprintExposedDefaults.Id;")
            .And.Contain("global::System.Activator.CreateInstance(typeof(global::Inner.GetWidgetQuery))");
    }

    [Fact]
    public void A_required_inner_property_with_an_initialiser_starts_from_that_initialiser()
    {
        // The inner pipeline makes such an operation with Activator.CreateInstance, so its initialiser runs and an omitted
        // value stays 1. The exposed property used to have no initialiser, and the defaults instance set every required
        // member to default, so an omitted value was forwarded as 0.
        var inner = InnerSource.Replace("public int Revisions { get; set; } = 1;", "public required int Revisions { get; set; } = 1;", StringComparison.Ordinal);
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerContract");

        var run = RunAllGenerators([ClientSource, GatewaySource, AppSource], contract);

        run.EmitErrors.Should().BeEmpty();
        var properties = run.Source("Gateway.GetGatewayWidgetQuery_ExposedProperties.g.cs");
        properties.Should().Contain("public required int Revisions { get; set; } = __blueprintExposedDefaults.Revisions;")
            .And.Contain("(global::Inner.GetWidgetQuery)global::System.Activator.CreateInstance(typeof(global::Inner.GetWidgetQuery))!")
            .And.NotContain("Revisions = default!");
    }

    [Fact]
    public void An_internal_validation_attribute_the_gateway_can_access_is_carried_onto_the_contributed_property()
    {
        // Only public attributes used to be copied, so an internal one was dropped even where InternalsVisibleTo lets the
        // gateway use it, and the gateway never ran that rule before forwarding.
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(WithInternalValidation(InnerSource, visibleToGateway: true), "InnerContract");

        var run = RunAllGenerators([ClientSource, GatewaySource, AppSource], contract);

        run.GeneratorErrors.Should().BeEmpty();
        run.EmitErrors.Should().BeEmpty();
        run.Source("Gateway.GetGatewayWidgetQuery_ExposedProperties.g.cs").Should().Contain("[global::Inner.EvenAttribute]");
    }

    [Fact]
    public void When_an_inner_validation_attribute_is_inaccessible_to_the_gateway_then_BPGW018_is_reported()
    {
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(WithInternalValidation(InnerSource, visibleToGateway: false), "InnerContract");

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([GatewaySource], additionalReferences: [contract]);

        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW018").Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("GetGatewayWidgetQuery").And.Contain("EvenAttribute").And.Contain("Revisions");
        Located(diagnostic).Should().Be("Expose<Inner.GetWidgetQuery>");
    }

    [Fact]
    public void A_self_link_placeholder_binds_a_contributed_property()
    {
        var gateway = GatewaySource.Replace(
            "[RootLink(\"/gateway/widgets/{Id}\")]",
            "[SelfLink(typeof(Inner.WidgetResult), \"/gateway/widgets/{Id}\")]",
            StringComparison.Ordinal);

        var run = RunAllGenerators([ClientSource, gateway, AppSource]);

        run.GeneratorErrors.Should().BeEmpty();
        run.EmitErrors.Should().BeEmpty();
        run.Source("Gateway.GetGatewayWidgetQuery_Descriptor.g.cs").Should().Contain("/gateway/widgets/{Id}");
    }

    [Fact]
    public void A_GET_and_POST_exposed_operation_reads_an_unannotated_contributed_property_from_a_GETs_query_string_and_not_its_body()
    {
        // Symprex/blueprint#149's rule: a GET+POST operation's unannotated property is read from a GET's query string and
        // from any other method's body, and a GET reads no body when nothing is left there.
        var gateway = GatewaySource.Replace("[HttpGet]", "[HttpGet]\n    [HttpPost]", StringComparison.Ordinal);

        var run = RunAllGenerators([ClientSource, gateway, AppSource]);

        run.GeneratorErrors.Should().BeEmpty();
        run.EmitErrors.Should().BeEmpty();

        var pipeline = run.Source("Gateway.GetGatewayWidgetQueryExecutorPipeline.g.cs");
        pipeline.Should().Contain("BindQueryValue(httpContext.Request.Query, \"revisions\"")
            .And.Contain("reader.ValueTextEquals(\"revisions\"u8)")
            .And.Contain("if (!global::Microsoft.AspNetCore.Http.HttpMethods.IsGet(httpContext.Request.Method))");
    }

    [Fact]
    public void When_the_gateway_disallows_expose_then_BPGW001_is_reported_at_the_attribute()
    {
        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>(
            [GatewaySource],
            new BuildPropertyOptionsProvider(("build_property.BlueprintGatewayAllowExpose", "false")),
            [_innerContract.Value]);

        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW001").Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("GetGatewayWidgetQuery");
        Located(diagnostic).Should().StartWith("Expose<");
    }

    [Theory]
    [InlineData("true")]
    [InlineData("")]
    public void When_the_gateway_allows_expose_or_says_nothing_then_BPGW001_is_not_reported(string value)
    {
        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>(
            [GatewaySource],
            new BuildPropertyOptionsProvider(("build_property.BlueprintGatewayAllowExpose", value)),
            [_innerContract.Value]);

        result.Diagnostics.Should().NotContain(static d => d.Id == "BPGW001");
    }

    [Fact]
    public void When_an_exposed_operation_has_only_a_non_native_authorisation_attribute_then_BPGW002_is_reported()
    {
        var gateway = GatewaySource.Replace("[AllowAnonymous]", "[Authorize]", StringComparison.Ordinal)
            + """

            namespace Gateway
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class AuthorizeAttribute : System.Attribute;
            }
            """;

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([gateway], additionalReferences: [_innerContract.Value]);

        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW002").Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("GetGatewayWidgetQuery");
    }

    [Theory]
    [InlineData("[MustBeAuthenticated]")]
    [InlineData("[Resource(typeof(Inner.WidgetResult))]")]
    public void When_an_exposed_operation_declares_a_native_authorisation_then_BPGW002_is_not_reported(string declaration)
    {
        var gateway = GatewaySource.Replace("[AllowAnonymous]", declaration, StringComparison.Ordinal);

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([gateway], additionalReferences: [_innerContract.Value]);

        result.Diagnostics.Should().NotContain(static d => d.Id == "BPGW002");
    }

    [Fact]
    public void When_no_client_includes_the_inner_operation_then_BPGW003_is_reported()
    {
        var run = RunAllGenerators([GatewaySource, AppSource]);

        var diagnostic = run.GeneratorDiagnostics.Should().ContainSingle(static d => d.Id == "BPGW003").Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("GetGatewayWidgetQuery").And.Contain("GetWidgetQuery");
    }

    [Fact]
    public void When_two_clients_include_the_inner_operation_then_BPGW004_is_reported_naming_both()
    {
        var secondClient = ClientSource.Replace("class WidgetApi", "class OtherWidgetApi", StringComparison.Ordinal);

        var run = RunAllGenerators([ClientSource, secondClient, GatewaySource, AppSource]);

        var diagnostic = run.GeneratorDiagnostics.Should().ContainSingle(static d => d.Id == "BPGW004").Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("WidgetApi").And.Contain("OtherWidgetApi");
    }

    [Fact]
    public void A_referenced_clients_hand_written_method_taking_the_inner_operation_is_not_an_include()
    {
        // Clients in a referenced assembly used to be indexed by any public XAsync method whose first parameter is an
        // operation, so AuditApi's own LogAsync counted as an include and the forward reported BPGW004 beside WidgetApi.
        var referenced = InnerSource + "\n" + ClientSource + "\n" + """
            namespace Inner
            {
                using Blueprint.Model;
                using Blueprint.Model.Attributes;

                [RootLink("/pings")]
                [HttpGet]
                public partial class PingQuery : IQuery<WidgetResult>;
            }

            namespace Gateway
            {
                using Blueprint.HttpClient;

                [BlueprintClient]
                [BlueprintClientInclude(typeof(Inner.PingQuery))]
                public partial class AuditApi
                {
                    public System.Threading.Tasks.Task LogAsync(Inner.GetWidgetQuery operation) => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;

        var run = RunAllGenerators([GatewaySource, AppSource], CreateReferenceWithClients(referenced));

        run.GeneratorErrors.Should().BeEmpty();
        run.EmitErrors.Should().BeEmpty();
        run.Source("Gateway.GetGatewayWidgetQueryExecutorPipeline.g.cs")
            .Should().Contain("global::Blueprint.HttpClient.BlueprintClientTransport.Create<global::Gateway.WidgetApi>");
    }

    [Fact]
    public void When_an_exposed_operation_has_a_hand_written_handler_then_BPGW005_is_reported()
    {
        var handler = """
            namespace Gateway
            {
                public partial class GetGatewayWidgetQuery
                {
                    public System.Threading.Tasks.Task<Inner.WidgetResult> HandleAsync() => System.Threading.Tasks.Task.FromResult(new Inner.WidgetResult());
                }
            }
            """;

        var run = RunAllGenerators([ClientSource, GatewaySource, handler, AppSource]);

        var diagnostic = run.GeneratorDiagnostics.Should().ContainSingle(static d => d.Id == "BPGW005").Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("GetGatewayWidgetQuery");
    }

    [Fact]
    public void When_an_exposed_operation_declares_no_operation_interface_then_BPGW006_is_reported()
    {
        var gateway = GatewaySource.Replace(" : IQuery<Inner.WidgetResult>;", ";", StringComparison.Ordinal);

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([gateway], additionalReferences: [_innerContract.Value]);

        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW006").Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("GetGatewayWidgetQuery").And.Contain("IQuery<global::Inner.WidgetResult>");
    }

    [Fact]
    public void The_generated_forward_passes_its_context_so_it_authenticates_and_translates_as_its_operation_declares()
    {
        // The context overload of ForwardAsync (#64) is what applies the caller's credential, [UpstreamCredential<T>] and
        // [TranslateError]; the context-free one applies none of them.
        var run = RunAllGenerators([ClientSource, CredentialSource, CredentialGatewaySource, AppSource]);

        run.EmitErrors.Should().BeEmpty();
        run.GeneratorErrors.Should().BeEmpty();
        run.Source("Gateway.GetGatewayWidgetQueryExecutorPipeline.g.cs").Should().Contain(
            "forwardTransport.ForwardAsync<global::Inner.GetWidgetQuery, global::Inner.WidgetResult>(context, forwardInner, null, context.OperationCancelled)");
    }

    [Fact]
    public void When_an_operation_carries_two_upstream_credential_attributes_then_BPGW015_is_reported_naming_both()
    {
        var gateway = CredentialGatewaySource.Replace(
            "[TenantToken(TenantIdProperty = nameof(Inner.GetWidgetQuery.Region))]",
            "[TenantToken(TenantIdProperty = nameof(Inner.GetWidgetQuery.Region))]\n    [UpstreamCredential<TenantTokenStrategy>]",
            StringComparison.Ordinal);

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([CredentialSource, gateway], additionalReferences: [_innerContract.Value]);

        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW015").Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("GetGatewayWidgetQuery").And.Contain("TenantTokenAttribute").And.Contain("UpstreamCredentialAttribute<TenantTokenStrategy>");
    }

    [Fact]
    public void When_the_credential_type_is_not_an_upstream_credential_strategy_then_BPGW016_is_reported_at_the_attribute()
    {
        var gateway = CredentialGatewaySource.Replace(
            "[TenantToken(TenantIdProperty = nameof(Inner.GetWidgetQuery.Region))]",
            "[UpstreamCredential<NotAStrategy>]",
            StringComparison.Ordinal) + "\nnamespace Gateway { public sealed class NotAStrategy; }";

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([CredentialSource, gateway], additionalReferences: [_innerContract.Value]);

        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW016").Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("GetGatewayWidgetQuery").And.Contain("NotAStrategy").And.Contain("IUpstreamCredentialStrategy");
        Located(diagnostic).Should().StartWith("UpstreamCredential<NotAStrategy>");
    }

    [Theory]
    [InlineData("\"NoSuchMember\"", "NoSuchMember")]
    [InlineData("nameof(NotAFactory)", "NotAFactory")]
    [InlineData("nameof(InstanceFactory)", "InstanceFactory")]
    public void When_an_operations_TranslateError_names_no_static_ApiExceptionFactory_then_BPGW017_is_reported_at_the_attribute(string to, string name)
    {
        var gateway = CredentialGatewaySource.Replace("nameof(WidgetMissing)", to, StringComparison.Ordinal);

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([CredentialSource, gateway], additionalReferences: [_innerContract.Value]);

        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW017").Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("GetGatewayWidgetQuery").And.Contain("widget_gone").And.Contain("'" + name + "'");
        Located(diagnostic).Should().StartWith("TranslateError(");
    }

    [Fact]
    public void When_a_hand_written_forwarding_operations_TranslateError_names_no_factory_then_BPGW017_is_reported()
    {
        // A hand-written ForwardAsync(context, …) reads the same attributes, so it gets the same check.
        var operation = """
            namespace Gateway
            {
                using Blueprint.Model;
                using Blueprint.Model.Attributes;

                [AllowAnonymous]
                [RootLink("/gateway/hand-written")]
                [HttpGet]
                [TranslateError("widget_gone", "Missing")]
                public partial class HandWrittenQuery : IQuery<Inner.WidgetResult>;
            }
            """;

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([operation], additionalReferences: [_innerContract.Value]);

        result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW017").Which.GetMessage().Should().Contain("HandWrittenQuery");
    }

    [Fact]
    public void When_the_apps_TranslateError_names_no_static_ApiExceptionFactory_then_BPGW017_is_reported_on_the_app()
    {
        var app = AppSource.Replace("[BlueprintAppScan]", "[BlueprintAppScan]\n    [Blueprint.Model.Attributes.TranslateError(\"widget_gone\", \"Missing\")]", StringComparison.Ordinal);

        var run = RunAllGenerators([ClientSource, GatewaySource, app]);

        var diagnostic = run.GeneratorDiagnostics.Should().ContainSingle(static d => d.Id == "BPGW017").Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("GatewayApp").And.Contain("'Missing'");
        Located(diagnostic).Should().StartWith("Blueprint.Model.Attributes.TranslateError(");
    }

    [Fact]
    public void When_the_credential_and_translations_are_valid_then_no_credential_or_translation_diagnostic_is_reported()
    {
        var app = AppSource.Replace(
            "public partial class GatewayApp : BlueprintApp;",
            "[Blueprint.Model.Attributes.TranslateError(\"widget_gone\", nameof(Missing))]\n    public partial class GatewayApp : BlueprintApp { public static Blueprint.ApiExceptionFactory Missing { get; } = new(\"Missing\", \"missing\", 404); }",
            StringComparison.Ordinal);

        var run = RunAllGenerators([ClientSource, CredentialSource, CredentialGatewaySource, app]);

        run.GeneratorDiagnostics.Should().NotContain(static d => d.Id == "BPGW015" || d.Id == "BPGW016" || d.Id == "BPGW017");
        run.EmitErrors.Should().BeEmpty();
    }

    /// <summary>
    /// Adds an internal validation attribute to the inner operation's <c>Revisions</c>, visible to the gateway's assembly or not.
    /// </summary>
    private static string WithInternalValidation(string innerSource, bool visibleToGateway)
    {
        var attribute = """
            namespace Inner
            {
                internal sealed class EvenAttribute : System.ComponentModel.DataAnnotations.ValidationAttribute
                {
                    public override bool IsValid(object? value) => value is int i && i % 2 == 0;
                }
            }
            """;

        var source = innerSource.Replace("[Range(1, 10)]", "[Range(1, 10)]\n        [Even]", StringComparison.Ordinal) + "\n" + attribute;
        return visibleToGateway ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Gateway\")]\n" + source : source;
    }

    internal static string Located(Diagnostic diagnostic)
    {
        var tree = diagnostic.Location.SourceTree!;
        return tree.GetText().ToString(diagnostic.Location.SourceSpan);
    }

    private static string Updated(MultiGeneratorEditResult result, string hintName)
    {
        return result.Updated.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator))
            .Single(s => s.HintName == hintName).SourceText;
    }

    internal static AllGeneratorsRun RunAllGenerators(string[] sources, MetadataReference? innerContract = null)
    {
        var generators = new IIncrementalGenerator[]
        {
            new OperationDescriptorSourceGenerator(),
            new OperationExecutorPipelineSourceGenerator(),
            new AppDataModelSourceGenerator(),
            new TypedClientGenerator(),
        };
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CreateCompilation(sources, innerContract);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators.Select(static g => g.AsSourceGenerator()),
            parseOptions: parseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var first = driver.GetRunResult();
        first.Results.Should().OnlyContain(static r => r.Exception == null);

        var emitErrors = output.Emit(new MemoryStream()).Diagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToList();

        // The descriptor and app generators replay from cache. The pipeline generator re-renders on every compilation, as
        // it always has (its tests opt out with assertCacheability: false).
        var second = driver.RunGenerators(compilation.Clone()).GetRunResult();
        var cached = new[] { 0, 2 }.All(index => second.Results[index].TrackedOutputSteps
            .SelectMany(static s => s.Value)
            .SelectMany(static s => s.Outputs)
            .All(static o => o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));

        return new AllGeneratorsRun(
            first.Diagnostics,
            emitErrors,
            first.GeneratedTrees.ToDictionary(static t => Path.GetFileName(t.FilePath), static t => t.ToString(), StringComparer.Ordinal),
            cached);
    }

    /// <summary>
    /// Builds a referenced assembly as a consumer's is built, with its operations' request builders and its typed clients'
    /// generated halves, which another assembly's generators see only as metadata.
    /// </summary>
    private static MetadataReference CreateReferenceWithClients(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "InnerContract",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TestHelpers.CreateMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver.Create(
                [new OperationDescriptorSourceGenerator().AsSourceGenerator(), new TypedClientGenerator().AsSourceGenerator()],
                parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var diagnostics);
        diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty(string.Join(Environment.NewLine, diagnostics));

        using var stream = new MemoryStream();
        var emit = generated.Emit(stream);
        emit.Success.Should().BeTrue(string.Join(Environment.NewLine, emit.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    internal static CSharpCompilation CreateCompilation(string[] sources, MetadataReference? innerContract = null)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Diagnose);
        var references = TestHelpers.CreateMetadataReferences().Append(innerContract ?? _innerContract.Value);

        return CSharpCompilation.Create(
            "Gateway",
            sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, parseOptions, s.Contains("[Expose<", StringComparison.Ordinal) ? "Gateway.cs" : $"Input{i}.cs")),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    internal sealed record AllGeneratorsRun(
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        IReadOnlyList<string> EmitErrors,
        IReadOnlyDictionary<string, string> Sources,
        bool DescriptorAndAppReplayedFromCache)
    {
        // BP007 asks the app for a serializer context, which the STJ generator would complete; it does not run here.
        public IEnumerable<Diagnostic> GeneratorErrors => this.GeneratorDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error && d.Id != "BP007");

        public string Source(string hintName)
        {
            return this.Sources.TryGetValue(hintName, out var source)
                ? source
                : throw new InvalidOperationException($"No generated source '{hintName}'; generated: {string.Join(", ", this.Sources.Keys)}");
        }
    }

    private sealed class BuildPropertyOptionsProvider(params (string Key, string Value)[] globals)
        : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider
    {
        private readonly Options _global = new(globals.ToDictionary(static g => g.Key, static g => g.Value, StringComparer.Ordinal));

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GlobalOptions => this._global;

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return Options.Empty;
        }

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return Options.Empty;
        }

        private sealed class Options(Dictionary<string, string> values) : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions
        {
            public static readonly Options Empty = new([]);

            public override bool TryGetValue(string key, out string value)
            {
                return values.TryGetValue(key, out value!);
            }
        }
    }
}
