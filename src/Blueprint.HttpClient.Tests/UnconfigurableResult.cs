using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// A result type System.Text.Json refuses to configure: two members claim the same JSON name, so asking for its metadata
/// throws.
/// </summary>
public sealed class UnconfigurableResult
{
    /// <summary>
    /// Gets or sets a member named <c>value</c>.
    /// </summary>
    [JsonPropertyName("value")]
    public string? First { get; set; }

    /// <summary>
    /// Gets or sets another member named <c>value</c>.
    /// </summary>
    [JsonPropertyName("value")]
    public string? Second { get; set; }
}

/// <summary>
/// Returns a result System.Text.Json cannot configure. Called through <see cref="FaultyResultApi"/>.
/// </summary>
[RootLink("/faulty")]
[HttpGet]
[AllowAnonymous]
public partial class GetUnconfigurableQuery : IQuery<UnconfigurableResult>
{
}

/// <summary>
/// A typed client with one operation whose result cannot be configured, beside one that can.
/// </summary>
[BlueprintClient]
[BlueprintClientInclude(typeof(GetUnconfigurableQuery))]
[BlueprintClientInclude(typeof(FindTemplateCopiesQuery))]
internal partial class FaultyResultApi;
