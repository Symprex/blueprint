using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Gateway;

/// <summary>
/// Declared mode (D10): an operation marked <c>[ForwardsTo&lt;TInner&gt;]</c> writes out its own public properties, and
/// each one is paired with the inner property it forwards to, by <c>[ForwardAs]</c> or by name. The pair decides what the
/// forward copies, and which validation rules and docs the declared property takes from the inner one.
/// </summary>
/// <remarks>
/// A <c>[ForwardAs]</c> claims its inner property: a declared property that only shares that property's name is then the
/// gateway's own, for the operation's map hook, and is neither forwarded nor type-checked against it (D16).
/// </remarks>
internal static class DeclaredForward
{
    /// <summary>
    /// The metadata name of the declared-mode attribute, <c>Blueprint.Model.Attributes.ForwardsToAttribute&lt;TInner&gt;</c>.
    /// </summary>
    public const string ForwardsToAttributeMetadataName = "Blueprint.Model.Attributes.ForwardsToAttribute`1";

    /// <summary>
    /// The metadata name of <c>Blueprint.Model.Attributes.ForwardAsAttribute</c>.
    /// </summary>
    public const string ForwardAsAttributeMetadataName = "Blueprint.Model.Attributes.ForwardAsAttribute";

    /// <summary>
    /// Finds the inner operation an operation forwards to with <c>[ForwardsTo&lt;TInner&gt;]</c>.
    /// </summary>
    /// <param name="operationType">The operation.</param>
    /// <param name="attribute">The <c>[ForwardsTo&lt;TInner&gt;]</c> attribute, when there is one.</param>
    /// <returns>The inner operation, or <see langword="null"/> when the operation is not in declared mode.</returns>
    public static INamedTypeSymbol? FindForwardedType(INamedTypeSymbol operationType, out AttributeData? attribute)
    {
        foreach (var candidate in operationType.GetAttributes())
        {
            if (candidate.AttributeClass is { IsGenericType: true, TypeArguments: [INamedTypeSymbol { TypeKind: not TypeKind.Error } inner] } attributeClass
                && attributeClass.ConstructedFrom.ToFullMetadataName() == ForwardsToAttributeMetadataName)
            {
                attribute = candidate;
                return inner;
            }
        }

        attribute = null;
        return null;
    }

