using Blueprint.SourceGenerator.Generators.Clients;
using Blueprint.SourceGenerator.Generators.Descriptors;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Tests.Generators.Clients;

public class TypedClientGeneratorTests
{
    public const string SnapshotsFolder = "Generators/Clients/Snapshots/";

    // One operation per reader the generator chooses, and per HTTP method, in a contract built with Blueprint's descriptor
    // generator as a consumer's contract is, so each carries the request builders that generator emits.
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
                                    public partial class SearchItemsQuery : IQuery<ItemApiResource[]>
                                    {
                                        public string? Q { get; set; }

                                        public int Page { get; set; }
                                    }

                                    [RootLink("/items")]
                                    [HttpPost]
                                    [AllowAnonymous]
                                    public partial class CreateItemCommand : ICommand<ResourceCreated<ItemApiResource>>
                                    {
                                        public string? Name { get; set; }
                                    }

                                    [RootLink("/items/{Id}")]
                                    [HttpDelete]
                                    [AllowAnonymous]
                                    public partial class DeleteItemCommand : ICommand<KeyOnlyResourceEventResult>
                                    {
                                        public string Id { get; set; } = "";
                                    }

                                    [RootLink("/items/{Id}")]
                                    [HttpGet]
                                    [AllowAnonymous]
                                    public partial class GetItemQuery : IQuery<Results<OkResult<ItemApiResource>, NotFoundResult<ItemApiResource>>>
                                    {
                                        public string Id { get; set; } = "";
                                    }

                                    [RootLink("/items/{Id}")]
                                    [HttpPut]
                                    [AllowAnonymous]
                                    public partial class UpdateItemCommand : ICommand<ResourceEventResult<ItemApiResource>>
                                    {
                                        public string Id { get; set; } = "";
                                    }

                                    [RootLink("/items/{Id}/touch")]
                                    [HttpPatch]
                                    [AllowAnonymous]
                                    public partial class TouchItemCommand : ICommand<HttpResult>
                                    {
                                        public string Id { get; set; } = "";
                                    }

                                    [RootLink("/items/queue")]
                                    [HttpPost]
                                    [AllowAnonymous]
                                    public partial class QueueItemsCommand : ICommand<StatusCodeResult.Accepted>
                                    {
                                    }

                                    [RootLink("/items/export")]
                                    [HttpMethod("POST")]
                                    [AllowAnonymous]
                                    public partial class ExportItemsCommand : ICommand<AcceptedResult<ItemApiResource>>
                                    {
                                    }

                                    [RootLink("/items/{Id}/purge")]
                                    [HttpMethod("purge")]
                                    [AllowAnonymous]
                                    public partial class PurgeItemCommand : ICommand<string?>
                                    {
                                        public string Id { get; set; } = "";
                                    }

                                    [RootLink("/ping")]
                                    [AllowAnonymous]
                                    public partial class PingQuery : IQuery<PlainTextResult>
                                    {
                                    }

                                    public abstract class PagedItemsQuery<T> : IQuery<PagedApiResource<T>>
                                    {
                                        public int Page { get; set; }
                                    }

                                    [RootLink("/items/paged")]
                                    [AllowAnonymous]
                                    public partial class ListItemsQuery : PagedItemsQuery<ItemApiResource>
                                    {
                                    }

                                    [RootLink("/items/hidden")]
                                    [HttpGet]
                                    [AllowAnonymous]
                                    [UnexposedOperation]
                                    public partial class HiddenItemsQuery : IQuery<string>
                                    {
                                    }
                                    """;

    [Fact]
    public async Task When_client_includes_operations_of_a_referenced_contract_then_each_calls_its_builders_with_the_reader_its_result_needs()
    {
        var source = """
                     using Blueprint.HttpClient;
                     using Contract;

                     namespace Client;

