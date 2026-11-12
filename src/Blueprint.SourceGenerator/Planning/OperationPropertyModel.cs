using Blueprint.SourceGenerator.Helpers;

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Where an operation property comes from.
/// </summary>
public enum OperationPropertyOrigin
{
    /// <summary>
    /// Declared in source on the operation or one of its base types.
    /// </summary>
    Declared,

    /// <summary>
    /// Contributed by a generator, from the inner operation named by <c>[Expose&lt;TInner&gt;]</c>, and emitted into the
    /// operation's partial by the descriptor generator.
    /// </summary>
    Contributed,
}

/// <summary>
/// The properties an operation has once generation is complete: the ones declared in source plus the ones a generator
/// contributes (D25). Every generator builds it from its own compilation with <see cref="OperationPropertyModelBuilder"/>,
/// so each sees the same properties although none can see another's, or its own, generated source.
/// </summary>
/// <remarks>
/// It holds no <c>ISymbol</c> or <c>Location</c>, so it can cross an incremental pipeline step and decide whether a step's
/// output is cached.
/// </remarks>
/// <param name="OperationTypeName">The fully qualified metadata name of the operation.</param>
/// <param name="Exposed">The <c>[Expose&lt;TInner&gt;]</c> inner operation, if any.</param>
/// <param name="Properties">The operation's public instance properties, declared first, then contributed.</param>
/// <param name="Bindings">
/// The inner properties D11 binds or hides, which are therefore not among <paramref name="Properties"/>: the generated
/// forward fills them, from a claim, the gateway's route or properties, or a fixed value, or leaves them unset.
/// </param>
public sealed record OperationPropertyModel(
    string OperationTypeName,
    ExposedOperationInfo? Exposed,
    EquatableList<OperationPropertyInfo> Properties,
    EquatableList<Modules.Gateway.GatewayPropertyBinding> Bindings)
{
    /// <summary>
    /// Gets a value indicating whether the operation has a partial to emit: contributed properties, or documentation or a
    /// description it inherits from the inner operation.
    /// </summary>
    public bool HasPartialToEmit => this.HasContributedProperties
        || this.Exposed is { InheritsDocumentation: true } or { InheritedDescription: not null };

    /// <summary>
    /// Gets a value indicating whether any property is contributed and so has to be emitted into the partial.
    /// </summary>
    public bool HasContributedProperties
    {
        get
        {
            foreach (var property in this.Properties)
            {
                if (property.Origin == OperationPropertyOrigin.Contributed)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>
/// The inner operation an operation exposes with <c>[Expose&lt;TInner&gt;]</c>.
/// </summary>
/// <param name="MetadataName">The inner operation's fully qualified metadata name.</param>
/// <param name="TypeName">The inner operation as C# source, <c>global::</c>-qualified.</param>
/// <param name="DefaultsCreation">
/// The C# expression that creates an inner operation to read the defaults its initialisers give, which metadata does not
/// carry, or <see langword="null"/> when the exposing assembly cannot create one.
/// </param>
/// <param name="InheritsDocumentation">
/// Whether the exposing operation has no documentation comment of its own, so its partial inherits the inner operation's
/// (D15): the documentation file then carries an <c>&lt;inheritdoc cref&gt;</c> the runtime resolves against the inner
/// contract's file, as Roslyn gives no documentation for a project reference's metadata.
/// </param>
/// <param name="InheritedDescription">
/// The inner operation's <c>[Description]</c>, which the partial carries when the exposing operation declares none of its
/// own, or <see langword="null"/>.
/// </param>
/// <param name="InnerDependencies">
/// What the exposing operation's descriptor reads from the inner operation beyond its properties, its errors (D14) and its
/// docs (D15), so the descriptor's incremental key changes when they do: the checksum of each file declaring an inner
/// operation in this compilation, or, for one in metadata, its assembly's <c>[BlueprintApiExceptionStatus]</c> entries for
/// it and its documentation. Empty when not built.
/// </param>
public sealed record ExposedOperationInfo(
    string MetadataName,
    string TypeName,
    string? DefaultsCreation,
    bool InheritsDocumentation = false,
    string? InheritedDescription = null,
    EquatableList<string>? InnerDependencies = null);

/// <summary>
/// One property of an <see cref="OperationPropertyModel"/>.
/// </summary>
/// <param name="Name">The property name.</param>
/// <param name="TypeName">The property type, fully qualified with its nullable annotation, as C# source.</param>
/// <param name="Origin">Whether the property is declared or contributed.</param>
/// <param name="OriginTypeName">
/// The fully qualified metadata name of the type that declares the property's original member: the operation or one of
/// its bases for a declared property, the inner operation or one of its bases for a contributed one.
/// </param>
/// <param name="HasPublicSetter">Whether the property has a public setter or initialiser.</param>
/// <param name="HasPublicGetter">Whether the property has a public getter, which code outside its type can read.</param>
/// <param name="IsInitOnly">Whether the setter is <c>init</c>.</param>
/// <param name="IsRequired">Whether the property has the <c>required</c> modifier.</param>
/// <param name="Attributes">The property's attributes as C# attribute source, for example <c>global::X.RangeAttribute(1, 10)</c>.</param>
/// <param name="DocumentationCommentXml">The original member's XML documentation, or <see langword="null"/> when there is none.</param>
/// <param name="Location">
/// Where to report a diagnostic about the property: the declaration for a declared one, the <c>[Expose&lt;TInner&gt;]</c>
/// attribute for a contributed one.
/// </param>
public sealed record OperationPropertyInfo(
    string Name,
    string TypeName,
    OperationPropertyOrigin Origin,
    string OriginTypeName,
    bool HasPublicSetter,
    bool HasPublicGetter,
    bool IsInitOnly,
    bool IsRequired,
    EquatableList<string> Attributes,
    string? DocumentationCommentXml,
    LocationInfo? Location);
