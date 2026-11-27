using System.Net;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Persistence;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Issue #197: the factory an operation names with <c>[ConcurrencyConflictError]</c> is documented among its error responses,
/// whether it is a static field, found as every factory field is, or a getter-backed property, which has no backing field
/// to find and is documented because the attribute names it.
/// </summary>
public sealed class OpenApiConcurrencyConflictErrorTests
{
    [Fact]
    public void A_getter_backed_concurrency_conflict_error_is_documented_as_a_409()
    {
        var conflict = Responses("concurrency-conflict/getter-claims").GetProperty("409");

        JsonExample(conflict).EnumerateObject().Select(static p => p.Name).Should().Equal("ConcurrencyConflictClaimTaken");
        JsonExample(conflict).GetProperty("ConcurrencyConflictClaimTaken").GetProperty("value").GetProperty("type").GetString()
            .Should().Be("concurrency_conflict_claim_taken");
    }

    [Fact]
    public void An_operation_naming_no_concurrency_conflict_error_documents_no_409()
    {
        // Blueprint's own concurrency-conflict answers any saving operation's conflict, so documenting it would add a 409 to every one.
        Responses("concurrency-conflict/unnamed-claims").TryGetProperty("409", out _).Should().BeFalse();
    }

    private static JsonElement Responses(string route)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = OpenApiSchemaNaming.Legacy, UsePlainJsonForErrorResponses = true });
        using var document = JsonDocument.Parse(rendered.Json);

        var path = document.RootElement.GetProperty("paths").EnumerateObject()
            .Single(p => p.Name.EndsWith(route, StringComparison.Ordinal));

        return path.Value.GetProperty("post").GetProperty("responses").Clone();
    }

    private static JsonElement JsonExample(JsonElement response)
    {
        return response.GetProperty("content").GetProperty("application/json").GetProperty("example");
    }
}

/// <summary>A command whose concurrency conflict is answered by a getter-backed factory.</summary>
[AllowAnonymous]
[RootLink("/concurrency-conflict/getter-claims")]
[HttpPost]
[ConcurrencyConflictError(nameof(ClaimTaken))]
public partial class ConcurrencyConflictGetterClaimCommand : ICommand<string>
{
    /// <summary>Gets the error for a claim another request made first.</summary>
    public static ApiExceptionFactory ClaimTaken => new("Claim taken", "concurrency_conflict_claim_taken", HttpStatusCode.Conflict);

    /// <summary>Handles the command.</summary>
    /// <param name="command">The command.</param>
    /// <returns>A value.</returns>
    public static string Handle(ConcurrencyConflictGetterClaimCommand command)
    {
        return "claimed";
    }
}

/// <summary>A command that names no error for a concurrency conflict.</summary>
[AllowAnonymous]
[RootLink("/concurrency-conflict/unnamed-claims")]
[HttpPost]
public partial class ConcurrencyConflictUnnamedClaimCommand : ICommand<string>
{
    /// <summary>Handles the command.</summary>
    /// <param name="command">The command.</param>
    /// <returns>A value.</returns>
    public static string Handle(ConcurrencyConflictUnnamedClaimCommand command)
    {
        return "claimed";
    }
}
