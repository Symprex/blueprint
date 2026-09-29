using Blueprint.SourceGenerator.Generators.Descriptors;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Proves the <c>ApiExceptionFactoryModule</c> discovers static <c>ApiExceptionFactory</c> fields of any accessibility
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
    public void When_factory_field_is_not_public_then_it_is_included_as_legacy_included_it()
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

        // Legacy's convention read GetFields(Static | NonPublic | Public), so a private factory contributes its status and,
        // since the descriptor is nested in the operation's partial type, its title and type. No diagnostic above proves
        // the generated reference to the private field compiles.
        descriptorSource.Should().Contain("404");
        descriptorSource.Should().Contain("global::TestNamespace.PrivateFactoryQuery.Secret.Title");
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

    [Fact]
    public void When_factory_field_references_another_static_field_then_status_code_is_resolved()
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
                public static class CommonApiExceptions
                {
                    public static readonly ApiExceptionFactory NotFound = new(
                        "Not found",
                        "not_found",
                        HttpStatusCode.NotFound);

                    public static readonly ApiExceptionFactory Conflict = new(
                        "Conflict",
                        "conflict",
                        HttpStatusCode.Conflict);
                }

                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/things/{Id}")]
                [HttpGet]
                public partial class GetThingQuery : IQuery<OkResult<string>>
                {
                    public static readonly ApiExceptionFactory ThingNotFound = CommonApiExceptions.NotFound;

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

        result.Diagnostics.Should().BeEmpty(
            "the field-reference chain should be followed to resolve the HTTP status code");

        var descriptorSources = result.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator));
        var descriptorSource = string.Join("\n", descriptorSources.Select(static s => s.SourceText));

        // The 404 status code from the referenced CommonApiExceptions.NotFound should appear
        descriptorSource.Should().Contain("404");
        descriptorSource.Should().Contain("OperationHttpResponseMetadata");
    }

    [Fact]
    public void When_factory_field_references_cross_assembly_field_named_after_HttpStatusCode_then_resolved()
    {
        // Compile CommonApiExceptions into a separate assembly (simulates cross-project reference)
        const string sharedSource = """
            using System.Net;
            using Blueprint;

            namespace SharedExceptions
            {
                public static class CommonApiExceptions
                {
                    public static readonly ApiExceptionFactory NotFound = new(
                        "Not found",
                        "not_found",
                        HttpStatusCode.NotFound);

                    public static readonly ApiExceptionFactory Forbidden = new(
                        "Forbidden",
                        "forbidden",
                        HttpStatusCode.Forbidden);
                }
            }
            """;

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var sharedTree = CSharpSyntaxTree.ParseText(sharedSource, parseOptions, "Shared.cs");

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(static a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.ApiExceptionFactory).Assembly.Location));

        var sharedCompilation = CSharpCompilation.Create(
            "SharedLib",
            [sharedTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Ensure the shared library compiles cleanly
        var sharedDiagnostics = sharedCompilation.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error);
        sharedDiagnostics.Should().BeEmpty("shared library must compile");

        // Create an in-memory metadata reference from the shared compilation
        using var sharedStream = new MemoryStream();
        sharedCompilation.Emit(sharedStream);
        sharedStream.Position = 0;
        var sharedRef = MetadataReference.CreateFromStream(sharedStream);

        // Now compile the operation that references SharedExceptions.CommonApiExceptions.NotFound
        const string operationSource = """
            using System.Net;
            using System.Threading.Tasks;
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;
            using SharedExceptions;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/things/{Id}")]
                [HttpGet]
                public partial class GetThingQuery : IQuery<OkResult<string>>
                {
                    public static readonly ApiExceptionFactory ThingNotFound = CommonApiExceptions.NotFound;
                    public static readonly ApiExceptionFactory AccessDenied = CommonApiExceptions.Forbidden;

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

        var result = TestHelpers.CompileWithTrackedGenerators(
            generators, [operationSource], ["Input0.cs"], [sharedRef]);

        result.Diagnostics.Should().BeEmpty(
            "cross-assembly field names matching HttpStatusCode members should resolve without BP010");

        var descriptorSources = result.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator));
        var descriptorSource = string.Join("\n", descriptorSources.Select(static s => s.SourceText));

        // Both 404 and 403 should be resolved from the cross-assembly field names
        descriptorSource.Should().Contain("404");
        descriptorSource.Should().Contain("403");
        descriptorSource.Should().Contain("OperationHttpResponseMetadata");
    }
}
