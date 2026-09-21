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
/// Minimal FromResource attribute stub used to trigger the auth/resource migration diagnostic.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class FromResourceAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FromResourceAttribute"/> class with only an operation type.
    /// This constructor matches the migrated form after stripping the resource key template.
    /// </summary>
    /// <param name="operationType">The referenced legacy operation type.</param>
    public FromResourceAttribute(Type operationType)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FromResourceAttribute"/> class.
    /// </summary>
    /// <param name="operationType">The referenced legacy operation type.</param>
    /// <param name="resourceKeyTemplate">The legacy auth/resource template.</param>
    public FromResourceAttribute(Type operationType, string resourceKeyTemplate)
    {
    }
}

/// <summary>
/// Minimal legacy query type referenced by the auth/resource inheritance fixture.
/// </summary>
public sealed class AccountLookupQuery
{
}

/// <summary>
/// Minimal legacy command type carrying FromResource metadata that must migrate to generated auth/resource metadata.
/// </summary>
[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class AccountCommand
{
}

/// <summary>
/// Minimal legacy Resource attribute stub used to trigger the resource-arity migration diagnostic.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ResourceAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceAttribute"/> class with only a noun and verb.
    /// This constructor matches the migrated form after stripping the resource key template.
    /// </summary>
    /// <param name="noun">The resource noun.</param>
    /// <param name="verb">The action verb.</param>
    public ResourceAttribute(string noun, string verb)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceAttribute"/> class.
    /// </summary>
    /// <param name="noun">The resource noun.</param>
    /// <param name="verb">The action verb.</param>
    /// <param name="resourceKeyTemplate">The legacy resource key template.</param>
    public ResourceAttribute(string noun, string verb, string resourceKeyTemplate)
    {
    }
}

/// <summary>
/// Minimal legacy query type carrying a three-argument Resource attribute whose template references a
/// non-auth property, so it must be flagged for manual review rather than auto-fixed.
/// </summary>
[Resource("Account", "Read", "/Tenants/{TenantId}")]
public sealed class GetAccountQuery
{
}

/// <summary>
/// Minimal legacy command type carrying a three-argument Resource attribute whose template is a pure
/// auth-claim reference, so it must be auto-fixed by dropping the resource key template argument.
/// </summary>
[Resource("Disclaimer", "Update", "/Tenants/{Auth:AccountId}")]
public sealed class UpdateDisclaimerCommand
{
}

/// <summary>
/// Minimal legacy query type carrying a three-argument Resource attribute whose template mixes a safe
/// auth-claim placeholder with a non-auth instance-key placeholder. Every placeholder must be an auth
/// claim before the fix applies, so this must be flagged for manual review rather than auto-fixed, even
/// though the first placeholder alone would pass.
/// </summary>
[Resource("User", "Read", "/Tenants/{Auth:AccountId}/Users/{Id}")]
public sealed class GetUserQuery
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

// Generated bulk of independent BPMIG200 sites. None overlaps another, so a batching
// applier must collapse them into a single compile-and-analyse pass per diagnostic id;
// a per-fix applier pays one pass each. The assertion lives in ApplyModeTests.
[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand1
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand2
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand3
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand4
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand5
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand6
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand7
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand8
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand9
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand10
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand11
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand12
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand13
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand14
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand15
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand16
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand17
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand18
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand19
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand20
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand21
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand22
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand23
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand24
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand25
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand26
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand27
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand28
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand29
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand30
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand31
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand32
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand33
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand34
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand35
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand36
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand37
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand38
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand39
{
}

[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand40
{
}

