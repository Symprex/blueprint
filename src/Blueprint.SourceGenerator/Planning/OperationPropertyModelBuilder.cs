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
    /// The name of the thread-static slot holding the inner instance the emitted partial's initialisers read their defaults from.
    /// </summary>
    public const string DefaultsFieldName = "__blueprintExposedDefaults";

    /// <summary>
    /// The name of the emitted method that makes the inner instance for the first initialiser and gives it to the rest.
    /// </summary>
    public const string ReadDefaultsMethodName = "__blueprintReadExposedDefaults";

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
                CreateInfo(member, OperationPropertyOrigin.Declared, location is null ? null : LocationInfo.CreateFrom(location), operationType.ContainingAssembly),
                member));
        }

        HashSet<string>? bound = null;
        foreach (var member in GetContributedMembers(operationType, out var exposeAttribute))
        {
            if (!seenNames.Add(member.Name))
            {
                continue;
            }

            var exposeLocation = exposeAttribute?.ApplicationSyntaxReference is { } reference
                ? LocationInfo.CreateFrom(reference.GetSyntax())
                : null;

            bound ??= GetBoundNames(operationType);
            result.Add(new ResolvedOperationProperty(CreateInfo(member, OperationPropertyOrigin.Contributed, exposeLocation, operationType.ContainingAssembly, bound), member));
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
        // Every site reads members through here, so an operation that exposes nothing (most of them) stops at its own,
        // rather than building a property model it would discard, and an exposed one adds the contributed members alone.
        var declaredNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in operationType.GetAllMembers())
        {
            if (member is IPropertySymbol property && IsPublicInstanceProperty(property))
            {
                declaredNames.Add(property.Name);
            }

            yield return member;
        }

        foreach (var contributed in GetContributedMembers(operationType, out _))
        {
            // As GetProperties: a declared property wins over a contributed one, and the first contributed one of a name wins.
            if (declaredNames.Add(contributed.Name))
            {
                yield return contributed;
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
    /// Gets the names of the inner properties an operation binds or hides (D11).
    /// </summary>
    /// <param name="operationType">The operation.</param>
    /// <returns>The names, empty when the operation exposes nothing or binds nothing.</returns>
    public static HashSet<string> GetBoundNames(INamedTypeSymbol operationType)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in GetBindings(operationType))
        {
            names.Add(binding.PropertyName);
        }

        return names;
    }

    /// <summary>
    /// Whether a contributed property's validation rule names a property the operation binds or hides. The gateway has no
    /// such property to compare against, so the rule is neither emitted nor planned there: the inner operation applies it
    /// to the forwarded instance, with the bound value in place.
    /// </summary>
    /// <param name="attribute">The rule.</param>
    /// <param name="boundNames">The operation's bound and hidden property names, from <see cref="GetBoundNames"/>.</param>
    /// <returns><see langword="true"/> when the rule is left to the inner operation.</returns>
    public static bool IsRuleLeftToInner(AttributeData attribute, HashSet<string> boundNames)
    {
        return boundNames.Count > 0
            && attribute.AttributeClass is { } attributeClass
            && NamesAnotherProperty(attributeClass)
            && Modules.ValidationSymbolHelpers.GetReferencedPropertyName(attribute) is { } name
            && boundNames.Contains(name);
    }

    /// <summary>
    /// Whether a rule's first argument names another property, as the validation module's missing-property checks read it,
    /// rather than being a literal such as <c>[RegularExpression]</c>'s pattern that may spell a property's name.
    /// </summary>
    private static bool NamesAnotherProperty(INamedTypeSymbol attributeClass)
    {
        return Modules.ValidationSymbolHelpers.IsCompareAttribute(attributeClass)
            || Modules.ValidationSymbolHelpers.IsGreaterThanPropertyAttribute(attributeClass)
            || Modules.ValidationSymbolHelpers.IsLessThanPropertyAttribute(attributeClass)
            || Modules.ValidationSymbolHelpers.IsMustBeGreaterThanOrEqualAttribute(attributeClass)
            || Modules.ValidationSymbolHelpers.IsRequiredIfAttribute(attributeClass)
            || Modules.ValidationSymbolHelpers.IsRequiredIfNotAttribute(attributeClass)
            || Modules.ValidationSymbolHelpers.IsNotAllowedIfAttribute(attributeClass)
            || Modules.ValidationSymbolHelpers.IsRequiredItemsIfAttribute(attributeClass)
            || Modules.ValidationSymbolHelpers.IsRequiredItemsIfNotAttribute(attributeClass);
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
    /// default, read from a fresh inner instance for each exposed one because initialisers are not in metadata (D9).
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

        var readsDefaults = exposed.DefaultsCreation is not null
            && model.Properties.Any(static p => p is { Origin: OperationPropertyOrigin.Contributed, HasPublicGetter: true });
        if (readsDefaults)
        {
            // One fresh inner operation for each exposed one, so a default made per construction (a new Guid, the time, a new
            // list) is made per construction here too. Initialisers cannot read an instance field, so the instance is kept in
            // a thread-static slot: the first initialiser below makes it, the rest read it, and the initialisers of one part
            // run in order, one construction at a time on a thread.
            source.AppendLine("    [global::System.ThreadStatic]");
            source.Append("    private static ").Append(exposed.TypeName).Append("? ").Append(DefaultsFieldName).AppendLine(";");
            source.AppendLine();
            source.Append("    private static ").Append(exposed.TypeName).Append(' ').Append(ReadDefaultsMethodName).AppendLine("(bool first)");
            source.AppendLine("    {");
            source.Append("        return first || ").Append(DefaultsFieldName).Append(" is null ? ").Append(DefaultsFieldName).Append(" = ")
                .Append(exposed.DefaultsCreation).Append(" : ").Append(DefaultsFieldName).AppendLine(";");
            source.AppendLine("    }");
        }

        var first = true;

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

            // An inner property the gateway cannot read (a private getter, or none) gives no default to start from.
            if (readsDefaults && property.HasPublicGetter)
            {
                source.Append(" = ").Append(ReadDefaultsMethodName).Append(first ? "(true)." : "(false).").Append(property.Name).Append(';');
                first = false;
            }
            else if (!property.IsRequired && !property.TypeName.EndsWith("?", StringComparison.Ordinal))
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

    /// <summary>
    /// Finds the constructor <c>new T()</c> or <c>new T { … }</c> calls from another assembly: the public parameterless
    /// one, or else a public one whose every parameter is optional.
    /// </summary>
    /// <param name="type">The type to create.</param>
    /// <returns>The constructor, or <see langword="null"/> when the type cannot be created without arguments.</returns>
    public static IMethodSymbol? FindArgumentlessConstructor(INamedTypeSymbol type)
    {
        if (type.IsAbstract)
        {
            return null;
        }

        IMethodSymbol? allOptional = null;
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            if (constructor.Parameters.Length == 0)
            {
                return constructor;
            }

            if (constructor.Parameters.All(static p => p.IsOptional || p.IsParams))
            {
                allOptional ??= constructor;
            }
        }

        return allOptional;
    }

    /// <summary>
    /// Whether a constructor is marked <c>[SetsRequiredMembers]</c>, so an object creation through it need set none.
    /// </summary>
    /// <param name="constructor">The constructor.</param>
    /// <returns><see langword="true"/> when it sets every required member.</returns>
    public static bool SetsRequiredMembers(IMethodSymbol constructor)
    {
        return constructor.GetAttributes().Any(static a => a.AttributeClass?.ToFullMetadataName() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute");
    }

    /// <summary>
    /// Whether a type has a <c>required</c> member, which an object creation must set unless its constructor sets them.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns><see langword="true"/> when it or a base type declares one.</returns>
    public static bool HasRequiredMembers(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers().Any(static m => m is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
            {
                return true;
            }
        }

        return false;
    }

    private static ExposedOperationInfo CreateExposedInfo(INamedTypeSymbol exposed)
    {
        var typeName = exposed.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Made as the inner pipeline makes it where an object creation would have to set required members: Activator, which
        // needs a parameterless constructor, runs every initialiser and sets no member. Otherwise new T(), through any
        // constructor callable without arguments.
        string? creation = null;
        if (FindArgumentlessConstructor(exposed) is { } constructor)
        {
            if (!HasRequiredMembers(exposed) || SetsRequiredMembers(constructor))
            {
                creation = "new " + typeName + "()";
            }
            else if (constructor.Parameters.Length == 0)
            {
                creation = "(" + typeName + ")global::System.Activator.CreateInstance(typeof(" + typeName + "))!";
            }
        }

        return new ExposedOperationInfo(exposed.ToFullMetadataName(), typeName, creation);
    }

    private static bool IsPublicInstanceProperty(IPropertySymbol member)
    {
        return !member.IsStatic && !member.IsIndexer && member.DeclaredAccessibility == Accessibility.Public;
    }

    private static bool IsObsolete(ISymbol member)
    {
        return member.GetAttributes().Any(static a => a.AttributeClass?.ToFullMetadataName() == "System.ObsoleteAttribute");
    }

    /// <summary>
    /// Gets the attributes of a contributed property that the exposing operation's partial cannot carry because its assembly
    /// cannot access a type the attribute names, its own or one in its arguments: the ones whose effect would be lost.
    /// </summary>
    /// <param name="member">The inner operation's property.</param>
    /// <param name="within">The exposing operation's assembly.</param>
    /// <returns>The classes of the attributes that cannot be carried.</returns>
    public static IEnumerable<INamedTypeSymbol> GetUncarriedAttributes(IPropertySymbol member, IAssemblySymbol within)
    {
        foreach (var attribute in member.GetAttributes())
        {
            if (attribute.AttributeClass is { TypeKind: not TypeKind.Error } attributeClass
                && !IsNeverCarried(attributeClass)
                && !CanCarry(attribute, attributeClass, within))
            {
                yield return attributeClass;
            }
        }
    }

    /// <summary>
    /// Attributes no emitted property carries: the compiler's own, and <c>[Obsolete]</c>, whose properties are not contributed.
    /// </summary>
    private static bool IsNeverCarried(INamedTypeSymbol attributeClass)
    {
        return attributeClass.IsInNamespace("System", "Runtime", "CompilerServices")
            || attributeClass.ToFullMetadataName() == "System.ObsoleteAttribute";
    }

    private static bool CanCarry(AttributeData attribute, INamedTypeSymbol attributeClass, IAssemblySymbol within)
    {
        // No constructor is resolved where the one applied is one metadata does not import for this compilation: an internal
        // one of another assembly.
        return IsAccessibleFrom(attributeClass, within)
            && attribute.AttributeConstructor is { } constructor
            && IsMemberAccessibleFrom(constructor, within)
            && attribute.NamedArguments.All(argument => IsNamedArgumentAccessibleFrom(attributeClass, argument.Key, within))
            && attribute.ConstructorArguments.All(argument => IsAccessibleFrom(argument, within))
            && attribute.NamedArguments.All(argument => IsAccessibleFrom(argument.Value, within));
    }

    /// <summary>
    /// Whether code in <paramref name="within"/> can call or set a member: public, or internal to an assembly that gives
    /// <paramref name="within"/> access to its internals.
    /// </summary>
    private static bool IsMemberAccessibleFrom(ISymbol member, IAssemblySymbol within)
    {
        return member.DeclaredAccessibility switch
        {
            Accessibility.Public => true,
            Accessibility.Internal or Accessibility.ProtectedOrInternal => SymbolEqualityComparer.Default.Equals(member.ContainingAssembly, within)
                || member.ContainingAssembly.GivesAccessTo(within),
            _ => false,
        };
    }

    /// <summary>
    /// Whether code in <paramref name="within"/> can set an attribute's named argument: the property's setter, or the field.
    /// </summary>
    private static bool IsNamedArgumentAccessibleFrom(INamedTypeSymbol attributeClass, string name, IAssemblySymbol within)
    {
        for (var current = attributeClass; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name))
            {
                switch (member)
                {
                    case IPropertySymbol { SetMethod: { } setter }:
                        return IsMemberAccessibleFrom(setter, within);
                    case IFieldSymbol field:
                        return IsMemberAccessibleFrom(field, within);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether every type an attribute argument renders is accessible: a <c>typeof</c>'s type, an enum value's type, and an
    /// array's element type and elements.
    /// </summary>
    private static bool IsAccessibleFrom(TypedConstant constant, IAssemblySymbol within)
    {
        if (constant.Type is { } type && !IsAccessibleFrom(type, within))
        {
            return false;
        }

        return constant.Kind switch
        {
            TypedConstantKind.Type => constant.Value is not ITypeSymbol value || IsAccessibleFrom(value, within),
            TypedConstantKind.Array => constant.IsNull || constant.Values.All(element => IsAccessibleFrom(element, within)),
            _ => true,
        };
    }

    /// <summary>
    /// Whether code in <paramref name="within"/> can name a type: an array's element type, a pointer's, and each type
    /// argument, and a named type and each type containing it public, or internal to an assembly that gives
    /// <paramref name="within"/> access to its internals.
    /// </summary>
    private static bool IsAccessibleFrom(ITypeSymbol type, IAssemblySymbol within)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return IsAccessibleFrom(array.ElementType, within);
            case IPointerTypeSymbol pointer:
                return IsAccessibleFrom(pointer.PointedAtType, within);
            case ITypeParameterSymbol:
                return true;
            case INamedTypeSymbol named when named.TypeArguments.Any(argument => !IsAccessibleFrom(argument, within)):
                return false;
        }

        for (var current = type as INamedTypeSymbol; current is not null; current = current.ContainingType)
        {
            var accessible = current.DeclaredAccessibility switch
            {
                Accessibility.Public => true,
                Accessibility.Internal or Accessibility.ProtectedOrInternal => SymbolEqualityComparer.Default.Equals(current.ContainingAssembly, within)
                    || current.ContainingAssembly.GivesAccessTo(within),
                _ => false,
            };

            if (!accessible)
            {
                return false;
            }
        }

        return true;
    }

    private static OperationPropertyInfo CreateInfo(
        IPropertySymbol member,
        OperationPropertyOrigin origin,
        LocationInfo? location,
        IAssemblySymbol within,
        HashSet<string>? boundNames = null)
    {
        var attributes = new List<string>();
        foreach (var attribute in member.GetAttributes())
        {
            if (boundNames is not null && IsRuleLeftToInner(attribute, boundNames))
            {
                continue;
            }

            if (RenderAttribute(attribute, within) is { } rendered)
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
            member.GetMethod is { DeclaredAccessibility: Accessibility.Public },
            member.SetMethod?.IsInitOnly == true,
            member.IsRequired,
            EquatableList<string>.TakeOwnership(attributes),
            string.IsNullOrWhiteSpace(documentation) ? null : documentation,
            location);
    }

    private static string? RenderAttribute(AttributeData attribute, IAssemblySymbol within)
    {
        if (attribute.AttributeClass is not { TypeKind: not TypeKind.Error } attributeClass
            || IsNeverCarried(attributeClass)
            || !CanCarry(attribute, attributeClass, within))
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
        // Typed, so that the copy selects the constructor overload the original did: a bare null is ambiguous between a
        // string and a Type parameter. A reference type is cast to its nullable form, as (string)null raises CS8600 under
        // #nullable enable; a nullable value type already is one.
        if (constant.IsNull)
        {
            if (constant.Type is not { } nullType)
            {
                return "null";
            }

            var nullTypeName = nullType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return "(" + nullTypeName + (nullType.IsReferenceType ? "?" : string.Empty) + ")null";
        }

        return constant.Kind switch
        {
            TypedConstantKind.Type => "typeof(" + ((ITypeSymbol)constant.Value!).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")",
            TypedConstantKind.Enum => "(" + constant.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")(" + Convert.ToString(constant.Value, CultureInfo.InvariantCulture) + ")",
            TypedConstantKind.Array => "new " + constant.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + " { " + string.Join(", ", constant.Values.Select(RenderConstant)) + " }",

            // A constant passed as object (a params object[], say) is boxed as its literal's type, so a type no literal has
            // is cast and a double gets its suffix: (short)1, not 1, which would box an int.
            _ => constant.Type?.SpecialType switch
            {
                SpecialType.System_Byte => "(byte)",
                SpecialType.System_SByte => "(sbyte)",
                SpecialType.System_Int16 => "(short)",
                SpecialType.System_UInt16 => "(ushort)",
                _ => string.Empty,
            }
                + Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatPrimitive(constant.Value!, quoteStrings: true, useHexadecimalNumbers: false)
                + constant.Type?.SpecialType switch
                {
                    SpecialType.System_Double => "D",
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
