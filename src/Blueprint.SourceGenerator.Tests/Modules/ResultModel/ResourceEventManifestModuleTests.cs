using Blueprint.SourceGenerator.Modules;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Tests.Modules.ResultModel;

/// <summary>
/// Tests that <see cref="ResourceEventManifestModule"/> contributes per-operation resource-event
/// metadata (<c>resourceEvents.enabled</c>, <c>resourceEvents.events</c>) to the shared debug manifest.
/// </summary>
public class ResourceEventManifestModuleTests
{
    [Fact]
    public void When_operation_has_no_resource_event_result_then_manifest_shows_disabled()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class PlainQuery : IQuery<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new ResourceEventManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.PlainQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("resourceEvents.enabled");
        result.Value.Entries["resourceEvents.enabled"].ToString().Should().Be("False");
        result.Value.Entries.Should().NotContainKey("resourceEvents.events");
    }

    [Fact]
    public void When_operation_returns_body_bearing_resource_event_with_attribute_then_manifest_shows_event()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;

            namespace TestNamespace;

            public class ItemResource { public string Id { get; set; } }

            [ResourceEvent("item.created")]
            public class ItemCreatedBody : ItemResource { }

            public partial class CreateItemCommand : ICommand<ResourceEventResult<ItemCreatedBody>> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new ResourceEventManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.CreateItemCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["resourceEvents.enabled"].ToString().Should().Be("True");

        var events = result.Value.Entries["resourceEvents.events"] as JArray;
        events.Should().NotBeNull();
        events!.Count.Should().Be(1);

        var entry = events[0] as JObject;
        entry.Should().NotBeNull();
        entry!["eventId"]!.ToString().Should().Be("item.created");
        entry["bodyType"]!.ToString().Should().Contain("ItemCreatedBody");
        entry["resultType"]!.ToString().Should().Contain("ResourceEventResult");
    }

    [Fact]
    public void When_operation_returns_key_only_resource_event_then_manifest_shows_key_only()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;

            namespace TestNamespace;

            public partial class DeleteItemCommand : ICommand<KeyOnlyResourceEventResult>
            {
                public string Id { get; set; }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new ResourceEventManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.DeleteItemCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["resourceEvents.enabled"].ToString().Should().Be("True");

        var events = result.Value.Entries["resourceEvents.events"] as JArray;
        events.Should().NotBeNull();
        events!.Count.Should().Be(1);

        var entry = events[0] as JObject;
        entry.Should().NotBeNull();
        entry!["shape"]!.ToString().Should().Be("key-only");
        entry["resultType"]!.ToString().Should().Contain("KeyOnlyResourceEventResult");
    }

    [Fact]
    public void When_operation_returns_union_with_multiple_resource_events_then_manifest_lists_all()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;

            namespace TestNamespace;

            public class ItemResource { public string Id { get; set; } }

            [ResourceEvent("item.created")]
            public class ItemCreatedBody : ItemResource { }

            [ResourceEvent("item.updated")]
            public class ItemUpdatedBody : ItemResource { }

            public partial class UpsertItemCommand : ICommand<Results<ResourceEventResult<ItemCreatedBody>, ResourceEventResult<ItemUpdatedBody>>> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new ResourceEventManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.UpsertItemCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["resourceEvents.enabled"].ToString().Should().Be("True");

        var events = result.Value.Entries["resourceEvents.events"] as JArray;
        events.Should().NotBeNull();
        events!.Count.Should().Be(2);

        var eventIds = events.Select(static e => ((JObject)e)["eventId"]?.ToString()).ToList();
        eventIds.Should().Contain("item.created");
        eventIds.Should().Contain("item.updated");
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Runtime.Exec.ResourceEventResult<>).Assembly.Location));

        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
