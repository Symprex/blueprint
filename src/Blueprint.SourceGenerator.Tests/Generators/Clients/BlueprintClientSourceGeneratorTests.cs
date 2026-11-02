using Blueprint.SourceGenerator.Generators.Clients;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.Clients;

/// <summary>
/// The <c>[BlueprintClient]</c> typed-client generator: its output for a representative client, and each <c>BPCL</c>
/// diagnostic at its location. <see cref="TestHelpers.Compile{T}(string, bool)"/> also proves every run is fully cached.
/// </summary>
public class BlueprintClientSourceGeneratorTests
{
    private const string SnapshotsFolder = "Generators/Clients/Snapshots/";

    private const string Usings = """
        using System.Threading;
        using System.Threading.Tasks;
        using Blueprint;
        using Blueprint.Http;
        using Blueprint.HttpClient;
        using Blueprint.Model;
        using Blueprint.Model.Attributes;
        using Blueprint.Runtime.Exec;

        namespace TestNamespace;

        public partial class WidgetApiResource : ApiResource
        {
            public string Id { get; set; }
        }

        """;

    [Fact]
    public async Task When_client_includes_operations_then_generates_interface_and_implementation()
    {
        var source = Usings + """
            [BlueprintClient(SerializerContext = typeof(WidgetJsonContext))]
            [BlueprintClientInclude(typeof(GetWidgetQuery))]
            [BlueprintClientInclude(typeof(CreateWidgetCommand))]
            [BlueprintClientInclude(typeof(FindWidgetQuery))]
            [BlueprintClientInclude(typeof(ArchiveWidgetCommand))]
            public partial class WidgetsApi
            {
                private readonly string _region;

                private static readonly int _notAParameter = 1;

                public IWidgetsApi ForRegion(string region, int attempts = 3)
                {
                    return this.WithRequestModifier(request => request.Headers.Add("X-Region", region));
                }

                public IWidgetsApi Sorted(WidgetSort? sort = WidgetSort.Newest, WidgetSort fallback = WidgetSort.Oldest)
                {
                    return this;
                }

                public override string ToString()
                {
                    return "not copied";
                }

                protected void NotCopied()
                {
                }
            }

            public enum WidgetSort
            {
                Oldest,
                Newest,
            }

            [RootLink("/widgets/{Id}")]
            public partial class GetWidgetQuery : IQuery<WidgetApiResource>
            {
                public string Id { get; set; }

                public string Locale { get; set; }

                public static class Http
                {
                    public static string BuildUrl(GetWidgetQuery operation) => "/widgets";
                    public static string AppendQueryString(GetWidgetQuery operation, string url) => url;
                }
            }

            [RootLink("/widgets")]
            [HttpPost]
            public partial class CreateWidgetCommand : ICommand<ResourceCreated<WidgetApiResource>>
            {
                public string Name { get; set; }

                public static class Http
                {
                    public static string BuildUrl(CreateWidgetCommand operation) => "/widgets";
                }
            }

            [RootLink("/widgets/find/{Id}")]
            public partial class FindWidgetQuery : IQuery<Results<OkResult<WidgetApiResource>, NotFoundResult<WidgetApiResource>>>
            {
                public string Id { get; set; }

                public static class Http
                {
                    public static string BuildUrl(FindWidgetQuery operation) => "/widgets/find";
                    public static string AppendQueryString(FindWidgetQuery operation, string url) => url;
                }
            }

            [RootLink("/widgets/{Id}/archive")]
            [HttpPost]
            public partial class ArchiveWidgetCommand : ICommand<StatusCodeResult.Accepted>
            {
                public string Id { get; set; }

                public static class Http
                {
                    public static string BuildUrl(ArchiveWidgetCommand operation) => "/widgets/archive";
                }
            }

            [System.Text.Json.Serialization.JsonSerializable(typeof(WidgetApiResource))]
            [System.Text.Json.Serialization.JsonSerializable(typeof(GetWidgetQuery))]
            [System.Text.Json.Serialization.JsonSerializable(typeof(CreateWidgetCommand))]
            [System.Text.Json.Serialization.JsonSerializable(typeof(FindWidgetQuery))]
            [System.Text.Json.Serialization.JsonSerializable(typeof(ArchiveWidgetCommand))]
            public sealed class WidgetJsonContext : System.Text.Json.Serialization.JsonSerializerContext
            {
                private WidgetJsonContext()
                    : base(null)
                {
                }

                public static WidgetJsonContext Default { get; } = new WidgetJsonContext();

                protected override System.Text.Json.JsonSerializerOptions GeneratedSerializerOptions => null;

                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo GetTypeInfo(System.Type type) => null;
            }
            """;

        var result = Compile(source);

        result.Diagnostics.Should().BeEmpty();
        await result.VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public void When_a_readonly_field_would_take_the_transport_parameters_name_then_it_keeps_its_own()
    {
        var source = Usings + """
            [BlueprintClient]
            public sealed partial class WidgetsApi
            {
                private readonly object _client;
            }
            """;

        var result = Compile(source);

        result.Diagnostics.Should().BeEmpty();
        result.Output.Single().ToString().Should()
            .Contain("public WidgetsApi(global::Blueprint.HttpClient.BlueprintHttpClient client, object _client)")
            .And.Contain("private IWidgetsApi WithRequestModifier(");
    }

    [Fact]
    public void When_operation_declares_more_than_one_http_method_then_sends_the_one_the_descriptor_registers_first()
    {
        // The descriptor registers explicit methods in ordinal order and BlueprintHttpClient<TApp> sends the first, so
        // declaration order must not decide the typed client's method.
        var source = Usings + """
            [BlueprintClient]
            [BlueprintClientInclude(typeof(SaveWidgetCommand))]
            public partial class WidgetsApi
            {
            }

            [RootLink("/widgets/save")]
            [HttpPut]
            [HttpDelete]
            public partial class SaveWidgetCommand : ICommand<WidgetApiResource>
            {
                public static class Http
                {
                    public static string BuildUrl(SaveWidgetCommand operation) => "/widgets/save";
                }
            }
            """;

        var result = Compile(source);

        result.Output.Single().ToString().Should().Contain("global::System.Net.Http.HttpMethod.Delete,");
    }

    [Fact]
    public void When_a_readonly_fields_own_name_is_already_a_parameter_then_it_gets_one_still_unused()
    {
        // A field named "client" falls back to its own name, the transport's; "region" falls back to the name "_region"
        // has just taken. Each fallback used to be taken as it was, duplicating a constructor parameter (CS0100).
        var source = Usings + """
            [BlueprintClient]
            public partial class WidgetsApi
            {
                private readonly object client;

                private readonly string _region;

                private readonly string region;
            }
            """;

        var result = Compile(source);

        result.Diagnostics.Should().BeEmpty();
        result.Output.Single().ToString().Should().Contain(
            "public WidgetsApi(global::Blueprint.HttpClient.BlueprintHttpClient client, object client2, string region, string region2)");
    }

    [Fact]
    public void When_a_public_method_returns_or_takes_by_reference_then_the_interface_keeps_the_ref_kinds()
    {
        // The interface member used to return by value, which the class's by-reference method cannot implement (CS8152).
        var source = Usings + """
            [BlueprintClient]
            public partial class WidgetsApi
            {
                private int _count;

                public ref int Count() => ref this._count;

                public ref readonly int PeekCount() => ref this._count;

                public int Add(ref readonly int amount) => this._count += amount;
            }
            """;

        var result = Compile(source);

        result.Diagnostics.Should().BeEmpty();
        result.Output.Single().ToString().Should()
            .Contain("ref int Count();")
            .And.Contain("ref readonly int PeekCount();")
            .And.Contain("int Add(ref readonly int amount);");
    }

    [Fact]
    public void When_serializer_context_reaches_a_type_through_a_declared_types_property_then_reports_no_BPCL007()
    {
        // System.Text.Json generates metadata for every type a declared type reaches, so the context covers the result
        // through the command's property. Only the declared roots used to count.
        var source = Usings + """
            [BlueprintClient(SerializerContext = typeof(WidgetJsonContext))]
            [BlueprintClientInclude(typeof(SaveWidgetCommand))]
            public partial class WidgetsApi
            {
            }

            [RootLink("/widgets/save")]
            [HttpPost]
            public partial class SaveWidgetCommand : ICommand<WidgetApiResource>
            {
                public System.Collections.Generic.List<WidgetApiResource> Widgets { get; set; }

                public static class Http
                {
                    public static string BuildUrl(SaveWidgetCommand operation) => "/widgets/save";
                }
            }

            [System.Text.Json.Serialization.JsonSerializable(typeof(SaveWidgetCommand))]
            public sealed class WidgetJsonContext : System.Text.Json.Serialization.JsonSerializerContext
            {
                private WidgetJsonContext()
                    : base(null)
                {
                }

                public static WidgetJsonContext Default { get; } = new WidgetJsonContext();

                protected override System.Text.Json.JsonSerializerOptions GeneratedSerializerOptions => null;

                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo GetTypeInfo(System.Type type) => null;
            }
            """;

        Compile(source).Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void When_included_operation_is_itself_named_Http_then_BPCL004_says_so()
    {
        var source = Usings + """
            [BlueprintClient]
            [BlueprintClientInclude(typeof(Http))]
            public partial class WidgetsApi
            {
            }

            [RootLink("/widgets")]
            public partial class Http : IQuery<WidgetApiResource>
            {
            }
            """;

        Compile(source).Diagnostics.Should().ContainSingle(static d => d.Id == "BPCL004")
            .Which.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Should().Contain("is itself named Http");
    }

    [Fact]
    public void When_two_method_names_differ_only_in_their_first_letters_case_then_their_plans_stay_distinct()
    {
        // Lower-casing the method name's first letter used to name both plan fields _getWidgetAsyncPlan (CS0102).
        var source = Usings + """
            [BlueprintClient]
            [BlueprintClientInclude(typeof(GetWidgetQuery))]
            [BlueprintClientInclude(typeof(getWidgetQuery))]
            public partial class WidgetsApi
            {
            }

            [RootLink("/widgets/upper")]
            public partial class GetWidgetQuery : IQuery<WidgetApiResource>
            {
                public static class Http
                {
                    public static string BuildUrl(GetWidgetQuery operation) => "/widgets/upper";

                    public static string AppendQueryString(GetWidgetQuery operation, string url) => url;
                }
            }

            [RootLink("/widgets/lower")]
            public partial class getWidgetQuery : IQuery<WidgetApiResource>
            {
                public static class Http
                {
                    public static string BuildUrl(getWidgetQuery operation) => "/widgets/lower";

                    public static string AppendQueryString(getWidgetQuery operation, string url) => url;
                }
            }
            """;

        Compile(source).Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task When_client_is_not_partial_then_reports_BPCL001()
    {
        var source = Usings + """
            [BlueprintClient]
            [BlueprintClientInclude(typeof(GetWidgetQuery))]
            public class WidgetsApi
            {
            }

            public partial class Outer
            {
                [BlueprintClient]
                public partial class NestedApi
                {
                }
            }

            [BlueprintClient]
            public partial class GenericApi<T>
            {
            }

            [BlueprintClient]
            public abstract partial class AbstractApi
            {
            }

            [RootLink("/widgets/{Id}")]
            public partial class GetWidgetQuery : IQuery<WidgetApiResource>
            {
                public string Id { get; set; }

                public static class Http
                {
                    public static string BuildUrl(GetWidgetQuery operation) => "/widgets";
                    public static string AppendQueryString(GetWidgetQuery operation, string url) => url;
                }
            }
            """;

        await Compile(source).VerifyCompilationFailedAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task When_include_is_not_a_concrete_non_generic_operation_then_reports_BPCL002()
    {
        var source = Usings + """
            [BlueprintClient]
            [BlueprintClientInclude(typeof(WidgetQueryBase))]
            [BlueprintClientInclude(typeof(GenericWidgetQuery<>))]
            [BlueprintClientInclude(typeof(WidgetApiResource))]
            public partial class WidgetsApi
            {
            }

            public abstract class WidgetQueryBase : IQuery<WidgetApiResource>
            {
            }

            [RootLink("/widgets/generic")]
            public partial class GenericWidgetQuery<T> : IQuery<WidgetApiResource>
            {
            }
            """;

        await Compile(source).VerifyCompilationFailedAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task When_operation_has_no_typed_result_then_reports_BPCL003()
    {
        var source = Usings + """
            [BlueprintClient]
            [BlueprintClientInclude(typeof(PingCommand))]
            public partial class WidgetsApi
            {
            }

            [RootLink("/ping")]
            [HttpPost]
            public partial class PingCommand : ICommand
            {
            }
            """;

        await Compile(source).VerifyCompilationFailedAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task When_operation_has_no_typed_request_builder_or_is_unexposed_then_reports_BPCL004()
    {
        var source = Usings + """
            [BlueprintClient]
            [BlueprintClientInclude(typeof(UnroutedQuery))]
            [BlueprintClientInclude(typeof(HttpMemberQuery))]
            [BlueprintClientInclude(typeof(InternalOnlyQuery))]
            [BlueprintClientInclude(typeof(SpecialWidgetQuery))]
            public partial class WidgetsApi
            {
            }

            public partial class UnroutedQuery : IQuery<WidgetApiResource>
            {
            }

            [RootLink("/widgets/http")]
            public partial class HttpMemberQuery : IQuery<WidgetApiResource>
            {
                public string Http { get; set; }
            }

            [RootLink("/widgets/internal")]
            [UnexposedOperation]
            public partial class InternalOnlyQuery : IQuery<WidgetApiResource>
            {
            }

            [RootLink("/widgets/{Id}")]
            public partial class GetWidgetQuery : IQuery<WidgetApiResource>
            {
                public string Id { get; set; }

                public static class Http
                {
                    public static string BuildUrl(GetWidgetQuery operation) => "/widgets";
                    public static string AppendQueryString(GetWidgetQuery operation, string url) => url;
                }
            }

            [RootLink("/widgets/special/{Id}")]
            public partial class SpecialWidgetQuery : GetWidgetQuery
            {
            }
            """;

        await Compile(source).VerifyCompilationFailedAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task When_operation_is_included_twice_or_two_share_a_method_name_then_reports_BPCL005()
    {
        var source = Usings + """
            [BlueprintClient]
            [BlueprintClientInclude(typeof(GetWidgetQuery))]
            [BlueprintClientInclude(typeof(GetWidgetQuery))]
            [BlueprintClientInclude(typeof(GetWidgetCommand))]
            public partial class WidgetsApi
            {
            }

            [RootLink("/widgets/{Id}")]
            public partial class GetWidgetQuery : IQuery<WidgetApiResource>
            {
                public string Id { get; set; }

                public static class Http
                {
                    public static string BuildUrl(GetWidgetQuery operation) => "/widgets";
                    public static string AppendQueryString(GetWidgetQuery operation, string url) => url;
                }
            }

            [RootLink("/widgets/get/{Id}")]
            [HttpPost]
            public partial class GetWidgetCommand : ICommand<WidgetApiResource>
            {
                public string Id { get; set; }

                public static class Http
                {
                    public static string BuildUrl(GetWidgetCommand operation) => "/widgets/get";
                }
            }
            """;

        await Compile(source).VerifyCompilationFailedAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task When_public_method_is_generic_then_reports_BPCL006()
    {
        var source = Usings + """
            [BlueprintClient]
            [BlueprintClientInclude(typeof(GetWidgetQuery))]
            public partial class WidgetsApi
            {
                public T Echo<T>(T value)
                {
                    return value;
                }
            }

            [RootLink("/widgets/{Id}")]
            public partial class GetWidgetQuery : IQuery<WidgetApiResource>
            {
                public string Id { get; set; }

                public static class Http
                {
                    public static string BuildUrl(GetWidgetQuery operation) => "/widgets";
                    public static string AppendQueryString(GetWidgetQuery operation, string url) => url;
                }
            }
            """;

        await Compile(source).VerifyCompilationFailedAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task When_serializer_context_lacks_a_type_then_reports_BPCL007_warning()
    {
        var source = Usings + """
            [BlueprintClient(SerializerContext = typeof(WidgetJsonContext))]
            [BlueprintClientInclude(typeof(CreateWidgetCommand))]
            public partial class WidgetsApi
            {
            }

            [RootLink("/widgets")]
            [HttpPost]
            public partial class CreateWidgetCommand : ICommand<WidgetApiResource>
            {
                public string Name { get; set; }

                public static class Http
                {
                    public static string BuildUrl(CreateWidgetCommand operation) => "/widgets";
                }
            }

            [System.Text.Json.Serialization.JsonSerializable(typeof(WidgetApiResource))]
            public sealed class WidgetJsonContext : System.Text.Json.Serialization.JsonSerializerContext
            {
                private WidgetJsonContext()
                    : base(null)
                {
                }

                public static WidgetJsonContext Default { get; } = new WidgetJsonContext();

                protected override System.Text.Json.JsonSerializerOptions GeneratedSerializerOptions => null;

                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo GetTypeInfo(System.Type type) => null;
            }
            """;

        await Compile(source).VerifySuccessWithDiagnosticsAsync(SnapshotsFolder, "BPCL007");
    }

    [Fact]
    public void When_result_is_an_abstract_resource_event_alone_or_in_a_union_then_reports_BPCL008()
    {
        // BlueprintHttpClient<TApp> refuses either before sending; the typed client used to choose a JSON reader for it.
        var source = Usings + """
            [BlueprintClient]
            [BlueprintClientInclude(typeof(SaveWidgetCommand))]
            [BlueprintClientInclude(typeof(ArchiveWidgetCommand))]
            public partial class WidgetsApi
            {
            }

            public abstract class AuditedWidgetEvent : Blueprint.Http.ResourceEvent<WidgetApiResource>
            {
                protected AuditedWidgetEvent(Blueprint.Http.ResourceEventChangeType changeType, string eventId, WidgetApiResource data)
                    : base(changeType, eventId, data)
                {
                }
            }

            [RootLink("/widgets/save")]
            [HttpPost]
            public partial class SaveWidgetCommand : ICommand<AuditedWidgetEvent>
            {
                public static class Http
                {
                    public static string BuildUrl(SaveWidgetCommand operation) => "/widgets/save";
                }
            }

            [RootLink("/widgets/archive")]
            [HttpPost]
            public partial class ArchiveWidgetCommand : ICommand<Results<AuditedWidgetEvent, StatusCodeResult.Accepted>>
            {
                public static class Http
                {
                    public static string BuildUrl(ArchiveWidgetCommand operation) => "/widgets/archive";
                }
            }
            """;

        Compile(source).Diagnostics.Where(static d => d.Id == "BPCL008")
            .Select(static d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .Should().HaveCount(2)
            .And.Contain(static m => m.Contains("SaveWidgetCommand"))
            .And.Contain(static m => m.Contains("ArchiveWidgetCommand"));
    }

    [Fact]
    public async Task When_result_shape_has_no_reader_then_reports_BPCL008()
    {
        var source = Usings + """
            [BlueprintClient]
            [BlueprintClientInclude(typeof(DownloadWidgetQuery))]
            [BlueprintClientInclude(typeof(SaveWidgetCommand))]
            public partial class WidgetsApi
            {
            }

            [RootLink("/widgets/download")]
            public partial class DownloadWidgetQuery : IQuery<FileStreamResult>
            {
            }

            [RootLink("/widgets/save")]
            [HttpPost]
            public partial class SaveWidgetCommand : ICommand<OkResult>
            {
            }
            """;

        await Compile(source).VerifyCompilationFailedAsync(SnapshotsFolder);
    }

    private static GenerationResult Compile(string source)
    {
        return TestHelpers.Compile<BlueprintClientSourceGenerator>(source);
    }
}
