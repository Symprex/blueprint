using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using static Blueprint.SourceGenerator.Tests.Generators.Gateway.ExposeGeneratorTests;

namespace Blueprint.SourceGenerator.Tests.Generators.Gateway;

/// <summary>
/// Binding tiers (Symprex/blueprint#66, D11): <c>[assembly: GatewayBind]</c>, <c>[Bind]</c>, <c>[Hide]</c> and the
/// <c>OnMap</c>/<c>OnMapAsync</c> hook. A bound or hidden inner property is left out of the shared property model (D25),
/// so no generator gives it to the gateway operation's public shape, and the generated forward (D12) fills it instead.
/// </summary>
public class GatewayBindingGeneratorTests
{
    private const string InnerSource = """
        namespace Inner
        {
            using System.ComponentModel.DataAnnotations;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            public class NoteResult : ApiResource
            {
                public string TenantId { get; set; } = "";
            }

            [RootLink("/tenants/{TenantId}/notes")]
            [HttpPost]
            public partial class AddNoteCommand : ICommand<NoteResult>
            {
                /// <summary>The tenant the note belongs to.</summary>
                public string TenantId { get; set; } = "";

                /// <summary>The note.</summary>
                [Required]
                public string Text { get; set; } = "";

                /// <summary>Where the note came from.</summary>
                public string? Source { get; set; }

                /// <summary>A note for staff only.</summary>
                public string? InternalNote { get; set; }

                /// <summary>The partner adding the note.</summary>
                public System.Guid PartnerId { get; set; }

                /// <summary>The note's priority.</summary>
                public int Priority { get; set; }
            }
        }
        """;

    private const string ClientSource = """
        namespace Gateway
        {
            using Blueprint.HttpClient;

            [BlueprintClient]
            [BlueprintClientInclude(typeof(Inner.AddNoteCommand))]
            public partial class NoteApi;
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

            [Expose<Inner.AddNoteCommand>]
            [MustBeAuthenticated]
            [RootLink("/gateway/notes")]
            [HttpPost]
            public partial class AddGatewayNoteCommand : ICommand<Inner.NoteResult>;
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

    /// <summary>
    /// A gateway <c>TenantId</c> the forward does not copy, since its type is not the inner property's.
    /// </summary>
    private const string DeclaredTenantIdOfAnotherType = "public int TenantId { get; set; }";

    private static readonly Lazy<MetadataReference> _innerContract = new(
        static () => TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(InnerSource, "InnerNoteContract"));

    [Fact]
    public void An_app_bound_property_is_left_out_of_the_model()
    {
        var model = BuildModel(AppBindingSource, GatewaySource);

        model.Properties.Select(static p => p.Name).Should().Equal("Text", "Source", "InternalNote", "PartnerId", "Priority");
    }

    [Fact]
    public void Without_a_binding_every_inner_property_is_in_the_model()
    {
        var model = BuildModel(GatewaySource);

        model.Properties.Select(static p => p.Name).Should().Equal("TenantId", "Text", "Source", "InternalNote", "PartnerId", "Priority");
    }

    [Fact]
    public void An_op_bound_or_hidden_property_is_left_out_of_the_model()
    {
        var gateway = WithAttributes(
            GatewaySource,
            "[Bind(nameof(Inner.AddNoteCommand.Source), Value = \"partner-portal\")]",
            "[Hide(nameof(Inner.AddNoteCommand.InternalNote))]");

        var model = BuildModel(AppBindingSource, gateway);

        model.Properties.Select(static p => p.Name).Should().Equal("Text", "PartnerId", "Priority");
    }

    [Fact]
    public void A_bound_property_is_absent_from_the_emitted_partial_the_binding_plan_and_the_request_builders()
    {
        var gateway = WithAttributes(GatewaySource, "[Hide(nameof(Inner.AddNoteCommand.InternalNote))]");

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], _innerContract.Value);

        run.EmitErrors.Should().BeEmpty();
        run.GeneratorErrors.Should().BeEmpty();
        run.DescriptorAndAppReplayedFromCache.Should().BeTrue();

        var properties = run.Source("Gateway.AddGatewayNoteCommand_ExposedProperties.g.cs");
        properties.Should().Contain("public string Text { get; set; }")
            .And.NotContain("TenantId")
            .And.NotContain("InternalNote");

        var descriptor = run.Source("Gateway.AddGatewayNoteCommand_Descriptor.g.cs");
        descriptor.Should().Contain("OperationPropertyBindingEntry(\"Text\"")
            .And.NotContain("TenantId")
            .And.NotContain("InternalNote");
    }

    [Fact]
    public void A_GET_and_POST_operation_reads_neither_a_bound_nor_a_hidden_property_from_a_GETs_query_string_or_a_body()
    {
        // Symprex/blueprint#149's rule reads an unannotated property from a GET's query string and any other method's body.
        // It reads the property model, so a property D11 binds or hides is in neither, and only its binding sets it.
        var gateway = WithAttributes(GatewaySource, "[Hide(nameof(Inner.AddNoteCommand.InternalNote))]")
            .Replace("[HttpPost]", "[HttpGet]\n    [HttpPost]", StringComparison.Ordinal);

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], _innerContract.Value);

        run.GeneratorErrors.Should().BeEmpty();
        run.EmitErrors.Should().BeEmpty();

        var pipeline = run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs");
        pipeline.Should().Contain("BindQueryValue(httpContext.Request.Query, \"priority\"")
            .And.Contain("reader.ValueTextEquals(\"priority\"u8)")
            .And.NotContainEquivalentOf("\"tenantId\"")
            .And.NotContainEquivalentOf("\"internalNote\"")
            .And.NotContain("operation.TenantId")
            .And.NotContain("InternalNote")
            .And.Contain("TenantId = _bound_TenantId");
    }

    [Fact]
    public void The_forward_fills_an_app_bound_property_from_the_callers_claim()
    {
        var run = RunAllGenerators([ClientSource, AppBindingSource, GatewaySource, AppSource], _innerContract.Value);

        run.EmitErrors.Should().BeEmpty();
        var pipeline = run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs");
        pipeline.Should().Contain("FindFirst(\"tenant_id\")")
            .And.Contain("TenantId = _bound_TenantId")
            .And.Contain("Text = operation.Text");
    }

    [Fact]
    public void An_op_binding_overrides_the_app_binding_of_the_same_property()
    {
        var gateway = WithAttributes(GatewaySource, "[Bind(nameof(Inner.AddNoteCommand.TenantId), FromClaim = \"account_id\")]");

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], _innerContract.Value);

        run.EmitErrors.Should().BeEmpty();
        var pipeline = run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs");
        pipeline.Should().Contain("FindFirst(\"account_id\")").And.NotContain("\"tenant_id\"");
    }

    [Fact]
    public void A_hide_overrides_the_app_binding_and_leaves_the_property_unset()
    {
        var gateway = WithAttributes(GatewaySource, "[Hide(nameof(Inner.AddNoteCommand.TenantId))]");

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], _innerContract.Value);

        run.EmitErrors.Should().BeEmpty();
        var pipeline = run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs");
        pipeline.Should().NotContain("\"tenant_id\"").And.NotContain("TenantId =");
    }

    [Fact]
    public void Property_and_route_bindings_read_the_gateway_operations_own_properties()
    {
        var gateway = """
            namespace Gateway
            {
                using Blueprint.Model;
                using Blueprint.Model.Attributes;

