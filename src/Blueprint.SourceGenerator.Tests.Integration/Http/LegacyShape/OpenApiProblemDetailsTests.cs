using System.Text.Json;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that the problem-details components say what the host always sends, as legacy Blueprint's did: a problem
/// always has a status, so <c>status</c> is required and never null; a validation problem's <c>errors</c> may be null.
/// </summary>
/// <remarks>
/// The expected shapes are legacy's <c>ProblemDetails</c> and <c>ValidationProblemDetails</c>, from Signature365's
/// committed Partners document. The host sets the status on every problem it writes (see
/// <c>HttpOperationResultExecutor</c>).
/// </remarks>
public class OpenApiProblemDetailsTests
{
    [Theory]
    [InlineData("ProblemDetails")]
    [InlineData("ValidationProblemDetails")]
    public void A_problems_status_is_required_and_never_null(string component)
    {
        using var document = Render();
        var schema = Schema(document, component);

        schema.GetProperty("required").EnumerateArray().Select(static r => r.GetString()).Should().Contain("status");

        var status = schema.GetProperty("properties").GetProperty("status");
        status.GetProperty("type").GetString().Should().Be("integer");
        status.TryGetProperty("nullable", out var nullable).Should().BeFalse(nullable.ToString());
    }

    [Fact]
    public void A_validation_problems_errors_may_be_null()
    {
        using var document = Render();
        var errors = Schema(document, "ValidationProblemDetails").GetProperty("properties").GetProperty("errors");

        errors.GetProperty("nullable").GetBoolean().Should().BeTrue();
        Schema(document, "ValidationProblemDetails").GetProperty("required").EnumerateArray()
            .Select(static r => r.GetString()).Should().NotContain("errors");
    }

    [Theory]
    [InlineData("type")]
    [InlineData("title")]
    [InlineData("detail")]
    [InlineData("instance")]
    public void A_problems_other_members_stay_nullable(string member)
    {
        using var document = Render();

        Schema(document, "ProblemDetails").GetProperty("properties").GetProperty(member).GetProperty("nullable").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("ProblemDetails")]
    [InlineData("ValidationProblemDetails")]
    public void A_problems_extensions_are_documented_as_legacy_documented_them(string component)
    {
        using var document = Render();
        var schema = Schema(document, component);
        var extensions = schema.GetProperty("properties").GetProperty("extensions");

        // Legacy's, from Signature365's committed Partners document: a nullable open object, which is not required, and
        // a problem open to members of any value beside it.
        extensions.GetProperty("type").GetString().Should().Be("object");
        extensions.GetProperty("nullable").GetBoolean().Should().BeTrue();
        extensions.GetProperty("additionalProperties").EnumerateObject().Should().BeEmpty();
        schema.GetProperty("required").EnumerateArray().Select(static r => r.GetString()).Should().NotContain("extensions");
        schema.GetProperty("additionalProperties").GetProperty("nullable").GetBoolean().Should().BeTrue();
    }

    private static JsonElement Schema(JsonDocument document, string component)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(component);
    }

    private static JsonDocument Render()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());

        return JsonDocument.Parse(rendered.Json);
    }
}
