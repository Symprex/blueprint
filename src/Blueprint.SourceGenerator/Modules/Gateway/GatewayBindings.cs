using System.Text.RegularExpressions;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Gateway;

/// <summary>
/// Where a bound inner property gets its value (D11).
/// </summary>
public enum GatewayBindingSource
{
    /// <summary>
    /// The caller's claim, <c>[GatewayBind(FromClaim)]</c> or <c>[Bind(FromClaim)]</c>.
    /// </summary>
    Claim,

    /// <summary>
    /// The gateway operation's property a route placeholder binds, <c>[Bind(FromRoute)]</c>.
    /// </summary>
    Route,

    /// <summary>
    /// A property the gateway operation declares, <c>[Bind(FromProperty)]</c>.
    /// </summary>
    Property,

    /// <summary>
    /// A fixed value, <c>[Bind(Value)]</c>.
    /// </summary>
    Value,

    /// <summary>
    /// Nothing: the property is left unset, <c>[Hide]</c>.
    /// </summary>
    Hidden,
}

/// <summary>
/// How a claim's string value becomes the inner property's type, by the rules route binding uses.
/// </summary>
public enum GatewayClaimConversion
{
    /// <summary>
    /// Not a claim binding.
    /// </summary>
    None,

    /// <summary>
    /// The property is a string, so the claim's value is assigned as it is.
    /// </summary>
    String,

    /// <summary>
    /// A scalar, an enum, an <c>IParsable&lt;T&gt;</c> or a type with a static <c>TryParse</c>.
    /// </summary>
    TryParse,

    /// <summary>
    /// A type that converts from a string only through its <c>[TypeConverter]</c>.
    /// </summary>
    TypeConverter,
}

/// <summary>
/// One inner property a gateway operation binds or hides, after D11's tiers are applied: an operation's
/// <c>[Bind]</c> or <c>[Hide]</c> overrides the assembly's <c>[GatewayBind]</c> of the same property.
/// </summary>
/// <param name="PropertyName">The inner operation's property.</param>
/// <param name="Source">Where the value comes from.</param>
/// <param name="Argument">
/// The claim type for <see cref="GatewayBindingSource.Claim"/>, the gateway operation's property for
/// <see cref="GatewayBindingSource.Route"/> and <see cref="GatewayBindingSource.Property"/>, the value as C# source for
/// <see cref="GatewayBindingSource.Value"/>, and <see langword="null"/> for <see cref="GatewayBindingSource.Hidden"/>.
/// </param>
/// <param name="IsAppTier">Whether the binding is the assembly's <c>[GatewayBind]</c> rather than the operation's own.</param>
/// <param name="TargetTypeName">The inner property's type without <c>Nullable&lt;T&gt;</c>, <c>global::</c>-qualified.</param>
/// <param name="ClaimConversion">How a claim's value converts to <see cref="TargetTypeName"/>.</param>
/// <param name="TryParseFormat">
/// For <see cref="GatewayClaimConversion.TryParse"/>, the parse expression with <c>__value__</c> for the claim's value and
/// <c>__parsed__</c> for the parsed variable.
/// </param>
public sealed record GatewayPropertyBinding(
    string PropertyName,
    GatewayBindingSource Source,
    string? Argument,
    bool IsAppTier,
    string TargetTypeName,
    GatewayClaimConversion ClaimConversion,
    string? TryParseFormat);

/// <summary>
/// D11's binding tiers: reads an operation's <c>[Bind]</c> and <c>[Hide]</c>, and its assembly's <c>[GatewayBind]</c>, into
/// the bindings the property model leaves out of the public shape (D25) and the generated forward fills (D12).
/// </summary>
/// <remarks>
/// <see cref="Resolve"/> returns only bindings that are valid, so a generator never emits code for one that
/// <see cref="Analyse"/> reports; an invalid binding leaves its property contributed, as if it were not there. Each reads
/// only the operation's declared members, never the property model, which is built from them.
/// </remarks>
internal static class GatewayBindings
{
    /// <summary>
    /// The token in <see cref="GatewayPropertyBinding.TryParseFormat"/> that stands for the claim's value.
    /// </summary>
    public const string ValueToken = "__value__";

    /// <summary>
    /// The token in <see cref="GatewayPropertyBinding.TryParseFormat"/> that stands for the parsed variable.
    /// </summary>
    public const string ParsedToken = "__parsed__";

