using Blueprint.SourceGenerator.Artifacts;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    public async Task When_emitted_then_operations_manifest_serializes_stable_summary()
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
    public async Task When_http_host_exposure_present_then_operations_manifest_serializes_host_names()
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
}
