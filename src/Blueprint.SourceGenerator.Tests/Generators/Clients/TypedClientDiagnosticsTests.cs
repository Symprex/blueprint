using Blueprint.SourceGenerator.Generators.Clients;
using Blueprint.SourceGenerator.Generators.Descriptors;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Tests.Generators.Clients;

public class TypedClientDiagnosticsTests
{
    // A contract built with Blueprint's descriptor generator as a consumer's is, so each operation carries the request
    // builders that generator emits. SearchItemsCommand is named so that its method is the one SearchItemsQuery's is.
    private const string Contract = """
                                    using Blueprint;
                                    using Blueprint.Model;
                                    using Blueprint.Model.Attributes;
                                    using Blueprint.Runtime.Exec;

                                    namespace Contract;

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

                                    [RootLink("/items/touch")]
                                    [HttpPost]
                                    [AllowAnonymous]
                                    public partial class TouchItemsCommand : ICommand
                                    {
                                    }

                                    public sealed class TeapotResult : StatusCodeResult
                                    {
                                        public TeapotResult()
                                            : base(418)
                                        {
                                        }
                                    }

                                    [RootLink("/items/teapot")]
                                    [HttpPost]
                                    [AllowAnonymous]
                                    public partial class BrewCommand : ICommand<TeapotResult>
                                    {
                                    }

                                    [RootLink("/items/brew")]
                                    [HttpPost]
                                    [AllowAnonymous]
                                    public partial class BrewEitherCommand : ICommand<Results<NotFoundResult<string>, TeapotResult>>
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
    public void When_the_client_is_not_partial_then_BPCL001_is_reported_at_its_name_and_no_half_is_generated()
    {
        var result = Compile(
            """
            using Blueprint.HttpClient;
            using Contract;

            namespace Client;

            [BlueprintClient]
            [BlueprintClientInclude(typeof(SearchItemsQuery))]
            public class NotPartialApi
            {
            }
            """);

        AssertReported(result, "BPCL001", "NotPartialApi");
        result.Output.Should().BeEmpty("a class that is not partial cannot be completed");
    }

    [Fact]
    public void When_the_client_is_generic_static_or_nested_then_BPCL001_is_reported_at_each()
    {
        var result = Compile(
            """
            using Blueprint.HttpClient;

            namespace Client;

            [BlueprintClient]
            public partial class GenericApi<T>
            {
            }

            [BlueprintClient]
            public static partial class StaticApi
            {
            }

            public static class Holder
            {
                [BlueprintClient]
                public partial class NestedApi
                {
                }
            }
            """);

        var spans = result.Diagnostics.Where(static d => d.Id == "BPCL001").Select(d => TextOf(d)).ToArray();

        spans.Should().BeEquivalentTo("GenericApi", "StaticApi", "NestedApi");
    }

    [Fact]
    public void When_an_include_is_not_an_operation_then_BPCL002_is_reported_at_the_include()
    {
        var result = Compile(
            """
            using Blueprint.HttpClient;
            using Contract;

            namespace Client;

            public class NotAnOperation
            {
            }

            [BlueprintClient]
            [BlueprintClientInclude(typeof(SearchItemsQuery))]
            [BlueprintClientInclude(typeof(NotAnOperation))]
            public partial class ItemsApi
            {
            }
            """);

        AssertReported(result, "BPCL002", "BlueprintClientInclude(typeof(NotAnOperation))");
        GeneratedHalf(result).Should().Contain("SearchItemsAsync", "the valid include is still generated");
    }

    [Fact]
    public void When_an_included_operation_declares_no_result_then_BPCL003_is_reported_at_the_include()
    {
        var result = Compile(
            """
            using Blueprint.HttpClient;
            using Contract;

            namespace Client;

