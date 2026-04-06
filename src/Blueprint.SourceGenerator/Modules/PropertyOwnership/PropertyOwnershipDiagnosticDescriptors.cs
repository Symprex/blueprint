using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Diagnostic descriptors for the property ownership and binding module (BPPO prefix).
/// </summary>
internal static class PropertyOwnershipDiagnosticDescriptors
{
    /// <summary>
    /// BPPO001: A route-bound property has a type that cannot be converted from a string route value.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedRoutePropertyType = new(
        id: "BPPO001",
        title: "Unsupported route property type",
        messageFormat: "Property '{0}' on operation '{1}' is bound from route placeholder '{{{0}}}' but its type '{2}' cannot be converted from a string route value",
        category: "PropertyOwnership",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPPO002: An operation using GET or DELETE has properties that would be body-owned by convention,
    /// but GET/DELETE operations should not have a request body.
    /// </summary>
    public static readonly DiagnosticDescriptor BodyPropertiesOnGetOrDelete = new(
        id: "BPPO002",
        title: "Body-owned properties on GET/DELETE operation",
        messageFormat: "Operation '{0}' uses {1} but has properties that would be body-owned by convention: {2}. Consider using [FromQuery] or changing the HTTP method.",
        category: "PropertyOwnership",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
