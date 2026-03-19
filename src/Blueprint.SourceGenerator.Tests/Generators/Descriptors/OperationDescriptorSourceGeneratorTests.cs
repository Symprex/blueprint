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
        result.Diagnostics.Select(d => d.ToDiagnostic().Id).Should().Contain("BPMOD004");
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
        result.Value!.Contributions.Select(c => c.ContributorId).Should().Equal("provider", "consumer");
        result.Value.VariableRegistry.Provisions.Select(p => p.ProducerId).Should().Equal("provider");
        result.Value.VariableRegistry.Requirements.Select(r => r.ConsumerId).Should().Equal("consumer");
    }

    internal static GenerationResult Compile(string source)
    {
        return TestHelpers.Compile<OperationDescriptorSourceGenerator>("using Blueprint.Model;\nusing Blueprint.Model.Attributes;\n\nnamespace TestNamespace;\n\n" + source);
    }

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
                               using Blueprint.Model;

                              namespace TestNamespace;

                              public partial class IncludedOperation : IOperation
                              {
                              }
                              """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));

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

        public void Contribute(PipelineContributorContext context)
        {
            context.Plan.AddContribution(new PipelineContribution(PipelineStage.Validation, this.ContributorId, "Consume a required session"));
            context.Plan.VariableRegistry.RegisterRequirement(
                new VariableRequirement(
                    new VariableKey("global::Marten.IDocumentSession", "document-session"),
                    ConsumerId: this.ContributorId,
                    IsRequired: true));
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

        public void Contribute(PipelineContributorContext context)
        {
            context.Plan.AddContribution(new PipelineContribution(PipelineStage.Setup, this.ContributorId, "Should never run"));
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

        public void Contribute(PipelineContributorContext context)
        {
            context.Plan.AddContribution(new PipelineContribution(PipelineStage.Setup, this.ContributorId, "Provide a session"));
            context.Plan.VariableRegistry.RegisterProvision(
                new VariableProvision(
                    new VariableKey("global::Marten.IDocumentSession", "document-session"),
                    ProducerId: this.ContributorId,
                    AvailableFrom: PipelineStage.Setup,
                    SourceKind: VariableSourceKind.Service,
                    Lifetime: VariableLifetime.Local));
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

        public void Contribute(PipelineContributorContext context)
        {
            context.Plan.AddContribution(new PipelineContribution(PipelineStage.Validation, this.ContributorId, "Consume the shared session"));
            context.Plan.VariableRegistry.RegisterRequirement(
                new VariableRequirement(
                    new VariableKey("global::Marten.IDocumentSession", "document-session"),
                    ConsumerId: this.ContributorId,
                    IsRequired: true));
        }
    }
}
