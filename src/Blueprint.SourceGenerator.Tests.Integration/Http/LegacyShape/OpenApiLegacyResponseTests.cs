using System.Text.Json;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that under Legacy naming a document describes the 422 and 500 every operation has as legacy did, and leaves
/// out the 400, 401 and 403 the pipeline adds for itself, while Default naming keeps the status texts and the responses.
/// </summary>
/// <remarks>
/// Legacy's <c>HttpHost</c> gave every operation a 500 "Unexpected error" and a 422 "Validation failure" (Signature365
/// 8b40ecc961, <c>Blueprint.Http/HttpHost.cs:90-94</c>) and documented no other response of its own: a 400, 401 or 403 came
/// only from an <c>ApiException</c> factory, where one is declared. In the 18 legacy documents rendered for Signature365
/// all 628 422s read "Validation failure" and 595 of the 628 500s "Unexpected error" (the other 33 are null, R125), and
/// every 400, 401 and 403 (99, 2 and 9) carries a factory's example. The generated client's
/// error handling reads the status codes an operation documents, so a response legacy never had is one it never handled.
/// </remarks>
public sealed class OpenApiLegacyResponseTests
{
    [Fact]
    public void Under_legacy_naming_the_422_and_500_are_described_as_legacy_described_them()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var responses = Responses(document, "/accounts/me", "get");

        responses.GetProperty("422").GetProperty("description").GetString().Should().Be("Validation failure");
        responses.GetProperty("500").GetProperty("description").GetString().Should().Be("Unexpected error");
    }

    [Fact]
    public void Under_default_naming_the_422_and_500_keep_their_status_texts()
    {
        using var document = Render(OpenApiSchemaNaming.Default);

        var responses = Responses(document, "/accounts/me", "get");

        responses.GetProperty("422").GetProperty("description").GetString().Should().Be("Unprocessable Entity");
        responses.GetProperty("500").GetProperty("description").GetString().Should().Be("Internal Server Error");
    }

    [Fact]
    public void Under_legacy_naming_the_pipelines_own_401_and_403_are_left_out()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var responses = Responses(document, "/accounts/me", "get");

        responses.TryGetProperty("401", out _).Should().BeFalse("legacy never documented the unauthenticated response");
        responses.TryGetProperty("403", out _).Should().BeFalse("legacy never documented the forbidden response");
        responses.TryGetProperty("200", out _).Should().BeTrue();
    }

    [Fact]
    public void Under_legacy_naming_the_pipelines_own_400_is_left_out()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        Responses(document, "/body-widgets/preview", "post").TryGetProperty("400", out _).Should().BeFalse("legacy never documented the binding failure response");
    }

    [Fact]
    public void Under_default_naming_the_pipelines_own_400_401_and_403_are_kept()
    {
        using var document = Render(OpenApiSchemaNaming.Default);

        Responses(document, "/accounts/me", "get").TryGetProperty("401", out _).Should().BeTrue();
        Responses(document, "/accounts/me", "get").TryGetProperty("403", out _).Should().BeTrue();
        Responses(document, "/body-widgets/preview", "post").TryGetProperty("400", out _).Should().BeTrue();
    }

    [Fact]
    public void Under_legacy_naming_a_400_and_a_403_an_operations_factories_declare_are_kept_with_their_titles()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        Responses(document, "/legacy-shape/problem-example-widgets", "post").GetProperty("400").GetProperty("description").GetString()
            .Should().Be("Problem example widget is invalid");
        Responses(document, "/legacy-shape/problem-example-guarded", "post").GetProperty("403").GetProperty("description").GetString()
            .Should().Be("Problem example widget is guarded");
    }

    private static JsonElement Responses(JsonDocument document, string path, string method)
    {
        return document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("responses");
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming });

        return JsonDocument.Parse(rendered.Json);
    }
}