                [Expose<Inner.AddNoteCommand>]
                [MustBeAuthenticated]
                [RootLink("/gateway/tenants/{tenant}/notes")]
                [HttpPost]
                [Bind(nameof(Inner.AddNoteCommand.TenantId), FromRoute = "tenant")]
                [Bind(nameof(Inner.AddNoteCommand.Source), FromProperty = nameof(Origin))]
                public partial class AddGatewayNoteCommand : ICommand<Inner.NoteResult>
                {
                    public string Tenant { get; set; } = "";

                    public string? Origin { get; set; }
                }
            }
            """;

        var run = RunAllGenerators([ClientSource, gateway, AppSource], _innerContract.Value);

        run.EmitErrors.Should().BeEmpty();
        run.GeneratorErrors.Should().BeEmpty();
        var pipeline = run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs");
        pipeline.Should().Contain("TenantId = operation.Tenant").And.Contain("Source = operation.Origin");
    }

    [Fact]
    public void The_on_map_async_hook_runs_after_the_copy_with_injected_parameters()
    {
        var gateway = WithBody(
            GatewaySource,
            "internal System.Threading.Tasks.ValueTask OnMapAsync(Inner.AddNoteCommand inner, System.TimeProvider clock, System.Threading.CancellationToken cancellationToken) { inner.Text += clock.GetUtcNow().Year; return default; }");

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], _innerContract.Value);

        run.EmitErrors.Should().BeEmpty();
        run.GeneratorErrors.Should().BeEmpty();
        var pipeline = run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs");
        pipeline.Should().Contain("GetRequiredService<global::System.TimeProvider>")
            .And.Contain("await operation.OnMapAsync(forwardInner, _handlerArg_clock, context.OperationCancelled).ConfigureAwait(false);");
        pipeline.IndexOf("OnMapAsync(forwardInner", StringComparison.Ordinal)
            .Should().BeGreaterThan(pipeline.IndexOf("var forwardInner", StringComparison.Ordinal))
            .And.BeLessThan(pipeline.IndexOf("forwardTransport.ForwardAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void The_synchronous_on_map_hook_is_called()
    {
        var gateway = WithBody(GatewaySource, "internal void OnMap(Inner.AddNoteCommand inner) => inner.Priority = 5;");

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], _innerContract.Value);

        run.EmitErrors.Should().BeEmpty();
        run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs").Should().Contain("operation.OnMap(forwardInner);");
    }

    [Theory]
    [InlineData("internal static void OnMap(Inner.AddNoteCommand inner) => inner.Priority = 5;", "global::Gateway.AddGatewayNoteCommand.OnMap(forwardInner);")]
    [InlineData("public static System.Threading.Tasks.ValueTask OnMapAsync(Inner.AddNoteCommand inner, System.TimeProvider clock) { inner.Priority = clock.GetUtcNow().Hour; return default; }", "await global::Gateway.AddGatewayNoteCommand.OnMapAsync(forwardInner, _handlerArg_clock).ConfigureAwait(false);")]
    public void A_static_map_hook_is_called_on_the_operation_type(string hook, string call)
    {
        // Symprex/blueprint#176: a hook that read no instance data used to have to be an instance method, so CA1822 fired.
        var gateway = WithBody(GatewaySource, hook);

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], _innerContract.Value);

        run.GeneratorDiagnostics.Should().NotContain(static d => d.Id == "BPGW014");
        run.GeneratorErrors.Should().BeEmpty();
        run.EmitErrors.Should().BeEmpty();
        var pipeline = run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs");
        pipeline.Should().Contain(call).And.NotContain("operation.OnMap");
    }

    [Fact]
    public void A_static_map_hook_can_fill_a_hidden_required_inner_property()
    {
        var gateway = WithBody(
            WithAttributes(GatewaySource, "[Hide(nameof(Inner.AddNoteCommand.InternalNote))]"),
            "internal static void OnMap(Inner.AddNoteCommand inner) => inner.InternalNote = \"staff\";");

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], RequiredInternalNoteContract());

        run.GeneratorDiagnostics.Should().NotContain(static d => d.Id == "BPGW009" || d.Id == "BPGW014" || d.Id == "BPGW035");
        run.EmitErrors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("internal partial void OnMap(Inner.AddNoteCommand inner);", "internal partial void OnMap(Inner.AddNoteCommand inner) => inner.Priority = 5;", "operation.OnMap(forwardInner);")]
    [InlineData("internal partial System.Threading.Tasks.ValueTask OnMapAsync(Inner.AddNoteCommand inner);", "internal partial System.Threading.Tasks.ValueTask OnMapAsync(Inner.AddNoteCommand inner) => default;", "await operation.OnMapAsync(forwardInner)")]
    public void A_map_hook_split_across_partial_declarations_is_called(string definition, string implementation, string call)
    {
        var gateway = WithBody(GatewaySource, definition);
        const string implementationPart = """
            namespace Gateway
            {
                public partial class AddGatewayNoteCommand
                {
                    IMPLEMENTATION
                }
            }
            """;

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, implementationPart.Replace("IMPLEMENTATION", implementation, StringComparison.Ordinal), AppSource], _innerContract.Value);

        run.EmitErrors.Should().BeEmpty();
        run.GeneratorDiagnostics.Should().NotContain(static d => d.Id == "BPGW014");
        run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs").Should().Contain(call);
    }

    [Fact]
    public async Task The_generated_forward_with_every_binding_tier_matches_the_snapshot()
    {
        var gateway = WithAttributes(
            GatewaySource,
            "[Bind(nameof(Inner.AddNoteCommand.PartnerId), FromClaim = \"partner_id\")]",
            "[Bind(nameof(Inner.AddNoteCommand.Source), Value = \"partner-portal\")]",
            "[Hide(nameof(Inner.AddNoteCommand.InternalNote))]");
        gateway = WithBody(
            gateway,
            "internal System.Threading.Tasks.ValueTask OnMapAsync(Inner.AddNoteCommand inner, System.TimeProvider clock) { inner.Priority = clock.GetUtcNow().Hour; return default; }");

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], _innerContract.Value);

        run.EmitErrors.Should().BeEmpty();

        await Verify(string.Join(
                "\n",
                new[] { "Gateway.AddGatewayNoteCommand_ExposedProperties.g.cs", "Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs" }
                    .Select(hint => hint + ":\n\n" + run.Source(hint))))
            .UseDirectory("Snapshots");
    }

    [Fact]
    public void When_a_binding_names_a_property_the_inner_operation_lacks_then_BPGW007_is_reported()
    {
        var gateway = WithAttributes(GatewaySource, "[Hide(\"Colour\")]");

        var diagnostic = SingleDescriptorDiagnostic(gateway, "BPGW007");

        diagnostic.GetMessage().Should().Contain("Colour").And.Contain("AddNoteCommand");
        Located(diagnostic).Should().StartWith("Hide(");
    }

    [Theory]
    [InlineData("[Bind(nameof(Inner.AddNoteCommand.Source))]")]
    [InlineData("[Bind(nameof(Inner.AddNoteCommand.Source), Value = \"a\", FromClaim = \"b\")]")]
    public void When_a_binding_sets_no_source_or_several_then_BPGW008_is_reported(string binding)
    {
        var diagnostic = SingleDescriptorDiagnostic(WithAttributes(GatewaySource, binding), "BPGW008");

        diagnostic.GetMessage().Should().Contain("Source");
        Located(diagnostic).Should().StartWith("Bind(");
    }

    [Fact]
    public void When_an_app_binding_sets_no_claim_then_BPGW008_is_reported()
    {
        var appBinding = "[assembly: Blueprint.Model.Attributes.GatewayBind(\"TenantId\")]";

        var diagnostic = SingleDescriptorDiagnostic([appBinding, GatewaySource], "BPGW008");

        Located(diagnostic).Should().StartWith("Blueprint.Model.Attributes.GatewayBind(");
    }

    [Fact]
    public void When_a_required_inner_property_is_hidden_then_BPGW009_is_reported()
    {
        var inner = InnerSource.Replace("public string? InternalNote { get; set; }", "public required string InternalNote { get; set; }", StringComparison.Ordinal);
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");
        var gateway = WithAttributes(GatewaySource, "[Hide(nameof(Inner.AddNoteCommand.InternalNote))]");

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([AppBindingSource, gateway], additionalReferences: [contract]);

        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW009").Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("InternalNote").And.Contain("OnMap");
    }

    [Theory]
    [InlineData("internal void OnMap(Inner.AddNoteCommand inner) => inner.InternalNote = \"staff\";")]
    [InlineData("internal System.Threading.Tasks.ValueTask OnMapAsync(Inner.AddNoteCommand inner, System.TimeProvider clock) { inner.InternalNote = clock.GetUtcNow().ToString(); return default; }")]
    public void A_required_inner_property_the_map_hook_assigns_can_be_hidden(string hook)
    {
        // Symprex/blueprint#175: this used to need a placeholder [Bind(…, Value = "")] the hook then overwrote.
        var contract = RequiredInternalNoteContract();
        var gateway = WithBody(WithAttributes(GatewaySource, "[Hide(nameof(Inner.AddNoteCommand.InternalNote))]"), hook);

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], contract);

        run.GeneratorDiagnostics.Should().NotContain(static d => d.Id == "BPGW009" || d.Id == "BPGW019" || d.Id == "BPGW035");
        run.GeneratorErrors.Should().BeEmpty();
        run.EmitErrors.Should().BeEmpty();
        run.Source("Gateway.AddGatewayNoteCommand_ExposedProperties.g.cs").Should().NotContain("InternalNote");
        run.Source("Gateway.AddGatewayNoteCommand_Descriptor.g.cs").Should().NotContain("InternalNote");

        var pipeline = run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs");
        pipeline.Should().Contain("InternalNote = default!")
            .And.Contain("if (forwardInner.InternalNote is null)");
        pipeline.IndexOf("if (forwardInner.InternalNote is null)", StringComparison.Ordinal)
            .Should().BeGreaterThan(pipeline.IndexOf("OnMap", pipeline.IndexOf("var forwardInner", StringComparison.Ordinal), StringComparison.Ordinal))
            .And.BeLessThan(pipeline.IndexOf("forwardTransport.ForwardAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void An_inner_property_with_Required_the_map_hook_assigns_can_be_hidden_and_keeps_its_inner_default()
    {
        var gateway = WithBody(
            WithAttributes(GatewaySource, "[Hide(nameof(Inner.AddNoteCommand.Text))]"),
            "internal void OnMap(Inner.AddNoteCommand inner) { inner.Text = \"from the gateway\"; }");

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], _innerContract.Value);

        run.GeneratorDiagnostics.Should().NotContain(static d => d.Id == "BPGW009" || d.Id == "BPGW035");
        run.EmitErrors.Should().BeEmpty();
        run.Source("Gateway.AddGatewayNoteCommand_ExposedProperties.g.cs").Should().NotContain(" Text ");

        // Not a required member, so the initialiser leaves the inner default alone; [Required] still refuses a null.
        var pipeline = run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs");
        pipeline.Should().NotContain("Text =").And.Contain("if (forwardInner.Text is null)");
    }

    [Fact]
    public void A_required_nullable_value_the_map_hook_assigns_is_hidden_without_a_null_check()
    {
        // CloseTenant's shape: a required int? the hook fills from options, where null may be the value it means to send.
        var inner = InnerSource.Replace("public int Priority { get; set; }", "public required int? Priority { get; set; }", StringComparison.Ordinal);
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");
        var gateway = WithBody(
            WithAttributes(GatewaySource, "[Hide(nameof(Inner.AddNoteCommand.Priority))]"),
            "internal System.Threading.Tasks.Task OnMapAsync(Inner.AddNoteCommand inner, System.TimeProvider clock) { inner.Priority = clock.GetUtcNow().Hour; return System.Threading.Tasks.Task.CompletedTask; }");

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], contract);

        run.GeneratorDiagnostics.Should().NotContain(static d => d.Id == "BPGW009" || d.Id == "BPGW019" || d.Id == "BPGW035");
        run.EmitErrors.Should().BeEmpty();
        var pipeline = run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs");
        pipeline.Should().Contain("Priority = default!").And.NotContain("forwardInner.Priority is null");
    }

    [Theory]
    [InlineData("internal void OnMap(Inner.AddNoteCommand inner) => inner.Priority = 5;")]
    [InlineData("internal System.Threading.Tasks.ValueTask OnMapAsync(Inner.AddNoteCommand inner) { var internalNote = \"staff\"; return default; }")]
    [InlineData("internal void OnMap(Inner.AddNoteCommand inner) { var other = new Inner.AddNoteCommand { TenantId = \"\", Text = \"\", InternalNote = \"staff\" }; }")]
    [InlineData("internal void OnMap(Inner.AddNoteCommand inner) { if (inner.Priority > 0) inner.InternalNote = \"staff\"; }")]
    [InlineData("internal void OnMap(Inner.AddNoteCommand inner) { System.Action set = () => inner.InternalNote = \"staff\"; }")]
    [InlineData("internal void OnMap(Inner.AddNoteCommand inner) { void Set() => inner.InternalNote = \"staff\"; }")]
    [InlineData("internal void OnMap(Inner.AddNoteCommand inner) { if (inner.Priority > 0) { return; } inner.InternalNote = \"staff\"; }")]
    [InlineData("internal System.Threading.Tasks.Task OnMapAsync(Inner.AddNoteCommand inner) { if (inner.Priority > 0) return System.Threading.Tasks.Task.CompletedTask; inner.InternalNote = \"staff\"; return System.Threading.Tasks.Task.CompletedTask; }")]
    [InlineData("internal void OnMap(Inner.AddNoteCommand inner) { inner = new Inner.AddNoteCommand { TenantId = \"\", Text = \"\", InternalNote = \"\" }; inner.InternalNote = \"staff\"; }")]
    [InlineData("internal void OnMap(Inner.AddNoteCommand inner) { Replace(out inner); inner.InternalNote = \"staff\"; } private static void Replace(out Inner.AddNoteCommand inner) => inner = new Inner.AddNoteCommand { TenantId = \"\", Text = \"\", InternalNote = \"\" };")]
    [InlineData("internal void OnMap(Inner.AddNoteCommand inner) { Replace(ref inner); inner.InternalNote = \"staff\"; } private static void Replace(ref Inner.AddNoteCommand inner) { }")]
    [InlineData("internal void OnMap(Inner.AddNoteCommand inner) { if (inner.Priority > 0) goto Done; inner.InternalNote = \"staff\"; Done: ; }")]
    public void When_a_hidden_required_inner_property_is_not_assigned_by_the_map_hook_then_BPGW035_is_reported(string hook)
    {
        var contract = RequiredInternalNoteContract();
        var gateway = WithBody(WithAttributes(GatewaySource, "[Hide(nameof(Inner.AddNoteCommand.InternalNote))]"), hook);

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([AppBindingSource, gateway], additionalReferences: [contract]);

        result.Diagnostics.Should().NotContain(static d => d.Id == "BPGW009");
        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW035").Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("AddGatewayNoteCommand").And.Contain("InternalNote").And.Contain(hook.Contains("OnMapAsync", StringComparison.Ordinal) ? "OnMapAsync" : "OnMap");
    }

    [Fact]
    public void A_required_nullable_value_the_map_hook_assigns_only_conditionally_is_BPGW035()
    {
        // No null check backs a required int?, so a skipped assignment would send the initial null (#180 review).
        var inner = InnerSource.Replace("public int Priority { get; set; }", "public required int? Priority { get; set; }", StringComparison.Ordinal);
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");
        var gateway = WithBody(
            WithAttributes(GatewaySource, "[Hide(nameof(Inner.AddNoteCommand.Priority))]"),
            "internal void OnMap(Inner.AddNoteCommand inner) { if (inner.Text.Length > 0) { inner.Priority = 5; } }");

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([AppBindingSource, gateway], additionalReferences: [contract]);

        result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW035");
    }

    [Theory]
    [InlineData("internal void OnMap(Inner.AddNoteCommand inner) { if (inner.Priority < 0) { throw new System.InvalidOperationException(); } inner.InternalNote = null; }")]
    [InlineData("internal System.Threading.Tasks.Task OnMapAsync(Inner.AddNoteCommand inner) { { inner.InternalNote ??= \"staff\"; } return System.Threading.Tasks.Task.CompletedTask; }")]
    [InlineData("internal void OnMap(Inner.AddNoteCommand inner) { System.Func<int> one = () => { return 1; }; inner.InternalNote = \"staff\"; }")]
    public void An_unconditional_assignment_after_a_guard_or_in_a_nested_block_satisfies_the_map_hook_check(string hook)
    {
        var gateway = WithBody(WithAttributes(GatewaySource, "[Hide(nameof(Inner.AddNoteCommand.InternalNote))]"), hook);

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([AppBindingSource, gateway], additionalReferences: [RequiredInternalNoteContract()]);

        result.Diagnostics.Should().NotContain(static d => d.Id == "BPGW009" || d.Id == "BPGW035");
    }

    [Fact]
    public void A_map_hook_read_from_metadata_keeps_its_binding()
    {
        // #180 review: a gateway operation an app scans from a referenced assembly has a hook with no syntax to check. Its
        // own build checked it; dropping the binding would make the forward copy a property the gateway does not have.
        var inner = InnerSource.Replace("public string? InternalNote { get; set; }", "public required string InternalNote { get; set; }", StringComparison.Ordinal);
        var gateway = WithBody(
            WithAttributes(GatewaySource, "[Hide(nameof(Inner.AddNoteCommand.InternalNote))]"),
            "public static void OnMap(Inner.AddNoteCommand inner) => inner.InternalNote = \"staff\";");
        var contract = TestHelpers.CreateReferenceFromSource(inner + "\n" + gateway, "GatewayContract");
        var compilation = CreateCompilation(["namespace App { }"], contract);

        var bindings = OperationPropertyModelBuilder.GetBindings(compilation.GetTypeByMetadataName("Gateway.AddGatewayNoteCommand")!);

        bindings.Should().ContainSingle(static b => b.PropertyName == "InternalNote").Which.Source.Should().Be(Blueprint.SourceGenerator.Modules.Gateway.GatewayBindingSource.MapHook);
    }

    [Fact]
    public void A_required_inner_property_whose_constructor_sets_required_members_gets_no_placeholder()
    {
        // #180 review: the [SetsRequiredMembers] constructor's own value would be replaced by a null before the hook ran.
        var inner = InnerSource
            .Replace("public string? InternalNote { get; set; }", "public required string InternalNote { get; set; }", StringComparison.Ordinal)
            .Replace(
                "/// <summary>The tenant the note belongs to.</summary>",
                "[System.Diagnostics.CodeAnalysis.SetsRequiredMembers] public AddNoteCommand() { this.InternalNote = \"none\"; }\n\n/// <summary>The tenant the note belongs to.</summary>",
                StringComparison.Ordinal);
        inner.Should().Contain("SetsRequiredMembers");
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");
        var gateway = WithBody(
            WithAttributes(GatewaySource, "[Hide(nameof(Inner.AddNoteCommand.InternalNote))]"),
            "internal void OnMap(Inner.AddNoteCommand inner) => inner.InternalNote = \"staff\";");

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], contract);

        run.EmitErrors.Should().BeEmpty();
        run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs").Should().NotContain("InternalNote = default!");
    }

    [Theory]
    [InlineData("public string Text { private get; set; } = \"\";")]
    [InlineData("public string Text { set { } }")]
    public void A_Required_inner_property_without_a_public_getter_is_filled_without_a_null_check(string declaration)
    {
        // #180 review: the check would read a getter the pipeline cannot call (CS0271, CS0154).
        var inner = InnerSource.Replace("public string Text { get; set; } = \"\";", declaration, StringComparison.Ordinal);
        inner.Should().Contain(declaration);
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");
        var gateway = WithBody(
            WithAttributes(GatewaySource, "[Hide(nameof(Inner.AddNoteCommand.Text))]"),
            "internal void OnMap(Inner.AddNoteCommand inner) => inner.Text = \"from the gateway\";");

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], contract);

        run.GeneratorDiagnostics.Should().NotContain(static d => d.Id == "BPGW009" || d.Id == "BPGW035");
        run.EmitErrors.Should().BeEmpty();
        run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs").Should().NotContain("forwardInner.Text is null");
    }

    [Fact]
    public async Task The_generated_forward_of_a_property_the_map_hook_fills_matches_the_snapshot()
    {
        var gateway = WithBody(
            WithAttributes(GatewaySource, "[Hide(nameof(Inner.AddNoteCommand.InternalNote))]"),
            "internal void OnMap(Inner.AddNoteCommand inner) => inner.InternalNote = \"staff\";");

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], RequiredInternalNoteContract());

        run.EmitErrors.Should().BeEmpty();

        await Verify(run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs")).UseDirectory("Snapshots");
    }

    [Fact]
    public void A_required_inner_property_the_forward_sets_from_a_binding_is_not_BPGW019()
    {
        // The command's TenantId, not the result's, which comes first.
        const string tenantId = "public string TenantId { get; set; } = \"\";";
        var index = InnerSource.IndexOf(tenantId, InnerSource.IndexOf("The tenant the note belongs to", StringComparison.Ordinal), StringComparison.Ordinal);
        var inner = InnerSource[..index] + "public required string TenantId { get; set; }" + InnerSource[(index + tenantId.Length)..];
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");

        var run = RunAllGenerators([ClientSource, AppBindingSource, GatewaySource, AppSource], contract);

        run.GeneratorDiagnostics.Should().NotContain(static d => d.Id == "BPGW019");
        run.EmitErrors.Should().BeEmpty();
        run.Sources.Values.Should().Contain(static s => s.Contains("TenantId = _bound_TenantId", StringComparison.Ordinal));
    }

    [Fact]
    public void When_an_inner_route_placeholder_the_gateway_route_lacks_is_contributed_then_BPGW010_is_not_reported_and_the_forward_sets_it()
    {
        // The gateway takes the contributed TenantId from its query string, and the forward copies it into the inner
        // operation, so the inner request builder has its route value (#161).
        var run = RunAllGenerators([ClientSource, GatewaySource, AppSource], _innerContract.Value);

        run.GeneratorDiagnostics.Should().NotContain(static d => d.Id == "BPGW010");
        run.EmitErrors.Should().BeEmpty();
        run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs").Should().Contain("TenantId = operation.TenantId");
    }

    [Fact]
    public void When_an_inner_route_placeholder_the_gateway_route_lacks_is_neither_copied_nor_bound_nor_hidden_then_BPGW010_is_reported()
    {
        // A declared property of another type is the gateway's own, so nothing gives the inner property a value.
        var diagnostic = SingleDescriptorDiagnostic([WithBody(GatewaySource, DeclaredTenantIdOfAnotherType)], "BPGW010");

        diagnostic.GetMessage().Should().Contain("TenantId").And.Contain("/tenants/{TenantId}/notes");
        Located(diagnostic).Should().StartWith("Expose<");
    }

    [Fact]
    public void When_an_inner_defaulted_placeholder_binds_a_property_that_cannot_be_null_then_BPGW010_is_reported()
    {
        // The inner request builder always sends an int, so it would send 0 and the route's default would never apply.
        var inner = InnerSource.Replace("/tenants/{TenantId}/notes", "/notes/{Priority:int=1}", StringComparison.Ordinal);
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([WithBody(GatewaySource, "public string Priority { get; set; } = \"\";")], additionalReferences: [contract]);

        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW010").Which;
        diagnostic.GetMessage().Should().Contain("Priority").And.Contain("/notes/{Priority:int=1}");
    }

    [Theory]
    [InlineData("/notes/{Source=web}")]
    [InlineData("/notes/{Source?}")]
    public void When_an_inner_optional_or_defaulted_placeholder_binds_a_nullable_property_then_BPGW010_is_not_reported(string route)
    {
        // The inner request builder sends the default, or leaves the placeholder out, when the property is null.
        var inner = InnerSource.Replace("/tenants/{TenantId}/notes", route, StringComparison.Ordinal);
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([WithBody(GatewaySource, "public int Source { get; set; }")], additionalReferences: [contract]);

        result.Diagnostics.Should().NotContain(static d => d.Id == "BPGW010");
    }

    [Fact]
    public void When_the_gateway_fills_an_inner_optional_segment_but_not_one_before_it_then_BPGW029_is_reported()
    {
        // Source is the gateway's own int, so the forward leaves the inner Source null while InternalNote, copied, may be
        // set: /notes/{InternalNote} would bind InternalNote's value to Source, so the inner client builds no URL.
        var inner = InnerSource.Replace("/tenants/{TenantId}/notes", "/notes/{Source?}/{InternalNote?}", StringComparison.Ordinal);
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([WithBody(GatewaySource, "public int Source { get; set; }")], additionalReferences: [contract]);

        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW029").Which;
        diagnostic.GetMessage().Should().Contain("'Source'").And.Contain("'InternalNote'").And.Contain("/notes/{Source?}/{InternalNote?}");
        result.Diagnostics.Should().NotContain(static d => d.Id == "BPGW010");
    }

    [Theory]
    [InlineData("/notes/{Source?}/{InternalNote?}", "")]
    [InlineData("/notes/{Text}.{Source?}/{InternalNote?}", "public int Source { get; set; }")]
    [InlineData("/notes/{Source?}/{InternalNote?}", "public int Source { get; set; }\n    public int InternalNote { get; set; }")]
    public void When_every_inner_optional_segment_before_one_the_gateway_fills_can_be_filled_too_then_BPGW029_is_not_reported(string route, string body)
    {
        // Copied, both can be set; after a '.', Source leaves its segment there; and with neither filled, nothing moves.
        var inner = InnerSource.Replace("/tenants/{TenantId}/notes", route, StringComparison.Ordinal);
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");
        var gateway = body.Length == 0 ? GatewaySource : WithBody(GatewaySource, body);

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([gateway], additionalReferences: [contract]);

        result.Diagnostics.Should().NotContain(static d => d.Id == "BPGW029");
    }

    [Theory]
    [InlineData("[Hide(nameof(Inner.AddNoteCommand.Source))]")]
    [InlineData("[Hide(nameof(Inner.AddNoteCommand.Source))]\n    [Bind(nameof(Inner.AddNoteCommand.InternalNote), Value = null)]")]
    public void When_the_gateway_hides_an_inner_optional_segment_or_binds_the_later_one_to_null_then_BPGW029_is_not_reported(string attributes)
    {
        // Hiding leaves Source to the inner operation's initialiser or the gateway's OnMap, which may set it, as it does for
        // BPGW010; and InternalNote bound to null is never set, so nothing moves into Source's place.
        var inner = InnerSource.Replace("/tenants/{TenantId}/notes", "/notes/{Source?}/{InternalNote?}", StringComparison.Ordinal);
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([WithAttributes(GatewaySource, attributes)], additionalReferences: [contract]);

        result.Diagnostics.Should().NotContain(static d => d.Id == "BPGW029");
    }

    [Fact]
    public void When_the_gateway_route_has_an_inner_optional_segment_its_property_does_not_copy_then_BPGW029_is_reported()
    {
        // The gateway's own int Source fills its route, but is not copied into the inner string? Source, so the forward
        // still leaves that null before the copied InternalNote.
        var inner = InnerSource.Replace("/tenants/{TenantId}/notes", "/notes/{Source?}/{InternalNote?}", StringComparison.Ordinal);
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");
        var gateway = WithBody(
            GatewaySource.Replace("[RootLink(\"/gateway/notes\")]", "[RootLink(\"/gateway/notes/{Source}\")]", StringComparison.Ordinal),
            "public int Source { get; set; }");

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([gateway], additionalReferences: [contract]);

        result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW029");
    }

    [Fact]
    public void When_the_gateway_binds_an_inner_optional_segment_to_null_before_one_it_fills_then_BPGW029_is_reported()
    {
        // A null value sets Source to null on every forward, so InternalNote, copied, moves into its place whenever it is set.
        var inner = InnerSource.Replace("/tenants/{TenantId}/notes", "/notes/{Source?}/{InternalNote?}", StringComparison.Ordinal);
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([WithAttributes(GatewaySource, "[Bind(nameof(Inner.AddNoteCommand.Source), Value = null)]")], additionalReferences: [contract]);

        result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW029");
    }

    [Theory]
    [InlineData(AppBindingSource, "")]
    [InlineData("", "[Hide(nameof(Inner.AddNoteCommand.TenantId))]")]
    [InlineData("", "[Bind(nameof(Inner.AddNoteCommand.TenantId), Value = \"t\")]")]
    public void When_the_inner_route_placeholder_is_bound_or_hidden_then_BPGW010_is_not_reported(string appBinding, string opBinding)
    {
        var gateway = opBinding.Length == 0 ? GatewaySource : WithAttributes(GatewaySource, opBinding);

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([appBinding, gateway], additionalReferences: [_innerContract.Value]);

        result.Diagnostics.Should().NotContain(static d => d.Id.StartsWith("BPGW", StringComparison.Ordinal));
    }

    [Fact]
    public void When_the_gateway_route_has_the_inner_placeholder_then_BPGW010_is_not_reported()
    {
        var gateway = GatewaySource.Replace("[RootLink(\"/gateway/notes\")]", "[RootLink(\"/gateway/tenants/{TenantId}/notes\")]", StringComparison.Ordinal);

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([gateway], additionalReferences: [_innerContract.Value]);

        result.Diagnostics.Should().NotContain(static d => d.Id.StartsWith("BPGW", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[Bind(nameof(Inner.AddNoteCommand.Priority), Value = \"high\")]", "Priority")]
    [InlineData("[Bind(nameof(Inner.AddNoteCommand.Source), FromProperty = nameof(Count))]", "Source")]
    [InlineData("[Bind(nameof(Inner.AddNoteCommand.Priority), FromClaim = \"priority_bag\")]", null)]
    public void When_a_binding_source_does_not_convert_to_the_inner_type_then_BPGW011_is_reported(string binding, string? property)
    {
        // A claim converts to any type route binding converts to, so only a type route binding cannot read fails.
        var inner = property is null
            ? InnerSource.Replace("public int Priority { get; set; }", "public System.Collections.Generic.List<int> Priority { get; set; } = new();", StringComparison.Ordinal)
            : InnerSource;
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");
        var gateway = WithAttributes(GatewaySource, binding).Replace(
            "public partial class AddGatewayNoteCommand : ICommand<Inner.NoteResult>;",
            "public partial class AddGatewayNoteCommand : ICommand<Inner.NoteResult> { public int Count { get; set; } }",
            StringComparison.Ordinal);

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([AppBindingSource, gateway], additionalReferences: [contract]);

        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW011").Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain(property ?? "Priority");
    }

    [Theory]
    [InlineData("[Bind(nameof(Inner.AddNoteCommand.Source), FromProperty = \"Missing\")]", "Missing")]
    [InlineData("[Bind(nameof(Inner.AddNoteCommand.Source), FromRoute = \"missing\")]", "missing")]
    public void When_a_binding_source_is_not_on_the_gateway_operation_then_BPGW012_is_reported(string binding, string source)
    {
        var diagnostic = SingleDescriptorDiagnostic(WithAttributes(GatewaySource, binding), "BPGW012");

        diagnostic.GetMessage().Should().Contain(source);
    }

    [Theory]
    [InlineData("public string? Origin { private get; set; }", "FromProperty")]
    [InlineData("public string? Origin { set { } }", "FromProperty")]
    [InlineData("public string? Origin { private get; set; }", "FromRoute")]
    public void When_a_binding_reads_a_property_without_a_public_getter_then_BPGW012_is_reported(string origin, string source)
    {
        var gateway = WithBody(
            WithAttributes(GatewaySource, "[Bind(nameof(Inner.AddNoteCommand.Source), " + source + " = \"Origin\")]")
                .Replace("/gateway/notes", "/gateway/notes/{Origin}", StringComparison.Ordinal),
            origin);

        var diagnostic = SingleDescriptorDiagnostic(gateway, "BPGW012");

        diagnostic.GetMessage().Should().Contain("Origin");
    }

    [Fact]
    public void A_null_value_binding_sets_the_inner_property_to_null()
    {
        var gateway = WithAttributes(GatewaySource, "[Bind(nameof(Inner.AddNoteCommand.Source), Value = null)]");

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], _innerContract.Value);

        run.EmitErrors.Should().BeEmpty();
        run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs").Should().Contain("Source = null");
    }

    [Theory]
    [InlineData("/notes/{**TenantId}")]
    [InlineData("/notes/{*TenantId}")]
    public void When_an_unbound_inner_catch_all_placeholder_is_not_in_the_gateway_route_then_BPGW010_is_reported(string route)
    {
        var inner = InnerSource.Replace("/tenants/{TenantId}/notes", route, StringComparison.Ordinal);
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([WithBody(GatewaySource, DeclaredTenantIdOfAnotherType)], additionalReferences: [contract]);

        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW010").Which;
        diagnostic.GetMessage().Should().Contain("TenantId").And.Contain(route);
    }

    [Theory]
    [InlineData(AppBindingSource, "")]
    [InlineData("", "[Bind(nameof(Inner.AddNoteCommand.TenantId), FromClaim = \"tenant_id\")]")]
    [InlineData("", "[Hide(nameof(Inner.AddNoteCommand.TenantId))]")]
    [InlineData("", "[Bind(nameof(Inner.AddNoteCommand.TenantId), FromProperty = \"Other\")]")]
    public void When_a_binding_fills_a_property_the_gateway_declares_from_elsewhere_then_BPGW021_is_reported(string appBinding, string opBinding)
    {
        var gateway = WithBody(
            opBinding.Length == 0 ? GatewaySource : WithAttributes(GatewaySource, opBinding),
            "public string TenantId { get; set; } = \"\";\n    public string Other { get; set; } = \"\";");

        var diagnostic = SingleDescriptorDiagnostic([appBinding, gateway], "BPGW021");

        diagnostic.GetMessage().Should().Contain("AddGatewayNoteCommand").And.Contain("TenantId");
    }

    [Theory]
    [InlineData("[FromQuery] public string? Origin { get; set; }")]
    [InlineData("public string? Origin => null;")]
    public void When_a_route_binding_reads_a_property_the_route_does_not_bind_then_BPGW012_is_reported(string origin)
    {
        var gateway = WithBody(
            WithAttributes(GatewaySource, "[Bind(nameof(Inner.AddNoteCommand.Source), FromRoute = \"Origin\")]")
                .Replace("/gateway/notes", "/gateway/notes/{Origin}", StringComparison.Ordinal),
            origin);

        var diagnostic = SingleDescriptorDiagnostic(gateway, "BPGW012");

        diagnostic.GetMessage().Should().Contain("Origin");
    }

    [Fact]
    public void When_an_inner_property_with_Required_is_hidden_then_BPGW009_is_reported()
    {
        var diagnostic = SingleDescriptorDiagnostic(WithAttributes(GatewaySource, "[Hide(nameof(Inner.AddNoteCommand.Text))]"), "BPGW009");

        diagnostic.GetMessage().Should().Contain("Text");
    }

    [Fact]
    public void When_the_inner_operations_first_route_has_a_placeholder_its_later_routes_lack_then_BPGW010_is_reported()
    {
        // The forward always sends the inner operation's first URL, so a later route without the placeholder is no way out.
        var inner = InnerSource.Replace("[RootLink(\"/tenants/{TenantId}/notes\")]", "[RootLink(\"/tenants/{TenantId}/notes\")]\n    [RootLink(\"/notes\")]", StringComparison.Ordinal);
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>([WithBody(GatewaySource, DeclaredTenantIdOfAnotherType)], additionalReferences: [contract]);

        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPGW010").Which;
        diagnostic.GetMessage().Should().Contain("TenantId").And.Contain("/tenants/{TenantId}/notes");
    }

    [Fact]
    public void A_claim_binds_a_type_whose_only_TryParse_takes_a_format_provider()
    {
        const string region = """
            namespace Inner
            {
                public readonly struct Region
                {
                    public static bool TryParse(string? s, System.IFormatProvider? provider, out Region result)
                    {
                        result = default;
                        return s is not null;
                    }
                }
            }
            """;
        var inner = InnerSource.Replace("public int Priority { get; set; }", "public int Priority { get; set; }\n\n                public Region Region { get; set; }", StringComparison.Ordinal) + "\n" + region;
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");
        var gateway = WithAttributes(GatewaySource, "[Bind(\"Region\", FromClaim = \"region\")]");

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], contract);

        run.GeneratorDiagnostics.Should().NotContain(static d => d.Id == "BPGW011");
        run.EmitErrors.Should().BeEmpty();
        run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs").Should().Contain("global::System.Globalization.CultureInfo.InvariantCulture");
    }

    [Fact]
    public void A_claim_binds_a_type_that_implements_IParsable_explicitly()
    {
        const string region = """
            namespace Inner
            {
                public readonly struct Region : System.IParsable<Region>
                {
                    static Region System.IParsable<Region>.Parse(string s, System.IFormatProvider? provider) => default;

                    static bool System.IParsable<Region>.TryParse(string? s, System.IFormatProvider? provider, out Region result)
                    {
                        result = default;
                        return s is not null;
                    }
                }
            }
            """;
        var inner = InnerSource.Replace("public int Priority { get; set; }", "public int Priority { get; set; }\n\n                public Region Region { get; set; }", StringComparison.Ordinal) + "\n" + region;
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");
        var gateway = WithAttributes(GatewaySource, "[Bind(\"Region\", FromClaim = \"region\")]");

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], contract);

        run.EmitErrors.Should().BeEmpty();
        run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs").Should().Contain("TryParseInvariant<global::Inner.Region>");
    }

    [Fact]
    public void When_a_route_binding_reads_a_placeholder_one_of_the_gateways_routes_lacks_then_BPGW012_is_reported()
    {
        // On the route without it, a POST would fill the property from its body, and the forward would send that instead.
        var gateway = WithBody(
            WithAttributes(GatewaySource, "[Bind(nameof(Inner.AddNoteCommand.Source), FromRoute = \"Origin\")]")
                .Replace("[RootLink(\"/gateway/notes\")]", "[RootLink(\"/gateway/notes/{Origin}\")]\n[RootLink(\"/gateway/notes\")]", StringComparison.Ordinal),
            "public string? Origin { get; set; }");

        var diagnostic = SingleDescriptorDiagnostic(gateway, "BPGW012");

        diagnostic.GetMessage().Should().Contain("Origin");
    }

    [Fact]
    public void A_claim_binding_uses_a_types_converter_when_its_only_TryParse_is_private()
    {
        const string region = """
            namespace Inner
            {
                [System.ComponentModel.TypeConverter(typeof(RegionConverter))]
                public readonly struct Region
                {
                    private static bool TryParse(string? s, out Region result)
                    {
                        result = default;
                        return s is not null;
                    }
                }

