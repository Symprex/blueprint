using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using static Blueprint.SourceGenerator.Tests.Generators.Gateway.ExposeGeneratorTests;

namespace Blueprint.SourceGenerator.Tests.Generators.Gateway;

/// <summary>
/// Declared mode (Symprex/blueprint#68, D10 and D16): <c>[ForwardsTo&lt;TInner&gt;]</c> on a gateway operation that writes
/// out its own public properties. Each declared property with an inner counterpart, by name or by <c>[ForwardAs]</c>, takes
/// the inner property's validation rules and docs where it declares none of its own, and the generated forward copies it.
/// </summary>
public class ForwardsToGeneratorTests
{
    private const string InnerSource = """
        namespace Inner
        {
            using System.ComponentModel.DataAnnotations;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            public enum ImportRunType
            {
                Full,
                Delta,
            }

            public class ImportRunResult : ApiResource
            {
                public string Id { get; set; } = "";
            }

            [RootLink("/tenants/{TenantId}/importRuns")]
            [HttpPost]
            public partial class CreateImportRunCommand : ICommand<ImportRunResult>
            {
                /// <summary>The tenant the run belongs to.</summary>
                public string TenantId { get; set; } = "";

                /// <summary>The name of the source to import from.</summary>
                [Required]
                [MaxLength(100)]
                public required string SourceName { get; init; }

                /// <summary>Whether the run imports everything or only what changed.</summary>
                public ImportRunType RequestedRunType { get; set; }

                /// <summary>The source's own identifier.</summary>
                [MaxLength(40)]
                public string? SourceId { get; set; }

                /// <summary>Who started the run.</summary>
                [Required]
                public string? Owner { get; set; }
            }
        }
        """;

    private const string ClientSource = """
        namespace Gateway
        {
            using Blueprint.HttpClient;

            [BlueprintClient]
            [BlueprintClientInclude(typeof(Inner.CreateImportRunCommand))]
            public partial class ImportApi;
        }
        """;

    private const string AppBindingSource = """
        [assembly: Blueprint.Model.Attributes.GatewayBind("TenantId", FromClaim = "tenant_id")]
        """;

    private const string GatewaySource = """
        namespace Gateway
        {
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            [ForwardsTo<Inner.CreateImportRunCommand>]
            [MustBeAuthenticated]
            [RootLink("/v1/importRuns")]
            [HttpPost]
            public partial class CreateImportRunCommand : ICommand<Inner.ImportRunResult>
            {
                public required string SourceName { get; init; }

                public Inner.ImportRunType RequestedRunType { get; init; }

                [ForwardAs("SourceId")]
                public string? ExternalId { get; init; }

                public string? Owner { get; set; }

                public string? Comment { get; set; }
            }
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

    private static readonly Lazy<MetadataReference> _innerContract = new(
        static () => TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(InnerSource, "InnerImportContract"));

    [Fact]
    public void Every_generator_together_compiles_the_declared_operation_and_its_forward_and_a_second_run_is_cached()
    {
        var run = Run(GatewaySource);

        run.EmitErrors.Should().BeEmpty();
        run.GeneratorErrors.Should().BeEmpty();
        run.GeneratorDiagnostics.Should().NotContain(static d => d.Id.StartsWith("BPGW", StringComparison.Ordinal));
        run.DescriptorAndAppReplayedFromCache.Should().BeTrue();

        // Declared mode writes nothing into the partial: the class declares its public shape itself.
        run.Sources.Keys.Should().NotContain(static k => k.EndsWith("_ExposedProperties.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void The_forward_copies_same_named_properties_maps_a_rename_and_fills_a_binding()
    {
        var forward = ForwardInitialiser(Run(GatewaySource));

        forward.Should().Be(
            "new global::Inner.CreateImportRunCommand { SourceName = operation.SourceName, RequestedRunType = operation.RequestedRunType, "
            + "SourceId = operation.ExternalId, Owner = operation.Owner, TenantId = _bound_TenantId }");
    }

    [Fact]
    public void A_declared_property_with_no_rules_of_its_own_is_validated_by_the_inner_properties_rules()
    {
        var pipeline = Run(GatewaySource).Source("Gateway.CreateImportRunCommandExecutorPipeline.g.cs");

        // The inner [MaxLength(100)] on SourceName, and the inner [MaxLength(40)] on SourceId through the rename.
        pipeline.Should().Contain("operation.SourceName.Length > 100")
            .And.Contain("operation.ExternalId.Length > 40")
            .And.Contain("global::System.String.IsNullOrWhiteSpace(operation.Owner)");
    }

    [Theory]
    [InlineData("[ForwardAs(\"Owner\")]\n        private string? HiddenOwner { get; set; }")]
    [InlineData("[ForwardAs(\"Owner\")]\n        public static string? StaticOwner { get; set; }")]
    public void A_rename_on_a_property_that_is_not_public_and_instance_claims_nothing(string member)
    {
        // Every member's [ForwardAs] was read, so a private or static one claimed Owner, and the public Owner stopped
        // being forwarded or validated (Copilot on #169).
        var gateway = GatewaySource.Replace(
            "public string? Comment { get; set; }",
            "public string? Comment { get; set; }\n\n        " + member,
            StringComparison.Ordinal);

        var run = Run(gateway);

        ForwardInitialiser(run).Should().Contain("Owner = operation.Owner");
        ValidationBlock(run).Should().Contain("global::System.String.IsNullOrWhiteSpace(operation.Owner)");
        CompileDescriptors(gateway).Diagnostics.Should().NotContain(static d => d.Id == "BPGW025");
    }

    [Fact]
    public void A_rename_on_a_base_declaration_the_operation_hides_claims_nothing()
    {
        var gateway = GatewaySource
            .Replace(
                "public partial class CreateImportRunCommand : ICommand<Inner.ImportRunResult>",
                "public partial class CreateImportRunCommand : GatewayBase, ICommand<Inner.ImportRunResult>",
                StringComparison.Ordinal)
            .Replace(
                "public string? Comment { get; set; }",
                "public string? Comment { get; set; }\n\n        public new string? Alias { get; set; }",
                StringComparison.Ordinal)
            + """

