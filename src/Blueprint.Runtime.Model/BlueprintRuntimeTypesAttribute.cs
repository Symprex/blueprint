namespace Blueprint.Model;

/// <summary>
/// Declares the runtime types an open-typed member of a response body (<see cref="object" />, an interface, an abstract
/// or non-sealed base, <c>JsonElement</c>, or a collection or dictionary of these) may hold, so the generator can check
/// that the app's serializer context registers each of them.
/// </summary>
/// <remarks>
/// <para>
/// A declared response root is written by the app's source-generated <c>JsonSerializerContext</c> alone, which knows only
/// the types it registers. A member whose static type does not say what it holds is reported (BP012) because a value of
/// an unregistered type fails the response when it is written. This attribute clears that report for its member and
/// instead requires every listed type to be registered with <c>[JsonSerializable]</c> on the app's context (BP013).
/// </para>
/// <para>
/// List the types the member actually holds - for a <c>Dictionary&lt;string, object&gt;</c> the value types, for an
/// <c>object</c> property the types assigned to it. The other ways to clear BP012 are a <c>[JsonConverter]</c> on the
/// member, which then owns the member's shape, and the app-wide
/// <see cref="BlueprintAppSerializerContextAttribute.AllowRuntimeTypeFallback" />.
/// </para>
/// <para>
/// Not conditional, unlike the app-level attributes: response types often live in a contract assembly, and the generator
/// reads this from that assembly's metadata.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class BlueprintRuntimeTypesAttribute : Attribute
{
    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintRuntimeTypesAttribute" /> class.
    /// </summary>
    /// <param name="types">The runtime types the member may hold, each of which the app's serializer context must register.</param>
    public BlueprintRuntimeTypesAttribute(params Type[] types)
    {
        this.Types = types;
    }

    /// <summary>
    /// Gets the runtime types the member may hold.
    /// </summary>
    public IReadOnlyList<Type> Types { get; }
}
