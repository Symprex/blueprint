using System.Text;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Proves that a consumer-space companion generator can implement <see cref="IEnabledHostContributor"/> to register
/// an alternate host name, and that the descriptor/manifest planning seam correctly projects that custom host exposure
/// without any production code changes. This validates the consumer extensibility boundary for non-HTTP hosts.
/// </summary>
public class ConsumerHostContributorTests
{
    // ── Plan tests ────────────────────────────────────────────────────

    [Fact]
    public void When_consumer_host_contributor_present_then_custom_host_exposure_added_to_descriptor_plan()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public interface IMessageConsumer { }

            public partial class ProcessOrderMessage : ICommand<string>, IMessageConsumer
            {
                public static Task<string> HandleAsync(ProcessOrderMessage operation)
                {
                    return Task.FromResult("processed");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new TestMessagingHostModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);

        var executionPlan = config.BuildExecutionPlan();
        executionPlan.Diagnostics.Should().BeEmpty();

        // Build descriptor plan manually to inspect metadata
        var operationType = compilation.GetTypeByMetadataName("TestNamespace.ProcessOrderMessage")!;
        var operationModel = OperationModel.Create(operationType);
        var descriptorPlan = new DescriptorPlan(operationModel);
        var operationLookup = new CompilationOperationLookup(compilation);

        foreach (var contributor in executionPlan.Value!.DescriptorContributors)
        {
            contributor.ContributeToDescriptor(new DescriptorContributorContext(operationModel, descriptorPlan, operationLookup));
        }

        var hostRegistrations = descriptorPlan.GetMetadata<HostExposureRegistration>().ToArray();
        hostRegistrations.Should().ContainSingle()
            .Which.HostName.Should().Be("messaging");
    }

    [Fact]
    public void When_operation_does_not_implement_marker_then_no_host_exposure_added()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public interface IMessageConsumer { }

            public partial class RegularCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(RegularCommand operation)
                {
                    return Task.FromResult("regular");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new TestMessagingHostModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);

        var executionPlan = config.BuildExecutionPlan();
        executionPlan.Diagnostics.Should().BeEmpty();

        var operationType = compilation.GetTypeByMetadataName("TestNamespace.RegularCommand")!;
        var operationModel = OperationModel.Create(operationType);
        var descriptorPlan = new DescriptorPlan(operationModel);
        var operationLookup = new CompilationOperationLookup(compilation);

        foreach (var contributor in executionPlan.Value!.DescriptorContributors)
        {
            contributor.ContributeToDescriptor(new DescriptorContributorContext(operationModel, descriptorPlan, operationLookup));
        }