            namespace Gateway
            {
                using Blueprint.Model.Attributes;

                public class GatewayBase
                {
                    [ForwardAs("Owner")]
                    public string? Alias { get; set; }
                }
            }
            """;

        ForwardInitialiser(Run(gateway)).Should().Contain("Owner = operation.Owner").And.NotContain("operation.Alias");
    }

    [Fact]
    public void A_class_level_validation_attribute_keeps_the_inherited_property_checks()
    {
        // The whole-object fallback returned before any property was planned, and it reads only the gateway's own
        // attributes, so the inherited rules were dropped (Copilot on #169).
        var gateway = GatewaySource
            .Replace(
                "[ForwardsTo<Inner.CreateImportRunCommand>]",
                "[ForwardsTo<Inner.CreateImportRunCommand>]\n    [System.ComponentModel.DataAnnotations.CustomValidation(typeof(Checks), nameof(Checks.Check))]",
                StringComparison.Ordinal)
            + """

            namespace Gateway
            {
                public static class Checks
                {
                    public static System.ComponentModel.DataAnnotations.ValidationResult? Check(object value) => System.ComponentModel.DataAnnotations.ValidationResult.Success;
                }
            }
            """;

        var run = Run(gateway);

        run.EmitErrors.Should().BeEmpty();
        ValidationBlock(run).Should().Contain("operation.SourceName.Length > 100")
            .And.Contain("Validator.TryValidateObject(operation");
    }

    [Fact]
    public void An_inherited_rule_without_a_fast_path_does_not_drop_the_others()
    {
        // [CreditCard] has no generated check, and one rule without one rejected the property's whole plan, so the
        // inherited [Required] was dropped with it (Copilot on #169).
        var inner = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(
            InnerSource.Replace("[Required]\n        public string? Owner", "[Required]\n        [CreditCard]\n        public string? Owner", StringComparison.Ordinal),
            "InnerImportContractWithCard");

        var run = RunAllGenerators([ClientSource, AppBindingSource, GatewaySource, AppSource], inner);

        run.EmitErrors.Should().BeEmpty();
        ValidationBlock(run).Should().Contain("global::System.String.IsNullOrWhiteSpace(operation.Owner)");
    }

    [Fact]
    public void A_declared_property_with_rules_of_its_own_keeps_only_its_own()
    {
        var gateway = GatewaySource.Replace(
            "public required string SourceName { get; init; }",
            "[System.ComponentModel.DataAnnotations.MaxLength(10)]\n        public required string SourceName { get; init; }",
            StringComparison.Ordinal);

        var pipeline = Run(gateway).Source("Gateway.CreateImportRunCommandExecutorPipeline.g.cs");

        pipeline.Should().Contain("operation.SourceName.Length > 10").And.NotContain("operation.SourceName.Length > 100");
    }

    [Fact]
    public void A_declared_property_with_no_inner_counterpart_takes_no_rules_and_is_not_forwarded()
    {
        var run = Run(GatewaySource);

        ForwardInitialiser(run).Should().NotContain("Comment");
        ValidationBlock(run).Should().NotContain("Comment");
    }

    [Fact]
    public async Task The_generated_forward_and_its_inherited_validation_match_the_snapshot()
    {
        var run = Run(GatewaySource);

        run.EmitErrors.Should().BeEmpty();

        await Verify(run.Source("Gateway.CreateImportRunCommandExecutorPipeline.g.cs")).UseDirectory("Snapshots");
    }

    [Fact]
    public void The_model_gives_a_declared_property_the_inner_properties_rules_and_docs_where_it_has_none_of_its_own()
    {
        var gateway = GatewaySource.Replace(
            "public string? Owner { get; set; }",
            "/// <summary>The gateway's own owner text.</summary>\n        public string? Owner { get; set; }",
            StringComparison.Ordinal);
        var compilation = CreateCompilation([InnerSource, gateway], TestHelpers.CreateReferenceFromSource("namespace Unused { }", "Unused"));

        var model = OperationPropertyModelBuilder.Build(compilation.GetTypeByMetadataName("Gateway.CreateImportRunCommand")!);

        var sourceName = model.Properties.Single(static p => p.Name == "SourceName");
        sourceName.Origin.Should().Be(OperationPropertyOrigin.Declared);
        sourceName.Attributes.Should().Equal(
            "global::System.ComponentModel.DataAnnotations.RequiredAttribute",
            "global::System.ComponentModel.DataAnnotations.MaxLengthAttribute(100)");
        sourceName.DocumentationCommentXml.Should().Contain("The name of the source to import from.");

        var externalId = model.Properties.Single(static p => p.Name == "ExternalId");
        externalId.Attributes.Should().Equal(
            "global::Blueprint.Model.Attributes.ForwardAsAttribute(\"SourceId\")",
            "global::System.ComponentModel.DataAnnotations.MaxLengthAttribute(40)");
        externalId.DocumentationCommentXml.Should().Contain("The source's own identifier.");

        model.Properties.Single(static p => p.Name == "Owner").DocumentationCommentXml.Should().Contain("The gateway's own owner text.")
            .And.NotContain("Who started the run.");
        model.Properties.Single(static p => p.Name == "Comment").Attributes.Should().BeEmpty();
    }

    [Fact]
    public void The_descriptor_documents_a_declared_property_with_the_inner_properties_summary_where_it_has_none_of_its_own()
    {
        // An <inheritdoc> alone, which satisfies documentation analysers, is no documentation of the property's own.
        var gateway = GatewaySource
            .Replace(
                "public string? Owner { get; set; }",
                "/// <summary>The gateway's own owner text.</summary>\n        public string? Owner { get; set; }",
                StringComparison.Ordinal)
            .Replace(
                "public required string SourceName { get; init; }",
                "/// <inheritdoc cref=\"Inner.CreateImportRunCommand.SourceName\"/>\n        public required string SourceName { get; init; }",
                StringComparison.Ordinal);

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([InnerSource, AppBindingSource, gateway]);

        var descriptor = result.Output.Single(static t => t.FilePath.EndsWith("Gateway.CreateImportRunCommand_Descriptor.g.cs", StringComparison.Ordinal)).ToString();
        descriptor.Should().Contain("The name of the source to import from.")
            .And.Contain("The source's own identifier.")
            .And.Contain("The gateway's own owner text.")
            .And.NotContain("Who started the run.");
    }

    [Fact]
    public void When_a_declared_property_has_another_type_than_the_inner_one_then_BPGW022_is_reported_at_the_property()
    {
        var gateway = GatewaySource.Replace(
            "public Inner.ImportRunType RequestedRunType { get; init; }",
            "public string RequestedRunType { get; init; } = \"\";",
            StringComparison.Ordinal);

        var diagnostic = SingleDescriptorDiagnostic(gateway, "BPGW022");

        Located(diagnostic).Should().Be("RequestedRunType");
        diagnostic.GetMessage().Should().Contain("CreateImportRunCommand").And.Contain("string").And.Contain("ImportRunType");
    }

    [Fact]
    public void When_a_renamed_property_has_another_type_than_its_target_then_BPGW022_is_reported_at_the_property()
    {
        var gateway = GatewaySource.Replace("public string? ExternalId { get; init; }", "public int ExternalId { get; init; }", StringComparison.Ordinal);

        var diagnostic = SingleDescriptorDiagnostic(gateway, "BPGW022");

        Located(diagnostic).Should().Be("ExternalId");
    }

    [Fact]
    public void A_same_named_property_of_another_type_is_not_a_mismatch_once_a_rename_moves_it_off_the_inner_property()
    {
        var gateway = GatewaySource
            .Replace(
                "public Inner.ImportRunType RequestedRunType { get; init; }",
                "public Inner.ImportRunType RunType { get; init; }\n\n        [ForwardAs(\"RequestedRunType\")]\n        public Inner.ImportRunType RequestedRunTypeValue { get; init; }\n\n        public string? RequestedRunType { get; init; }",
                StringComparison.Ordinal);

        var result = CompileDescriptors(gateway);

        result.Diagnostics.Should().NotContain(static d => d.Id == "BPGW022");
    }

    [Fact]
    public void A_nullable_annotation_alone_is_not_a_type_mismatch()
    {
        var gateway = GatewaySource.Replace("public required string SourceName { get; init; }", "public required string? SourceName { get; init; }", StringComparison.Ordinal);

        CompileDescriptors(gateway).Diagnostics.Should().NotContain(static d => d.Id == "BPGW022");
    }

    [Fact]
    public void When_a_rename_names_no_settable_inner_property_then_BPGW023_is_reported()
    {
        var gateway = GatewaySource.Replace("[ForwardAs(\"SourceId\")]", "[ForwardAs(\"SourceKey\")]", StringComparison.Ordinal);

        var diagnostic = SingleDescriptorDiagnostic(gateway, "BPGW023");

        Located(diagnostic).Should().Be("ForwardAs(\"SourceKey\")");
        diagnostic.GetMessage().Should().Contain("SourceKey").And.Contain("ExternalId");
    }

    [Fact]
    public void When_two_declared_properties_forward_to_one_inner_property_then_BPGW024_is_reported()
    {
        var gateway = GatewaySource.Replace(
            "public string? Comment { get; set; }",
            "[ForwardAs(\"SourceId\")]\n        public string? OtherId { get; set; }",
            StringComparison.Ordinal);

        var diagnostic = SingleDescriptorDiagnostic(gateway, "BPGW024");

        diagnostic.GetMessage().Should().Contain("SourceId").And.Contain("ExternalId").And.Contain("OtherId");
    }

    [Fact]
    public void When_a_rename_targets_a_bound_inner_property_then_BPGW024_is_reported()
    {
        var gateway = GatewaySource.Replace("[ForwardAs(\"SourceId\")]", "[ForwardAs(\"TenantId\")]", StringComparison.Ordinal)
            .Replace("public string? ExternalId { get; init; }", "public string ExternalId { get; init; } = \"\";", StringComparison.Ordinal);

        var diagnostic = SingleDescriptorDiagnostic(gateway, "BPGW024");

        diagnostic.GetMessage().Should().Contain("TenantId");
    }

    [Fact]
    public void When_an_inner_property_marked_Required_is_supplied_by_nothing_then_BPGW025_is_reported()
    {
        var gateway = GatewaySource.Replace("public string? Owner { get; set; }", string.Empty, StringComparison.Ordinal);

        var diagnostic = SingleDescriptorDiagnostic(gateway, "BPGW025");

        diagnostic.GetMessage().Should().Contain("Owner").And.Contain("CreateImportRunCommand");
    }

    [Fact]
    public void An_inner_property_marked_Required_and_supplied_by_a_binding_is_not_reported()
    {
        var gateway = GatewaySource.Replace("public string? Owner { get; set; }", string.Empty, StringComparison.Ordinal)
            .Replace("[ForwardsTo<Inner.CreateImportRunCommand>]", "[ForwardsTo<Inner.CreateImportRunCommand>]\n    [Bind(\"Owner\", FromClaim = \"sub\")]", StringComparison.Ordinal);

        CompileDescriptors(gateway).Diagnostics.Should().NotContain(static d => d.Id == "BPGW025" || d.Id == "BPGW026");
    }

    [Fact]
    public void When_only_the_map_hook_could_supply_an_inner_property_marked_Required_then_BPGW026_warns()
    {
        var gateway = GatewaySource.Replace(
            "public string? Owner { get; set; }",
            "internal void OnMap(Inner.CreateImportRunCommand inner) { inner.Owner = this.Comment; }",
            StringComparison.Ordinal);

        var result = CompileDescriptors(gateway);

        result.Diagnostics.Should().NotContain(static d => d.Id == "BPGW025");
        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW026").Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
        diagnostic.GetMessage().Should().Contain("Owner").And.Contain("Bind").And.Contain("Hide");
    }

    [Fact]
    public void When_a_required_inner_member_is_supplied_by_nothing_then_the_forward_reports_BPGW019()
    {
        var gateway = GatewaySource.Replace("public required string SourceName { get; init; }", string.Empty, StringComparison.Ordinal);

        var run = Run(gateway);

        var diagnostic = run.GeneratorDiagnostics.Should().ContainSingle(static d => d.Id == "BPGW019").Which;
        diagnostic.GetMessage().Should().Contain("SourceName");
    }

    [Fact]
    public void When_a_rename_is_used_without_ForwardsTo_then_BPGW027_is_reported()
    {
        var gateway = """
            namespace Gateway
            {
                using Blueprint.Model;
                using Blueprint.Model.Attributes;

