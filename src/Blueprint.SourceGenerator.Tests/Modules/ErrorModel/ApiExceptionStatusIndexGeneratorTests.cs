using Blueprint.SourceGenerator.Generators.ApiExceptionStatusIndex;
using Blueprint.SourceGenerator.Generators.Descriptors;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.ErrorModel;

/// <summary>
/// Proves the <c>ApiExceptionStatusIndexGenerator</c> discovers <c>public static readonly ApiExceptionFactory</c>
/// fields and emits assembly-level <c>[BlueprintApiExceptionStatus]</c> attributes that downstream generators
/// can read for cross-assembly status code resolution.
/// </summary>
public class ApiExceptionStatusIndexGeneratorTests
{
    [Fact]
    public void When_compilation_has_factory_fields_then_emits_assembly_attributes()
    {
        const string source = """
            using System.Net;
            using Blueprint;

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

                    public static readonly ApiExceptionFactory ServerError = new(
                        "Server error",
                        "server_error",
                        HttpStatusCode.InternalServerError);
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<ApiExceptionStatusIndexGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var generatedSources = result.GetGeneratedSources(nameof(ApiExceptionStatusIndexGenerator));
        generatedSources.Should().HaveCountGreaterOrEqualTo(2, "should emit the attribute type and the assembly attributes");

        var statusesSource = generatedSources
            .FirstOrDefault(s => s.HintName.Contains("ApiExceptionStatuses"));
        statusesSource.Should().NotBeNull("should emit a Blueprint.ApiExceptionStatuses.g.cs file");

        var sourceText = statusesSource!.SourceText.ToString();
        sourceText.Should().Contain("\"TestNamespace.CommonApiExceptions\"");
        sourceText.Should().Contain("\"NotFound\"");
        sourceText.Should().Contain("404");
        sourceText.Should().Contain("\"Conflict\"");
        sourceText.Should().Contain("409");
        sourceText.Should().Contain("\"ServerError\"");
        sourceText.Should().Contain("500");
    }

    [Fact]
    public void When_factory_uses_int_literal_status_then_emits_attribute()
    {
        const string source = """
            using Blueprint;

            namespace TestNamespace
            {
                public static class ApiExceptions
                {
                    public static readonly ApiExceptionFactory CustomError = new(
                        "Custom error",
                        "custom_error",
                        422);
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<ApiExceptionStatusIndexGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var statusesSource = result.GetGeneratedSources(nameof(ApiExceptionStatusIndexGenerator))
            .FirstOrDefault(s => s.HintName.Contains("ApiExceptionStatuses"));
        statusesSource.Should().NotBeNull();
        statusesSource!.SourceText.ToString().Should().Contain("422");
    }

    [Fact]
    public void When_factory_references_another_field_in_same_compilation_then_follows_reference()
    {
        const string source = """
            using System.Net;
            using Blueprint;

            namespace TestNamespace
            {
                public static class BaseExceptions
                {
                    public static readonly ApiExceptionFactory NotFound = new(
                        "Not found",
                        "not_found",
                        HttpStatusCode.NotFound);
                }

                public static class DomainExceptions
                {
                    public static readonly ApiExceptionFactory MissingThing = BaseExceptions.NotFound;
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<ApiExceptionStatusIndexGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var statusesSource = result.GetGeneratedSources(nameof(ApiExceptionStatusIndexGenerator))
            .FirstOrDefault(s => s.HintName.Contains("ApiExceptionStatuses"));
        statusesSource.Should().NotBeNull();

        var text = statusesSource!.SourceText.ToString();

        // Both the original and the reference should resolve to 404
        text.Should().Contain("\"NotFound\", 404");
        text.Should().Contain("\"MissingThing\", 404");
    }

    [Fact]
    public void When_factory_field_is_not_public_then_not_emitted()
    {
        const string source = """
            using System.Net;
            using Blueprint;

            namespace TestNamespace
            {
                public static class ApiExceptions
                {
                    private static readonly ApiExceptionFactory Secret = new(
                        "Secret",
                        "secret",
                        HttpStatusCode.NotFound);

                    internal static readonly ApiExceptionFactory Internal = new(
                        "Internal only",
                        "internal_only",
                        HttpStatusCode.BadRequest);
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<ApiExceptionStatusIndexGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        // No assembly attributes should be emitted for non-public fields
        var statusesSource = result.GetGeneratedSources(nameof(ApiExceptionStatusIndexGenerator))
            .FirstOrDefault(s => s.HintName.Contains("ApiExceptionStatuses"));
        statusesSource.Should().BeNull("non-public fields should not be indexed");
    }

    [Fact]
    public void When_cross_assembly_has_status_attributes_then_descriptor_resolves_custom_names()
    {
        // Step 1: Compile a shared library with custom-named factory fields + the index generator
        const string sharedSource = """
            using System.Net;
            using Blueprint;

            namespace SharedExceptions
            {
                public static class BillingApiExceptions
                {
                    public static readonly ApiExceptionFactory MissingStripeAccount = new(
                        "Missing Stripe Account",
                        "missing_stripe_account",
                        HttpStatusCode.NotFound);

                    public static readonly ApiExceptionFactory StripeError = new(
                        "Stripe Error",
                        "stripe_error",
                        HttpStatusCode.InternalServerError);
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

        // Run the index generator on the shared compilation
        var indexGenerator = new ApiExceptionStatusIndexGenerator();
        var driver = CSharpGeneratorDriver.Create(indexGenerator).WithUpdatedParseOptions(parseOptions);
        driver = (CSharpGeneratorDriver)driver.RunGenerators(sharedCompilation);

        var driverResult = driver.GetRunResult();
        driverResult.Diagnostics.Should().BeEmpty();

        // The shared compilation now has the generated assembly attributes
        var updatedSharedCompilation = sharedCompilation.AddSyntaxTrees(
            driverResult.GeneratedTrees);

        // Emit the shared assembly with the generated attributes baked in
        using var sharedStream = new MemoryStream();
        var emitResult = updatedSharedCompilation.Emit(sharedStream);
        emitResult.Diagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("shared assembly with generated attributes must compile");
        sharedStream.Position = 0;
        var sharedRef = MetadataReference.CreateFromStream(sharedStream);

        // Step 2: Compile the downstream operation that references the shared assembly
        const string operationSource = """
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

                [RootLink("/billing/{Id}")]
                [HttpGet]
                public partial class GetBillingQuery : IQuery<OkResult<string>>
                {
                    public static readonly ApiExceptionFactory MissingAccount = BillingApiExceptions.MissingStripeAccount;
                    public static readonly ApiExceptionFactory Stripe = BillingApiExceptions.StripeError;

                    public string Id { get; set; }

                    public static Task<OkResult<string>> HandleAsync(GetBillingQuery operation) =>
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
            "custom-named cross-assembly fields should resolve via assembly-level attributes (no BP010 warning)");

        var descriptorSources = result.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator));
        var descriptorSource = string.Join("\n", descriptorSources.Select(static s => s.SourceText));

        // MissingStripeAccount → 404, StripeError → 500 should be resolved
        descriptorSource.Should().Contain("404");
        descriptorSource.Should().Contain("500");
        descriptorSource.Should().Contain("OperationHttpResponseMetadata");
    }

    [Fact]
    public void When_no_factory_fields_exist_then_no_statuses_file_emitted()
    {
        const string source = """
            namespace TestNamespace
            {
                public class RegularClass
                {
                    public string Name { get; set; }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<ApiExceptionStatusIndexGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var statusesSource = result.GetGeneratedSources(nameof(ApiExceptionStatusIndexGenerator))
            .FirstOrDefault(s => s.HintName.Contains("ApiExceptionStatuses"));
        statusesSource.Should().BeNull("no factory fields means no statuses file");
    }
}
