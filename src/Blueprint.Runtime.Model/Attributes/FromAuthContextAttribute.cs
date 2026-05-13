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
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class FromAuthContextAttribute : Attribute;
