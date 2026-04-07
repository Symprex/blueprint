using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsFailureOutcomesTests
{
    [Fact]
    public void When_pipeline_uses_default_exception_translators_then_generated_descriptor_sets_failure_outcomes_feature_data()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            public partial class IncludedOperation : IQuery<string> {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("IncludedOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("this.SetFeatureData(global::Blueprint.Model.WellKnownFeatureData.DefaultFailureOutcomes);");
    }

    [Fact]
    public void When_package_exception_translation_module_is_enabled_then_generated_descriptor_includes_package_failure_outcome()
    {
        var result = TestHelpers.Compile<PackageAwareOperationDescriptorSourceGenerator>("""
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            public partial class IncludedOperation : IQuery<string> {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("IncludedOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("new global::Blueprint.Model.OperationFailureOutcome(\"package-security-exception\", \"System.Security.SecurityException\", \"Blueprint.Runtime.Exec.UnhandledExceptionResult\")");
    }

    private sealed class PackageAwareOperationDescriptorSourceGenerator : OperationDescriptorSourceGenerator
    {
        public PackageAwareOperationDescriptorSourceGenerator()
            : base(CreateDefaultConfig([new PackageExceptionTranslationModule()]))
        {
        }
    }

    private sealed class PackageExceptionTranslationModule : IBlueprintSourceGeneratorModule, IExceptionTranslationContributor
    {
        public string ModuleId => nameof(PackageExceptionTranslationModule);

        public BlueprintModuleCategory Category => BlueprintModuleCategory.Consumer;

        public string ContributorId => "package-exception-translation";

        public int Order => 0;

        public bool IsUnique => true;

        public IReadOnlyList<string> DependsOn { get; } = [];

        public void ContributeToExceptionTranslation(ExceptionTranslationContributorContext context)
        {
            context.AddTranslator(new PackageSecurityExceptionTranslator());
        }
    }

    private sealed class PackageSecurityExceptionTranslator : IPipelineExceptionTranslator
    {
        public string TranslatorId => "package-security-exception";

        public string HandledExceptionTypeName => "System.Security.SecurityException";

        public IReadOnlyList<string> OwnedExceptionTypeNames { get; } = ["System.Security.SecurityException"];

        public string ResultTypeName => "Blueprint.Runtime.Exec.UnhandledExceptionResult";

        public int Order => 150;

        public bool IsFallback => false;

        public void WriteCatchBlock(ICodeWriter writer, bool isHandlerAsync)
        {
            writer.Catch(
                "System.Security.SecurityException e",
                catchWriter =>
                {
                    catchWriter.Return(
                        isHandlerAsync
                            ? "new Blueprint.Runtime.Exec.UnhandledExceptionResult(e)"
                            : "System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>(new Blueprint.Runtime.Exec.UnhandledExceptionResult(e))");
                });
        }
    }
}
