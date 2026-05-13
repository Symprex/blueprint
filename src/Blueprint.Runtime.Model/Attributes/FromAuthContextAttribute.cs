namespace Blueprint.Model.Attributes;

/// <summary>
/// Marks a property as populated by the authentication/authorisation pipeline rather than from
/// HTTP request data (body, query, header). When applied, the property will not appear in OpenAPI
/// parameters or request body schemas, and no HTTP binding code will be generated for it.
/// <para>
/// This is the correct attribute for properties such as <c>TenantId</c> that are resolved from
/// the authenticated user's claims rather than sent by the client. If the property name also
/// appears as a route placeholder (e.g. <c>/tenants/{TenantId}/resources</c>), route binding
/// takes precedence and the property is bound from the URL as normal.
/// </para>
/// <para>
/// When <see cref="ClaimType"/> is specified, the source generator emits inline claim-reading
/// code directly in the pipeline (after authentication), eliminating the need for a runtime
/// <c>IPropertyPopulator</c>. When <see cref="ClaimType"/> is omitted (the default), the
/// property must still be populated by a runtime populator or custom middleware.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class FromAuthContextAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the claim type to read from the authenticated user's <see cref="System.Security.Claims.ClaimsPrincipal"/>.
    /// When set, the generator emits <c>context.UserPrincipal?.FindFirst("claimType")?.Value</c>
    /// inline in the pipeline, bypassing any runtime <c>IPropertyPopulator</c> for this property.
    /// <para>
    /// Only <c>string</c> properties are currently supported for claim mapping. A diagnostic
    /// is emitted if a non-string property specifies <see cref="ClaimType"/>.
    /// </para>
    /// </summary>
    public string? ClaimType { get; set; }
}