                     [BlueprintClient]
                     [BlueprintClientInclude(typeof(SearchItemsQuery))]
                     [BlueprintClientInclude(typeof(CreateItemCommand))]
                     [BlueprintClientInclude(typeof(DeleteItemCommand))]
                     [BlueprintClientInclude(typeof(GetItemQuery))]
                     [BlueprintClientInclude(typeof(UpdateItemCommand))]
                     [BlueprintClientInclude(typeof(TouchItemCommand))]
                     [BlueprintClientInclude(typeof(QueueItemsCommand))]
                     [BlueprintClientInclude(typeof(ExportItemsCommand))]
                     [BlueprintClientInclude(typeof(PurgeItemCommand))]
                     [BlueprintClientInclude(typeof(PingQuery))]
                     [BlueprintClientInclude(typeof(ListItemsQuery))]
                     public partial class ItemsApi
                     {
                     }
                     """;

        await TestHelpers.Compile<TypedClientGenerator>(source, [CreateContract()]).VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task When_client_includes_operations_of_its_own_assembly_then_it_calls_the_builders_the_same_build_generates()
    {
        // The descriptor generator emits these operations' builders in the same build, where the client generator cannot see
        // them; the client is generated on the strength of what the descriptor generator is known to emit. Built together,
        // the two must compile. TouchItemCommand declares its own `Http` member, so it is given no builders, and is not included.
        var source = """
                     using Blueprint.HttpClient;
                     using Blueprint.Model;
                     using Blueprint.Model.Attributes;

                     namespace Contract;

                     [RootLink("/items/search")]
                     [HttpGet]
                     [AllowAnonymous]
                     public partial class SearchItemsQuery : IQuery<string[]>
                     {
                         public string? Q { get; set; }
                     }

                     [RootLink("/items/{Id}")]
                     [AllowAnonymous]
                     public partial class RenameItemCommand : ICommand<string>
                     {
                         public string Id { get; set; } = "";
                     }

                     [RootLink("/items/{Id}/touch")]
                     [HttpPatch]
                     [AllowAnonymous]
                     public partial class TouchItemCommand : ICommand<string>
                     {
                         public string Id { get; set; } = "";

                         public string? Http { get; set; }
                     }

                     [BlueprintClient]
                     [BlueprintClientInclude(typeof(SearchItemsQuery))]
                     [BlueprintClientInclude(typeof(RenameItemCommand))]
                     internal partial class ItemsApi
                     {
                     }
                     """;

        var result = TestHelpers.CompileWithTrackedGenerators(
            [
                TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<TypedClientGenerator>(),
            ],
            [source],
            ["Input0.cs"]);

        result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        // Verify resolves a directory relative to the calling file, which is beside the snapshots here.
        await Verify(string.Join("\n", result.GetGeneratedSources(nameof(TypedClientGenerator)).Select(static s => s.HintName + ":\n\n" + s.SourceText)))
            .UseDirectory("Snapshots");
    }

    [Fact]
    public async Task When_the_client_declares_extension_points_then_the_generated_half_builds_on_them_and_the_client_still_compiles()
    {
        // A readonly field with no value becomes a constructor parameter every clone carries; one with a value, a static or a
        // mutable one does not. A public instance method is repeated on the interface as declared, one returning the
        // generated interface included; a private, static or overriding one is not. Each included operation has an OnConfigure hook, which one implementation takes up; it runs before the call's options are used.
        var source = """
                     #nullable enable

                     using System;
                     using System.Collections.Generic;
                     using System.Net.Http;
                     using Blueprint.HttpClient;
                     using Blueprint.Model;
                     using Blueprint.Model.Attributes;

                     namespace Client;

                     [RootLink("/items/search")]
                     [HttpGet]
                     [AllowAnonymous]
                     public partial class SearchItemsQuery : IQuery<string[]>
                     {
                         public string? Q { get; set; }
                     }

                     [BlueprintClient]
                     [BlueprintClientInclude(typeof(SearchItemsQuery))]
                     public partial class ItemsApi
                     {
                         private static readonly string _staticField = "";
                         private readonly Dictionary<string, Uri> _regions;
                         private readonly string? _optionalName;
                         private readonly int _retries;
                         private readonly string _class;
                         private readonly List<string> _withValue = new();
                         private string _mutable = "";

                         public IItemsApi ForRegion(string region, int attempts = 3, string? note = null) => this;

                         public static void Static() { }

                         public override string ToString() => "";

                         private void Hidden() { }

