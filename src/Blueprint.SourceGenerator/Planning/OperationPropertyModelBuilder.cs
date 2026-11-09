using System.Globalization;
using System.Text;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules.Gateway;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Builds an operation's <see cref="OperationPropertyModel"/> (D25), and renders the contributed properties into its partial.
/// </summary>
/// <remarks>
/// A site that would enumerate <c>GetAllMembers().OfType&lt;IPropertySymbol&gt;()</c> on an operation enumerates
/// <see cref="GetProperties"/> instead. Each entry pairs the equatable model with the property's original member, so a site
/// that analyses the type (TryParse, converters, nullability) keeps doing so against a symbol from its own compilation: a
/// contributed property's original member is the inner operation's, which every generator's compilation can see. That
/// symbol never leaves the step that built it.
/// </remarks>
internal static class OperationPropertyModelBuilder
{
    /// <summary>
    /// The metadata name of the minimal-mode attribute, <c>Blueprint.Model.Attributes.ExposeAttribute&lt;TInner&gt;</c>.
    /// </summary>
    public const string ExposeAttributeMetadataName = "Blueprint.Model.Attributes.ExposeAttribute`1";

    /// <summary>
    /// The name of the static inner instance the emitted partial reads its defaults from.
    /// </summary>
    public const string DefaultsFieldName = "__blueprintExposedDefaults";

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// Builds the equatable property model of an operation.
    /// </summary>
    /// <param name="operationType">The operation.</param>
    /// <returns>The operation's declared and contributed properties.</returns>
    public static OperationPropertyModel Build(INamedTypeSymbol operationType)
    {
        var properties = GetProperties(operationType);
        var infos = new OperationPropertyInfo[properties.Count];
        for (var i = 0; i < properties.Count; i++)
        {
            infos[i] = properties[i].Info;
        }

        var exposed = FindExposedType(operationType, out _);

        return new OperationPropertyModel(
            operationType.ToFullMetadataName(),
            exposed is null ? null : CreateExposedInfo(exposed),
            new EquatableList<OperationPropertyInfo>(infos),
            GetBindings(operationType));
    }

    /// <summary>
    /// Gets an operation's public instance properties, declared then contributed, each with its original member. A declared
    /// property hides an inherited one of the same name, and a contributed property is left out when the operation declares
    /// one of that name.
    /// </summary>
    /// <param name="operationType">The operation.</param>
    /// <returns>The properties, in declaration order, most derived type first.</returns>
    public static IReadOnlyList<ResolvedOperationProperty> GetProperties(INamedTypeSymbol operationType)
    {
        var result = new List<ResolvedOperationProperty>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in operationType.GetAllMembers().OfType<IPropertySymbol>())
        {
            if (!IsPublicInstanceProperty(member) || !seenNames.Add(member.Name))
            {
                continue;
            }

            var location = member.Locations.FirstOrDefault(static l => l.IsInSource);
            result.Add(new ResolvedOperationProperty(
                CreateInfo(member, OperationPropertyOrigin.Declared, location is null ? null : LocationInfo.CreateFrom(location)),
                member));
        }

        foreach (var member in GetContributedMembers(operationType, out var exposeAttribute))
        {
            if (!seenNames.Add(member.Name))
            {
                continue;
            }

            var exposeLocation = exposeAttribute?.ApplicationSyntaxReference is { } reference
                ? LocationInfo.CreateFrom(reference.GetSyntax())
                : null;

            result.Add(new ResolvedOperationProperty(CreateInfo(member, OperationPropertyOrigin.Contributed, exposeLocation), member));
        }

