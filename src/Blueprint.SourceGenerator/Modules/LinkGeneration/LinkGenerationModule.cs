using System.Text.RegularExpressions;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// A module that will configure URLs and links for operations.
/// </summary>
public class LinkGenerationModule : IBlueprintSourceGeneratorModule, IPostInitializationContributor, IDescriptorContributor, IEnabledHostContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(LinkGenerationModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(LinkGenerationModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public IReadOnlyList<string> HostNames { get; } = ["http"];

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

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        LookForRootLinks(context);
        LookForSelfLinks(context);

        context.AddRenderStep(codeWriter => RenderDescriptor(codeWriter, context.GetMetadata<UrlRegistration>(), context.GetMetadata<LinkRegistration>()));
    }

    // Normalise away the parameter names, so that we can compare URLs with different property names (as utimately they are
    // the same and therefore we cannot distinguish them)
    private static string NormaliseRouteParamsUrl(string url)
    {
        return _placeholderRegex.Replace(url, "{<prop>}");
    }

    private static void RenderDescriptor(
        ICodeWriter codeWriter,
        IEnumerable<UrlRegistration> urlRegistrations,
        IEnumerable<LinkRegistration> linkRegistrations)
    {
        foreach (var r in urlRegistrations)
        {
            codeWriter.AppendLine($"this.AddUrl(new global::Blueprint.Model.ApiOperationUrl(\"{r.Url}\"));");
        }

        foreach (var r in linkRegistrations)
        {
            codeWriter.AppendLine($"this.AddLink(new global::Blueprint.Model.ApiOperationLink(this, \"{r.Url}\", \"{r.Url}\", \"{r.Rel}\", typeof({r.ResourceType})));");
        }
    }

    private static void LookForRootLinks(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        var rootLinkAttribute = RootLinkAttributeDescriptor.Instance.ParseSingle(operationType);

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

                if (operationType.GetMembers().Any(m => m.Name == placeholderName) == false)
                {
                    var argumentLocationInfo = locationInfo.WithSubRange(placeholder.Index + 2, placeholder.Length - 2);
                    context.AddDiagnostic(CreatePlaceholderMissingDiagnostic(operationType, placeholderName, argumentLocationInfo));
                }
            }
        }

        var urlRegistration = new UrlRegistration(url, locationInfo);
        context.AddMetadata(urlRegistration);
        context.AddMetadata(HostExposureRegistration.HttpAt(locationInfo));
    }

    private static void LookForSelfLinks(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        var selfLinkAttributes = SelfLinkAttributeDescriptor.Instance.ParseAll(operationType);

        foreach (var selfLinkAttribute in selfLinkAttributes)
        {
            var (_, urlLocation, url) = selfLinkAttribute.GetConstructorArgument(SelfLinkAttributeDescriptor.UrlArgument);
            var (_, resourceLocation, resourceType) = selfLinkAttribute.GetConstructorArgument(SelfLinkAttributeDescriptor.ResourceTypeArgument);

            if (resourceType is not INamedTypeSymbol resourceTypeSymbol || resourceTypeSymbol.AllInterfaces.Any(i => i.Name == "ILinkableResource") == false)
            {
                context.AddDiagnostic(
                    new DiagnosticInfo(
                        _notLinkableResourceError,
                        resourceLocation,
                        [resourceType.ToDisplayString(), operationType.ToDisplayString()]));

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
                                operationType,
                                routePropertyName,
                                argumentLocationInfo,
                                context))
                        {
                            // Now, make sure the resource has the route name OR the alternate name. We will only do this
                            // check if the operation check passes
                            if (string.IsNullOrEmpty(resourcePropertyName))
                            {
                                // No resource property name given, make sure the route param also exists on the resource
                                TryFindMember(
                                    resourceTypeSymbol,
                                    routePropertyName,
                                    argumentLocationInfo,
                                    context);
                            }
                            else
                            {
                                // An alternative name was given, make sure that exists on the resource
                                TryFindMember(
                                    resourceTypeSymbol,
                                    resourcePropertyName!,
                                    argumentLocationInfo,
                                    context);
                            }
                        }

                        return "{" + routePropertyName + "}";
                    });
            }

            if (context.HasDiagnostics())
            {
                return;
            }

            var urlRegistration = new UrlRegistration(normalisedUrl, urlLocation);
            var linkRegistration = new LinkRegistration(normalisedUrl, "self", resourceTypeSymbol, urlLocation);

            context.AddMetadata(urlRegistration);
            context.AddMetadata(linkRegistration);
            context.AddMetadata(HostExposureRegistration.HttpAt(urlLocation));
        }
    }

    private static bool TryFindMember(INamedTypeSymbol namedTypeSymbol, string placeholderName, LocationInfo argumentLocationInfo, DescriptorContributorContext context)
    {
        var memberExists = namedTypeSymbol.GetMembers().Any(m => m.Name == placeholderName);

        if (!memberExists)
        {
            context.AddDiagnostic(
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

    private sealed record UrlRegistration(string Url, LocationInfo? LocationInfo);

    private sealed record LinkRegistration(string Url, string Rel, INamedTypeSymbol ResourceType, LocationInfo LocationInfo);
}
