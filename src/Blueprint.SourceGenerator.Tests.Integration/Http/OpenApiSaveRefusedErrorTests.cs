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
/// Issue #197: the factory an operation names with <c>[SaveRefusedError]</c> is documented among its error responses,
/// whether it is a static field, found as every factory field is, or a getter-backed property, which has no backing field
/// to find and is documented because the attribute names it.
/// </summary>
public sealed class OpenApiSaveRefusedErrorTests
{
    [Fact]
    public void A_getter_backed_save_refused_error_is_documented_as_a_409()
    {
        var conflict = Responses("save-refused/getter-claims").GetProperty("409");

        JsonExample(conflict).EnumerateObject().Select(static p => p.Name).Should().Equal("SaveRefusedClaimTaken");
        JsonExample(conflict).GetProperty("SaveRefusedClaimTaken").GetProperty("value").GetProperty("type").GetString()
            .Should().Be("save_refused_claim_taken");
    }

    [Fact]
    public void An_operation_naming_no_save_refused_error_documents_no_409()
    {
        // Blueprint's own save_refused answers any saving operation's refusal, so documenting it would add a 409 to every one.
        Responses("save-refused/unnamed-claims").TryGetProperty("409", out _).Should().BeFalse();
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

/// <summary>A command whose refused save is answered by a getter-backed factory.</summary>
[AllowAnonymous]
[RootLink("/save-refused/getter-claims")]
[HttpPost]
[SaveRefusedError(nameof(ClaimTaken))]
public partial class SaveRefusedGetterClaimCommand : ICommand<string>
{
    /// <summary>Gets the error for a claim another request made first.</summary>
    public static ApiExceptionFactory ClaimTaken => new("Claim taken", "save_refused_claim_taken", HttpStatusCode.Conflict);

    /// <summary>Handles the command.</summary>
    /// <param name="command">The command.</param>
    /// <returns>A value.</returns>
    public static string Handle(SaveRefusedGetterClaimCommand command)
    {
        return "claimed";
    }
}

/// <summary>A command that names no error for a refused save.</summary>
[AllowAnonymous]
[RootLink("/save-refused/unnamed-claims")]
[HttpPost]
public partial class SaveRefusedUnnamedClaimCommand : ICommand<string>
{
    /// <summary>Handles the command.</summary>
    /// <param name="command">The command.</param>
    /// <returns>A value.</returns>
    public static string Handle(SaveRefusedUnnamedClaimCommand command)
    {
        return "claimed";
    }
}
