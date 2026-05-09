using Blueprint.SourceGenerator.Artifacts;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Tests.Modules;

public class OperationDiscoveryManifestModuleTests
{
    private const string SnapshotsFolder = "OperationDiscoveryManifest/Snapshots/";

    [Fact]
    public void When_debug_manifest_requested_via_public_api_then_operation_discovery_module_populates_entries()
    {
        var compilation = CreateCompilation();
        var config = new BlueprintSourceGenerationConfig([new OperationDiscoveryManifestModule()]);

        var result = config.BuildDebugManifest("TestNamespace.ListUsersQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["kind"].ToString().Should().Be("query");
        result.Value.Entries["namespace"].ToString().Should().Be("TestNamespace");
        result.Value.Entries["typeName"].ToString().Should().Be("ListUsersQuery");
    }

    [Fact]
    public async Task When_emitted_then_operations_manifest_serializes_stable_summary_Async()
    {
        var compilation = CreateCompilation();
        var config = new BlueprintSourceGenerationConfig([new OperationDiscoveryManifestModule()]);
        var manifests = new[]
        {
            BuildManifest(config, compilation, "TestNamespace.ListUsersQuery"),
            BuildManifest(config, compilation, "TestNamespace.CreateUserCommand"),
        };

        var manifest = OperationsDiscoveryManifestEmitter.CreateBuildManifest(
            "TestNamespace.TestBlueprintApp",
            "Test.Assembly",
            "1.2.3",
            manifests.Reverse());

        await Verify(manifest.ToJson())
            .UseDirectory(SnapshotsFolder);
    }

    [Fact]
    public void When_http_host_exposure_exists_then_operations_manifest_includes_host_names()
    {
        var manifest = CreateHostExposedOperationsManifest();

        var listUsersOperation = (JObject)manifest.Operations["TestNamespace.ListUsersQuery"];
        listUsersOperation.TryGetValue("hostExposure", out var hostExposure).Should().BeTrue();
        hostExposure.Should().NotBeNull();
        hostExposure!.Type.Should().Be(JTokenType.Array);
        hostExposure.Values<string>().Should().Equal("http");
        ((JObject)manifest.Operations["TestNamespace.CreateUserCommand"]).Property("hostExposure").Should().BeNull();
    }

    [Fact]
    public async Task When_http_host_exposure_present_then_operations_manifest_serializes_host_names_Async()
    {
        var manifest = CreateHostExposedOperationsManifest();

        await Verify(manifest.ToJson())
            .UseDirectory(SnapshotsFolder);
    }

    [Fact]
    public void When_same_operation_manifest_emitted_twice_then_duplicate_operation_key_is_rejected()
    {
        var compilation = CreateCompilation();
        var config = new BlueprintSourceGenerationConfig([new OperationDiscoveryManifestModule()]);
        var operationManifest = BuildManifest(config, compilation, "TestNamespace.ListUsersQuery");

        var act = () => OperationsDiscoveryManifestEmitter.CreateBuildManifest(
            "TestNamespace.TestBlueprintApp",
            "Test.Assembly",
            "1.2.3",
            [operationManifest, operationManifest]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TestNamespace.ListUsersQuery*");
    }

    [Fact]
    public void When_operation_implements_non_blueprint_icommand_then_manifest_kind_remains_operation()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace CustomContracts
            {
                public interface ICommand
                {
                }
            }

            namespace TestNamespace
            {
                public partial class IncludedOperation : IOperation, CustomContracts.ICommand
                {
                }
            }
            """);
        var config = new BlueprintSourceGenerationConfig([new OperationDiscoveryManifestModule()]);

        var result = config.BuildDebugManifest("TestNamespace.IncludedOperation", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["kind"].ToString().Should().Be("operation");
    }

    [Fact]
    public void When_debug_manifest_operation_cannot_be_found_then_public_api_returns_module_diagnostic()
    {
        var compilation = CreateCompilation();
        var config = new BlueprintSourceGenerationConfig([new OperationDiscoveryManifestModule()]);

        var result = config.BuildDebugManifest("TestNamespace.MissingOperation", compilation);

        result.Value.Should().BeNull();
        result.Diagnostics.Should().ContainSingle();
        result.Diagnostics[0].ToDiagnostic().Id.Should().Be("BPMOD005");
    }

    [Fact]
    public void When_host_exposure_has_no_enabled_host_contributor_then_public_api_returns_operation_discovery_diagnostic()
    {
        var compilation = CreateCompilation();
        var config = new BlueprintSourceGenerationConfig(
        [
            new TestHostExposureDescriptorModule("http"),
            new OperationDiscoveryManifestModule(),
        ]);

        var result = config.BuildDebugManifest("TestNamespace.ListUsersQuery", compilation);

        result.Value.Should().BeNull();
        result.Diagnostics.Should().ContainSingle();
        result.Diagnostics[0].ToDiagnostic().Id.Should().Be("BPOD001");
        result.Diagnostics[0].ToDiagnostic().GetMessage().Should().Contain("http");
        result.Diagnostics[0].ToDiagnostic().GetMessage().Should().Contain("ListUsersQuery");
    }

    [Fact]
    public void When_operation_is_exposed_to_http_and_tasks_hosts_then_public_api_returns_conflicting_host_diagnostic()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model.Attributes;
            using Blueprint.Tasks;

            namespace TestNamespace;

            [RootLink("/tasks/send-welcome-email")]
            public partial class SendWelcomeEmailTask : IBackgroundTask
            {
            }
            """);
        var config = new BlueprintSourceGenerationConfig(
        [
            new LinkGenerationModule(),
            new TasksHostModule(),
            new OperationDiscoveryManifestModule(),
        ]);

        var result = config.BuildDebugManifest("TestNamespace.SendWelcomeEmailTask", compilation);

        result.Value.Should().BeNull();
        result.Diagnostics.Should().ContainSingle();

        var diagnostic = result.Diagnostics[0].ToDiagnostic();
        diagnostic.Id.Should().Be("BPOD002");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("SendWelcomeEmailTask");
        diagnostic.GetMessage().Should().Contain("http");
        diagnostic.GetMessage().Should().Contain("tasks");
    }

