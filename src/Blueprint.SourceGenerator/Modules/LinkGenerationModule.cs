using System.Text.RegularExpressions;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.OperationDescription;
using CodeGenHelpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// A module that will configure URLs and links for operations.
/// </summary>
public class LinkGenerationModule : IBlueprintSourceGeneratorModule
{
    private static readonly DiagnosticDescriptor _missingPlaceholderError = new(
        id: "BPLG001",
        title: "Link format is invalid",
        messageFormat: "A public property '{0}' cannot be found on type '{1}'",
        category: "OperationLinks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _duplicateUrlError = new(
        id: "BPLG002",
        title: "Duplicate URL",
        messageFormat: "The URL '{0}' has already been registered with other operations: '{1}'",
        category: "OperationLinks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _notLinkableResourceError = new(
        id: "BPLG003",
        title: "Resource type is not ILinkableResource",
        messageFormat: "The resource type '{0}' is not an ILinkableResource. Check the link attributes on '{1}'.",
        category: "OperationLinks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly Regex _placeholderRegex = new(@"\{[A-Za-z0-9]+\}", RegexOptions.Compiled);
    private static readonly Regex _mappedPlaceholderRegex = new(@"{(?<routeName>.*?)(:(?<resourcePropName>.*?))?(\((?<format>.*)\))?}", RegexOptions.Compiled);

    /// <inheritdoc/>
    public void RegisterMarkerAttributes(IncrementalGeneratorPostInitializationContext initializationContext)
    {
        RootLinkAttributeDescriptor.Instance.AddSource(initializationContext);
        SelfLinkAttributeDescriptor.Instance.AddSource(initializationContext);
    }

    /// <inheritdoc/>
    public void ConfigureModel(OperationConfigurationModel registration)
    {
        var linkRegistrations = new List<UrlRegistration>();

        LookForRootLinks(registration, linkRegistrations);
        LookForSelfLinks(registration, linkRegistrations);

        // Ensure that we do not have any duplicate URLs
        // foreach (var duplicates in linkRegistrations.GroupBy(r => NormaliseRouteParamsUrl(r.Url)).Where(g => g.Count() > 1))
        // {
        //     foreach (var r in duplicates)
        //     {
        //         var duplicateRegistrationsAsString = string.Join(
        //             ", ",
        //             duplicates.Where(dr => dr != r).Select(dr => dr.Registration.TypeSymbol.ToString()));
        //
        //         r.Registration.AddDiagnostic(
        //             new DiagnosticInfo(
        //                 _duplicateUrlError,
        //                 r.LocationInfo,
        //                 [r.Url, duplicateRegistrationsAsString]));
        //     }
        // }
    }

    // Normalise away the parameter names, so that we can compare URLs with different property names (as utimately they are
    // the same and therefore we cannot distinguish them)
    private static string NormaliseRouteParamsUrl(string url)
    {
        return _placeholderRegex.Replace(url, "{<prop>}");
    }

    /// <inheritdoc/>
    public void ConfigureDescriptor(ICodeWriter codeWriter, OperationToRegisterModel registration)
    {
        foreach (var r in registration.GetMetadatas<UrlRegistration>())
        {
            codeWriter.AppendLine($"this.AddUrl(new global::Blueprint.Model.ApiOperationUrl(\"{r.Url}\"));");
        }

        foreach (var r in registration.GetMetadatas<LinkRegistration>())
        {
            codeWriter.AppendLine($"this.AddLink(new global::Blueprint.Model.ApiOperationLink(this, \"{r.Url}\", \"{r.Url}\", \"{r.Rel}\", typeof({r.ResourceType})));");
        }
    }

    private static void LookForRootLinks(
        OperationConfigurationModel registrationModel,
        List<UrlRegistration> linkRegistrations)
    {
        var rootLinkAttribute = RootLinkAttributeDescriptor.Instance.ParseSingle(registrationModel.TypeSymbol);

        if (rootLinkAttribute == null)
        {
            return;
        }

        var (_, locationInfo, url) = rootLinkAttribute.GetConstructorArgument(RootLinkAttributeDescriptor.UrlArgument);

        // We have a placeholder. We need to find all of these placeholders and ensure that they point to a
        // property of the operation. If they do not, we must register a diagnostic error.
        if (url.Contains("{"))
        {
            var placeholders = _placeholderRegex.Matches(url);

            foreach (var placeholder in placeholders.Cast<Match>())
            {
                var placeholderName = placeholder.Value.Trim('{', '}');

                if (registrationModel.TypeSymbol.GetMembers().Any(m => m.Name == placeholderName) == false)
                {
                    var argumentLocationInfo = locationInfo.WithSubRange(placeholder.Index + 2, placeholder.Length - 2);
                    registrationModel.AddDiagnostic(CreatePlaceholderMissingDiagnostic(registrationModel.TypeSymbol, placeholderName, argumentLocationInfo));
                }
            }
        }

        var urlRegistration = new UrlRegistration(url, locationInfo, registrationModel);

        linkRegistrations.Add(urlRegistration);
        registrationModel.OperationToRegisterModel.AddMetadata(urlRegistration);
    }

    private static void LookForSelfLinks(
        OperationConfigurationModel registrationModel,
        List<UrlRegistration> linkRegistrations)
    {
        var selfLinkAttributes = SelfLinkAttributeDescriptor.Instance.ParseAll(registrationModel.TypeSymbol);

        foreach (var selfLinkAttribute in selfLinkAttributes)
        {
            var (_, urlLocation, url) = selfLinkAttribute.GetConstructorArgument(SelfLinkAttributeDescriptor.UrlArgument);
            var (_, resourceLocation, resourceType) = selfLinkAttribute.GetConstructorArgument(SelfLinkAttributeDescriptor.ResourceTypeArgument);

            if (resourceType is not INamedTypeSymbol resourceTypeSymbol || resourceTypeSymbol.AllInterfaces.Any(i => i.Name == "ILinkableResource") == false)
            {
                registrationModel.AddDiagnostic(
                    new DiagnosticInfo(
                        _notLinkableResourceError,
                        resourceLocation,
                        [resourceType.ToDisplayString(), registrationModel.TypeSymbol.ToDisplayString()]));

                continue;
            }

            var normalisedUrl = url;

            // We have a placeholder. We need to find all of these placeholders and ensure that they point to a
            // property of the operation. If they do not, we must register a diagnostic error.
            if (normalisedUrl.Contains("{"))
            {
                normalisedUrl = _mappedPlaceholderRegex.Replace(
                    normalisedUrl,
                    match =>
                    {
                        var routePropertyName = match.Groups["routeName"].Value;
                        var resourcePropertyName = match.Groups["resourcePropName"]?.Value;

                        var argumentLocationInfo = urlLocation.WithSubRange(
                            match.Index + 2,
                            match.Length - 2);

                        // First, make sure the route property exists
                        if (TryFindMember(
                                registrationModel.TypeSymbol,
                                routePropertyName,
                                argumentLocationInfo,
                                registrationModel))
                        {
                            // Now, make sure the resource has the route name OR the alternate name. We will only do this
                            // check if the operation check passes
                            if (string.IsNullOrEmpty(resourcePropertyName))
                            {
                                // No resource property name given, make sure the route param also exists on the resource
                                TryFindMember(
                                    registrationModel.TypeSymbol,
                                    routePropertyName,
                                    argumentLocationInfo,
                                    registrationModel);
                            }
                            else
                            {
                                // An alternative name was given, make sure that exists on the resource
                                TryFindMember(
                                    resourceTypeSymbol,
                                    resourcePropertyName!,
                                    argumentLocationInfo,
                                    registrationModel);
                            }
                        }

                        return "{" + routePropertyName + "}";
                    });
            }

            if (registrationModel.Diagnostics.Any())
            {
                return;
            }

            var urlRegistration = new UrlRegistration(normalisedUrl, urlLocation, registrationModel);
            var linkRegistration = new LinkRegistration(normalisedUrl, "self", resourceTypeSymbol, urlLocation, registrationModel);

            linkRegistrations.Add(urlRegistration);
            registrationModel.OperationToRegisterModel.AddMetadata(urlRegistration);
            registrationModel.OperationToRegisterModel.AddMetadata(linkRegistration);
        }
    }

    private static bool TryFindMember(INamedTypeSymbol namedTypeSymbol, string placeholderName, LocationInfo argumentLocationInfo, OperationConfigurationModel operationConfigurationModel)
    {
        var memberExists = namedTypeSymbol.GetMembers().Any(m => m.Name == placeholderName);

        if (!memberExists)
        {
            operationConfigurationModel.AddDiagnostic(
                CreatePlaceholderMissingDiagnostic(
                    namedTypeSymbol,
                    placeholderName,
                    argumentLocationInfo));
        }

        return memberExists;
    }

    private static DiagnosticInfo CreatePlaceholderMissingDiagnostic(ITypeSymbol namedType, string placeholderName, LocationInfo argumentLocationInfo)
    {
        return new DiagnosticInfo(
            _missingPlaceholderError,
            argumentLocationInfo,
            [placeholderName, namedType.Name]);
    }

    private sealed record UrlRegistration(string Url, LocationInfo? LocationInfo, OperationConfigurationModel Registration);

    private sealed record LinkRegistration(string Url, string Rel, INamedTypeSymbol ResourceType, LocationInfo LocationInfo, OperationConfigurationModel Registration);
}
