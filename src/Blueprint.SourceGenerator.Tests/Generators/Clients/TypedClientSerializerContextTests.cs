using Blueprint.SourceGenerator.Generators.Clients;
using Blueprint.SourceGenerator.Generators.Descriptors;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Tests.Generators.Clients;

public class TypedClientSerializerContextTests
{
    // A contract built with Blueprint's descriptor generator as a consumer's is, so each operation carries the request
    // builders that generator emits: a GET and a POST of the same result, a resource event and a content result.
    private const string Contract = """
                                    using Blueprint;
                                    using Blueprint.Http;
                                    using Blueprint.Model;
                                    using Blueprint.Model.Attributes;
                                    using Blueprint.Runtime.Exec;

                                    namespace Contract;

                                    public partial class ItemApiResource : ApiResource
                                    {
                                        public string Id { get; set; } = "";
                                    }

                                    [RootLink("/items/search")]
                                    [HttpGet]
                                    [AllowAnonymous]
                                    public partial class SearchItemsQuery : IQuery<string[]>
                                    {
                                        public string? Q { get; set; }
                                    }

                                    [RootLink("/items/search")]
                                    [HttpPost]
                                    [AllowAnonymous]
                                    public partial class SearchItemsCommand : ICommand<string[]>
                                    {
                                    }

                                    [RootLink("/items/find")]
                                    [HttpGet]
                                    [AllowAnonymous]
                                    public partial class FindItemsQuery : IQuery<string[]>
                                    {
                                    }

                                    [RootLink("/items")]
                                    [HttpPost]
                                    [AllowAnonymous]
                                    public partial class CreateItemCommand : ICommand<ResourceCreated<ItemApiResource>>
                                    {
                                    }

                                    [RootLink("/items/export")]
                                    [HttpPost]
                                    [AllowAnonymous]
                                    public partial class ExportItemsCommand : ICommand<AcceptedResult<ItemApiResource>>
                                    {
                                    }

                                    [RootLink("/items/{Id}")]
                                    [HttpPut]
                                    [AllowAnonymous]
                                    public partial class UpdateItemCommand : ICommand<ResourceEventResult<ItemApiResource>>
                                    {
                                        public string Id { get; set; } = "";
                                    }
                                    """;

    [Fact]
    public void When_the_context_covers_every_type_a_call_needs_then_no_coverage_warning_is_reported()
    {
        var result = Compile(
            Client(
                "[JsonSerializable(typeof(SearchItemsCommand))] [JsonSerializable(typeof(string[]))] [JsonSerializable(typeof(CreateItemCommand))] [JsonSerializable(typeof(ItemApiResource))]",
                "FindItemsQuery",
                "SearchItemsCommand",
                "CreateItemCommand"));

        result.Diagnostics.Where(static d => d.Id == "BPCL007").Should().BeEmpty();
    }

    [Fact]
    public void When_the_context_does_not_cover_a_result_then_BPCL007_warns_at_the_include_and_the_call_is_still_generated()
    {
        var result = Compile(
            Client("[JsonSerializable(typeof(SearchItemsCommand))]", "SearchItemsCommand"));

        var diagnostic = AssertWarned(result, "BlueprintClientInclude(typeof(SearchItemsCommand))");

        diagnostic.GetMessage().Should().Contain("does not cover string[]");
        GeneratedHalf(result).Should().Contain("SearchItemsAsync");
    }

    [Fact]
    public void When_the_context_does_not_cover_the_request_of_an_operation_with_a_body_then_BPCL007_names_it()
    {
        var result = Compile(
            Client("[JsonSerializable(typeof(string[]))]", "SearchItemsCommand"));

        AssertWarned(result, "BlueprintClientInclude(typeof(SearchItemsCommand))").GetMessage().Should().Contain("does not cover SearchItemsCommand,");
    }

    [Fact]
    public void When_the_operation_is_a_GET_then_its_request_need_not_be_covered_for_it_sends_no_body()
    {
        var result = Compile(
            Client("[JsonSerializable(typeof(string[]))]", "SearchItemsQuery"));

        result.Diagnostics.Where(static d => d.Id == "BPCL007").Should().BeEmpty();
    }

    [Fact]
    public void When_the_result_is_a_resource_event_then_the_resource_it_carries_must_be_covered_not_the_event()
    {
        // Naming the event would cover the resource too, as its Data property reaches it; the message names the resource.
        var uncovered = Compile(
            Client("[JsonSerializable(typeof(CreateItemCommand))]", "CreateItemCommand"));
        var covered = Compile(
            Client("[JsonSerializable(typeof(CreateItemCommand))] [JsonSerializable(typeof(ItemApiResource))]", "CreateItemCommand"));

        AssertWarned(uncovered, "BlueprintClientInclude(typeof(CreateItemCommand))").GetMessage().Should().Contain("does not cover ItemApiResource,");
        covered.Diagnostics.Where(static d => d.Id == "BPCL007").Should().BeEmpty();
    }

