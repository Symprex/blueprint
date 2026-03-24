namespace Legacy.Blueprint;

/// <summary>
/// Minimal legacy Blueprint builder stub used by the codemod dry-run fixture.
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
/// Minimal authentication builder stub used to trigger the configured AddAuthentication migration diagnostic.
/// </summary>
public sealed class BlueprintAuthenticationBuilder
{
}

/// <summary>
/// Minimal extension surface that mirrors the legacy activation APIs used by the dry-run fixture.
/// </summary>
public static class BlueprintApiBuilderExtensions
{
    /// <summary>
    /// Simulates the legacy parameterless AddOpenApi activation seam.
    /// </summary>
    /// <param name="pipelineBuilder">The fake Blueprint builder.</param>
    /// <returns>The same fake builder.</returns>
    public static BlueprintApiBuilder AddOpenApi(this BlueprintApiBuilder pipelineBuilder)
        => pipelineBuilder;

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

    /// <summary>
    /// Simulates the legacy configured AddAuthentication activation seam.
    /// </summary>
    /// <param name="apiBuilder">The fake Blueprint builder.</param>
    /// <param name="configure">The legacy authentication configuration callback.</param>
    /// <returns>The same fake builder.</returns>
    public static BlueprintApiBuilder AddAuthentication(
        this BlueprintApiBuilder apiBuilder,
        Action<BlueprintAuthenticationBuilder> configure)
        => apiBuilder;
}

/// <summary>
/// Entry point for the minimal fixture solution used by the codemod dry-run tests.
/// </summary>
public static class Program
{
    /// <summary>
    /// Exercises the legacy activation patterns that the codemod dry run should collect.
    /// </summary>
    public static void Main()
    {
        var builder = new BlueprintApiBuilder();
        builder.AddOpenApi();
        builder.AddOpenApi(options => options.Title = "Legacy");
        builder.AddAuthentication(_ => { });
    }
}
