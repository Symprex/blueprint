using System.Text.RegularExpressions;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules.LinkGeneration;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// A module that will configure URLs and links for operations (descriptor-level),
/// and populate $links.self on returned resources at PostExecution (pipeline-level).
/// </summary>
public class LinkGenerationModule : IBlueprintSourceGeneratorModule, IPostInitializationContributor, IDescriptorContributor, IEnabledHostContributor, IPipelineContributor
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

    private static readonly DiagnosticDescriptor _notLinkableResourceError = new(
        id: "BPLG003",
        title: "Resource type is not ILinkableResource",
        messageFormat: "The resource type '{0}' is not an ILinkableResource. Check the link attributes on '{1}'.",
        category: "OperationLinks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _ambiguousSelfLinkError = new(
        id: "BPLG004",
        title: "Ambiguous self-link declarations",
        messageFormat: "Multiple self-link URL templates target resource type '{0}': {1}. Only the first will be used for link population.",
        category: "OperationLinks",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _emptyRouteTemplateError = new(
        id: "BPLG005",
        title: "Empty route template",
        messageFormat: "The route template on '{0}' is empty or whitespace. Provide a valid URL path.",
        category: "OperationLinks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _linkMissingRelError = new(
        id: "BPLG006",
        title: "Link missing Rel value",
        messageFormat: "The [Link] attribute on '{0}' must specify a non-empty Rel value. Use [SelfLink] for self-links or provide Rel = \"relName\".",
        category: "OperationLinks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly Regex _placeholderRegex = new(@"\{\*{0,2}[A-Za-z0-9]+\}", RegexOptions.Compiled);
    private static readonly Regex _mappedPlaceholderRegex = new(@"\{(?<catchAll>\*{0,2})(?<routeName>[A-Za-z0-9]+)(:(?<resourcePropName>.*?))?(\((?<format>.*)\))?\}", RegexOptions.Compiled);

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
        LookForLinks(context);

        context.AddRenderStep(codeWriter => RenderDescriptor(codeWriter, context.GetMetadata<UrlRegistration>(), context.GetMetadata<LinkRegistration>()));
    }

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        var handler = context.Handler;
        if (handler is null)
        {
            return;
        }

        var resourceBinding = ResolveLinkableResourceType(context, handler);
        if (resourceBinding is null)
        {
            return;
        }

        var selfLinks = context.FindSelfLinksForResourceType(resourceBinding.ResourceType);
        if (selfLinks.Count == 0)
        {
            return;
        }

        // Warn when multiple distinct URL templates target the same resource type
        var distinctUrls = selfLinks.Select(static l => l.UrlTemplate).Distinct().ToList();
        if (distinctUrls.Count > 1)
        {
            context.AddDiagnostic(new DiagnosticInfo(
                _ambiguousSelfLinkError,
                context.Operation.TypeSymbol.Locations.FirstOrDefault(),
                new EquatableList<string>([resourceBinding.ResourceType.Name, string.Join(", ", distinctUrls.Select(static u => $"'{u}'"))])));
        }

        // Require the handler result variable from Execution stage
        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            PipelineStage.PostExecution);

        context.RequireAsync();
        context.AddRenderStep(PipelineStage.PostExecution, renderContext =>
        {
            RenderLinkPopulation(renderContext, resourceBinding, selfLinks);
            RenderFullHateoasLinkPopulation(renderContext);
        });
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

        var linkIndex = 0;

        foreach (var r in linkRegistrations)
        {
            if (r.PlaceholderMappings.Count > 0)
            {
                // Generate a link with a compiled URL builder that uses direct property access
                var resourceTypeFqn = r.ResourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var urlBuilder = BuildCompiledUrlBuilderLambda(r.Url, r.PlaceholderMappings, resourceTypeFqn);
                var varName = $"link_{linkIndex}";

                codeWriter.AppendLine($"var {varName} = new global::Blueprint.Model.ApiOperationLink(this, \"{r.Url}\", \"{r.Url}\", \"{r.Rel}\", typeof({r.ResourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}));");
                codeWriter.AppendLine($"{varName}.CompiledUrlBuilder = {urlBuilder};");
                codeWriter.AppendLine($"this.AddLink({varName});");
            }
            else
            {
                codeWriter.AppendLine($"this.AddLink(new global::Blueprint.Model.ApiOperationLink(this, \"{r.Url}\", \"{r.Url}\", \"{r.Rel}\", typeof({r.ResourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})));");
            }

            linkIndex++;
        }
    }

    /// <summary>
    /// Generates a lambda expression that builds a URL from a resource object using direct
    /// property access instead of reflection. Returns <see langword="null"/> when any required
    /// property value is <see langword="null"/>.
    /// For single-placeholder links, the lambda also handles <see cref="string"/> and
    /// <see cref="int"/> scalars directly, allowing <c>CreateSelfLink&lt;T&gt;(string id)</c>
    /// to skip anonymous-object allocation and reflection.
    /// </summary>
    private static string BuildCompiledUrlBuilderLambda(
        string urlFormat,
        EquatableList<LinkPlaceholderMapping> mappings,
        string resourceTypeFqn)
    {
        var mappingLookup = mappings.ToDictionary(static m => m.RouteName, static m => m, StringComparer.Ordinal);

        // Track whether the single placeholder uses catch-all (no escaping) semantics
        var isSinglePlaceholderCatchAll = false;

        var interpolated = _mappedPlaceholderRegex.Replace(
            urlFormat,
            match =>
            {
                var routeName = match.Groups["routeName"].Value;
                if (!mappingLookup.TryGetValue(routeName, out var mapping))
                {
                    return match.Value;
                }

                // Value types always have a value and cannot use ?. operator
                var propertyValueExpression = mapping.IsValueType
                    ? $"r.{mapping.ResourcePropertyName}.ToString()"
                    : $"r.{mapping.ResourcePropertyName}?.ToString() ?? \"\"";

                var isCatchAll = match.Groups["catchAll"].Length > 0;

                if (mappings.Count == 1)
                {
                    isSinglePlaceholderCatchAll = isCatchAll;
                }

                return isCatchAll
                    ? $"{{{propertyValueExpression}}}"
                    : $"{{System.Uri.EscapeDataString({propertyValueExpression})}}";
            });

        // Build null-guard conditions: return null if any required reference-type property is null.
        // Value type properties always have a value and are excluded from null checks.
        var nullableProperties = mappings.Where(static m => !m.IsValueType).ToList();

        // Build the resource-typed case
        string resourceCase;

        if (nullableProperties.Count > 0)
        {
            var nullChecks = string.Join(
                " || ",
                nullableProperties.Select(static m => $"r.{m.ResourcePropertyName} == null"));

            resourceCase = $"{resourceTypeFqn} r when !({nullChecks}) => $\"{interpolated}\"";
        }
        else
        {
            resourceCase = $"{resourceTypeFqn} r => $\"{interpolated}\"";
        }

        // Single-placeholder links get additional scalar cases so that
        // CreateSelfLink<T>(string id) and CreateSelfLink<T>(Guid id) avoid
        // anonymous-object allocation, reflection, and intermediate ToString() allocations.
        if (mappings.Count == 1)
        {
            // Build the scalar interpolated URL (replace the single placeholder with {id})
            var scalarInterpolated = _mappedPlaceholderRegex.Replace(
                urlFormat,
                match =>
                {
                    var routeName = match.Groups["routeName"].Value;
                    return mappingLookup.ContainsKey(routeName)
                        ? (isSinglePlaceholderCatchAll ? "{id}" : "{System.Uri.EscapeDataString(id)}")
                        : match.Value;
                });

            // Guid format (00000000-0000-0000-0000-000000000000) is always URL-safe —
            // hex digits and dashes need no encoding. Use {g} directly to leverage
            // ISpanFormattable.TryFormat, which writes into the interpolated string
            // handler's buffer without a separate Guid.ToString() heap allocation.
            var guidInterpolated = _mappedPlaceholderRegex.Replace(
                urlFormat,
                match =>
                {
                    var routeName = match.Groups["routeName"].Value;
                    return mappingLookup.ContainsKey(routeName) ? "{g}" : match.Value;
                });

            return $"static (obj) => obj switch {{ {resourceCase}, string id => $\"{scalarInterpolated}\", global::System.Guid g => $\"{guidInterpolated}\", _ => null }}";
        }

        return $"static (obj) => obj switch {{ {resourceCase}, _ => null }}";
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

        if (string.IsNullOrWhiteSpace(url))
        {
            context.AddDiagnostic(
                new DiagnosticInfo(
                    _emptyRouteTemplateError,
                    locationInfo,
                    [operationType.Name]));

            return;
        }
        if (url.Contains("{"))
        {
            var placeholders = _placeholderRegex.Matches(url);

            foreach (var placeholder in placeholders.Cast<Match>())
            {
                var placeholderName = placeholder.Value.Trim('{', '}', '*');

                if (operationType.GetAllMembers().Any(m => m.Name == placeholderName) == false)
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

            if (string.IsNullOrWhiteSpace(url))
            {
                context.AddDiagnostic(
                    new DiagnosticInfo(
                        _emptyRouteTemplateError,
                        urlLocation,
                        [operationType.Name]));

                continue;
            }

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
            var placeholderMappings = new List<LinkPlaceholderMapping>();

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

                                var isValueType = IsResourcePropertyValueType(resourceTypeSymbol, routePropertyName);
                                placeholderMappings.Add(new LinkPlaceholderMapping(routePropertyName, routePropertyName, isValueType));
                            }
                            else
                            {
                                // An alternative name was given, make sure that exists on the resource
                                TryFindMember(
                                    resourceTypeSymbol,
                                    resourcePropertyName!,
                                    argumentLocationInfo,
                                    context);

                                var isValueType = IsResourcePropertyValueType(resourceTypeSymbol, resourcePropertyName!);
                                placeholderMappings.Add(new LinkPlaceholderMapping(routePropertyName, resourcePropertyName!, isValueType));
                            }
                        }

                        return "{" + match.Groups["catchAll"].Value + routePropertyName + "}";
                    });
            }

            if (context.HasDiagnostics())
            {
                return;
            }

            var urlRegistration = new UrlRegistration(normalisedUrl, urlLocation);
            var linkRegistration = new LinkRegistration(normalisedUrl, "self", resourceTypeSymbol, urlLocation, new EquatableList<LinkPlaceholderMapping>(placeholderMappings.ToArray()));

            context.AddMetadata(urlRegistration);
            context.AddMetadata(linkRegistration);
            context.AddMetadata(HostExposureRegistration.HttpAt(urlLocation));
        }
    }

    /// <summary>
    /// Discovers <c>[Link(typeof(X), "/path", Rel = "relName")]</c> attributes from
    /// the runtime model <c>LinkAttribute</c> base class.
    /// These represent non-self HATEOAS links that also register an HTTP endpoint.
    /// </summary>
    private static void LookForLinks(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();

        foreach (var attribute in operationType.GetAttributes())
        {
            var attrClass = attribute.AttributeClass;

            if (attrClass is null)
            {
                continue;
            }

            // Match LinkAttribute but NOT its derived SelfLinkAttribute/RootLinkAttribute
            // (those are already handled by LookForSelfLinks/LookForRootLinks).
            if (attrClass.Name != "LinkAttribute" ||
                !string.Equals(attrClass.ContainingNamespace?.ToDisplayString(), "Blueprint.Model.Attributes", StringComparison.Ordinal))
            {
                continue;
            }

            // LinkAttribute(Type? resourceType, string routePattern, string? rel = null)
            if (attribute.ConstructorArguments.Length < 2)
            {
                continue;
            }

            var resourceTypeArg = attribute.ConstructorArguments[0];
            var routePatternArg = attribute.ConstructorArguments[1];
            var url = routePatternArg.Value as string;

            // Use the attribute's application syntax for location when available
            var attributeLocation = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
            var locationInfo = attributeLocation is not null
                ? LocationInfo.CreateFrom(attributeLocation)
                : operationType.Locations.FirstOrDefault() is { } loc
                    ? LocationInfo.CreateFrom(loc)
                    : null;

            if (string.IsNullOrWhiteSpace(url))
            {
                if (locationInfo is not null)
                {
                    context.AddDiagnostic(
                        new DiagnosticInfo(
                            _emptyRouteTemplateError,
                            locationInfo,
                            [operationType.Name]));
                }

                continue;
            }

            // Resolve the Rel value — third constructor arg or named argument
            string? rel = null;

            if (attribute.ConstructorArguments.Length >= 3)
            {
                rel = attribute.ConstructorArguments[2].Value as string;
            }

            // Also check named arguments (Rel can be set via property syntax: Rel = "relName")
            foreach (var namedArg in attribute.NamedArguments)
            {
                if (namedArg.Key == "Rel" && namedArg.Value.Value is string namedRel)
                {
                    rel = namedRel;
                }
            }

            // When resourceType is null, treat as root-link semantics (URL registration only)
            if (resourceTypeArg.IsNull)
            {
                if (url!.Contains("{"))
                {
                    var placeholders = _placeholderRegex.Matches(url);

                    foreach (var placeholder in placeholders.Cast<Match>())
                    {
                        var placeholderName = placeholder.Value.Trim('{', '}', '*');

                        if (!operationType.GetAllMembers().Any(m => m.Name == placeholderName))
                        {
                            context.AddDiagnostic(CreatePlaceholderMissingDiagnostic(
                                operationType,
                                placeholderName,
                                locationInfo?.WithSubRange(placeholder.Index + 2, placeholder.Length - 2)));
                        }
                    }
                }

                context.AddMetadata(new UrlRegistration(url!, locationInfo));
                context.AddMetadata(HostExposureRegistration.HttpAt(locationInfo));

                continue;
            }

            // Non-null resourceType: require a Rel value for non-self links
            if (string.IsNullOrWhiteSpace(rel))
            {
                var diagLocation = locationInfo ?? (operationType.Locations.FirstOrDefault() is { } fallbackLoc
                    ? LocationInfo.CreateFrom(fallbackLoc) : null);

                context.AddDiagnostic(
                    new DiagnosticInfo(
                        _linkMissingRelError,
                        diagLocation,
                        [operationType.Name]));

                continue;
            }

            if (resourceTypeArg.Value is not INamedTypeSymbol resourceTypeSymbol || resourceTypeSymbol.TypeKind == TypeKind.Error)
            {
                continue;
            }

            // Verify resource type implements ILinkableResource
            if (!resourceTypeSymbol.AllInterfaces.Any(static i => i.Name == "ILinkableResource"))
            {
                var diagLocation = locationInfo ?? (operationType.Locations.FirstOrDefault() is { } fallbackLoc2
                    ? LocationInfo.CreateFrom(fallbackLoc2) : null);

                context.AddDiagnostic(
                    new DiagnosticInfo(
                        _notLinkableResourceError,
                        diagLocation,
                        [resourceTypeSymbol.ToDisplayString(), operationType.ToDisplayString()]));

                continue;
            }

            var normalisedUrl = url!;
            var placeholderMappings = new List<LinkPlaceholderMapping>();

            if (normalisedUrl.Contains("{"))
            {
                normalisedUrl = _mappedPlaceholderRegex.Replace(
                    normalisedUrl,
                    match =>
                    {
                        var routePropertyName = match.Groups["routeName"].Value;
                        var resourcePropertyName = match.Groups["resourcePropName"]?.Value;

                        var argumentLocationInfo = locationInfo?.WithSubRange(
                            match.Index + 2,
                            match.Length - 2);

                        if (TryFindMember(operationType, routePropertyName, argumentLocationInfo, context))
                        {
                            if (string.IsNullOrEmpty(resourcePropertyName))
                            {
                                TryFindMember(resourceTypeSymbol, routePropertyName, argumentLocationInfo, context);

                                var isValueType = IsResourcePropertyValueType(resourceTypeSymbol, routePropertyName);
                                placeholderMappings.Add(new LinkPlaceholderMapping(routePropertyName, routePropertyName, isValueType));
                            }
                            else
                            {
                                TryFindMember(resourceTypeSymbol, resourcePropertyName!, argumentLocationInfo, context);

                                var isValueType = IsResourcePropertyValueType(resourceTypeSymbol, resourcePropertyName!);
                                placeholderMappings.Add(new LinkPlaceholderMapping(routePropertyName, resourcePropertyName!, isValueType));
                            }
                        }

                        return "{" + match.Groups["catchAll"].Value + routePropertyName + "}";
                    });
            }

            if (context.HasDiagnostics())
            {
                return;
            }

            var urlRegistration = new UrlRegistration(normalisedUrl, locationInfo);
            var linkRegistration = new LinkRegistration(normalisedUrl, rel!, resourceTypeSymbol, locationInfo!, new EquatableList<LinkPlaceholderMapping>(placeholderMappings.ToArray()));

            context.AddMetadata(urlRegistration);
            context.AddMetadata(linkRegistration);
            context.AddMetadata(HostExposureRegistration.HttpAt(locationInfo));
        }
    }

    private static bool TryFindMember(INamedTypeSymbol namedTypeSymbol, string placeholderName, LocationInfo? argumentLocationInfo, DescriptorContributorContext context)
    {
        var memberExists = namedTypeSymbol.GetAllMembers().Any(m => m.Name == placeholderName);

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

    /// <summary>
    /// Checks whether a named property on a resource type is a non-nullable value type, which
    /// affects the generated URL builder code (value types cannot use <c>?.</c> or be compared to null).
    /// </summary>
    private static bool IsResourcePropertyValueType(INamedTypeSymbol resourceType, string propertyName)
    {
        var member = resourceType.GetAllMembers().FirstOrDefault(m => m.Name == propertyName);

        if (member is IPropertySymbol property)
        {
            return property.Type.IsValueType
                   && property.Type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
                   && property.Type.NullableAnnotation != NullableAnnotation.Annotated;
        }

        return false;
    }

    private static DiagnosticInfo CreatePlaceholderMissingDiagnostic(ITypeSymbol namedType, string placeholderName, LocationInfo? argumentLocationInfo)
    {
        return new DiagnosticInfo(
            _missingPlaceholderError,
            argumentLocationInfo,
            [placeholderName, namedType.Name]);
    }

    private static LinkableResourceBinding? ResolveLinkableResourceType(PipelineContributorContext context, OperationHandlerModel handler)
    {
        var linkableResourceInterface = context.FindTypeByMetadataName("Blueprint.Model.ILinkableResource");
        if (linkableResourceInterface is null)
        {
            return null;
        }

        var resourceType = ExtractContentResourceType(context, handler);
        if (resourceType is null)
        {
            return null;
        }

        if (!resourceType.AllInterfaces.Contains(linkableResourceInterface, SymbolEqualityComparer.Default))
        {
            return null;
        }

        var (contentAccess, guardType) = GetContentAccessInfo(context, handler);
        return new LinkableResourceBinding(resourceType, contentAccess, guardType);
    }

    private static INamedTypeSymbol? ExtractContentResourceType(PipelineContributorContext context, OperationHandlerModel handler)
    {
        if (OperationResultContractHelper.GetDeclaredOperationResultType(context.Operation.TypeSymbol) is not INamedTypeSymbol declaredResultType)
        {
            return null;
        }

        // ResourceEventResult<T> → T
        var resourceEventResultGeneric = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.ResourceEventResult`1");
        if (resourceEventResultGeneric is not null
            && declaredResultType.IsGenericType
            && SymbolEqualityComparer.Default.Equals(declaredResultType.OriginalDefinition, resourceEventResultGeneric))
        {
            return declaredResultType.TypeArguments[0] as INamedTypeSymbol;
        }

        // OkResult<T> → T
        var okResultGeneric = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.OkResult`1");
        if (okResultGeneric is not null
            && declaredResultType.IsGenericType
            && SymbolEqualityComparer.Default.Equals(declaredResultType.OriginalDefinition, okResultGeneric))
        {
            return declaredResultType.TypeArguments[0] as INamedTypeSymbol;
        }

        // Results<T1, T2> union — check each member for linkable resource
        if (OperationResultContractHelper.TryGetResultsUnionMembers(declaredResultType, out var unionMembers))
        {
            foreach (var member in unionMembers)
            {
                if (member is INamedTypeSymbol namedMember)
                {
                    var innerResource = ExtractResourceFromResultMember(context, namedMember);
                    if (innerResource is not null)
                    {
                        return innerResource;
                    }
                }
            }
        }

        // Plain T that is itself ILinkableResource
        return declaredResultType;
    }

    private static INamedTypeSymbol? ExtractResourceFromResultMember(PipelineContributorContext context, INamedTypeSymbol member)
    {
        var resourceEventResultGeneric = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.ResourceEventResult`1");
        if (resourceEventResultGeneric is not null
            && member.IsGenericType
            && SymbolEqualityComparer.Default.Equals(member.OriginalDefinition, resourceEventResultGeneric))
        {
            return member.TypeArguments[0] as INamedTypeSymbol;
        }

        var okResultGeneric = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.OkResult`1");
        if (okResultGeneric is not null
            && member.IsGenericType
            && SymbolEqualityComparer.Default.Equals(member.OriginalDefinition, okResultGeneric))
        {
            return member.TypeArguments[0] as INamedTypeSymbol;
        }

        return null;
    }

    private static (string ContentAccess, string GuardTypeName) GetContentAccessInfo(PipelineContributorContext context, OperationHandlerModel handler)
    {
        if (OperationResultContractHelper.GetDeclaredOperationResultType(context.Operation.TypeSymbol) is not INamedTypeSymbol declaredResultType)
        {
            return ("((Blueprint.Runtime.Exec.OkResult)handlerResult).Content", "Blueprint.Runtime.Exec.OkResult");
        }

        var resourceEventResultGeneric = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.ResourceEventResult`1");
        if (resourceEventResultGeneric is not null
            && declaredResultType.IsGenericType
            && SymbolEqualityComparer.Default.Equals(declaredResultType.OriginalDefinition, resourceEventResultGeneric))
        {
            var resourceTypeName = declaredResultType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var fullResultTypeName = declaredResultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return ($"(({fullResultTypeName})handlerResult).Content", fullResultTypeName);
        }

        var okResultGeneric = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.OkResult`1");
        if (okResultGeneric is not null
            && declaredResultType.IsGenericType
            && SymbolEqualityComparer.Default.Equals(declaredResultType.OriginalDefinition, okResultGeneric))
        {
            var resourceTypeName = declaredResultType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var fullResultTypeName = declaredResultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return ($"(({fullResultTypeName})handlerResult).Content", fullResultTypeName);
        }

        // Union case: look for first content-bearing member
        if (OperationResultContractHelper.TryGetResultsUnionMembers(declaredResultType, out var unionMembers))
        {
            foreach (var member in unionMembers)
            {
                if (member is not INamedTypeSymbol namedMember)
                {
                    continue;
                }

                if (resourceEventResultGeneric is not null
                    && namedMember.IsGenericType
                    && SymbolEqualityComparer.Default.Equals(namedMember.OriginalDefinition, resourceEventResultGeneric))
                {
                    var resourceTypeName = namedMember.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var fullResultTypeName = namedMember.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    return ($"(({fullResultTypeName})handlerResult).Content", fullResultTypeName);
                }

                if (okResultGeneric is not null
                    && namedMember.IsGenericType
                    && SymbolEqualityComparer.Default.Equals(namedMember.OriginalDefinition, okResultGeneric))
                {
                    var resourceTypeName = namedMember.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var fullResultTypeName = namedMember.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    return ($"(({fullResultTypeName})handlerResult).Content", fullResultTypeName);
                }
            }
        }

        // Fallback: non-generic OkResult
        return ("((Blueprint.Runtime.Exec.OkResult)handlerResult).Content", "Blueprint.Runtime.Exec.OkResult");
    }

    private static void RenderLinkPopulation(
        PipelineRenderContext context,
        LinkableResourceBinding binding,
        IReadOnlyList<SelfLinkBindingInfo> selfLinks)
    {
        var resourceTypeName = binding.ResourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        context.WriteBlankLine();
        context.WriteStatement("// Populate $links.self on the returned resource (skip 304 Not Modified)");
        context.If(
            $"handlerResult is {binding.GuardTypeName} and not global::Blueprint.Runtime.Exec.INotModifiedResult",
            branch =>
            {
                branch.DeclareVariable("linkableResource", $"({resourceTypeName}){binding.ContentAccessExpression}");

                // Deduplicate by URL template: multiple operations may declare [SelfLink] for the
                // same resource type with the same URL, but we only emit AddLink("self", ...) once.
                var emittedUrls = new HashSet<string>(StringComparer.Ordinal);
                foreach (var selfLink in selfLinks)
                {
                    if (!emittedUrls.Add(selfLink.UrlTemplate))
                    {
                        continue;
                    }

                    var hrefExpression = BuildHrefInterpolation(selfLink.UrlTemplate);
                    branch.WriteStatement($"linkableResource.AddLink(\"self\", new global::Blueprint.Model.Link {{ Href = {hrefExpression} }});");
                }
            });
    }

    /// <summary>
    /// Generates a single call to <c>PipelineHelpers.PopulateHateoasLinksAsync</c> which resolves
    /// <c>IApiLinkGenerator</c> and <c>IResourceLinkGenerator</c> from DI and calls
    /// <c>LinkGeneratorHandler.AddLinksAsync</c> to populate all HATEOAS links on the result.
    /// Replaces a 10-line inline block that was previously emitted per-pipeline.
    /// </summary>
    private static void RenderFullHateoasLinkPopulation(PipelineRenderContext context)
    {
        context.WriteBlankLine();
        context.WriteStatement("// Populate full HATEOAS links (skip 304 Not Modified)");
        context.WriteStatement("await Blueprint.Runtime.Exec.PipelineHelpers.PopulateHateoasLinksAsync(context.ServiceProvider, context, handlerResult).ConfigureAwait(false);");
    }

    private static string BuildHrefInterpolation(string urlTemplate)
    {
        var placeholders = _mappedPlaceholderRegex.Matches(urlTemplate);
        if (placeholders.Count == 0)
        {
            return $"\"{urlTemplate}\"";
        }

        var interpolated = _mappedPlaceholderRegex.Replace(
            urlTemplate,
            match =>
            {
                var routeName = match.Groups["routeName"].Value;
                var resourcePropName = match.Groups["resourcePropName"]?.Value;

                // Use the resource property name if provided, otherwise use the route name
                var propName = string.IsNullOrEmpty(resourcePropName) ? routeName : resourcePropName;
                return $"{{linkableResource.{propName}}}";
            });

        return $"$\"{interpolated}\"";
    }

    private sealed record LinkableResourceBinding(INamedTypeSymbol ResourceType, string ContentAccessExpression, string GuardTypeName);
}