    [Fact]
    public void When_the_result_is_a_content_result_or_a_resource_event_result_then_its_content_must_be_covered()
    {
        var result = Compile(
            Client("[JsonSerializable(typeof(ExportItemsCommand))] [JsonSerializable(typeof(UpdateItemCommand))]", "ExportItemsCommand", "UpdateItemCommand"));

        result.Diagnostics.Where(static d => d.Id == "BPCL007").Select(static d => d.GetMessage()).Should().HaveCount(2)
            .And.OnlyContain(static m => m.Contains("does not cover ItemApiResource,"));
    }

    [Fact]
    public void When_a_type_is_reachable_from_a_type_the_context_names_then_it_is_covered()
    {
        // The STJ source generator gives a context metadata for every type reachable from the types it names, through members,
        // collection and array elements and generic arguments, so a type a named type carries needs no entry of its own.
        var result = Compile(
            Client("[JsonSerializable(typeof(ItemsEnvelope))]", "SearchItemsCommand", "CreateItemCommand", "UpdateItemCommand"));

        result.Diagnostics.Where(static d => d.Id == "BPCL007").Select(static d => d.GetMessage())
            .Should().ContainSingle("only UpdateItemCommand, the request no member of the envelope reaches, is uncovered")
            .Which.Should().Contain("does not cover UpdateItemCommand,");
    }

    [Fact]
    public async Task When_a_client_names_a_serializer_context_then_each_call_reads_and_writes_with_metadata_resolved_once_per_options()
    {
        var result = Compile(
            Client(
                "[JsonSerializable(typeof(CreateItemCommand))] [JsonSerializable(typeof(ExportItemsCommand))] [JsonSerializable(typeof(UpdateItemCommand))] [JsonSerializable(typeof(SearchItemsCommand))] [JsonSerializable(typeof(string[]))] [JsonSerializable(typeof(ItemApiResource))]",
                "FindItemsQuery",
                "SearchItemsCommand",
                "CreateItemCommand",
                "ExportItemsCommand",
                "UpdateItemCommand"));

        result.Diagnostics.Where(static d => d.Id == "BPCL007").Should().BeEmpty();

        await result.VerifySuccessAsync(TypedClientGeneratorTests.SnapshotsFolder);
    }

    private static string Client(string contextAttributes, params string[] includes)
    {
        return $$"""
                 #nullable enable
                 using System;
                 using System.Text.Json;
                 using System.Text.Json.Serialization;
                 using System.Text.Json.Serialization.Metadata;
                 using Blueprint.HttpClient;
                 using Contract;

                 namespace Client;

                 // Reaches SearchItemsCommand as a property, string[] as a property, CreateItemCommand as a list's element, and
                 // ItemApiResource as a dictionary's value; UpdateItemCommand is not reached.
                 public class ItemsEnvelope
                 {
                     public SearchItemsCommand? Search { get; set; }

                     public string[] Names { get; set; } = [];

                     public System.Collections.Generic.List<CreateItemCommand> Creates { get; set; } = [];

                     public System.Collections.Generic.Dictionary<string, ItemApiResource?> Items { get; set; } = new();
                 }

                 {{contextAttributes}}
                 public sealed class ItemsContext : JsonSerializerContext
                 {
                     public static ItemsContext Default { get; } = new();

                     private ItemsContext() : base(null) { }

                     protected override JsonSerializerOptions? GeneratedSerializerOptions => null;

                     public override JsonTypeInfo? GetTypeInfo(Type type) => null;
                 }

                 [BlueprintClient(SerializerContext = typeof(ItemsContext))]
                 {{string.Join("\n", includes.Select(static i => $"[BlueprintClientInclude(typeof({i}))]"))}}
                 public partial class ItemsApi
                 {
                 }
                 """;
    }

    private static GenerationResult Compile(string source)
    {
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(Contract, "Contract");

        return TestHelpers.Compile<TypedClientGenerator>(source, [contract]);
    }

    private static string GeneratedHalf(GenerationResult result)
    {
        return string.Join("\n", result.Output.Select(static t => t.ToString()));
    }

    private static Diagnostic AssertWarned(GenerationResult result, string expectedText)
    {
        var diagnostic = result.Diagnostics.Should().ContainSingle(d => d.Id == "BPCL007").Subject;

        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan).Should().Be(expectedText);

        return diagnostic;
    }
}