                public sealed class RegionConverter : System.ComponentModel.TypeConverter
                {
                }
            }
            """;
        var inner = InnerSource.Replace("public int Priority { get; set; }", "public int Priority { get; set; }\n\n                public Region Region { get; set; }", StringComparison.Ordinal) + "\n" + region;
        var gateway = WithAttributes(GatewaySource, "[Bind(\"Region\", FromClaim = \"region\")]");

        // In the gateway's own compilation, where a private member is visible; a referenced assembly's is not imported.
        var run = RunAllGenerators([inner, ClientSource, AppBindingSource, gateway, AppSource], MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        run.EmitErrors.Should().BeEmpty();
        run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs").Should().NotContain("Region.TryParse").And.Contain("TypeDescriptor");
    }

    [Fact]
    public void A_contributed_rule_whose_argument_only_spells_a_bound_name_is_kept()
    {
        var inner = InnerSource.Replace(
            "public string? Source { get; set; }",
            "[RegularExpression(\"TenantId\")]\n                public string? Source { get; set; }",
            StringComparison.Ordinal);
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");

        var run = RunAllGenerators([ClientSource, AppBindingSource, GatewaySource, AppSource], contract);

        run.EmitErrors.Should().BeEmpty();
        run.Source("Gateway.AddGatewayNoteCommand_ExposedProperties.g.cs").Should().Contain("RegularExpression");
    }

    [Fact]
    public void A_contributed_rule_that_names_a_bound_property_is_left_to_the_inner_operation()
    {
        var inner = InnerSource
            .Replace("public string? Source { get; set; }", "[Blueprint.Validation.RequiredIf(nameof(Enabled), true)]\n                public string? Source { get; set; }\n\n                public bool Enabled { get; set; }", StringComparison.Ordinal);
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");
        var gateway = WithAttributes(GatewaySource, "[Bind(\"Enabled\", Value = true)]");

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], contract);

        run.GeneratorDiagnostics.Should().NotContain(static d => d.Id.StartsWith("BPV", StringComparison.Ordinal));
        run.EmitErrors.Should().BeEmpty();
        run.Source("Gateway.AddGatewayNoteCommand_ExposedProperties.g.cs").Should().NotContain("RequiredIf");
    }

    [Fact]
    public void A_binding_that_reads_the_gateways_own_property_of_the_same_name_is_allowed()
    {
        var gateway = WithBody(
            WithAttributes(GatewaySource, "[Bind(nameof(Inner.AddNoteCommand.TenantId), FromProperty = \"TenantId\")]"),
            "public string TenantId { get; set; } = \"\";");

        var run = RunAllGenerators([ClientSource, AppBindingSource, gateway, AppSource], _innerContract.Value);

        run.GeneratorDiagnostics.Should().NotContain(static d => d.Id == "BPGW021");
        run.EmitErrors.Should().BeEmpty();
        run.Source("Gateway.AddGatewayNoteCommandExecutorPipeline.g.cs").Should().Contain("TenantId = operation.TenantId");
    }

    [Theory]
    [InlineData("[Bind(nameof(Inner.AddNoteCommand.Source), Value = \"a\")]", "[Hide(nameof(Inner.AddNoteCommand.Source))]")]
    [InlineData("[Bind(nameof(Inner.AddNoteCommand.Source), Value = \"a\")]", "[Bind(nameof(Inner.AddNoteCommand.Source), Value = \"b\")]")]
    public void When_a_property_is_bound_twice_on_one_operation_then_BPGW013_is_reported(string first, string second)
    {
        var diagnostic = SingleDescriptorDiagnostic(WithAttributes(GatewaySource, first, second), "BPGW013");

        diagnostic.GetMessage().Should().Contain("Source");
    }

    [Theory]
    [InlineData("internal void OnMap(Inner.NoteResult inner) { }")]
    [InlineData("private void OnMap(Inner.AddNoteCommand inner) { }")]
    [InlineData("internal int OnMapAsync(Inner.AddNoteCommand inner) => 0;")]
    [InlineData("internal static void OnMap<T>(Inner.AddNoteCommand inner) { }")]
    [InlineData("internal async void OnMap(Inner.AddNoteCommand inner) { await System.Threading.Tasks.Task.Yield(); }")]
    public void When_the_map_hook_has_the_wrong_shape_then_BPGW014_is_reported(string hook)
    {
        var gateway = GatewaySource.Replace(
            "public partial class AddGatewayNoteCommand : ICommand<Inner.NoteResult>;",
            "public partial class AddGatewayNoteCommand : ICommand<Inner.NoteResult> { " + hook + " }",
            StringComparison.Ordinal);

        var diagnostic = SingleDescriptorDiagnostic([AppBindingSource, gateway], "BPGW014");

        diagnostic.GetMessage().Should().Contain("AddGatewayNoteCommand").And.Contain("AddNoteCommand").And.Contain("static");
    }

    private static MetadataReference RequiredInternalNoteContract()
    {
        var inner = InnerSource.Replace("public string? InternalNote { get; set; }", "public required string InternalNote { get; set; }", StringComparison.Ordinal);
        return TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(inner, "InnerNoteContract");
    }

    private static OperationPropertyModel BuildModel(params string[] sources)
    {
        var compilation = CreateCompilation(sources, _innerContract.Value);
        return OperationPropertyModelBuilder.Build(compilation.GetTypeByMetadataName("Gateway.AddGatewayNoteCommand")!);
    }

    private static Diagnostic SingleDescriptorDiagnostic(string gateway, string id)
    {
        return SingleDescriptorDiagnostic([AppBindingSource, gateway], id);
    }

    private static Diagnostic SingleDescriptorDiagnostic(string[] sources, string id)
    {
        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>(sources, additionalReferences: [_innerContract.Value]);

        var diagnostic = result.Diagnostics.Should().ContainSingle(d => d.Id == id).Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        return diagnostic;
    }

    private static string WithBody(string gateway, string body)
    {
        return gateway.Replace(
            "public partial class AddGatewayNoteCommand : ICommand<Inner.NoteResult>;",
            "public partial class AddGatewayNoteCommand : ICommand<Inner.NoteResult>\n{\n    " + body + "\n}",
            StringComparison.Ordinal);
    }

    private static string WithAttributes(string gateway, params string[] attributes)
    {
        return gateway.Replace("[Expose<Inner.AddNoteCommand>]", "[Expose<Inner.AddNoteCommand>]\n" + string.Join("\n", attributes), StringComparison.Ordinal);
    }
}
