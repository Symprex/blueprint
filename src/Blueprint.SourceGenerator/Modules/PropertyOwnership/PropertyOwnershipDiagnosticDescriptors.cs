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

    /// <summary>
    /// BPPO003: A query-bound property has a type that cannot be converted from a string query value.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedQueryPropertyType = new(
        id: "BPPO003",
        title: "Unsupported query property type",
        messageFormat: "Property '{0}' on operation '{1}' would be bound from query string but its type '{2}' cannot be converted from a string value",
        category: "PropertyOwnership",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPPO004: An explicit [FromBody] attribute is applied to a property on a GET or DELETE operation.
    /// GET/DELETE operations should not have request bodies.
    /// </summary>
    public static readonly DiagnosticDescriptor ExplicitBodyOnGetOrDelete = new(
        id: "BPPO004",
        title: "[FromBody] on GET/DELETE operation",
        messageFormat: "Property '{0}' on operation '{1}' has [FromBody] but the operation uses {2}. GET/DELETE operations should not have a request body.",
        category: "PropertyOwnership",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPPO005: An explicit [FromRoute] attribute is applied but there is no matching route placeholder.
    /// </summary>
    public static readonly DiagnosticDescriptor ExplicitRouteWithoutPlaceholder = new(
        id: "BPPO005",
        title: "[FromRoute] without matching placeholder",
        messageFormat: "Property '{0}' on operation '{1}' has [FromRoute] but there is no matching '{{{0}}}' placeholder in the URL template",
        category: "PropertyOwnership",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPPO006: Multiple conflicting [FromX] attributes on the same property.
    /// </summary>
    public static readonly DiagnosticDescriptor ConflictingFromAttributes = new(
        id: "BPPO006",
        title: "Conflicting [FromX] attributes",
        messageFormat: "Property '{0}' on operation '{1}' has multiple conflicting binding attributes: {2}. A property can have at most one [FromX] attribute.",
        category: "PropertyOwnership",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPPO007: A header- or cookie-bound property has a type that cannot be converted from a string value.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedHeaderOrCookiePropertyType = new(
        id: "BPPO007",
        title: "Unsupported header/cookie property type",
        messageFormat: "Property '{0}' on operation '{1}' has [{2}] but its type '{3}' cannot be converted from a string value",
        category: "PropertyOwnership",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPPO008: A route template contains a malformed placeholder (unbalanced braces or empty name).
    /// </summary>
    public static readonly DiagnosticDescriptor MalformedRoutePlaceholder = new(
        id: "BPPO008",
        title: "Malformed route placeholder",
        messageFormat: "Route template '{0}' on operation '{1}' contains a malformed placeholder: {2}",
        category: "PropertyOwnership",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
