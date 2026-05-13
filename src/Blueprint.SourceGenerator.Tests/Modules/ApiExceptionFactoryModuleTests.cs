using Blueprint.SourceGenerator.Generators.Descriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Proves the <c>ApiExceptionFactoryModule</c> discovers <c>public static readonly ApiExceptionFactory</c> fields
/// on operation types and projects them into the generated descriptor as additional HTTP response metadata entries
/// for OpenAPI documentation.
/// </summary>
public class ApiExceptionFactoryModuleTests
{
    [Fact]
    public void When_operation_has_factory_with_HttpStatusCode_enum_then_descriptor_has_response_metadata()
    {
        const string source = """
            using System.Net;
            using System.Threading.Tasks;
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/things/{Id}")]
                [HttpGet]
                public partial class GetThingQuery : IQuery<OkResult<string>>
                {
                    public static readonly ApiExceptionFactory NotFound = new(
                        "Thing not found",
                        "thing_not_found",
                        HttpStatusCode.NotFound);

                    public string Id { get; set; }

                    public static Task<OkResult<string>> HandleAsync(GetThingQuery operation) =>
                        Task.FromResult(new OkResult<string>("ok"));
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSources = result.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator));
        var descriptorSource = string.Join("\n", descriptorSources.Select(static s => s.SourceText));

        // The 404 status code from HttpStatusCode.NotFound should appear in the response metadata
        descriptorSource.Should().Contain("404");
        descriptorSource.Should().Contain("OperationHttpResponseMetadata");
    }

    [Fact]
    public void When_operation_has_factory_with_int_status_then_descriptor_has_response_metadata()
    {
        const string source = """
            using System.Threading.Tasks;
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/items/{Id}")]
                [HttpGet]
                public partial class GetItemQuery : IQuery<OkResult<string>>
                {
                    public static readonly ApiExceptionFactory Gone = new(
                        "Item is gone",
                        "item_gone",
                        410);

                    public string Id { get; set; }

                    public static Task<OkResult<string>> HandleAsync(GetItemQuery operation) =>
                        Task.FromResult(new OkResult<string>("ok"));
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSources = result.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator));
        var descriptorSource = string.Join("\n", descriptorSources.Select(static s => s.SourceText));

        // The 410 status code from the int literal should appear in the response metadata
        descriptorSource.Should().Contain("410");
    }

    [Fact]
    public void When_operation_has_multiple_factories_then_all_status_codes_appear()
    {
        const string source = """
            using System.Net;
            using System.Threading.Tasks;
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/widgets/{Id}")]
                [HttpGet]
                public partial class GetWidgetQuery : IQuery<OkResult<string>>
                {
                    public static readonly ApiExceptionFactory NotFound = new(
                        "Widget not found",
                        "widget_not_found",
                        HttpStatusCode.NotFound);

                    public static readonly ApiExceptionFactory Conflict = new(
                        "Widget conflict",
                        "widget_conflict",
                        HttpStatusCode.Conflict);

                    public string Id { get; set; }

                    public static Task<OkResult<string>> HandleAsync(GetWidgetQuery operation) =>
                        Task.FromResult(new OkResult<string>("ok"));
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSources = result.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator));
        var descriptorSource = string.Join("\n", descriptorSources.Select(static s => s.SourceText));

        // Both 404 and 409 should be in the response metadata
        descriptorSource.Should().Contain("404");
        descriptorSource.Should().Contain("409");
    }

    [Fact]
    public void When_operation_has_no_factory_fields_then_no_additional_responses()
    {
        const string source = """
            using System.Threading.Tasks;
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/clean")]
                [HttpGet]
                public partial class CleanQuery : IQuery<OkResult<string>>
                {
                    public static Task<OkResult<string>> HandleAsync(CleanQuery operation) =>
                        Task.FromResult(new OkResult<string>("ok"));
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSources = result.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator));
        var descriptorSource = string.Join("\n", descriptorSources.Select(static s => s.SourceText));

        // No 404/409 etc. — only the standard failure responses (422, 500)
        descriptorSource.Should().NotContain("404");
        descriptorSource.Should().NotContain("409");
    }

    [Fact]
    public void When_factory_field_is_not_public_then_it_is_ignored()
    {
        const string source = """
            using System.Net;
            using System.Threading.Tasks;
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/private")]
                [HttpGet]
                public partial class PrivateFactoryQuery : IQuery<OkResult<string>>
                {
                    private static readonly ApiExceptionFactory Secret = new(
                        "Secret error",
                        "secret_error",
                        HttpStatusCode.NotFound);

                    public static Task<OkResult<string>> HandleAsync(PrivateFactoryQuery operation) =>
                        Task.FromResult(new OkResult<string>("ok"));
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSources = result.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator));
        var descriptorSource = string.Join("\n", descriptorSources.Select(static s => s.SourceText));

        // Private fields should not contribute to response metadata
        descriptorSource.Should().NotContain("404");
    }

    [Fact]
    public void When_operation_has_duplicate_status_codes_then_grouped_into_single_response()
    {
        const string source = """
            using System.Net;
            using System.Threading.Tasks;
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/dupes/{Id}")]
                [HttpGet]
                public partial class DuplicateStatusQuery : IQuery<OkResult<string>>
                {
                    public static readonly ApiExceptionFactory NotFoundA = new(
                        "Not found A",
                        "not_found_a",
                        HttpStatusCode.NotFound);

                    public static readonly ApiExceptionFactory NotFoundB = new(
                        "Not found B",
                        "not_found_b",
                        HttpStatusCode.NotFound);

                    public string Id { get; set; }

                    public static Task<OkResult<string>> HandleAsync(DuplicateStatusQuery operation) =>
                        Task.FromResult(new OkResult<string>("ok"));
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSources = result.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator));
        var descriptorSource = string.Join("\n", descriptorSources.Select(static s => s.SourceText));

        // Should have 404 but HttpResponseMetadataModule groups by status code
        descriptorSource.Should().Contain("404");
    }
}