        var hostRegistrations = descriptorPlan.GetMetadata<HostExposureRegistration>().ToArray();
        hostRegistrations.Should().BeEmpty();
    }

    // ── Diagnostic tests ──────────────────────────────────────────────

    [Fact]
    public void When_consumer_host_exposure_without_matching_contributor_then_BPOD001_reported()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public interface IMessageConsumer { }

            public partial class ProcessOrderMessage : ICommand<string>, IMessageConsumer
            {
                public static Task<string> HandleAsync(ProcessOrderMessage operation)
                {
                    return Task.FromResult("processed");
                }
            }
            """);

        // Config WITHOUT the consumer host module but with a module that adds the "messaging" exposure
        var config = new BlueprintSourceGenerationConfig(
        [
            new TestMessagingDescriptorOnlyModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);

        var result = config.BuildDebugManifest("TestNamespace.ProcessOrderMessage", compilation);

        result.Diagnostics.Should().NotBeEmpty();
        result.Diagnostics.Should().Contain(
            static d => d.Descriptor.Id == "BPOD001",
            "unsupported host exposure should fire when no IEnabledHostContributor registers the host name");
    }

    [Fact]
    public void When_consumer_host_exposure_with_matching_contributor_then_no_BPOD001()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public interface IMessageConsumer { }

            public partial class ProcessOrderMessage : ICommand<string>, IMessageConsumer
            {
                public static Task<string> HandleAsync(ProcessOrderMessage operation)
                {
                    return Task.FromResult("processed");
                }
            }
            """);

        // Config WITH the consumer host module that both adds exposure AND registers the host name
        var config = new BlueprintSourceGenerationConfig(
        [
            new TestMessagingHostModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);

        var result = config.BuildDebugManifest("TestNamespace.ProcessOrderMessage", compilation);

        result.Diagnostics.Should().BeEmpty(
            "consumer IEnabledHostContributor should satisfy host exposure validation");
        result.Value.Should().NotBeNull();
    }

    // ── Snapshot test ─────────────────────────────────────────────────

    [Fact]
    public async Task When_companion_descriptor_generator_with_consumer_host_then_generated_descriptor_includes_host_exposure()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                public interface IMessageConsumer { }

                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class ProcessOrderMessage : ICommand<string>, IMessageConsumer
                {
                    public static Task<string> HandleAsync(ProcessOrderMessage operation)
                    {
                        return Task.FromResult("processed");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<TestMessagingCompanionDescriptorSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(
            generators,
            [source],
            ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty("the compilation should have no diagnostics");

        var descriptorSources = result.GetGeneratedSources(nameof(TestMessagingCompanionDescriptorSourceGenerator));
        descriptorSources.Should().HaveCountGreaterOrEqualTo(1, "descriptor should be generated with consumer host module");

        var outputText = new StringBuilder();
        foreach (var s in descriptorSources.OrderBy(static s => s.HintName))
        {
            outputText.AppendLine(s.HintName + ":");
            outputText.AppendLine();
            outputText.AppendLine(s.SourceText);
            outputText.AppendLine();
        }

        await Verify(outputText)
            .UseDirectory("Snapshots");
    }

    // ── Debug manifest test ───────────────────────────────────────────

    [Fact]
    public void When_consumer_host_contributor_then_debug_manifest_contains_hostExposure_entry()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public interface IMessageConsumer { }

            public partial class ProcessOrderMessage : ICommand<string>, IMessageConsumer
            {
                public static Task<string> HandleAsync(ProcessOrderMessage operation)
                {
                    return Task.FromResult("processed");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new TestMessagingHostModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
            new OperationDiscoveryManifestModule(),
        ]);

        var result = config.BuildDebugManifest("TestNamespace.ProcessOrderMessage", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        // OperationDiscoveryManifestModule projects host exposure as "hostExposure" JArray
        var entries = result.Value!.Entries;
        entries.Should().ContainKey("hostExposure");

        var hostExposureArray = entries["hostExposure"]!.ToObject<string[]>();
        hostExposureArray.Should().Contain("messaging",
            "consumer-registered host exposure should appear in the debug manifest");
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "ConsumerHostContributorTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}

/// <summary>
/// A consumer-space host module that registers the "messaging" host name and detects operations implementing
/// <c>IMessageConsumer</c> to add host-exposure metadata. Proves that companion generators can bring their own
/// host surface without modifying Blueprint core.
/// Host modules use <see cref="BlueprintModuleCategory.Core"/> because they define a host surface that
/// <see cref="OperationDiscoveryManifestModule"/> must project — mirroring the <see cref="TasksHostModule"/> pattern.
/// </summary>
internal sealed class TestMessagingHostModule : IBlueprintSourceGeneratorModule, IDescriptorContributor, IEnabledHostContributor
{
    private const string MessageConsumerInterfaceName = "IMessageConsumer";

    /// <inheritdoc />
    public string ModuleId => nameof(TestMessagingHostModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(TestMessagingHostModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public IReadOnlyList<string> HostNames { get; } = ["messaging"];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();

        if (!IsMessageConsumer(operationType))
        {
            return;
        }

        var locationInfo = FindMessageConsumerLocation(operationType);
        context.AddMetadata(locationInfo is null
            ? new HostExposureRegistration("messaging")
            : new HostExposureRegistration("messaging", locationInfo));
    }

    private static bool IsMessageConsumer(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.Name == MessageConsumerInterfaceName);
    }

    private static LocationInfo? FindMessageConsumerLocation(INamedTypeSymbol operationType)
    {
        foreach (var syntaxReference in operationType.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is not TypeDeclarationSyntax typeDeclaration
                || typeDeclaration.BaseList is null)
            {
                continue;
            }

            foreach (var baseType in typeDeclaration.BaseList.Types)
            {
                if (baseType.Type.ToString() == MessageConsumerInterfaceName)
                {
                    return LocationInfo.CreateFrom(baseType.Type);
                }
            }
        }

        return operationType.Locations.Length > 0 ? LocationInfo.CreateFrom(operationType.Locations[0]) : null;
    }
}

/// <summary>
/// A descriptor-only module that adds "messaging" host exposure but does NOT implement <see cref="IEnabledHostContributor"/>.
/// Used to prove that host-exposure metadata without a matching enabled contributor triggers <c>BPOD001</c>.
/// Uses <see cref="BlueprintModuleCategory.Core"/> to ensure it runs before projection modules.
/// </summary>
internal sealed class TestMessagingDescriptorOnlyModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
    private const string MessageConsumerInterfaceName = "IMessageConsumer";

    /// <inheritdoc />
    public string ModuleId => nameof(TestMessagingDescriptorOnlyModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(TestMessagingDescriptorOnlyModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();

        if (!operationType.AllInterfaces.Any(static candidate =>
                candidate.Name == MessageConsumerInterfaceName))
        {
            return;
        }

        context.AddMetadata(new HostExposureRegistration("messaging"));
    }
}

/// <summary>
/// A derived descriptor source generator that composes the consumer messaging host module into the default
/// descriptor configuration. Proves that companion generator packages can register alternate host exposure
/// through the existing <see cref="OperationDescriptorSourceGenerator"/> inheritance seam.
/// </summary>
internal sealed class TestMessagingCompanionDescriptorSourceGenerator : OperationDescriptorSourceGenerator
{
    public TestMessagingCompanionDescriptorSourceGenerator()
        : base(CreateDefaultConfig(new TestMessagingHostModule()))
    {
    }
}