            [BlueprintClient]
            [BlueprintClientInclude(typeof(TouchItemsCommand))]
            public partial class ItemsApi
            {
            }
            """);

        AssertReported(result, "BPCL003", "BlueprintClientInclude(typeof(TouchItemsCommand))");
    }

    [Fact]
    public void When_an_included_operation_has_no_request_builders_then_BPCL004_is_reported_at_the_include()
    {
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
            """,
            "Unbuilt");

        var result = Compile(
            """
            using Blueprint.HttpClient;
            using Blueprint.Model;
            using Contract;
            using Unbuilt;

            namespace Client;

            public partial class NoUrlQuery : IQuery<string>
            {
            }

            public partial class Outer
            {
                public partial class NestedQuery : IQuery<string>
                {
                }
            }

            [BlueprintClient]
            [BlueprintClientInclude(typeof(SearchItemsQuery))]
            [BlueprintClientInclude(typeof(UnbuiltQuery))]
            [BlueprintClientInclude(typeof(HiddenItemsQuery))]
            [BlueprintClientInclude(typeof(NoUrlQuery))]
            [BlueprintClientInclude(typeof(Outer.NestedQuery))]
            public partial class ItemsApi
            {
            }
            """,
            unbuilt);

        result.Diagnostics.Where(static d => d.Id == "BPCL004").Select(d => TextOf(d)).Should().BeEquivalentTo(
            "BlueprintClientInclude(typeof(UnbuiltQuery))",
            "BlueprintClientInclude(typeof(HiddenItemsQuery))",
            "BlueprintClientInclude(typeof(NoUrlQuery))",
            "BlueprintClientInclude(typeof(Outer.NestedQuery))");
    }

    [Fact]
    public void When_an_operation_is_included_twice_then_BPCL005_is_reported_at_the_second_include()
    {
        var result = Compile(
            """
            using Blueprint.HttpClient;
            using Contract;

            namespace Client;

            [BlueprintClient]
            [BlueprintClientInclude(typeof(SearchItemsQuery))]
            [BlueprintClientInclude(typeof(SearchItemsQuery))]
            public partial class ItemsApi
            {
            }
            """);

        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPCL005").Subject;

        TextOf(diagnostic).Should().Be("BlueprintClientInclude(typeof(SearchItemsQuery))");
        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().Be(7, "the repeat, not the first include, is reported");
    }

    [Fact]
    public void When_two_includes_produce_the_same_method_then_BPCL005_is_reported_at_the_later_include()
    {
        var result = Compile(
            """
            using Blueprint.HttpClient;
            using Contract;

            namespace Client;

            [BlueprintClient]
            [BlueprintClientInclude(typeof(SearchItemsQuery))]
            [BlueprintClientInclude(typeof(SearchItemsCommand))]
            public partial class ItemsApi
            {
            }
            """);

        AssertReported(result, "BPCL005", "BlueprintClientInclude(typeof(SearchItemsCommand))");
    }

    [Fact]
    public void When_a_public_method_is_generic_then_BPCL006_is_reported_at_its_name()
    {
        var result = Compile(
            """
            using Blueprint.HttpClient;
            using Contract;

            namespace Client;

            [BlueprintClient]
            [BlueprintClientInclude(typeof(SearchItemsQuery))]
            public partial class ItemsApi
            {
                public T Convert<T>() => default!;
            }
            """);

        AssertReported(result, "BPCL006", "Convert");
    }

    [Fact]
    public void When_a_result_has_no_reader_then_BPCL008_is_reported_at_the_include()
    {
        // TeapotResult derives from StatusCodeResult without the Instance singleton its reader is given, alone and as a
        // member of a union.
        var result = Compile(
            """
            using Blueprint.HttpClient;
            using Contract;

            namespace Client;

            [BlueprintClient]
            [BlueprintClientInclude(typeof(SearchItemsQuery))]
            [BlueprintClientInclude(typeof(BrewCommand))]
            [BlueprintClientInclude(typeof(BrewEitherCommand))]
            public partial class ItemsApi
            {
            }
            """);

        result.Diagnostics.Where(static d => d.Id == "BPCL008").Select(d => TextOf(d)).Should().BeEquivalentTo(
            "BlueprintClientInclude(typeof(BrewCommand))",
            "BlueprintClientInclude(typeof(BrewEitherCommand))");
    }

    [Fact]
    public void When_a_readonly_field_is_named_after_generated_state_then_BPCL009_is_reported_at_the_field()
    {
        var result = Compile(
            """
            using Blueprint.HttpClient;
            using Contract;

            namespace Client;

            [BlueprintClient]
            [BlueprintClientInclude(typeof(SearchItemsQuery))]
            public partial class ItemsApi
            {
                private readonly string _credential;
            }
            """);

        AssertReported(result, "BPCL009", "_credential");
    }

    [Fact]
    public void When_a_readonly_field_would_give_a_reserved_parameter_name_then_BPCL009_is_reported_at_the_field()
    {
        var result = Compile(
            """
            using Blueprint.HttpClient;
            using Contract;

            namespace Client;

            [BlueprintClient]
            [BlueprintClientInclude(typeof(SearchItemsQuery))]
            public partial class ItemsApi
            {
                private readonly string _source;

                private readonly string _kept;
            }
            """);

        AssertReported(result, "BPCL009", "_source");
    }

    [Fact]
    public void When_a_public_method_clashes_with_a_generated_one_then_BPCL009_is_reported_at_the_method()
    {
        var result = Compile(
            """
            using Blueprint.HttpClient;
            using Contract;

            namespace Client;

            [BlueprintClient]
            [BlueprintClientInclude(typeof(SearchItemsQuery))]
            public partial class ItemsApi
            {
                public void SearchItemsAsync()
                {
                }

                public IItemsApi WithCredential(IUpstreamCredential credential) => this;
            }
            """);

        result.Diagnostics.Where(static d => d.Id == "BPCL009").Select(d => TextOf(d)).Should().BeEquivalentTo("SearchItemsAsync", "WithCredential");
    }

    [Fact]
    public void When_a_member_of_any_kind_is_named_after_generated_state_or_a_generated_method_then_BPCL009_is_reported_at_it()
    {
        // The generated half declares these whatever the class declares, so a member of the same name is always a clash: a
        // field that is not readonly, a property, a nested type and a method that is not public.
        var result = Compile(
            """
            using System;
            using System.Net.Http;
            using Blueprint.HttpClient;
            using Contract;

            namespace Client;

            [BlueprintClient]
            [BlueprintClientInclude(typeof(SearchItemsQuery))]
            public partial class ItemsApi
            {
                private string? _plan;

                private int _requestModifier => 0;

                private sealed class BlueprintClientPlan
                {
                }

                protected IItemsApi WithRequestModifier(Action<HttpRequestMessage> modify) => this;
            }
            """);

        result.Diagnostics.Where(static d => d.Id == "BPCL009").Select(d => TextOf(d))
            .Should().BeEquivalentTo("_plan", "_requestModifier", "BlueprintClientPlan", "WithRequestModifier");
    }

    [Fact]
    public void When_a_constructor_takes_a_generated_constructor_s_parameters_then_BPCL009_is_reported_at_it()
    {
        // The generated half declares the constructor the client is made with, over the transport and then each readonly
        // field, and the one its clones are made with; any other constructor is the class's own to declare.
        var result = Compile(
            """
            using System;
            using System.Net.Http;
            using Blueprint.HttpClient;
            using Contract;

            namespace Client;

            [BlueprintClient]
            [BlueprintClientInclude(typeof(SearchItemsQuery))]
            public partial class ItemsApi
            {
                private readonly string? _region;

                public ItemsApi(BlueprintHttpClient transport, string region)
                {
                }

                private ItemsApi(ItemsApi other, IUpstreamCredential? upstream, Action<HttpRequestMessage>? modifier)
                {
                }

                public ItemsApi(BlueprintHttpClient transport)
                    : this(transport, "eu")
                {
                }
            }
            """);

        var diagnostics = result.Diagnostics.Where(static d => d.Id == "BPCL009").ToArray();

        diagnostics.Select(d => TextOf(d)).Should().Equal("ItemsApi", "ItemsApi");
        diagnostics.Select(static d => d.Location.GetLineSpan().StartLinePosition.Line).Should().Equal(13, 17);
    }

    [Fact]
    public void When_a_client_is_valid_then_no_client_diagnostic_is_reported()
    {
        var result = Compile(
            """
            using Blueprint.HttpClient;
            using Contract;

            namespace Client;

            [BlueprintClient]
            [BlueprintClientInclude(typeof(SearchItemsQuery))]
            public partial class ItemsApi
            {
                private readonly string _region;

                public string Region() => _region;
            }
            """);

        result.Diagnostics.Where(static d => d.Id.StartsWith("BPCL", StringComparison.Ordinal)).Should().BeEmpty();
    }

    private static GenerationResult Compile(string source, params MetadataReference[] others)
    {
        var contract = TestHelpers.CreateReferenceFromSource<OperationDescriptorSourceGenerator>(Contract, "Contract");

        return TestHelpers.Compile<TypedClientGenerator>(source, [contract, .. others]);
    }

    private static string GeneratedHalf(GenerationResult result)
    {
        return string.Join("\n", result.Output.Select(static t => t.ToString()));
    }

    private static string TextOf(Diagnostic diagnostic)
    {
        return diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);
    }

    private static void AssertReported(GenerationResult result, string id, string expectedText)
    {
        var diagnostic = result.Diagnostics.Should().ContainSingle(d => d.Id == id).Subject;

        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        TextOf(diagnostic).Should().Be(expectedText);
    }
}
