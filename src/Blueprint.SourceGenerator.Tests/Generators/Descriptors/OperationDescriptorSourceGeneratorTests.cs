using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTests
{
    public const string SnapshotsFolder = "Generators/OperationDescriptor/Snapshots/";

    [Fact]
    public void When_pipeline_execution_plan_fails_then_pipeline_plan_reports_module_diagnostic()
    {
        var compilation = CreateCompilation();
        var config = new BlueprintSourceGenerationConfig([new InvalidDependencyPipelineModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.IncludedOperation", compilation);

        result.Value.Should().BeNull();
        result.Diagnostics.Should().ContainSingle();
        result.Diagnostics[0].ToDiagnostic().Id.Should().Be("BPMOD002");
    }

    [Fact]
    public async Task When_not_partial_then_reports_diagnostic()
    {
        var source = """
                     public class NotPartialOperation : IOperation {}
                     """;

        await Compile(source)
            .VerifyCompilationFailedAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task When_executed_then_includes_marker_attributes()
    {
        var source = """
                     public partial class IncludedOperation : IOperation {}
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder, includeMarkerAttributes: true);
    }

    [Fact]
    public void When_required_pipeline_variable_missing_then_reports_module_diagnostic()
    {
        var compilation = CreateCompilation();
        var config = new BlueprintSourceGenerationConfig([new MissingVariablePipelineModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.IncludedOperation", compilation);

        result.Value.Should().BeNull();
        result.Diagnostics.Select(static d => d.ToDiagnostic().Id).Should().Contain("BPMOD004");
    }

    [Fact]
    public void When_pipeline_variable_is_only_available_after_consumer_stage_then_reports_module_diagnostic()
    {
        var compilation = CreateCompilation();
        var config = new BlueprintSourceGenerationConfig(
        [
            new ConsumingPipelineModule(),
            new LateProvidingPipelineModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.IncludedOperation", compilation);

        result.Value.Should().BeNull();
        result.Diagnostics.Select(static d => d.ToDiagnostic().Id).Should().Contain("BPMOD004");
    }

    [Fact]
    public void When_pipeline_variable_is_provided_then_shared_plan_builds_in_dependency_order()
    {
        var compilation = CreateCompilation();
        var config = new BlueprintSourceGenerationConfig(
        [
            new ConsumingPipelineModule(),
            new ProvidingPipelineModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.IncludedOperation", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Select(static c => c.ContributorId).Should().Equal("provider", "consumer");
        result.Value.VariableRegistry.Registrations.Select(static p => p.ProducerId).Should().Equal("provider");
        result.Value.VariableRegistry.Requirements.Select(static r => r.ConsumerId).Should().Equal("consumer");
    }

    [Fact]
    public void When_handler_execution_module_is_used_via_public_pipeline_plan_api_then_handler_metadata_is_resolved()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class IncludedOperation : IQuery<string>
            {
                public static Task<string> HandleAsync()
                {
                    return Task.FromResult("hello");
                }
            }
            """);
        var config = new BlueprintSourceGenerationConfig([new HandlerExecutionModule()]);

        var result = config.BuildPipelinePlan("TestNamespace.IncludedOperation", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().ContainSingle().Which.Should().Be(
            new PipelineContribution(PipelineStage.Execution, nameof(HandlerExecutionModule)));
        result.Value.VariableRegistry.Registrations.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new VariableRegistration(
                new VariableKey("string", HandlerExecutionModule.HandlerResultPurpose),
                nameof(HandlerExecutionModule),
                PipelineStage.Execution,
                VariableSourceKind.ProducedValue,
                VariableLifetime.Local));
    }

    [Fact]
    public void When_pipeline_contributor_uses_context_helpers_then_plan_uses_contributor_identity_and_implicit_stage_participation()
    {
        var compilation = CreateCompilation();
        var config = new BlueprintSourceGenerationConfig([new HelperMethodPipelineModule()]);

        var result = config.BuildPipelinePlan("TestNamespace.IncludedOperation", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().ContainSingle().Which.Should().Be(
            new PipelineContribution(PipelineStage.Execution, "helper-method"));
        var renderStep = result.Value.RenderSteps.Should().ContainSingle().Subject;
        renderStep.Stage.Should().Be(PipelineStage.Execution);
        renderStep.ContributorId.Should().Be("helper-method");
        result.Value.VariableRegistry.Registrations.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new VariableRegistration(
                new VariableKey("global::System.String", "helper-result"),
                "helper-method",
                PipelineStage.Execution,
                VariableSourceKind.ProducedValue,
                VariableLifetime.Local));
    }

    [Fact]
    public void When_descriptor_contributor_uses_helper_methods_then_descriptor_plan_is_populated_without_plan_bypass()
    {
        var compilation = CreateCompilation();
        var operation = OperationModel.Create(compilation.GetTypeByMetadataName("TestNamespace.IncludedOperation")!);
        var plan = new DescriptorPlan(operation);
        var context = new DescriptorContributorContext(operation, plan, new CompilationOperationLookup(compilation));

        context.AddMetadata("descriptor-metadata");
        context.AddRenderStep(writer => writer.AppendLine(context.GetOperationTypeSymbol().Name));

        context.FindOperationByMetadataName("TestNamespace.IncludedOperation")!.TypeName.Should().Be("IncludedOperation");
        plan.Metadata.Should().ContainSingle().Which.Should().Be("descriptor-metadata");
        var writer = new RecordingCodeWriter();

        plan.Render(writer);

        writer.Rendered.Should().Contain("IncludedOperation");
        context.HasDiagnostics().Should().BeFalse();
    }

    [Fact]
    public void When_pipeline_render_step_has_no_explicit_contribution_then_stage_participation_is_implicit()
    {
        var compilation = CreateCompilation();
        var config = new BlueprintSourceGenerationConfig([new OrphanRenderStepPipelineModule()]);

        var result = config.BuildPipelinePlan("TestNamespace.IncludedOperation", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().ContainSingle().Which.Should().Be(
            new PipelineContribution(PipelineStage.Execution, "orphan-render-step"));
        result.Value.RenderSteps.Should().ContainSingle();
    }

    internal static GenerationResult Compile(string source)
    {
        return TestHelpers.Compile<OperationDescriptorSourceGenerator>("using Blueprint.Model;\nusing Blueprint.Model.Attributes;\n\nnamespace TestNamespace;\n\n" + source);
    }

    private static CSharpCompilation CreateCompilation(string source = """
                                                           using Blueprint.Model;

                                                           namespace TestNamespace;

                                                           public partial class IncludedOperation : IOperation
                                                           {
                                                           }
                                                           """)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "OperationDescriptorSourceGeneratorTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private sealed class MissingVariablePipelineModule : IBlueprintSourceGeneratorModule, IPipelineContributor
    {
        public string ModuleId => nameof(MissingVariablePipelineModule);

        public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

        public string ContributorId => "missing-variable";

        public int Order => 0;

        public bool IsUnique => true;

        public IReadOnlyList<string> DependsOn { get; } = [];

        public void ContributeToPipeline(PipelineContributorContext context)
        {
            context.RequireVariable(
                new VariableKey("global::Marten.IDocumentSession", "document-session"),
                requiredBy: PipelineStage.Validation);
        }
    }

    private sealed class InvalidDependencyPipelineModule : IBlueprintSourceGeneratorModule, IPipelineContributor
    {
        public string ModuleId => nameof(InvalidDependencyPipelineModule);

        public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

        public string ContributorId => "invalid-dependency";

        public int Order => 0;

        public bool IsUnique => true;

        public IReadOnlyList<string> DependsOn { get; } = ["missing"];

        public void ContributeToPipeline(PipelineContributorContext context)
        {
            context.AddRenderStep(PipelineStage.Setup, static _ => { });
        }
    }

    private sealed class ProvidingPipelineModule : IBlueprintSourceGeneratorModule, IPipelineContributor
    {
        public string ModuleId => nameof(ProvidingPipelineModule);

        public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

        public string ContributorId => "provider";

        public int Order => 100;

        public bool IsUnique => true;

        public IReadOnlyList<string> DependsOn { get; } = [];

        public void ContributeToPipeline(PipelineContributorContext context)
        {
            context.RegisterVariable(
                new VariableKey("global::Marten.IDocumentSession", "document-session"),
                availableFrom: PipelineStage.Setup,
                sourceKind: VariableSourceKind.Service,
                lifetime: VariableLifetime.Local);
        }
    }

    private sealed class ConsumingPipelineModule : IBlueprintSourceGeneratorModule, IPipelineContributor
    {
        public string ModuleId => nameof(ConsumingPipelineModule);

        public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

        public string ContributorId => "consumer";

        public int Order => 0;

        public bool IsUnique => true;

        public IReadOnlyList<string> DependsOn { get; } = ["provider"];

        public void ContributeToPipeline(PipelineContributorContext context)
        {
            context.RequireVariable(
                new VariableKey("global::Marten.IDocumentSession", "document-session"),
                requiredBy: PipelineStage.Validation);
        }
    }

    private sealed class LateProvidingPipelineModule : IBlueprintSourceGeneratorModule, IPipelineContributor
    {
        public string ModuleId => nameof(LateProvidingPipelineModule);

        public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

        public string ContributorId => "provider";

        public int Order => 100;

        public bool IsUnique => true;

        public IReadOnlyList<string> DependsOn { get; } = [];

        public void ContributeToPipeline(PipelineContributorContext context)
        {
            context.RegisterVariable(
                new VariableKey("global::Marten.IDocumentSession", "document-session"),
                availableFrom: PipelineStage.PostExecution,
                sourceKind: VariableSourceKind.Service,
                lifetime: VariableLifetime.Local);
        }
    }

    private sealed class HelperMethodPipelineModule : IBlueprintSourceGeneratorModule, IPipelineContributor
    {
        public string ModuleId => nameof(HelperMethodPipelineModule);

        public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

        public string ContributorId => "helper-method";

        public int Order => 0;

        public bool IsUnique => true;

        public IReadOnlyList<string> DependsOn { get; } = [];

        public void ContributeToPipeline(PipelineContributorContext context)
        {
            context.AddRenderStep(PipelineStage.Execution, static _ => { });
            context.RegisterVariable(
                new VariableKey("global::System.String", "helper-result"),
                availableFrom: PipelineStage.Execution,
                sourceKind: VariableSourceKind.ProducedValue,
                lifetime: VariableLifetime.Local);
        }
    }

    private sealed class OrphanRenderStepPipelineModule : IBlueprintSourceGeneratorModule, IPipelineContributor
    {
        public string ModuleId => nameof(OrphanRenderStepPipelineModule);

        public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

        public string ContributorId => "orphan-render-step";

        public int Order => 0;

        public bool IsUnique => true;

        public IReadOnlyList<string> DependsOn { get; } = [];

        public void ContributeToPipeline(PipelineContributorContext context)
        {
            context.AddRenderStep(PipelineStage.Execution, static _ => { });
        }
    }

    private sealed class RecordingCodeWriter : Blueprint.SourceGenerator.CodeWriter.ICodeWriter
    {
        private readonly List<string> _lines = [];

        public string Rendered => string.Join(Environment.NewLine, this._lines);

        public void Append(string value)
        {
            this._lines.Add(value);
        }

        public void AppendUnindented(string value)
        {
            this._lines.Add(value);
        }

        public void NewLine()
        {
            this._lines.Add(string.Empty);
        }

        public void AppendLine(string value)
        {
            this._lines.Add(value);
        }

        public void AppendUnindentedLine(string value)
        {
            this._lines.Add(value);
        }

        public IDisposable Block(string value, params string[] constraints)
        {
            throw new NotSupportedException();
        }

        public Blueprint.SourceGenerator.CodeWriter.ICodeWriter BlockWriter(string originalLine)
        {
            throw new NotSupportedException();
        }
    }
}
