using Blueprint.SourceGenerator.Modules.Auditing;
using Blueprint.SourceGenerator.Modules.Observability;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Tests.Modules.Observability;

/// <summary>
/// Tests that <see cref="PerformanceMetricsModule"/> and <see cref="AuditingManifestModule"/> contribute
/// a trace event catalog to the debug manifest, documenting what Activity events and tags each operation
/// will emit at runtime based on its attribute configuration.
/// </summary>
public class TraceEventCatalogManifestTests
{
    [Fact]
    public void When_normal_audited_operation_then_manifest_has_trace_events_and_tags()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class NormalCommand : ICommand<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new PerformanceMetricsModule(), new AuditingManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.NormalCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        // PerformanceMetricsModule should emit trace.tags when activity is enabled
        result.Value!.Entries.Should().ContainKey("trace.tags");
        var traceTags = result.Value.Entries["trace.tags"] as JArray;
        traceTags.Should().NotBeNull();
        traceTags!.Should().Contain(t => t.ToString() == "otel.status_code");

        // AuditingManifestModule should emit trace.events when auditing is enabled
        result.Value.Entries.Should().ContainKey("trace.events");
        var traceEvents = result.Value.Entries["trace.events"] as JArray;
        traceEvents.Should().NotBeNull();
        var eventNames = traceEvents!.Select(static e => (string?)e["name"]).ToList();
        eventNames.Should().Contain("blueprint.audit.started");
        eventNames.Should().Contain("blueprint.audit.succeeded");
        eventNames.Should().Contain("blueprint.audit.failed");
    }

    [Fact]
    public void When_DoNotRecordPerformanceMetrics_then_manifest_has_no_trace_tags()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Auditing;
            using Blueprint.Model;

            namespace TestNamespace;

            [DoNotRecordPerformanceMetrics]
            public partial class HealthCheckQuery : IQuery<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new PerformanceMetricsModule(), new AuditingManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.HealthCheckQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        // No trace.tags when activity is suppressed
        result.Value!.Entries.Should().NotContainKey("trace.tags");

        // Audit events still present (auditing is independent of activity suppression)
        result.Value.Entries.Should().ContainKey("trace.events");
    }

    [Fact]
    public void When_DoNotAudit_then_manifest_has_no_trace_events_but_has_tags()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Auditing;
            using Blueprint.Model;

            namespace TestNamespace;

            [DoNotAudit]
            public partial class InternalQuery : IQuery<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new PerformanceMetricsModule(), new AuditingManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.InternalQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        // Trace tags still present (activity is still created)
        result.Value!.Entries.Should().ContainKey("trace.tags");

        // No trace.events when auditing is disabled
        result.Value.Entries.Should().NotContainKey("trace.events");
    }

    [Fact]
    public void When_DoNotAudit_and_DoNotRecordPerformanceMetrics_then_no_trace_catalog()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Auditing;
            using Blueprint.Model;

            namespace TestNamespace;

            [DoNotAudit]
            [DoNotRecordPerformanceMetrics]
            public partial class SilentQuery : IQuery<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new PerformanceMetricsModule(), new AuditingManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.SilentQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        // Neither trace.tags nor trace.events when both are suppressed
        result.Value!.Entries.Should().NotContainKey("trace.tags");
        result.Value.Entries.Should().NotContainKey("trace.events");
    }

    [Fact]
    public void When_audit_events_present_then_tags_include_operation_type()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class CreateUserCommand : ICommand<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new PerformanceMetricsModule(), new AuditingManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.CreateUserCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        var traceEvents = result.Value!.Entries["trace.events"] as JArray;
        traceEvents.Should().NotBeNull();

        // Each audit event should document its tags
        var startedEvent = traceEvents!.First(static e => (string?)e["name"] == "blueprint.audit.started");
        var tags = startedEvent["tags"] as JArray;
        tags.Should().NotBeNull();
        tags!.Select(static t => t.ToString()).Should().Contain("blueprint.operation.type");

        // The failed event should include exception tags
        var failedEvent = traceEvents.First(static e => (string?)e["name"] == "blueprint.audit.failed");
        var failedTags = failedEvent["tags"] as JArray;
        failedTags.Should().NotBeNull();
        failedTags!.Select(static t => t.ToString()).Should().Contain("exception.type");
        failedTags.Select(static t => t.ToString()).Should().Contain("exception.message");
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
