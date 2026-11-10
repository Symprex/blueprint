using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Gateway;

/// <summary>
/// The compile-time checks of what a gateway forward applies (D7, D8): an operation's
/// <c>[UpstreamCredential&lt;TStrategy&gt;]</c>, and the <c>[TranslateError]</c>s of an operation or its app.
/// </summary>
/// <remarks>
/// <c>BlueprintHttpClient.ForwardAsync(context, …)</c> reads these attributes at run time, for generated and hand-written
/// forwards alike, and throws on the first forward when they are invalid. These checks report the same mistakes at build
/// time instead. An unregistered strategy is a matter of the gateway's services, so it is still found at run time.
/// </remarks>
public static class UpstreamForwardPolicyAnalysis
{
    private const string UpstreamCredentialAttributeName = "Blueprint.Model.Attributes.UpstreamCredentialAttribute";
    private const string StrategyInterfaceName = "Blueprint.HttpClient.IUpstreamCredentialStrategy";
    private const string ApiExceptionFactoryName = "Blueprint.ApiExceptionFactory";

    /// <summary>
    /// Checks an operation's upstream credential and error translations.
    /// </summary>
    /// <param name="operationType">The operation.</param>
    /// <returns>BPGW015, BPGW016 and BPGW017, as they apply.</returns>
    public static IEnumerable<DiagnosticInfo> AnalyseOperation(INamedTypeSymbol operationType)
    {
        var credentials = operationType.GetAttributes()
            .Where(static a => a.AttributeClass is { } c && c.InheritsFrom(UpstreamCredentialAttributeName))
            .ToList();

        if (credentials.Count > 1)
        {
            yield return new DiagnosticInfo(
                GatewayDiagnosticDescriptors.SeveralUpstreamCredentials,
                LocationOf(credentials[1], operationType),
                [operationType.Name, credentials.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Join(", ", credentials.Select(static c => c.AttributeClass!.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)))]);
        }

        foreach (var credential in credentials)
        {
            // The strategy is resolved from the gateway's services, where the interface itself can be registered.
            if (FindStrategyType(credential.AttributeClass!) is { TypeKind: not TypeKind.Error } strategy
                && strategy.ToFullMetadataName() != StrategyInterfaceName
                && !strategy.ImplementsInterface(StrategyInterfaceName))
            {
                yield return new DiagnosticInfo(
                    GatewayDiagnosticDescriptors.CredentialTypeIsNotAStrategy,
                    LocationOf(credential, operationType),
                    [operationType.Name, strategy.Name]);
            }
        }

        foreach (var diagnostic in AnalyseTranslations(operationType))
        {
            yield return diagnostic;
        }
    }

    /// <summary>
    /// Checks the <c>[TranslateError]</c>s applied to an operation or an app.
    /// </summary>
    /// <param name="declaringType">The operation or app the attributes are applied to.</param>
    /// <returns>BPGW017 for each that names no static <c>ApiExceptionFactory</c> of <paramref name="declaringType"/>.</returns>
    public static IEnumerable<DiagnosticInfo> AnalyseTranslations(INamedTypeSymbol declaringType)
    {
        foreach (var attribute in declaringType.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: "TranslateErrorAttribute" } attributeClass
                || !attributeClass.IsInNamespace("Blueprint", "Model", "Attributes")
                || attribute.ConstructorArguments is not [{ Value: string fromType }, { Value: string to }])
            {
                continue;
            }

            // The runtime looks only at the attributed class's own static members, as reflection without FlattenHierarchy does,
            // and reads a property through its getter.
            var isFactory = declaringType.GetMembers(to).Any(static m => m.IsStatic && m switch
            {
                IFieldSymbol field => IsFactory(field.Type),
                IPropertySymbol property => property.GetMethod is not null && IsFactory(property.Type),
                _ => false,
            });

            if (!isFactory)
            {
                yield return new DiagnosticInfo(
                    GatewayDiagnosticDescriptors.TranslateErrorTargetIsNotAFactory,
                    LocationOf(attribute, declaringType),
                    [fromType, to, declaringType.Name]);
            }
        }
    }

    private static INamedTypeSymbol? FindStrategyType(INamedTypeSymbol attributeClass)
    {
        for (var current = attributeClass; current is not null; current = current.BaseType)
        {
            if (current is { IsGenericType: true, TypeArguments: [INamedTypeSymbol strategy] }
                && current.OriginalDefinition.ToFullMetadataName() == UpstreamCredentialAttributeName + "`1")
            {
                return strategy;
            }
        }

        return null;
    }

    private static bool IsFactory(ITypeSymbol type)
    {
        return type is INamedTypeSymbol named
            && (named.ToString() == ApiExceptionFactoryName || named.InheritsFrom(ApiExceptionFactoryName));
    }

    private static LocationInfo? LocationOf(AttributeData attribute, INamedTypeSymbol declaringType)
    {
        return attribute.ApplicationSyntaxReference is { } reference
            ? LocationInfo.CreateFrom(reference.GetSyntax())
            : LocationInfo.CreateFrom(declaringType.Locations.First());
    }
}