    [Fact]
    public void When_tasks_host_contributor_enabled_then_debug_manifest_includes_tasks_host_exposure()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Tasks;

            namespace TestNamespace;

            public partial class SendWelcomeEmailTask : IBackgroundTask
            {
            }
            """);
        var config = new BlueprintSourceGenerationConfig(
        [
            new TasksHostModule(),
            new OperationDiscoveryManifestModule(),
        ]);

        var result = config.BuildDebugManifest("TestNamespace.SendWelcomeEmailTask", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("hostExposure");
        result.Value.Entries["hostExposure"]
            .Should()
            .BeOfType<JArray>()
            .Which.Values<string>()
            .Should()
            .Equal("tasks");
    }

    [Fact]
    public void When_descriptor_host_exposure_contains_escaped_characters_then_rendered_feature_data_uses_valid_csharp_literals()
    {
        var compilation = CreateCompilation();
        var operation = OperationModel.Create(compilation.GetTypeByMetadataName("TestNamespace.ListUsersQuery")!);
        var plan = new DescriptorPlan(operation);
        var context = new DescriptorContributorContext(operation, plan, new CompilationOperationLookup(compilation));
        var module = new OperationDiscoveryManifestModule();

        context.AddMetadata(new HostExposureRegistration("http\\\"beta"));

        module.ContributeToDescriptor(context);

        var generated = RenderDescriptor(plan);

        generated.Should().Contain(
            """this.SetFeatureData(new global::Blueprint.Model.OperationHostExposureFeatureData(global::System.Collections.Immutable.ImmutableArray.Create("http\\\"beta")));""");
    }

    [Fact]
    public void When_host_exposure_locations_differ_then_cacheability_equality_ignores_location_metadata()
    {
        var left = new HostExposureRegistration(
            "http",
            new LocationInfo(
                "left.cs",
                new TextSpan(1, 2),
                new LinePositionSpan(new LinePosition(0, 1), new LinePosition(0, 3))));
        var right = new HostExposureRegistration(
            "http",
            new LocationInfo(
                "right.cs",
                new TextSpan(5, 2),
                new LinePositionSpan(new LinePosition(4, 2), new LinePosition(4, 4))));

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    private static GeneratedDebugManifest BuildManifest(
        BlueprintSourceGenerationConfig config,
        Compilation compilation,
        string metadataName)
    {
        var result = config.BuildDebugManifest(metadataName, compilation);
        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        return result.Value!;
    }

    private static GeneratedManifestEnvelope CreateHostExposedOperationsManifest()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            public partial class CreateUserCommand : ICommand<string>
            {
            }

            [RootLink("/users")]
            public partial class ListUsersQuery : IQuery<string>
            {
            }
            """);
        compilation.GetTypeByMetadataName("Blueprint.Model.Attributes.RootLinkAttribute").Should().NotBeNull();

        var config = new BlueprintSourceGenerationConfig(
        [
            new LinkGenerationModule(),
            new OperationDiscoveryManifestModule(),
        ]);
        var manifests = new[]
        {
            BuildManifest(config, compilation, "TestNamespace.ListUsersQuery"),
            BuildManifest(config, compilation, "TestNamespace.CreateUserCommand"),
        };

        return OperationsDiscoveryManifestEmitter.CreateBuildManifest(
            "TestNamespace.TestBlueprintApp",
            "Test.Assembly",
            "1.2.3",
            manifests);
    }

    private static string RenderDescriptor(DescriptorPlan plan)
    {
        var builder = CodeBuilder.Create("TestNamespace");
        builder.AddClass("Container")
            .AddMethod("Render")
            .WithBody(plan.Render);

        return builder.Build();
    }

    private static CSharpCompilation CreateCompilation(string source = """
                                                              using Blueprint.Model;

                                                              namespace TestNamespace;

                                                              public partial class CreateUserCommand : ICommand<string>
                                                              {
                                                              }

                                                              public partial class ListUsersQuery : IQuery<string>
                                                              {
                                                              }
                                                             """)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(source, parseOptions),
            CSharpSyntaxTree.ParseText(RootLinkAttributeDescriptor.Instance.GenerateSource(), parseOptions),
            CSharpSyntaxTree.ParseText(SelfLinkAttributeDescriptor.Instance.GenerateSource(), parseOptions),
        };
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.ICommand).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "OperationDiscoveryManifestModuleTests",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private sealed class TestHostExposureDescriptorModule(string hostName)
        : IBlueprintSourceGeneratorModule, IDescriptorContributor
    {
        public string ModuleId => nameof(TestHostExposureDescriptorModule);

        public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

        public string ContributorId => nameof(TestHostExposureDescriptorModule);

        public int Order => 0;

        public bool IsUnique => true;

        public IReadOnlyList<string> DependsOn { get; } = [];

        public void ContributeToDescriptor(DescriptorContributorContext context)
        {
            context.AddMetadata(new HostExposureRegistration(hostName));
        }
    }

    [Fact]
    public void When_operation_has_unexposed_attribute_then_descriptor_sets_IsExposed_false()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint;
            using Blueprint.Model;

            namespace TestNamespace;

            [UnexposedOperation]
            public partial class InternalCommand : ICommand<string>
            {
            }
            """);
        var operation = OperationModel.Create(compilation.GetTypeByMetadataName("TestNamespace.InternalCommand")!);
        var plan = new DescriptorPlan(operation);
        var context = new DescriptorContributorContext(operation, plan, new CompilationOperationLookup(compilation));
        var module = new OperationDiscoveryManifestModule();

        module.ContributeToDescriptor(context);

        var generated = RenderDescriptor(plan);

        generated.Should().Contain("this.IsExposed = false;");
    }

    [Fact]
    public void When_operation_is_tasks_only_then_descriptor_sets_IsExposed_false()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Tasks;

            namespace TestNamespace;

            public partial class SendEmailTask : IBackgroundTask
            {
            }
            """);
        var operation = OperationModel.Create(compilation.GetTypeByMetadataName("TestNamespace.SendEmailTask")!);
        var plan = new DescriptorPlan(operation);
        var context = new DescriptorContributorContext(operation, plan, new CompilationOperationLookup(compilation));
        var module = new OperationDiscoveryManifestModule();

        // Simulate TasksHostModule having run first by adding tasks host exposure
        context.AddMetadata(HostExposureRegistration.Tasks);

        module.ContributeToDescriptor(context);

        var generated = RenderDescriptor(plan);

        generated.Should().Contain("this.IsExposed = false;");
    }

    [Fact]
    public void When_operation_is_http_exposed_then_descriptor_does_not_set_IsExposed_false()
    {
        var compilation = CreateCompilation();
        var operation = OperationModel.Create(compilation.GetTypeByMetadataName("TestNamespace.ListUsersQuery")!);
        var plan = new DescriptorPlan(operation);
        var context = new DescriptorContributorContext(operation, plan, new CompilationOperationLookup(compilation));
        var module = new OperationDiscoveryManifestModule();

        context.AddMetadata(HostExposureRegistration.Http);

        module.ContributeToDescriptor(context);

        var generated = RenderDescriptor(plan);

        generated.Should().NotContain("this.IsExposed = false;");
    }
}
