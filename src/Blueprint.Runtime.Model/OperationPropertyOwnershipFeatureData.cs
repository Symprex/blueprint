using System.Collections.Immutable;

namespace Blueprint.Model;

/// <summary>
/// Enumerates the binding sources that can own a property during HTTP operation population.
/// </summary>
public enum PropertyBindingSource
{
    /// <summary>Property is populated from a route placeholder.</summary>
    Route,

    /// <summary>Property is populated from the JSON request body.</summary>
    Body,

    /// <summary>Property is populated from the query string.</summary>
    Query,

    /// <summary>Property is populated from an HTTP header.</summary>
    Header,

    /// <summary>Property is populated from an HTTP cookie.</summary>
    Cookie,

    /// <summary>Property is populated by the auth pipeline.</summary>
    AuthContext,

    /// <summary>Property is populated by the runtime (e.g. CancellationToken).</summary>
    System,
}

/// <summary>
/// Describes a single property's binding ownership as determined at compile time.
/// </summary>
/// <param name="PropertyName">The CLR property name on the operation type.</param>
/// <param name="Source">The binding source that owns this property.</param>
/// <param name="BindingName">
/// The external name used for binding (e.g. route placeholder name, camelCase query name, header name).
/// Null when the CLR name is used directly.
/// </param>
/// <param name="PropertyTypeName">
/// The fully-qualified CLR type name for this property (e.g. <c>string</c>, <c>int</c>, <c>System.Guid</c>).
/// Used by OpenAPI rendering to project parameter and request body schemas without runtime reflection.
/// Null for System/AuthContext entries where type projection is not applicable.
/// </param>
/// <param name="IsNullable">
/// Whether the property accepts null values (either a nullable reference type or <c>Nullable&lt;T&gt;</c>).
/// Used by OpenAPI rendering to emit the <c>nullable: true</c> annotation.
/// </param>
public sealed record OperationPropertyBindingEntry(
    string PropertyName,
    PropertyBindingSource Source,
    string? BindingName,
    string? PropertyTypeName,
    bool IsNullable = false);

/// <summary>
/// Feature data populated during descriptor generation with the compile-time property ownership plan
/// for an HTTP-exposed operation.
/// </summary>
/// <param name="Entries">The ordered property binding entries.</param>
public sealed record OperationPropertyOwnershipFeatureData(
    ImmutableArray<OperationPropertyBindingEntry> Entries);