        return result;
    }

    /// <summary>
    /// Gets the members of an operation, as <c>GetAllMembers()</c> does, followed by the original members of the properties
    /// contributed to it, for a site that matches names against every member (route placeholders, link templates).
    /// </summary>
    /// <param name="operationType">The operation.</param>
    /// <returns>The operation's own members, then the contributed properties' original members.</returns>
    public static IEnumerable<ISymbol> GetMembersWithContributed(INamedTypeSymbol operationType)
    {
        foreach (var member in operationType.GetAllMembers())
        {
            yield return member;
        }

        foreach (var property in GetProperties(operationType))
        {
            if (property.Info.Origin == OperationPropertyOrigin.Contributed)
            {
                yield return property.Member;
            }
        }
    }

    /// <summary>
    /// Gets the D11 bindings of an operation marked <c>[Expose&lt;TInner&gt;]</c>: the inner properties it binds or hides,
    /// which are left out of its contributed properties and filled by the generated forward instead.
    /// </summary>
    /// <param name="operationType">The operation.</param>
    /// <returns>The valid bindings, empty when the operation exposes nothing or binds nothing.</returns>
    public static EquatableList<GatewayPropertyBinding> GetBindings(INamedTypeSymbol operationType)
    {
        return FindExposedType(operationType, out _) is { } exposed
            ? GatewayBindings.Resolve(operationType, exposed)
            : EquatableList<GatewayPropertyBinding>.Empty;
    }

    /// <summary>
    /// Finds the inner operation an operation exposes with <c>[Expose&lt;TInner&gt;]</c>.
    /// </summary>
    /// <param name="operationType">The operation.</param>
    /// <param name="attribute">The <c>[Expose&lt;TInner&gt;]</c> attribute, when there is one.</param>
    /// <returns>The inner operation, or <see langword="null"/> when the operation exposes none.</returns>
    public static INamedTypeSymbol? FindExposedType(INamedTypeSymbol operationType, out AttributeData? attribute)
    {
        foreach (var candidate in operationType.GetAttributes())
        {
            if (candidate.AttributeClass is { IsGenericType: true, TypeArguments: [INamedTypeSymbol { TypeKind: not TypeKind.Error } inner] } attributeClass
                && attributeClass.ConstructedFrom.ToFullMetadataName() == ExposeAttributeMetadataName)
            {
                attribute = candidate;
                return inner;
            }
        }

        attribute = null;
        return null;
    }

    /// <summary>
    /// Renders the contributed properties of an operation into its partial, each starting from the inner operation's
    /// default, read from a static inner instance because initialisers are not in metadata (D9).
    /// </summary>
    /// <param name="model">The operation's property model.</param>
    /// <returns>The C# source of the partial.</returns>
    public static string RenderContributedProperties(OperationPropertyModel model)
    {
        var exposed = model.Exposed!;
        var lastDot = model.OperationTypeName.LastIndexOf('.');
        var ns = lastDot < 0 ? null : model.OperationTypeName.Substring(0, lastDot);
        var typeName = model.OperationTypeName.Substring(lastDot + 1);

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();

        if (ns is not null)
        {
            source.Append("namespace ").Append(ns).AppendLine(";");
            source.AppendLine();
        }

        source.Append("partial class ").AppendLine(typeName);
        source.AppendLine("{");

        var readsDefaults = exposed.CanCreateDefaults && model.Properties.Any(static p => p is { Origin: OperationPropertyOrigin.Contributed, IsRequired: false });
        if (readsDefaults)
        {
            source.Append("    private static readonly ").Append(exposed.TypeName).Append(' ').Append(DefaultsFieldName)
                .Append(" = new ").Append(exposed.TypeName);
            if (exposed.RequiredMemberNames.Count > 0)
            {
                source.Append(" { ").Append(string.Join(", ", exposed.RequiredMemberNames.Select(static name => name + " = default!"))).Append(" }");
            }
            else
            {
                source.Append("()");
            }

            source.AppendLine(";");
        }

        foreach (var property in model.Properties)
        {
            if (property.Origin != OperationPropertyOrigin.Contributed)
            {
                continue;
            }

            source.AppendLine();
            source.Append("    /// <inheritdoc cref=\"").Append(exposed.TypeName).Append('.').Append(property.Name).AppendLine("\"/>");
            foreach (var attribute in property.Attributes)
            {
                source.Append("    [").Append(attribute).AppendLine("]");
            }

            source.Append("    public ");
            if (property.IsRequired)
            {
                source.Append("required ");
            }

            source.Append(property.TypeName).Append(' ').Append(property.Name)
                .Append(property.IsInitOnly ? " { get; init; }" : " { get; set; }");

            if (property.IsRequired)
            {
                source.AppendLine();
                continue;
            }

            if (readsDefaults)
            {
                source.Append(" = ").Append(DefaultsFieldName).Append('.').Append(property.Name).Append(';');
            }
            else if (!property.TypeName.EndsWith("?", StringComparison.Ordinal))
            {
                // No instance to read from: left for the binder to fill, as the inner operation leaves it for its own.
                source.Append(" = default!;");
            }

            source.AppendLine();
        }

        source.AppendLine("}");
        return source.ToString();
    }

    private static IEnumerable<IPropertySymbol> GetContributedMembers(INamedTypeSymbol operationType, out AttributeData? exposeAttribute)
    {
        var exposedType = FindExposedType(operationType, out exposeAttribute);
        if (exposedType is null)
        {
            return [];
        }

        // D9: each public settable inner property that is not obsolete is contributed. D11's bound and hidden properties
        // are left out here, so that every site and the emitted partial agree that none is in the public shape.
        var bound = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in GatewayBindings.Resolve(operationType, exposedType))
        {
            bound.Add(binding.PropertyName);
        }

        return exposedType.GetAllMembers()
            .OfType<IPropertySymbol>()
            .Where(member => IsPublicInstanceProperty(member)
                && member.SetMethod is { DeclaredAccessibility: Accessibility.Public }
                && !IsObsolete(member)
                && !bound.Contains(member.Name));
    }

    private static ExposedOperationInfo CreateExposedInfo(INamedTypeSymbol exposed)
    {
        var required = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = exposed; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true } && seen.Add(member.Name))
                {
                    required.Add(member.Name);
                }
            }
        }

        var canCreate = !exposed.IsAbstract
            && exposed.InstanceConstructors.Any(static c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

        return new ExposedOperationInfo(
            exposed.ToFullMetadataName(),
            exposed.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            EquatableList<string>.TakeOwnership(required),
            canCreate);
    }

    private static bool IsPublicInstanceProperty(IPropertySymbol member)
    {
        return !member.IsStatic && !member.IsIndexer && member.DeclaredAccessibility == Accessibility.Public;
    }

    private static bool IsObsolete(ISymbol member)
    {
        return member.GetAttributes().Any(static a => a.AttributeClass?.ToFullMetadataName() == "System.ObsoleteAttribute");
    }

    private static OperationPropertyInfo CreateInfo(IPropertySymbol member, OperationPropertyOrigin origin, LocationInfo? location)
    {
        var attributes = new List<string>();
        foreach (var attribute in member.GetAttributes())
        {
            if (RenderAttribute(attribute) is { } rendered)
            {
                attributes.Add(rendered);
            }
        }

        var documentation = member.GetDocumentationCommentXml();

        return new OperationPropertyInfo(
            member.Name,
            member.Type.ToDisplayString(TypeFormat),
            origin,
            member.ContainingType.ToFullMetadataName(),
            member.SetMethod is { DeclaredAccessibility: Accessibility.Public },
            member.SetMethod?.IsInitOnly == true,
            member.IsRequired,
            EquatableList<string>.TakeOwnership(attributes),
            string.IsNullOrWhiteSpace(documentation) ? null : documentation,
            location);
    }

    private static string? RenderAttribute(AttributeData attribute)
    {
        if (attribute.AttributeClass is not { TypeKind: not TypeKind.Error } attributeClass
            || attributeClass.DeclaredAccessibility != Accessibility.Public
            || attributeClass.IsInNamespace("System", "Runtime", "CompilerServices")
            || attributeClass.ToFullMetadataName() == "System.ObsoleteAttribute")
        {
            return null;
        }

        var arguments = new List<string>();
        foreach (var argument in attribute.ConstructorArguments)
        {
            arguments.Add(RenderConstant(argument));
        }

        foreach (var argument in attribute.NamedArguments)
        {
            arguments.Add(argument.Key + " = " + RenderConstant(argument.Value));
        }

        var name = attributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return arguments.Count == 0 ? name : name + "(" + string.Join(", ", arguments) + ")";
    }

    /// <summary>
    /// Renders an attribute argument as C# source.
    /// </summary>
    /// <param name="constant">The argument.</param>
    /// <returns>The C# expression.</returns>
    internal static string RenderConstant(TypedConstant constant)
    {
        if (constant.IsNull)
        {
            return "null";
        }

        return constant.Kind switch
        {
            TypedConstantKind.Type => "typeof(" + ((ITypeSymbol)constant.Value!).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")",
            TypedConstantKind.Enum => "(" + constant.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")(" + Convert.ToString(constant.Value, CultureInfo.InvariantCulture) + ")",
            TypedConstantKind.Array => "new " + constant.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + " { " + string.Join(", ", constant.Values.Select(RenderConstant)) + " }",
            _ => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatPrimitive(constant.Value!, quoteStrings: true, useHexadecimalNumbers: false)
                + constant.Type?.SpecialType switch
                {
                    SpecialType.System_Single => "F",
                    SpecialType.System_Int64 => "L",
                    SpecialType.System_UInt32 => "U",
                    SpecialType.System_UInt64 => "UL",
                    SpecialType.System_Decimal => "M",
                    _ => string.Empty,
                },
        };
    }

    /// <summary>
    /// One property of an operation, with the member it was read from, for use within the pipeline step that built it.
    /// </summary>
    /// <param name="Info">The property's equatable model.</param>
    /// <param name="Member">
    /// The original member: the operation's own for a declared property, the inner operation's for a contributed one.
    /// Never stored in a pipeline step's output.
    /// </param>
    public readonly record struct ResolvedOperationProperty(OperationPropertyInfo Info, IPropertySymbol Member);
}
