using System.Diagnostics;

namespace Blueprint.Model;

/// <summary>
/// Declares the app-owned serializer context that future OpenAPI coverage-validation and schema slices should treat as
/// the canonical JSON metadata owner for a Blueprint app.
/// </summary>
[Conditional("CODE_ANALYSIS")]
[AttributeUsage(AttributeTargets.Class)]
public sealed class BlueprintAppSerializerContextAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the serializer context type that should cover the app's generated operation contracts.
    /// </summary>
    public Type? JsonSerializerContextType { get; set; }
}
