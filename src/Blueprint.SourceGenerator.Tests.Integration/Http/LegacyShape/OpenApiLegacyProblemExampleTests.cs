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
/// when it is the status's only description, or JSON <c>null</c> when the status has more than one.
/// </summary>
/// <remarks>
/// Legacy's <c>ApiExceptionFactoryResponseConvention</c> (Signature365 8b40ecc961,
/// <c>Blueprint/Configuration/ApiExceptionFactoryResponseConvention.cs</c>) read each factory field's value, and
/// <c>OpenApiDocumentBuilder.cs:196-201</c> and <c>:246-262</c> wrote the description and the map. Signature365's
/// committed documents show the result: 389 responses with a map, every key the PascalCase of its type, and a JSON
/// <c>null</c> description (93 responses, no empty string in any document) wherever a status had more than one, including
/// every 500, which also carried legacy's "Unexpected error". Signature365's client generator reads this singular <c>example</c> map to build each API's error catalogue.
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
    public void Under_legacy_naming_a_status_with_two_factories_has_a_null_description_and_both_examples_in_declaration_order()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var notFound = Responses(document).GetProperty("404");
        notFound.GetProperty("description").ValueKind.Should().Be(JsonValueKind.Null);
        JsonExample(notFound).EnumerateObject().Select(static p => p.Name)
            .Should().Equal("MissingProblemExampleWidget", "MissingProblemExampleOwner"); // declaration order: MissingOwner sorts first by field name
    }

    [Fact]
    public void Under_legacy_naming_a_factory_at_500_has_a_null_description_beside_the_unhandled_exception()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var serverError = Responses(document).GetProperty("500");
        serverError.GetProperty("description").ValueKind.Should().Be(JsonValueKind.Null);
        JsonExample(serverError).EnumerateObject().Select(static p => p.Name).Should().Equal("ProblemExampleUpstreamFailed");
    }

    [Fact]
    public void Under_legacy_naming_a_factory_at_400_on_a_body_binding_command_is_described_by_its_title()
    {
        // Legacy never documented the pipeline's own 400 (BindingFailureResult), so the factory was the status's only
        // description: 89 single-factory 400s in Signature365's committed documents are described by their title.
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var badRequest = Responses(document).GetProperty("400");
        badRequest.GetProperty("description").GetString().Should().Be("Problem example widget is invalid");
        JsonExample(badRequest).EnumerateObject().Select(static p => p.Name).Should().Equal("ProblemExampleWidgetInvalid");
    }

    [Fact]
    public void Under_legacy_naming_a_factory_at_403_on_a_resource_operation_is_described_by_its_title()
    {
        // Likewise legacy never documented the pipeline's own 403 (ForbiddenResult); 10 single-factory 403s in the
        // committed documents are described by their title.
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var forbidden = GuardedResponses(document).GetProperty("403");
        forbidden.GetProperty("description").GetString().Should().Be("Problem example widget is guarded");
        JsonExample(forbidden).EnumerateObject().Select(static p => p.Name).Should().Equal("ProblemExampleWidgetGuarded");
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

    private static JsonElement GuardedResponses(JsonDocument document)
    {
        var path = document.RootElement.GetProperty("paths").EnumerateObject()
            .Single(static p => p.Name.EndsWith("legacy-shape/problem-example-guarded", StringComparison.Ordinal));

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

/// <summary>An operation declaring factories at 400, 404 (two), 409 (referenced from another class), 410 (private) and 500.</summary>
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

    /// <summary>The widget is invalid.</summary>
    public static readonly ApiExceptionFactory WidgetInvalid = new(
        "Problem example widget is invalid",
        "problem_example_widget_invalid",
        HttpStatusCode.BadRequest);

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

/// <summary>A [Resource] operation, so the pipeline adds its own 403, declaring a factory at 403.</summary>
[Resource("problem-example", "update")]
[RootLink("/legacy-shape/problem-example-guarded")]
[HttpPost]
public partial class SubmitProblemExampleGuardedCommand : ICommand<string>
{
    /// <summary>The widget is guarded.</summary>
    public static readonly ApiExceptionFactory WidgetGuarded = new(
        "Problem example widget is guarded",
        "problem_example_widget_guarded",
        HttpStatusCode.Forbidden);

    public string? Name { get; set; }

    public static string Handle(SubmitProblemExampleGuardedCommand command)
    {
        return command.Name ?? string.Empty;
    }
}