    /// <summary>
    /// Finds a property's <c>[ForwardAs]</c>.
    /// </summary>
    /// <param name="property">The gateway operation's property.</param>
    /// <param name="innerName">The inner property name it gives, when there is one.</param>
    /// <returns>The attribute, or <see langword="null"/> when the property has none.</returns>
    public static AttributeData? FindForwardAs(IPropertySymbol property, out string? innerName)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.ToFullMetadataName() == ForwardAsAttributeMetadataName)
            {
                innerName = attribute.ConstructorArguments is [{ Value: string name }] ? name : null;
                return attribute;
            }
        }

        innerName = null;
        return null;
    }

    /// <summary>
    /// Gets the inner property names the operation's <c>[ForwardAs]</c> attributes claim: those on the properties the
    /// forward reads, its public instance ones, each by its most derived declaration, as the property model lists them.
    /// </summary>
    /// <param name="operationType">The gateway operation.</param>
    /// <returns>The claimed names.</returns>
    public static HashSet<string> GetRenameTargets(INamedTypeSymbol operationType)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in operationType.GetAllMembers())
        {
            if (member is IPropertySymbol { IsStatic: false, IsIndexer: false, DeclaredAccessibility: Accessibility.Public } property
                && seen.Add(property.Name)
                && FindForwardAs(property, out var innerName) is not null
                && innerName is not null)
            {
                targets.Add(innerName);
            }
        }

        return targets;
    }

    /// <summary>
    /// Gets the name of the inner property a declared property is paired with: its <c>[ForwardAs]</c> name, or its own
    /// unless a <c>[ForwardAs]</c> of the operation claims that name.
    /// </summary>
    /// <param name="property">The gateway operation's property.</param>
    /// <param name="renameTargets">The operation's claimed names, from <see cref="GetRenameTargets"/>.</param>
    /// <returns>The inner property name, or <see langword="null"/> when the property is the gateway's own.</returns>
    public static string? GetPairedName(IPropertySymbol property, HashSet<string> renameTargets)
    {
        if (FindForwardAs(property, out var innerName) is not null)
        {
            return innerName;
        }

        return renameTargets.Contains(property.Name) ? null : property.Name;
    }

    /// <summary>
    /// Gets the inner operation's public settable instance properties, by name: each name's most derived public instance
    /// declaration, which an object initialiser naming it sets, where that declaration has a public setter.
    /// </summary>
    /// <param name="innerType">The inner operation.</param>
    /// <returns>The properties a forward can set.</returns>
    public static Dictionary<string, IPropertySymbol> GetSettableProperties(INamedTypeSymbol innerType)
    {
        // A getter-only declaration hiding a settable base one leaves the name unsettable: C# resolves the derived one.
        var properties = new Dictionary<string, IPropertySymbol>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in innerType.GetAllMembers().OfType<IPropertySymbol>())
        {
            if (member is { IsStatic: false, IsIndexer: false, DeclaredAccessibility: Accessibility.Public }
                && seen.Add(member.Name)
                && member.SetMethod?.DeclaredAccessibility == Accessibility.Public)
            {
                properties.Add(member.Name, member);
            }
        }

        return properties;
    }

    /// <summary>
    /// Finds the inner property a declared property of an operation in declared mode is paired with.
    /// </summary>
    /// <param name="operationType">The gateway operation.</param>
    /// <param name="innerType">The inner operation it forwards to.</param>
    /// <param name="property">The gateway operation's property.</param>
    /// <returns>The inner property, or <see langword="null"/> when the property has no counterpart.</returns>
    public static IPropertySymbol? FindCounterpart(INamedTypeSymbol operationType, INamedTypeSymbol innerType, IPropertySymbol property)
    {
        // A declaration the operation hides is not forwarded: only the most derived one of a name is, whose [ForwardAs]
        // alone counts, so a hidden one pairs with nothing, even where a site lists it next to the one that hides it.
        return IsMostDerivedDeclaration(operationType, property)
            && GetPairedName(property, GetRenameTargets(operationType)) is { } name
            && GetSettableProperties(innerType).TryGetValue(name, out var counterpart)
                ? counterpart
                : null;
    }

    /// <summary>
    /// Whether a property is the declaration of its name that code naming it on the operation reads: the first public
    /// instance one, most derived type first, as the property model lists it.
    /// </summary>
    private static bool IsMostDerivedDeclaration(INamedTypeSymbol operationType, IPropertySymbol property)
    {
        foreach (var member in operationType.GetAllMembers())
        {
            if (member is IPropertySymbol { IsStatic: false, IsIndexer: false, DeclaredAccessibility: Accessibility.Public } candidate
                && candidate.Name == property.Name)
            {
                return SymbolEqualityComparer.Default.Equals(candidate, property);
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a declared property has its counterpart's type, ignoring nullable annotations, so the forward can copy it.
    /// </summary>
    /// <param name="property">The gateway operation's property.</param>
    /// <param name="counterpart">The inner property it is paired with.</param>
    /// <returns><see langword="true"/> when the types match.</returns>
    public static bool HasCounterpartType(IPropertySymbol property, IPropertySymbol counterpart)
    {
        return SymbolEqualityComparer.Default.Equals(property.Type, counterpart.Type);
    }

    /// <summary>
    /// Gets the validation rules a declared property of an operation in declared mode takes from its counterpart: all of
    /// the counterpart's, when the property declares none of its own and has the counterpart's type (D10).
    /// </summary>
    /// <remarks>
    /// A rule that names another property, such as <c>[Compare]</c>, is left to the inner operation, which validates the
    /// forwarded instance: the gateway may name that property otherwise, or not have it. So is one the gateway's assembly
    /// cannot access, which generated validation could not construct.
    /// </remarks>
    /// <param name="operationType">The gateway operation.</param>
    /// <param name="innerType">The inner operation, or <see langword="null"/> when the operation is not in declared mode.</param>
    /// <param name="property">The gateway operation's property.</param>
    /// <returns>The inherited rules, empty when there are none.</returns>
    public static IReadOnlyList<AttributeData> GetInheritedRules(INamedTypeSymbol operationType, INamedTypeSymbol? innerType, IPropertySymbol property)
    {
        if (innerType is null
            || ValidationSymbolHelpers.HasValidationAttribute(property)
            || FindCounterpart(operationType, innerType, property) is not { } counterpart
            || !HasCounterpartType(property, counterpart))
        {
            return [];
        }

        List<AttributeData>? rules = null;
        foreach (var attribute in GetEffectiveAttributes(counterpart))
        {
            if (attribute.AttributeClass is { } attributeClass
                && ValidationSymbolHelpers.IsValidationAttribute(attributeClass)
                && !OperationPropertyModelBuilder.NamesAnotherProperty(attributeClass)
                && OperationPropertyModelBuilder.CanCarry(attribute, operationType.ContainingAssembly))
            {
                (rules ??= []).Add(attribute);
            }
        }

        return rules ?? (IReadOnlyList<AttributeData>)[];
    }

    /// <summary>
    /// Gets the attributes the runtime reads on an inner property: its own, then those of each declaration it overrides
    /// that the attribute's <c>[AttributeUsage]</c> lets it inherit, as <c>TypeDescriptor</c>, and so DataAnnotations
    /// validation, collects them. A single-use attribute the property already has replaces the overridden one's.
    /// </summary>
    /// <param name="property">The inner operation's property.</param>
    /// <returns>The property's effective attributes.</returns>
    public static IReadOnlyList<AttributeData> GetEffectiveAttributes(IPropertySymbol property)
    {
        var attributes = property.GetAttributes();
        if (property.OverriddenProperty is null)
        {
            return attributes;
        }

        var result = new List<AttributeData>(attributes);
        for (var overridden = property.OverriddenProperty; overridden is not null; overridden = overridden.OverriddenProperty)
        {
            foreach (var attribute in overridden.GetAttributes())
            {
                if (attribute.AttributeClass is { } attributeClass
                    && GetUsage(attributeClass) is (Inherited: true, var allowMultiple)
                    && (allowMultiple || !result.Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeClass))))
                {
                    result.Add(attribute);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Reads an attribute class's <c>[AttributeUsage]</c>, its own or the nearest base class's, with the runtime's defaults.
    /// </summary>
    private static (bool Inherited, bool AllowMultiple) GetUsage(INamedTypeSymbol attributeClass)
    {
        for (var current = attributeClass; current is not null; current = current.BaseType)
        {
            foreach (var usage in current.GetAttributes())
            {
                if (usage.AttributeClass?.ToFullMetadataName() != "System.AttributeUsageAttribute")
                {
                    continue;
                }

                var inherited = true;
                var allowMultiple = false;
                foreach (var argument in usage.NamedArguments)
                {
                    if (argument is { Key: "Inherited", Value.Value: bool isInherited })
                    {
                        inherited = isInherited;
                    }
                    else if (argument is { Key: "AllowMultiple", Value.Value: bool isMultiple })
                    {
                        allowMultiple = isMultiple;
                    }
                }

                return (inherited, allowMultiple);
            }
        }

        return (true, false);
    }

    /// <summary>
    /// Gets the XML documentation a declared property of an operation in declared mode takes from its counterpart: the
    /// counterpart's, when the property has none of its own (D10). A comment that is only <c>&lt;inheritdoc/&gt;</c>, which
    /// satisfies documentation analysers, counts as none.
    /// </summary>
    /// <param name="operationType">The gateway operation.</param>
    /// <param name="innerType">The inner operation, or <see langword="null"/> when the operation is not in declared mode.</param>
    /// <param name="property">The gateway operation's property.</param>
    /// <returns>The inherited documentation, or <see langword="null"/> when the property keeps its own or there is none.</returns>
    public static string? GetInheritedDocumentation(INamedTypeSymbol operationType, INamedTypeSymbol? innerType, IPropertySymbol property)
    {
        if (innerType is null
            || HasOwnDocumentation(property.GetDocumentationCommentXml())
            || FindCounterpart(operationType, innerType, property) is not { } counterpart)
        {
            return null;
        }

        var documentation = counterpart.GetDocumentationCommentXml();
        return string.IsNullOrWhiteSpace(documentation) ? null : documentation;
    }

    /// <summary>
    /// Whether a documentation comment says anything of its own: it is not empty, and not just <c>&lt;inheritdoc/&gt;</c>.
    /// </summary>
    private static bool HasOwnDocumentation(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return false;
        }

        try
        {
            var root = System.Xml.Linq.XElement.Parse(xml);
            return root.Elements().Any(static e => e.Name.LocalName != "inheritdoc")
                || root.Nodes().OfType<System.Xml.Linq.XText>().Any(static t => !string.IsNullOrWhiteSpace(t.Value));
        }
        catch (System.Xml.XmlException)
        {
            return true;
        }
    }
}