    private const string GatewayBindAttributeMetadataName = "Blueprint.Model.Attributes.GatewayBindAttribute";
    private const string BindAttributeMetadataName = "Blueprint.Model.Attributes.BindAttribute";
    private const string HideAttributeMetadataName = "Blueprint.Model.Attributes.HideAttribute";

    private static readonly Regex Placeholder = RoutePlaceholderSyntax.Placeholder;

    /// <summary>
    /// Gets the valid bindings of an operation that exposes <paramref name="innerType"/>, the operation's own first.
    /// </summary>
    /// <param name="operationType">The gateway operation.</param>
    /// <param name="innerType">The inner operation it forwards to.</param>
    /// <returns>The bindings, one per bound or hidden inner property.</returns>
    public static EquatableList<GatewayPropertyBinding> Resolve(INamedTypeSymbol operationType, INamedTypeSymbol innerType)
    {
        return Evaluate(operationType, innerType, diagnostics: null);
    }

    /// <summary>
    /// Reports what is wrong with an operation's bindings, BPGW007 to BPGW013, and its map hook, BPGW014.
    /// </summary>
    /// <param name="operationType">The gateway operation.</param>
    /// <param name="innerType">The inner operation it forwards to.</param>
    /// <param name="exposeLocation">The <c>[Expose&lt;TInner&gt;]</c> attribute, where class-level problems are reported.</param>
    /// <returns>The diagnostics, empty when there are none.</returns>
    public static List<DiagnosticInfo> Analyse(INamedTypeSymbol operationType, INamedTypeSymbol innerType, LocationInfo? exposeLocation)
    {
        var diagnostics = new List<DiagnosticInfo>();
        var bindings = Evaluate(operationType, innerType, diagnostics);

        ReportUnboundInnerRoutePlaceholders(operationType, innerType, bindings, exposeLocation, diagnostics);
        GatewayMapHook.Find(operationType, innerType, diagnostics);

        return diagnostics;
    }

    private static EquatableList<GatewayPropertyBinding> Evaluate(INamedTypeSymbol operationType, INamedTypeSymbol innerType, List<DiagnosticInfo>? diagnostics)
    {
        var innerProperties = GetSettableProperties(innerType);
        var declared = GetDeclaredProperties(operationType);
        var result = new List<GatewayPropertyBinding>();
        var bound = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attribute in operationType.GetAttributes())
        {
            var metadataName = attribute.AttributeClass?.ToFullMetadataName();
            var isHide = metadataName == HideAttributeMetadataName;
            if (!isHide && metadataName != BindAttributeMetadataName)
            {
                continue;
            }

            var location = GetLocation(attribute, operationType);
            if (attribute.ConstructorArguments is not [{ Value: string propertyName }])
            {
                continue;
            }

            if (!innerProperties.TryGetValue(propertyName, out var innerProperty))
            {
                diagnostics?.Add(new DiagnosticInfo(
                    GatewayDiagnosticDescriptors.BoundPropertyNotOnInnerOperation,
                    location,
                    [operationType.Name, isHide ? "Hide" : "Bind", propertyName, innerType.Name]));
                continue;
            }

            if (!bound.Add(propertyName))
            {
                diagnostics?.Add(new DiagnosticInfo(GatewayDiagnosticDescriptors.PropertyBoundTwice, location, [operationType.Name, propertyName]));
                continue;
            }

            if (isHide)
            {
                if (IsRequired(innerProperty))
                {
                    diagnostics?.Add(new DiagnosticInfo(
                        GatewayDiagnosticDescriptors.HiddenPropertyIsRequired,
                        location,
                        [operationType.Name, propertyName, innerType.Name]));
                    continue;
                }

                AddUnlessDeclared(Create(innerProperty, GatewayBindingSource.Hidden, null, isAppTier: false), operationType, declared, location, result, diagnostics);
                continue;
            }

            var binding = CreateOperationBinding(attribute, operationType, innerProperty, location, diagnostics);
            if (binding is not null)
            {
                AddUnlessDeclared(binding, operationType, declared, location, result, diagnostics);
            }
        }

        foreach (var attribute in operationType.ContainingAssembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToFullMetadataName() != GatewayBindAttributeMetadataName
                || attribute.ConstructorArguments is not [{ Value: string propertyName }])
            {
                continue;
            }

