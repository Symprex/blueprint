namespace Legacy.Blueprint;

/// <summary>
/// Minimal legacy Blueprint builder stub used by the codemod dry-run broken fixture.
/// </summary>
public sealed class BlueprintApiBuilder
{
}

/// <summary>
/// Minimal OpenAPI options stub used to trigger the configured AddOpenApi migration diagnostic.
/// </summary>
public sealed class OpenApiOptions
{
    /// <summary>
    /// Gets or sets the legacy OpenAPI title.
    /// </summary>
    public string? Title { get; set; }
}

/// <summary>
/// Minimal extension surface that mirrors the legacy configured OpenAPI activation API.
/// </summary>
public static class BlueprintApiBuilderExtensions
{
    /// <summary>
    /// Simulates the legacy configured AddOpenApi activation seam.
    /// </summary>
    /// <param name="pipelineBuilder">The fake Blueprint builder.</param>
    /// <param name="configure">The legacy configuration callback.</param>
    /// <returns>The same fake builder.</returns>
    public static BlueprintApiBuilder AddOpenApi(
        this BlueprintApiBuilder pipelineBuilder,
        Action<OpenApiOptions> configure)
        => pipelineBuilder;
}

/// <summary>
/// Entry point for the broken fixture solution used to prove the dry-run runner rejects unrelated build failures.
/// </summary>
public static class Program
{
    /// <summary>
    /// Exercises one activation diagnostic and one unrelated compiler error.
    /// </summary>
    public static void Main()
    {
        var builder = new BlueprintApiBuilder();
        builder.AddOpenApi(options => options.Title = "Legacy");
        _ = missingSymbol;
    }
}
