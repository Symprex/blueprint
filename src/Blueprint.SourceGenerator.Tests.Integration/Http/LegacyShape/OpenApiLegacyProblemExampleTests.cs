using System.Net;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that under Legacy naming an operation's <see cref="ApiExceptionFactory"/> fields are documented as legacy
/// Blueprint documented them: each status's JSON content carries an <c>example</c> map keyed by the factory's type in
/// PascalCase, holding <c>{"value":{"status","title","type"}}</c>, and the response's description is the factory's title
/// when it is the status's only description, or empty when the status has more than one.
/// </summary>
/// <remarks>
/// Legacy's <c>ApiExceptionFactoryResponseConvention</c> (Signature365 8b40ecc961,
/// <c>Blueprint/Configuration/ApiExceptionFactoryResponseConvention.cs</c>) read each factory field's value, and
/// <c>OpenApiDocumentBuilder.cs:196-201</c> and <c>:246-262</c> wrote the description and the map. Signature365's
/// committed documents show the result: 389 responses with a map, every key the PascalCase of its type, and an empty
/// description wherever a status had more than one (including every 500, which also carried legacy's "Unexpected
/// error"). Signature365's client generator reads this singular <c>example</c> map to build each API's error catalogue.
/// </remarks>
public sealed class OpenApiLegacyProblemExampleTests
{
    [Fact]
    public void Under_legacy_naming_a_status_with_one_factory_is_described_by_its_title_and_carries_its_example()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var conflict = Responses(document).GetProperty("409");
        conflict.GetProperty("description").GetString().Should().Be("Problem example widget is locked");

        var example = JsonExample(conflict);
        example.EnumerateObject().Select(static p => p.Name).Should().Equal("ProblemExampleWidgetLocked");
        var value = example.GetProperty("ProblemExampleWidgetLocked").GetProperty("value");
        value.EnumerateObject().Select(static p => p.Name).Should().Equal("status", "title", "type");
        value.GetProperty("status").GetInt32().Should().Be(409);
        value.GetProperty("title").GetString().Should().Be("Problem example widget is locked");
        value.GetProperty("type").GetString().Should().Be("problem_example_widget_locked");
    }

    [Fact]
    public void Under_legacy_naming_a_status_with_two_factories_has_an_empty_description_and_both_examples()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var notFound = Responses(document).GetProperty("404");
        notFound.GetProperty("description").GetString().Should().BeEmpty();
        JsonExample(notFound).EnumerateObject().Select(static p => p.Name)
            .Should().BeEquivalentTo("MissingProblemExampleWidget", "MissingProblemExampleOwner");
    }

    [Fact]
    public void Under_legacy_naming_a_factory_at_500_has_an_empty_description_beside_the_unhandled_exception()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var serverError = Responses(document).GetProperty("500");
        serverError.GetProperty("description").GetString().Should().BeEmpty();
        JsonExample(serverError).EnumerateObject().Select(static p => p.Name).Should().Equal("ProblemExampleUpstreamFailed");
    }

    [Fact]
    public void Under_default_naming_no_example_is_written_and_descriptions_are_unchanged()
    {
        using var document = Render(OpenApiSchemaNaming.Default);

        foreach (var status in new[] { "404", "409", "500" })
        {
            var response = Responses(document).GetProperty(status);
            response.GetProperty("description").GetString().Should().NotBeNullOrEmpty();

            foreach (var content in response.GetProperty("content").EnumerateObject())
            {
                content.Value.TryGetProperty("example", out _).Should().BeFalse();
            }
        }
    }

    [Fact]
    public void A_non_public_factory_is_documented_too_as_legacy_documented_every_static_factory_field()
    {
        // Legacy's convention read GetFields(Static | NonPublic | Public), so a private factory's status and example were
        // documented like a public one's (Signature365's Portal declares 16 operations' factories privately).
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var gone = Responses(document).GetProperty("410");
        gone.GetProperty("description").GetString().Should().Be("Problem example widget is gone");
        JsonExample(gone).EnumerateObject().Select(static p => p.Name).Should().Equal("ProblemExampleWidgetGone");
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming, UsePlainJsonForErrorResponses = true });

        return JsonDocument.Parse(rendered.Json);
    }

    private static JsonElement Responses(JsonDocument document)
    {
        var path = document.RootElement.GetProperty("paths").EnumerateObject()
            .Single(static p => p.Name.EndsWith("legacy-shape/problem-example-widgets", StringComparison.Ordinal));

        return path.Value.GetProperty("post").GetProperty("responses");
    }

    private static JsonElement JsonExample(JsonElement response)
    {
        return response.GetProperty("content").GetProperty("application/json").GetProperty("example");
    }
}

/// <summary>Problems shared between operations, as Signature365 shares them (<c>PartnerApiExceptions</c>).</summary>
public static class ProblemExampleSharedProblems
{
    /// <summary>The widget is locked.</summary>
    public static readonly ApiExceptionFactory WidgetLocked = new(
        "Problem example widget is locked",
        "problem_example_widget_locked",
        HttpStatusCode.Conflict);
}

/// <summary>An operation declaring factories at 404 (two), 409 (referenced from another class), 410 (private) and 500.</summary>
[AllowAnonymous]
[RootLink("/legacy-shape/problem-example-widgets")]
[HttpPost]
public partial class SubmitProblemExampleWidgetCommand : ICommand<string>
{
    /// <summary>The widget does not exist.</summary>
    public static readonly ApiExceptionFactory MissingWidget = new(
        "Failed to find problem example widget",
        "missing_problem_example_widget",
        HttpStatusCode.NotFound);

    /// <summary>The owner does not exist.</summary>
    public static readonly ApiExceptionFactory MissingOwner = new(
        "Failed to find problem example owner",
        "missing_problem_example_owner",
        404);

    /// <summary>The widget is locked.</summary>
    public static readonly ApiExceptionFactory WidgetLocked = ProblemExampleSharedProblems.WidgetLocked;

    /// <summary>An upstream call failed.</summary>
    public static readonly ApiExceptionFactory UpstreamFailed = new(
        "Problem example upstream failed",
        "problem_example_upstream_failed",
        HttpStatusCode.InternalServerError);

    /// <summary>The widget has been removed; declared privately, as some Signature365 operations declare theirs.</summary>
    private static readonly ApiExceptionFactory _widgetGone = new(
        "Problem example widget is gone",
        "problem_example_widget_gone",
        HttpStatusCode.Gone);

    public string? Name { get; set; }

    public static string Handle(SubmitProblemExampleWidgetCommand command)
    {
        return command.Name ?? string.Empty;
    }
}
