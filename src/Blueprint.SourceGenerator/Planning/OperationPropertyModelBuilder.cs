using System.Text;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Builds an operation's <see cref="OperationPropertyModel"/>, and renders the contributed properties into its partial.
/// </summary>
/// <remarks>
/// Spike for Symprex/blueprint#75 (D25). A consumer that today enumerates <c>GetAllMembers().OfType&lt;IPropertySymbol&gt;()</c>
/// enumerates <see cref="GetProperties"/> instead. Each entry pairs the equatable model with the property's original
/// member, so a consumer that analyses the type (TryParse, converters, nullability) keeps doing so against a symbol from
/// its own compilation: a contributed property's original member is the inner operation's, which every generator's
/// compilation can see.
/// </remarks>
internal static class OperationPropertyModelBuilder
{
    /// <summary>
    /// The metadata name of the minimal-mode attribute, <c>Blueprint.Model.ExposeAttribute&lt;TInner&gt;</c>.
    /// </summary>
    public const string ExposeAttributeMetadataName = "Blueprint.Model.ExposeAttribute`1";

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

        return new OperationPropertyModel(
            operationType.ToFullMetadataName(),
            FindExposedType(operationType, out _)?.ToFullMetadataName(),
            new EquatableList<OperationPropertyInfo>(infos));
    }

    /// <summary>
    /// Gets an operation's public instance properties, declared then contributed, each with its original member. A
    /// declared property hides an inherited one of the same name, and a contributed property is left out when the
    /// operation declares one of that name.
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

        var exposedType = FindExposedType(operationType, out var exposeAttribute);
        if (exposedType is null)
        {
            return result;
        }

        var exposeLocation = exposeAttribute?.ApplicationSyntaxReference is { } reference
            ? LocationInfo.CreateFrom(reference.GetSyntax())
            : null;

        foreach (var member in exposedType.GetAllMembers().OfType<IPropertySymbol>())
        {
            // D9: each public settable inner property is contributed. D11's bound and hidden properties would be left out
            // here, so that every consumer and the emitted partial agree on them.
            if (!IsPublicInstanceProperty(member) || member.SetMethod is not { DeclaredAccessibility: Accessibility.Public })
            {
                continue;
            }

            if (!seenNames.Add(member.Name))
            {
                continue;
            }

            result.Add(new ResolvedOperationProperty(CreateInfo(member, OperationPropertyOrigin.Contributed, exposeLocation), member));
        }

        return result;
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
            if (candidate.AttributeClass is { IsGenericType: true, TypeArguments: [INamedTypeSymbol inner] } attributeClass
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
    /// Renders the contributed properties of an operation into its partial.
    /// </summary>
    /// <param name="model">The operation's property model.</param>
    /// <returns>The C# source of the partial.</returns>
    public static string RenderContributedProperties(OperationPropertyModel model)
    {
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

        var first = true;
        foreach (var property in model.Properties)
        {
            if (property.Origin != OperationPropertyOrigin.Contributed)
            {
                continue;
            }

            if (!first)
            {
                source.AppendLine();
            }

            first = false;

            source.Append("    /// <inheritdoc cref=\"").Append(model.ExposedTypeName).Append('.').Append(property.Name).AppendLine("\"/>");
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

            // An initialiser is compiled into the inner type's constructor, so it cannot be copied from metadata. A
            // non-nullable reference is left for the binder to fill, as the inner operation leaves it for its own.
            if (!property.IsRequired && property.IsReferenceType && !property.TypeName.EndsWith("?", StringComparison.Ordinal))
            {
                source.Append(" = default!;");
            }

            source.AppendLine();
        }

        source.AppendLine("}");
        return source.ToString();
    }

    private static bool IsPublicInstanceProperty(IPropertySymbol member)
    {
        return !member.IsStatic && !member.IsIndexer && member.DeclaredAccessibility == Accessibility.Public;
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
            member.Type.IsReferenceType,
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

            // Copying [Obsolete] would make every generated read of the property a CS0618 in the exposing assembly.
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

    private static string RenderConstant(TypedConstant constant)
    {
        if (constant.IsNull)
        {
            return "null";
        }

        return constant.Kind switch
        {
            TypedConstantKind.Type => "typeof(" + ((ITypeSymbol)constant.Value!).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")",
            TypedConstantKind.Enum => "(" + constant.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")(" + Convert.ToString(constant.Value, System.Globalization.CultureInfo.InvariantCulture) + ")",
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
