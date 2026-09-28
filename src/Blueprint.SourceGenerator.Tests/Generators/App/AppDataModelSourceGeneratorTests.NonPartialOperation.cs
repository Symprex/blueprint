using Blueprint.SourceGenerator.Generators.ApiExceptionStatusIndex;
using Blueprint.SourceGenerator.Generators.ApiResources;
using Blueprint.SourceGenerator.Generators.App;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Generators.ResourceEvents;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Generators.AppDataModel;

/// <summary>
/// Proves that an operation the descriptor generator rejects with BP005 (not partial) fails on its own: the error is
/// BP005 alone, and the app's other operations are still described and registered.
/// </summary>
/// <remarks>
/// Signature365's Portal met this when a merge brought in <c>ReconnectProbeBackgroundTask</c> as a plain
/// <c>public class</c>. The build reported BP005 for it and also a <c>CS1501</c> in the generated
/// <c>PortalBlueprintApp_DataModelRegistration.g.cs</c>, which pointed at another task's <c>Configure()</c> call and
/// made the one real error hard to find.
/// </remarks>
public class AppDataModelSourceGeneratorTests_NonPartialOperation
{
    private const string Source = """
        using Blueprint;
        using Blueprint.Model;
        using Blueprint.Model.Attributes;
        using Blueprint.Tasks;
        using System.Threading.Tasks;

        namespace TestNamespace.Microsoft365
        {
            public partial class ImportEntraIdBackgroundTask : IBackgroundTask
            {
                public static Task<string> InvokeAsync()
                {
                    return Task.FromResult("imported");
                }
            }
        }

        namespace TestNamespace.Microsoft365.BackgroundTasks
        {
            public class ReconnectProbeBackgroundTask : IBackgroundTask
            {
                public static Task<string> InvokeAsync()
                {
                    return Task.FromResult("probed");
                }
            }
        }

        namespace TestNamespace
        {
            [BlueprintAppScan]
            public partial class TestBlueprintApp : BlueprintApp
            {
            }
        }
        """;

    [Fact]
    public void When_one_operation_is_not_partial_then_BP005_is_the_only_error()
    {
        var result = CompileBothGenerators();

        result.Diagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => $"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}")
            .Should().Equal("BP005: The class 'ReconnectProbeBackgroundTask' must be partial");
    }

    [Fact]
    public void When_one_operation_is_not_partial_then_BP005_is_the_only_error_with_every_generator_running()
    {
        // As a consumer's build runs them: the package brings every generator.
        var result = TestHelpers.CompileWithTrackedGenerators(
            [
                TestHelpers.CreateTrackedGenerator<ApiResourceSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<ApiExceptionStatusIndexGenerator>(),
                TestHelpers.CreateTrackedGenerator<ResourceEventFamilySourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<AppDataModelSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<TypedHttpClientExtensionsGenerator>(),
            ],
            [Source],
            ["Input0.cs"]);

        result.Diagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => $"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}")
            .Should().Equal("BP005: The class 'ReconnectProbeBackgroundTask' must be partial");
    }

    [Fact]
    public void When_one_operation_is_not_partial_then_the_other_is_still_described_and_registered()
    {
        var result = CompileBothGenerators();

        result.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator))
            .Select(static s => s.HintName)
            .Should().Contain("TestNamespace.Microsoft365.ImportEntraIdBackgroundTask_Descriptor.g.cs");

        var registration = string.Join("\n", result.GetGeneratedSources(nameof(AppDataModelSourceGenerator)).Select(static s => s.SourceText));
        registration.Should().Contain("TestNamespace.Microsoft365.ImportEntraIdBackgroundTask.Configure()");
    }

    [Fact]
    public void When_a_referenced_assemblys_operation_has_no_Configure_then_it_is_still_registered_and_the_build_says_so()
    {
        // Only an operation of this compilation has BP005 to report it. One from a referenced assembly built without the
        // generator must not drop out of the app silently: the registration's missing Configure() is the error.
        var contract = CSharpCompilation.Create(
            "Contract",
            [CSharpSyntaxTree.ParseText("namespace Contract { public class RemoteQuery : Blueprint.Model.IQuery<string> { } }")],
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(static a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
                .Select(static a => (MetadataReference)MetadataReference.CreateFromFile(a.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // An emitted image, as a build references another project's output.
        using var image = new MemoryStream();
        contract.Emit(image).Success.Should().BeTrue();

        var result = TestHelpers.CompileWithTrackedGenerators(
            [
                TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<AppDataModelSourceGenerator>(),
            ],
            [
                """
                using Blueprint.Model;
                using Blueprint.Model.Attributes;

                namespace TestNamespace;

                [BlueprintAppScan(FromAssemblyOf = typeof(Contract.RemoteQuery))]
                public partial class TestBlueprintApp : BlueprintApp
                {
                }
                """,
            ],
            ["Input0.cs"],
            [MetadataReference.CreateFromImage(image.ToArray())]);

        result.Diagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.Id)
            .Should().Equal("CS0117");
    }

    [Fact]
    public void When_a_non_partial_record_operation_has_no_Configure_then_it_is_still_registered_and_the_build_says_so()
    {
        // BP005 reports a class declaration only, so a record would have no error at all if it were left out: it would
        // drop out of the app silently. It stays in, and the registration's missing Configure() is the error.
        var result = TestHelpers.CompileWithTrackedGenerators(
            [
                TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<AppDataModelSourceGenerator>(),
            ],
            [
                """
                using Blueprint.Model;
                using Blueprint.Model.Attributes;

                namespace TestNamespace
                {
                    public record RecordQuery : IQuery<string>;

                    [BlueprintAppScan]
                    public partial class TestBlueprintApp : BlueprintApp
                    {
                    }
                }
                """,
            ],
            ["Input0.cs"]);

        result.Diagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.Id)
            .Should().Equal("CS0117");

        var registration = string.Join("\n", result.GetGeneratedSources(nameof(AppDataModelSourceGenerator)).Select(static s => s.SourceText));
        registration.Should().Contain("TestNamespace.RecordQuery.Configure()");
    }

    private static MultiGeneratorGenerationResult CompileBothGenerators()
    {
        return TestHelpers.CompileWithTrackedGenerators(
            [
                TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<AppDataModelSourceGenerator>(),
            ],
            [Source],
            ["Input0.cs"]);
    }
}