            var claim = GetNamedString(attribute, "FromClaim");
            if (string.IsNullOrEmpty(claim))
            {
                diagnostics?.Add(new DiagnosticInfo(
                    GatewayDiagnosticDescriptors.BindingNeedsOneSource,
                    GetLocation(attribute, operationType),
                    ["GatewayBind", propertyName, "FromClaim"]));
                continue;
            }

            // The operation tier overrides the app tier, and an app binding applies only where the inner property exists.
            if (!innerProperties.TryGetValue(propertyName, out var innerProperty) || !bound.Add(propertyName))
            {
                continue;
            }

            var conversion = GetClaimConversion(innerProperty.Type, out var tryParseFormat);
            if (conversion == GatewayClaimConversion.None)
            {
                diagnostics?.Add(new DiagnosticInfo(
                    GatewayDiagnosticDescriptors.BindingDoesNotConvert,
                    GetLocation(attribute, operationType),
                    [operationType.Name, propertyName, "claim '" + claim + "'", innerProperty.Type.ToDisplayString()]));
                continue;
            }

            var appBinding = Create(innerProperty, GatewayBindingSource.Claim, claim, isAppTier: true) with
            {
                ClaimConversion = conversion,
                TryParseFormat = tryParseFormat,
            };
            AddUnlessDeclared(appBinding, operationType, declared, GetLocation(attribute, operationType), result, diagnostics);
        }

        return EquatableList<GatewayPropertyBinding>.TakeOwnership(result);
    }

    /// <summary>
    /// Adds a binding unless the gateway operation declares the property it fills and the binding does not read that
    /// declaration: the declared property would stay in the public shape, bound from the request and validated, but the
    /// forward would ignore it (BPGW021). A binding that reads it, <c>[Bind(X, FromProperty = "X")]</c> or a route
    /// placeholder that binds it, is the gateway forwarding its own property.
    /// </summary>
    private static void AddUnlessDeclared(
        GatewayPropertyBinding binding,
        INamedTypeSymbol operationType,
        Dictionary<string, IPropertySymbol> declared,
        LocationInfo? location,
        List<GatewayPropertyBinding> result,
        List<DiagnosticInfo>? diagnostics)
    {
        var readsDeclaration = binding.Source is GatewayBindingSource.Route or GatewayBindingSource.Property && binding.Argument == binding.PropertyName;
        if (declared.ContainsKey(binding.PropertyName) && !readsDeclaration)
        {
            diagnostics?.Add(new DiagnosticInfo(
                GatewayDiagnosticDescriptors.BoundPropertyDeclaredByGateway,
                location,
                [operationType.Name, binding.PropertyName]));
            return;
        }

        result.Add(binding);
    }

    private static GatewayPropertyBinding? CreateOperationBinding(
        AttributeData attribute,
        INamedTypeSymbol operationType,
        IPropertySymbol innerProperty,
        LocationInfo? location,
        List<DiagnosticInfo>? diagnostics)
    {
        var claim = GetNamedString(attribute, "FromClaim");
        var route = GetNamedString(attribute, "FromRoute");
        var property = GetNamedString(attribute, "FromProperty");
        var hasValue = attribute.NamedArguments.Any(static a => a.Key == "Value");

        var sources = (claim is null ? 0 : 1) + (route is null ? 0 : 1) + (property is null ? 0 : 1) + (hasValue ? 1 : 0);
        if (sources != 1)
        {
            diagnostics?.Add(new DiagnosticInfo(
                GatewayDiagnosticDescriptors.BindingNeedsOneSource,
                location,
                ["Bind", innerProperty.Name, "FromClaim, FromRoute, FromProperty or Value"]));
            return null;
        }

        if (claim is not null)
        {
            var conversion = GetClaimConversion(innerProperty.Type, out var tryParseFormat);
            if (conversion == GatewayClaimConversion.None)
            {
                diagnostics?.Add(new DiagnosticInfo(
                    GatewayDiagnosticDescriptors.BindingDoesNotConvert,
                    location,
                    [operationType.Name, innerProperty.Name, "claim '" + claim + "'", innerProperty.Type.ToDisplayString()]));
                return null;
            }

            return Create(innerProperty, GatewayBindingSource.Claim, claim, isAppTier: false) with
            {
                ClaimConversion = conversion,
                TryParseFormat = tryParseFormat,
            };
        }

        if (hasValue)
        {
            var value = attribute.NamedArguments.First(static a => a.Key == "Value").Value;
            if (!ValueConverts(value, innerProperty.Type))
            {
                diagnostics?.Add(new DiagnosticInfo(
                    GatewayDiagnosticDescriptors.BindingDoesNotConvert,
                    location,
                    [operationType.Name, innerProperty.Name, "value " + OperationPropertyModelBuilder.RenderConstant(value), innerProperty.Type.ToDisplayString()]));
                return null;
            }

            // A bare null: the attribute's typed null, (object?)null, would not convert to the inner property's type.
            return Create(innerProperty, GatewayBindingSource.Value, value.IsNull ? "null" : OperationPropertyModelBuilder.RenderConstant(value), isAppTier: false);
        }

        var declared = GetDeclaredProperties(operationType);
        var sourceName = route is not null ? ResolveRoutePlaceholder(operationType, declared, route) : property;

        // The pipeline reads the source from outside the operation, so it needs a public getter, as a copied property does.
        // A route's source has to be the property the route fills: request binding sets it, so it has a public setter, and
        // no other source attribute moves it off the route, or the forward would send a query or body value instead.
        if (sourceName is null
            || !declared.TryGetValue(sourceName, out var sourceProperty)
            || sourceProperty.GetMethod is not { DeclaredAccessibility: Accessibility.Public }
            || (route is not null && !IsBoundFromRoute(sourceProperty)))
        {
            diagnostics?.Add(new DiagnosticInfo(
                GatewayDiagnosticDescriptors.BindingSourceNotOnOperation,
                location,
                [operationType.Name, innerProperty.Name, route is not null ? "route placeholder '{" + route + "}'" : "property '" + property + "'"]));
            return null;
        }

        if (!IsSameType(sourceProperty.Type, innerProperty.Type))
        {
            diagnostics?.Add(new DiagnosticInfo(
                GatewayDiagnosticDescriptors.BindingDoesNotConvert,
                location,
                [operationType.Name, innerProperty.Name, "property '" + sourceProperty.Name + "' of type " + sourceProperty.Type.ToDisplayString(), innerProperty.Type.ToDisplayString()]));
            return null;
        }

        return Create(innerProperty, route is not null ? GatewayBindingSource.Route : GatewayBindingSource.Property, sourceProperty.Name, isAppTier: false);
    }

    /// <summary>
    /// D9's edge case: an inner route placeholder the gateway's routes do not carry is either copied, bound or hidden, or the
    /// inner operation's own request builder would have no value for it.
    /// </summary>
    private static void ReportUnboundInnerRoutePlaceholders(
        INamedTypeSymbol operationType,
        INamedTypeSymbol innerType,
        EquatableList<GatewayPropertyBinding> bindings,
        LocationInfo? exposeLocation,
        List<DiagnosticInfo> diagnostics)
    {
        var innerTemplates = GetRouteTemplates(innerType);
        if (innerTemplates.Count == 0)
        {
            return;
        }

        // The inner request builder always sends the first URL, in declaration order as the link module registers it, so a
        // later route without a placeholder is no way round it.
        var innerMembers = innerType.GetAllMembers().ToList();
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Placeholder.Matches(innerTemplates[0]))
        {
            // An optional or defaulted placeholder is left out of the URL when the forward has no value for it.
            if (!RoutePlaceholderSyntax.IsOptional(match)
                && RoutePlaceholderSyntax.ResolveMemberName(innerMembers, match.Groups[1].Value) is { } name)
            {
                required.Add(name);
            }
        }

        var gatewayPlaceholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in GetRouteTemplates(operationType))
        {
            foreach (Match match in Placeholder.Matches(template))
            {
                gatewayPlaceholders.Add(match.Groups[1].Value);
            }
        }

        // A copied property gives the inner request builder its value wherever the gateway reads it from, the query string
        // included (#161). A declared one of another type is the gateway's own, so it does not.
        var copied = GatewayForward.GetCopiedProperties(operationType, innerType, bindings, out _);

        foreach (var name in required)
        {
            if (gatewayPlaceholders.Contains(name) || copied.Contains(name) || bindings.Any(b => b.PropertyName == name))
            {
                continue;
            }

            diagnostics.Add(new DiagnosticInfo(
                GatewayDiagnosticDescriptors.UnboundInnerRoutePlaceholder,
                exposeLocation,
                [operationType.Name, name, innerType.Name, innerTemplates[0]]));
        }
    }

    private static GatewayPropertyBinding Create(IPropertySymbol innerProperty, GatewayBindingSource source, string? argument, bool isAppTier)
    {
        return new GatewayPropertyBinding(
            innerProperty.Name,
            source,
            argument,
            isAppTier,
            Unwrap(innerProperty.Type).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            GatewayClaimConversion.None,
            null);
    }

    private static GatewayClaimConversion GetClaimConversion(ITypeSymbol type, out string? tryParseFormat)
    {
        tryParseFormat = null;
        var effective = Unwrap(type);

        if (effective.SpecialType == SpecialType.System_String)
        {
            return GatewayClaimConversion.String;
        }

        // The shared helper writes a direct call: the scalar's own TryParse, or T.TryParse(value, provider, out T), which a
        // type implementing IParsable<T> explicitly does not have. That one is parsed through the interface instead.
        if (effective.TypeKind == TypeKind.Enum
            || IsScalarWithOwnTryParse(effective)
            || (TypeConversionHelpers.ImplementsIParsable(effective) && HasTryParse(effective, withProvider: true)))
        {
            tryParseFormat = TypeConversionHelpers.GetTryParseExpression(effective, ValueToken, ParsedToken);
            return GatewayClaimConversion.TryParse;
        }

        if (TypeConversionHelpers.ImplementsIParsable(effective))
        {
            tryParseFormat = "global::Blueprint.Runtime.Exec.PipelineHelpers.TryParseInvariant<"
                + effective.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">(" + ValueToken + ", out var " + ParsedToken + ")";
            return GatewayClaimConversion.TryParse;
        }

        // A conventional static TryParse: the shared helper calls TryParse(string, out T), which a type whose TryParse takes
        // a format provider lacks, so the call is chosen from the overloads the type has.
        if (TypeConversionHelpers.HasStaticTryParseMethod(effective))
        {
            var typeName = effective.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (HasTryParse(effective, withProvider: false))
            {
                tryParseFormat = typeName + ".TryParse(" + ValueToken + ", out var " + ParsedToken + ")";
                return GatewayClaimConversion.TryParse;
            }

            if (HasTryParse(effective, withProvider: true))
            {
                tryParseFormat = typeName + ".TryParse(" + ValueToken + ", global::System.Globalization.CultureInfo.InvariantCulture, out var " + ParsedToken + ")";
                return GatewayClaimConversion.TryParse;
            }
        }

        return TypeConversionHelpers.HasTypeConverterAttribute(effective) ? GatewayClaimConversion.TypeConverter : GatewayClaimConversion.None;
    }

    private static bool ValueConverts(TypedConstant value, ITypeSymbol target)
    {
        if (value.IsNull)
        {
            return !target.IsValueType || target.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        }

        return value.Type is not null && SymbolEqualityComparer.Default.Equals(value.Type, Unwrap(target));
    }

    /// <summary>
    /// Whether the shared helper parses the type with the type's own public TryParse, written out for it.
    /// </summary>
    private static bool IsScalarWithOwnTryParse(ITypeSymbol type)
    {
        return type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString()
            is "int" or "long" or "double" or "float" or "bool" or "decimal" or "System.Guid" or "System.DateTime" or "System.DateTimeOffset";
    }

    private static bool HasTryParse(ITypeSymbol type, bool withProvider)
    {
        // Public only: the generated pipeline calls it from outside the type, and a private one would not compile (CS0122).
        return type.GetMembers("TryParse").OfType<IMethodSymbol>().Any(m =>
            m is { IsStatic: true, DeclaredAccessibility: Accessibility.Public, ReturnType.SpecialType: SpecialType.System_Boolean }
            && m.Parameters.Length == (withProvider ? 3 : 2)
            && m.Parameters[0].Type.SpecialType == SpecialType.System_String
            && (!withProvider || m.Parameters[1].Type.ToDisplayString().TrimEnd('?') == "System.IFormatProvider")
            && m.Parameters[^1] is { RefKind: RefKind.Out } output
            && SymbolEqualityComparer.Default.Equals(output.Type, type));
    }

    private static bool IsBoundFromRoute(IPropertySymbol property)
    {
        return property.SetMethod is { DeclaredAccessibility: Accessibility.Public }
            && !property.GetAttributes().Any(static a => a.AttributeClass is { Name: "FromQueryAttribute" or "FromBodyAttribute" or "FromHeaderAttribute" or "FromCookieAttribute" or "FromAuthContextAttribute" } attributeClass
                && attributeClass.IsInNamespace("Blueprint", "Model", "Attributes"));
    }

    /// <summary>
    /// Whether the inner operation requires the property: the <c>required</c> modifier, or <c>[Required]</c>, which its
    /// validation applies to a value the gateway would leave unset.
    /// </summary>
    private static bool IsRequired(IPropertySymbol property)
    {
        return property.IsRequired
            || property.GetAttributes().Any(static a => a.AttributeClass?.InheritsFrom("System.ComponentModel.DataAnnotations.RequiredAttribute") == true
                || a.AttributeClass?.ToDisplayString() == "System.ComponentModel.DataAnnotations.RequiredAttribute");
    }

    private static bool IsSameType(ITypeSymbol source, ITypeSymbol target)
    {
        return SymbolEqualityComparer.Default.Equals(source, target)
            || (target.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && SymbolEqualityComparer.Default.Equals(source, Unwrap(target)));
    }

    private static ITypeSymbol Unwrap(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [var underlying] }
            ? underlying
            : type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
    }

    /// <summary>
    /// Resolves a <c>FromRoute</c> placeholder to the property it binds, only when every one of the gateway's routes carries
    /// it: on a route without it a POST fills the property from its body, and the forward would send that value instead.
    /// </summary>
    private static string? ResolveRoutePlaceholder(INamedTypeSymbol operationType, Dictionary<string, IPropertySymbol> declared, string placeholder)
    {
        string? resolved = null;
        foreach (var template in GetRouteTemplates(operationType))
        {
            var found = false;
            foreach (Match match in Placeholder.Matches(template))
            {
                if (string.Equals(match.Groups[1].Value, placeholder, StringComparison.OrdinalIgnoreCase))
                {
                    resolved ??= RoutePlaceholderSyntax.ResolveMemberName(declared.Values, match.Groups[1].Value);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return null;
            }
        }

        return resolved;
    }

    private static Dictionary<string, IPropertySymbol> GetSettableProperties(INamedTypeSymbol type)
    {
        var properties = new Dictionary<string, IPropertySymbol>(StringComparer.Ordinal);
        foreach (var member in type.GetAllMembers().OfType<IPropertySymbol>())
        {
            if (member is { IsStatic: false, IsIndexer: false, DeclaredAccessibility: Accessibility.Public, SetMethod.DeclaredAccessibility: Accessibility.Public }
                && !properties.ContainsKey(member.Name))
            {
                properties.Add(member.Name, member);
            }
        }

        return properties;
    }

    private static Dictionary<string, IPropertySymbol> GetDeclaredProperties(INamedTypeSymbol type)
    {
        var properties = new Dictionary<string, IPropertySymbol>(StringComparer.Ordinal);
        foreach (var member in type.GetAllMembers().OfType<IPropertySymbol>())
        {
            if (member is { IsStatic: false, IsIndexer: false, DeclaredAccessibility: Accessibility.Public } && !properties.ContainsKey(member.Name))
            {
                properties.Add(member.Name, member);
            }
        }

        return properties;
    }

    private static List<string> GetRouteTemplates(INamedTypeSymbol type)
    {
        var templates = new List<string>();
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass || !attributeClass.IsInNamespace("Blueprint", "Model", "Attributes"))
            {
                continue;
            }

            var template = attributeClass.Name switch
            {
                "RootLinkAttribute" when attribute.ConstructorArguments.Length >= 1 => attribute.ConstructorArguments[0].Value as string,
                "SelfLinkAttribute" or "LinkAttribute" when attribute.ConstructorArguments.Length >= 2 => attribute.ConstructorArguments[1].Value as string,
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(template))
            {
                templates.Add(template!);
            }
        }

        return templates;
    }

    private static string? GetNamedString(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name)
            {
                return argument.Value.Value as string;
            }
        }

        return null;
    }

    private static LocationInfo? GetLocation(AttributeData attribute, INamedTypeSymbol operationType)
    {
        return attribute.ApplicationSyntaxReference is { } reference
            ? LocationInfo.CreateFrom(reference.GetSyntax())
            : operationType.Locations.FirstOrDefault() is { } location ? LocationInfo.CreateFrom(location) : null;
    }
}