                         partial void OnConfigure(SearchItemsQuery operation, ref CallOptions? options)
                         {
                             var caller = options?.ModifyRequest;

                             options = new CallOptions { ModifyRequest = request => caller?.Invoke(request) };
                         }
                     }
                     """;

        var result = TestHelpers.CompileWithTrackedGenerators(
            [
                TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<TypedClientGenerator>(),
            ],
            [source],
            ["Input0.cs"]);

        result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        await Verify(string.Join("\n", result.GetGeneratedSources(nameof(TypedClientGenerator)).Select(static s => s.HintName + ":\n\n" + s.SourceText)))
            .UseDirectory("Snapshots");
    }

    [Fact]
    public void When_an_include_cannot_be_called_then_it_is_reported_and_left_out_and_the_client_still_compiles()
    {
        // An include the client cannot call is reported, and skipped rather than generated into code that does not compile:
        // an operation of an assembly built without Blueprint's generator (no builders), one with no declared result, a type
        // that is no operation, an open generic, and a repeat. An unexposed operation is skipped although it has builders, as
        // it is left out of every published surface. A class that is not partial gets no generated half at all.
        var source = """
                     using Blueprint.HttpClient;
                     using Contract;
                     using Unbuilt;

                     namespace Client;

                     public class NotAnOperation
                     {
                     }

                     [BlueprintClient]
                     [BlueprintClientInclude(typeof(SearchItemsQuery))]
                     [BlueprintClientInclude(typeof(UnbuiltQuery))]
                     [BlueprintClientInclude(typeof(NoResultCommand))]
                     [BlueprintClientInclude(typeof(NotAnOperation))]
                     [BlueprintClientInclude(typeof(GenericQuery<>))]
                     [BlueprintClientInclude(typeof(HiddenItemsQuery))]
                     [BlueprintClientInclude(typeof(SearchItemsQuery))]
                     public partial class ItemsApi
                     {
                     }

                     [BlueprintClient]
                     [BlueprintClientInclude(typeof(SearchItemsQuery))]
                     public class NotPartialApi
                     {
                     }
                     """;

        var unbuilt = TestHelpers.CreateReferenceFromSource(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace Unbuilt;

            [RootLink("/unbuilt")]
            [HttpGet]
            public partial class UnbuiltQuery : IQuery<string>
            {
            }

            [RootLink("/no-result")]
            [HttpPost]
            public partial class NoResultCommand : ICommand
            {
            }

            [RootLink("/generic")]
            [HttpGet]
            public class GenericQuery<T> : IQuery<T>
            {
            }
            """,
            "Unbuilt");

        var result = TestHelpers.Compile<TypedClientGenerator>(source, [CreateContract(), unbuilt]);

        result.Diagnostics.Select(static d => d.Id).Should().BeEquivalentTo("BPCL004", "BPCL003", "BPCL002", "BPCL002", "BPCL004", "BPCL005", "BPCL001");

        // Only the include the client can call is generated, on the class that can be completed.
        var generated = string.Join("\n", result.Output.Select(static t => t.ToString()));

        generated.Should().Contain("SearchItemsAsync").And.NotContain("UnbuiltAsync").And.NotContain("NoResultAsync").And.NotContain("HiddenItemsAsync");
        result.Output.Should().ContainSingle("the class that is not partial is given no half");
    }

    [Fact]
    public void When_an_operation_declares_several_methods_then_its_call_is_sent_with_the_first_its_descriptor_publishes()
    {
        // The descriptor publishes them in ordinal order, GET before POST, and the app-driven client sends the first; declared
        // POST first, the call must still be a GET.
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace Contract;

            [RootLink("/items/either")]
            [HttpMethod("post")]
            [HttpGet]
            [AllowAnonymous]
            public partial class EitherItemsQuery : IQuery<string>
            {
            }
            """,
            "Contract");

        var result = TestHelpers.Compile<TypedClientGenerator>(
            """
            using Blueprint.HttpClient;
            using Contract;

            namespace Client;

            [BlueprintClient]
            [BlueprintClientInclude(typeof(EitherItemsQuery))]
            public partial class ItemsApi
            {
            }
            """,
            [contract]);

        var generated = string.Join("\n", result.Output.Select(static t => t.ToString()));

        generated.Should().Contain("global::System.Net.Http.HttpMethod.Get,").And.NotContain("global::System.Net.Http.HttpMethod.Post,");
    }

    private static MetadataReference CreateContract()
    {
        return TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(Contract, "Contract");
    }
}