                [AllowAnonymous]
                [RootLink("/plain")]
                public partial class PlainQuery : IQuery<Inner.ImportRunResult>
                {
                    [ForwardAs("SourceId")]
                    public string? ExternalId { get; set; }

                    public Inner.ImportRunResult Invoke() => new();
                }
            }
            """;

        var diagnostic = SingleDescriptorDiagnostic(gateway, "BPGW027");

        diagnostic.GetMessage().Should().Contain("PlainQuery").And.Contain("ExternalId");
    }

    [Fact]
    public void When_an_operation_both_exposes_and_forwards_to_an_inner_operation_then_BPGW028_is_reported()
    {
        var gateway = GatewaySource.Replace(
            "[ForwardsTo<Inner.CreateImportRunCommand>]",
            "[ForwardsTo<Inner.CreateImportRunCommand>]\n    [Expose<Inner.CreateImportRunCommand>]",
            StringComparison.Ordinal);

        SingleDescriptorDiagnostic(gateway, "BPGW028").GetMessage().Should().Contain("CreateImportRunCommand");
    }

    [Fact]
    public void A_declared_operation_without_an_authorisation_declaration_reports_BPGW002()
    {
        var gateway = GatewaySource.Replace("[MustBeAuthenticated]", string.Empty, StringComparison.Ordinal);

        SingleDescriptorDiagnostic(gateway, "BPGW002");
    }

    [Fact]
    public void A_declared_operation_with_a_hand_written_handler_reports_BPGW005()
    {
        var gateway = GatewaySource.Replace(
            "public string? Comment { get; set; }",
            "public string? Comment { get; set; }\n\n        public Inner.ImportRunResult Invoke() => new();",
            StringComparison.Ordinal);

        Run(gateway).GeneratorDiagnostics.Should().ContainSingle(static d => d.Id == "BPGW005");
    }

    [Fact]
    public void A_declared_operation_reports_the_binding_tiers_diagnostics()
    {
        var gateway = GatewaySource.Replace(
            "[ForwardsTo<Inner.CreateImportRunCommand>]",
            "[ForwardsTo<Inner.CreateImportRunCommand>]\n    [Hide(\"NoSuchProperty\")]",
            StringComparison.Ordinal);

        SingleDescriptorDiagnostic(gateway, "BPGW007");
    }

    /// <summary>
    /// The <c>new TInner { … }</c> the forward sends.
    /// </summary>
    private static string ForwardInitialiser(AllGeneratorsRun run)
    {
        var pipeline = run.Source("Gateway.CreateImportRunCommandExecutorPipeline.g.cs");
        var start = pipeline.IndexOf("new global::Inner.CreateImportRunCommand {", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the forward creates the inner operation");
        return pipeline.Substring(start, pipeline.IndexOf('}', start) - start + 1);
    }

    /// <summary>
    /// The generated property checks, which come before the handler stage.
    /// </summary>
    private static string ValidationBlock(AllGeneratorsRun run)
    {
        var pipeline = run.Source("Gateway.CreateImportRunCommandExecutorPipeline.g.cs");
        var end = pipeline.IndexOf("var forwardInner", StringComparison.Ordinal);
        end.Should().BeGreaterThan(0);
        return pipeline.Substring(0, end);
    }

    private static AllGeneratorsRun Run(string gateway)
    {
        return RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], _innerContract.Value);
    }

    private static GenerationResult CompileDescriptors(string gateway)
    {
        return TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([AppBindingSource, gateway], additionalReferences: [_innerContract.Value]);
    }

    private static Diagnostic SingleDescriptorDiagnostic(string gateway, string id)
    {
        var diagnostic = CompileDescriptors(gateway).Diagnostics.Should().ContainSingle(d => d.Id == id).Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        return diagnostic;
    }
}
