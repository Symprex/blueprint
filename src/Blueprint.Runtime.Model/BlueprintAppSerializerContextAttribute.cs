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

    /// <summary>
    /// Gets or sets a value indicating whether a declared response root may hold, behind an open-typed member
    /// (<see cref="object" />, an interface, an abstract or non-sealed base, <c>JsonElement</c> and the like), a runtime
    /// type the serializer context does not register.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off by default: a declared root is written by the context alone, so a runtime type it does not register fails the
    /// response, and the generator reports an open-typed member it can reach from a declared root (BP012) so the gap is
    /// found at build time instead. Turning it on clears BP012 for the whole app and, when a response is written, adds
    /// reflection metadata behind the context for any type the context does not cover, logging a warning the first time
    /// each such type is written so the gap stays visible.
    /// </para>
    /// <para>
    /// The output has the same shape, since the same options, naming policy and converters apply. What it costs: the
    /// first sighting of each type builds reflection metadata and later writes of it are slower; the app can no longer be
    /// trimmed or AOT-compiled safely; and reflection writes every public property of whatever object reaches an open
    /// member, a domain entity or an exception included, which the strict context would have refused. Prefer a member
    /// <c>[JsonConverter]</c> or <see cref="BlueprintRuntimeTypesAttribute" /> where the runtime types are known.
    /// </para>
    /// </remarks>
    public bool AllowRuntimeTypeFallback { get; set; }
}
