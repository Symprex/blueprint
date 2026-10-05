using System.Diagnostics;

namespace Blueprint.Model;

/// <summary>
/// Declares which <c>TenantId</c> type the operations a Blueprint app hosts have filled from the caller's tenant.
/// </summary>
/// <remarks>
/// <para>
/// An operation's <c>TenantId</c> is bound from the request like any other property. For an authenticated caller whose
/// request left it unset, the generated pipeline fills it from the caller's tenant (<c>Blueprint.Tenancy.ITenantAccessor</c>).
/// With <see cref="TenantIdType"/> set, only a <c>TenantId</c> of that type, or its <see cref="Nullable{T}"/>, is filled;
/// a <c>TenantId</c> of any other type is bound from the request but never filled. Without it, a <c>TenantId</c> that is a
/// <see cref="string"/>, an <c>IParsable&lt;T&gt;</c>, a type with a conventional static <c>TryParse</c>, or a type with a
/// <c>[TypeConverter]</c> is filled.
/// </para>
/// <para>
/// Legacy Blueprint's Signature365 <c>SetTenantMiddleware</c> filled only an <c>ApiId</c> or <c>ApiId?</c>
/// <c>TenantId</c>, so Signature365 sets <c>TenantIdType = typeof(ApiId)</c> on each app root that fills one. Whatever
/// the setting, a <c>TenantId</c> is filled only when it has a public, non-init setter.
/// </para>
/// </remarks>
[Conditional("CODE_ANALYSIS")]
[AttributeUsage(AttributeTargets.Class)]
public sealed class BlueprintAppTenancyAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the only <c>TenantId</c> type (with its <see cref="Nullable{T}"/>) that is filled from the caller's
    /// tenant, or <see langword="null"/> to fill every supported type.
    /// </summary>
    public Type? TenantIdType { get; set; }
}
