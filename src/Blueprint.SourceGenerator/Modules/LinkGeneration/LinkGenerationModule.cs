using System.Text.RegularExpressions;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules.LinkGeneration;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// A module that will configure URLs and links for operations (descriptor-level),
/// and hand every HTTP operation's result to the runtime link population at PostExecution (pipeline-level).
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

    private static readonly Regex _placeholderRegex = new(@"\{\*{0,2}[A-Za-z0-9]+\}", RegexOptions.Compiled);

    // routeName allows '_' (unlike _placeholderRegex above, which only ever matches a bare, unmapped {Name}/{*Name}):
    // legacy's own placeholder parser took the property name as-is before any ':' or '(' (Signature365 8b40ecc961,
    // Blueprint/ApiOperationLink.cs:29, propName = ".*?", unrestricted), so a property named e.g. User_Id worked in a
    // mapped placeholder there. RoutePlaceholderSyntax (R97) already allows '_' for the same reason; this pattern is
    // kept separate rather than unified with it because it also parses the ':alternate'/'(format)' suffixes
    // RoutePlaceholderSyntax deliberately strips instead of capturing.
    private static readonly Regex _mappedPlaceholderRegex = new(@"\{(?<catchAll>\*{0,2})(?<routeName>[A-Za-z0-9_]+)(:(?<resourcePropName>.*?))?(\((?<format>.*)\))?\}", RegexOptions.Compiled);

    /// <inheritdoc/>
    public void RegisterMarkerAttributes(IncrementalGeneratorPostInitializationContext initializationContext)
    {
        // SelfLinkAttribute and RootLinkAttribute are now real classes in Blueprint.Runtime.Model,
        // so we no longer inject them as marker-attribute source. The descriptor ParseSingle/ParseAll
        // methods still work because they match by attribute name, not by reference.
    }

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        // Each kind of link attribute is read on its own, but what it registers is held until all three have been read and
        // then added in the order the attributes are declared, as legacy's one pass over them registered them.
        var registrations = new List<(int Position, object[] Metadata)>();

        LookForRootLinks(context, registrations);
        LookForSelfLinks(context, registrations);
        LookForLinks(context, registrations);

        foreach (var (_, metadata) in registrations.OrderBy(static r => r.Position))
        {
            foreach (var item in metadata)
            {
                context.AddMetadata(item);
            }
        }

        context.AddRenderStep(codeWriter => RenderDescriptor(
            codeWriter,
            context.GetMetadata<UrlRegistration>(),
            context.GetMetadata<LinkRegistration>(),
            context.GetMetadata<RootLinkRegistration>()));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Every HTTP-exposed operation's result is handed to the runtime link population, whatever its declared result, as
    /// legacy's <c>LinkGeneratorMiddlewareBuilder</c> matched every operation (Signature365 <c>8b40ecc961</c>,
    /// <c>LinkGeneratorMiddlewareBuilder.cs:17-20</c>) and its <c>LinkGeneratorHandler</c> decided at run time what to link:
    /// a returned resource, a resource event's data whatever the event's subclass, and every item of a collection. Every
    /// link, <c>self</c> included, is made there, by the registered <c>IResourceLinkGenerator</c>s, as legacy made it: absolute
    /// under the request's base URI, its placeholders escaped, and shown only when the user may see it
    /// (<c>EntityOperationResourceLinkGenerator.cs:59-108</c>). The generated pipeline no longer adds a <c>self</c> link of its
    /// own, which was relative, unescaped and never authorised.
    /// </remarks>
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        var handler = context.Handler;
        if (handler is null)
        {
            return;
        }

        if (ResolveLinkableResourceType(context, handler) is { } resourceType)
        {
            ReportAmbiguousSelfLinks(context, resourceType);
        }

        if (!HttpHostExposureHelper.HasHttpHostExposure(context.Operation.TypeSymbol))
        {
            return;
        }

        // Links are added last, after every post-execution step and so after the resource-event audit, just before the
        // return: legacy's LinkGeneratorMiddlewareBuilder ran after its ResourceEventHandlerMiddlewareBuilder (Signature365
        // 8b40ecc961, AdminPortalCloseTenantCommandExecutorPipeline.cs:140-149), so the data Signature365 audited carried
        // only the links its handler, or its self query's own pipeline, had given it.
        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            PipelineStage.Cleanup);

        context.RequireAsync();
        context.AddRenderStep(PipelineStage.Cleanup, RenderFullHateoasLinkPopulation);
    }

    /// <summary>
    /// Warns when multiple distinct URL templates target the same resource type with <c>Rel="self"</c> (custom-Rel links
    /// like <c>updateFields</c> are action links and don't conflict).
    /// </summary>
    private static void ReportAmbiguousSelfLinks(PipelineContributorContext context, INamedTypeSymbol resourceType)
    {
        var distinctSelfUrlSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in context.FindSelfLinksForResourceType(resourceType))
        {
            if (string.Equals(link.Rel, "self", StringComparison.Ordinal))
            {
                distinctSelfUrlSet.Add(link.UrlTemplate);
            }
        }

        if (distinctSelfUrlSet.Count > 1)
        {
            context.AddDiagnostic(new DiagnosticInfo(
                _ambiguousSelfLinkError,
                context.Operation.TypeSymbol.Locations.FirstOrDefault(),
                new EquatableList<string>([resourceType.Name, string.Join(", ", distinctSelfUrlSet.Select(static u => $"'{u}'"))])));
        }
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
        IEnumerable<LinkRegistration> linkRegistrations,
        IEnumerable<RootLinkRegistration> rootLinkRegistrations)
    {
        var seenUrls = new HashSet<string>(StringComparer.Ordinal);

        foreach (var r in urlRegistrations)
        {
            if (seenUrls.Add(r.Url))
            {
                codeWriter.AppendLine($"this.AddUrl(new global::Blueprint.Model.ApiOperationUrl(\"{r.Url}\"));");
            }
        }

        var linkIndex = 0;

        foreach (var r in linkRegistrations)
        {
            var resourceTypeFqn = r.ResourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (r.PlaceholderMappings.Count > 0)
            {
                // Generate a link with a compiled URL builder that uses direct property access
                var urlBuilder = BuildCompiledUrlBuilderLambda(r.Url, r.PlaceholderMappings, resourceTypeFqn);
                var varName = $"link_{linkIndex}";

                codeWriter.AppendLine($"var {varName} = new global::Blueprint.Model.ApiOperationLink(this, \"{r.Url}\", \"{r.Url}\", \"{r.Rel}\", typeof({resourceTypeFqn}));");
                codeWriter.AppendLine($"{varName}.CompiledUrlBuilder = {urlBuilder};");
                codeWriter.AppendLine($"this.AddLink({varName});");
            }
            else
            {
                codeWriter.AppendLine($"this.AddLink(new global::Blueprint.Model.ApiOperationLink(this, \"{r.Url}\", \"{r.Url}\", \"{r.Rel}\", typeof({resourceTypeFqn})));");
            }

            linkIndex++;
        }

        RenderRootLinks(codeWriter, rootLinkRegistrations);
    }

    /// <summary>
    /// Records the operation's root links as <c>OperationRootLinksFeatureData</c>, which the app's root-metadata document
    /// lists. They stay out of <c>this.AddLink</c>, whose links are indexed by resource type.
    /// </summary>
    private static void RenderRootLinks(ICodeWriter codeWriter, IEnumerable<RootLinkRegistration> rootLinkRegistrations)
    {
        var rootLinks = rootLinkRegistrations
            .Select(static r => $"new global::Blueprint.Model.OperationRootLink({CSharpSymbolDisplay.FormatLiteral(r.Rel, quote: true)}, {CSharpSymbolDisplay.FormatLiteral(r.Url, quote: true)})")
            .ToList();

        if (rootLinks.Count == 0)
        {
            return;
        }

        codeWriter.AppendLine(
            "this.SetFeatureData(new global::Blueprint.Model.OperationRootLinksFeatureData(global::System.Collections.Immutable.ImmutableArray.Create("
            + string.Join(", ", rootLinks)
            + ")));");
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
        // Linear search is faster than Dictionary for small collections (typically 1-3 mappings)
        static LinkPlaceholderMapping? FindMapping(EquatableList<LinkPlaceholderMapping> m, string routeName)
        {
            for (var i = 0; i < m.Count; i++)
            {
                if (string.Equals(m[i].RouteName, routeName, StringComparison.Ordinal))
                {
                    return m[i];
                }
            }

            return null;
        }

        // Track whether the single placeholder uses catch-all (no escaping) semantics
        var isSinglePlaceholderCatchAll = false;

        var interpolated = _mappedPlaceholderRegex.Replace(
            urlFormat,
            match =>
            {
                var routeName = match.Groups["routeName"].Value;
                var mapping = FindMapping(mappings, routeName);
                if (mapping is null)
                {
                    return match.Value;
                }

                string propertyValueExpression;

                if (mapping.FallbackPropertyName is not null)
                {
                    // Fallback: try primary, if null try fallback
                    var primaryExpr = mapping.IsValueType
                        ? $"r.{mapping.ResourcePropertyName}.ToString()"
                        : $"r.{mapping.ResourcePropertyName}?.ToString()";

                    var fallbackExpr = mapping.IsFallbackValueType
                        ? $"r.{mapping.FallbackPropertyName}.ToString()"
                        : $"r.{mapping.FallbackPropertyName}?.ToString()";

                    propertyValueExpression = $"{primaryExpr} ?? {fallbackExpr} ?? \"\"";
                }
                else
                {
                    // Simple: single property
                    propertyValueExpression = mapping.IsValueType
                        ? $"r.{mapping.ResourcePropertyName}.ToString()"
                        : $"r.{mapping.ResourcePropertyName}?.ToString() ?? \"\"";
                }

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
        // When a fallback is present, only null-guard if BOTH primary and fallback are null.
        string? nullChecks = null;
        for (var i = 0; i < mappings.Count; i++)
        {
            var m = mappings[i];
            if (m.FallbackPropertyName is not null)
            {
                // With fallback: only null if both are null (and both are ref types)
                if (!m.IsValueType && !m.IsFallbackValueType)
                {
                    var check = $"(r.{m.ResourcePropertyName} == null && r.{m.FallbackPropertyName} == null)";
                    nullChecks = nullChecks is null ? check : $"{nullChecks} || {check}";
                }

                // If either side is a value type, we always have a value — no null guard needed
            }
            else if (!m.IsValueType)
            {
                var check = $"r.{m.ResourcePropertyName} == null";
                nullChecks = nullChecks is null ? check : $"{nullChecks} || {check}";
            }
        }

        // Build the resource-typed case
        string resourceCase;

        if (nullChecks is not null)
        {
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
                    return FindMapping(mappings, routeName) is not null
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
                    return FindMapping(mappings, routeName) is not null ? "{g}" : match.Value;
                });

            return $"static (obj) => obj switch {{ {resourceCase}, string id => $\"{scalarInterpolated}\", global::System.Guid g => $\"{guidInterpolated}\", _ => null }}";
        }

        return $"static (obj) => obj switch {{ {resourceCase}, _ => null }}";
    }

    /// <summary>
    /// Registers every <c>[RootLink]</c> on the operation, each with its own <c>Rel</c>, as legacy's
    /// <c>OperationScanner</c> made a link of every link attribute (Signature365 <c>8b40ecc961</c>,
    /// <c>Blueprint/Configuration/OperationScanner.cs:294-298</c>; <c>RootLinkAttribute</c> is <c>AllowMultiple</c>).
    /// </summary>
    private static void LookForRootLinks(DescriptorContributorContext context, List<(int Position, object[] Metadata)> registrations)
    {
        var operationType = context.GetOperationTypeSymbol();

        // ParseAll keeps the attributes' order and parses every RootLinkAttribute (its one constructor argument always
        // matches), so the raw attribute at the same index is the one whose named Rel applies.
        var rootLinkAttributes = RootLinkAttributeDescriptor.Instance.ParseAll(operationType).ToList();
        var rawRootLinkAttributes = operationType.GetAttributes()
            .Where(static a => a.AttributeClass?.Name == "RootLinkAttribute")
            .ToList();
        HashSet<string>? memberNames = null;

        for (var index = 0; index < rootLinkAttributes.Count; index++)
        {
            var (_, locationInfo, url) = rootLinkAttributes[index].GetConstructorArgument(RootLinkAttributeDescriptor.UrlArgument);

            if (string.IsNullOrWhiteSpace(url))
            {
                context.AddDiagnostic(
                    new DiagnosticInfo(
                        _emptyRouteTemplateError,
                        locationInfo,
                        [operationType.Name]));

                continue;
            }

            if (url.Contains("{"))
            {
                var placeholders = _placeholderRegex.Matches(url);

                foreach (var placeholder in placeholders.Cast<Match>())
                {
                    var placeholderName = placeholder.Value.Trim('{', '}', '*');
                    memberNames ??= new HashSet<string>(operationType.GetAllMembers().Select(static m => m.Name), StringComparer.Ordinal);

                    if (!memberNames.Contains(placeholderName))
                    {
                        var argumentLocationInfo = locationInfo.WithSubRange(placeholder.Index + 2, placeholder.Length - 2);
                        context.AddDiagnostic(CreatePlaceholderMissingDiagnostic(operationType, placeholderName, argumentLocationInfo));
                    }
                }
            }

            var rel = index < rawRootLinkAttributes.Count
                ? rawRootLinkAttributes[index].NamedArguments.FirstOrDefault(static a => a.Key == "Rel").Value.Value as string
                : null;

            registrations.Add((
                DeclarationPosition(operationType, index < rawRootLinkAttributes.Count ? rawRootLinkAttributes[index] : null),
                [
                    new UrlRegistration(url, locationInfo),
                    HostExposureRegistration.HttpAt(locationInfo),
                    CreateRootLinkRegistration(operationType, rel, url),
                ]));
        }
    }

    /// <summary>
    /// Records a root link under the key legacy Blueprint's root-metadata document gave it (<see cref="GetDefaultRel"/>).
    /// </summary>
    private static RootLinkRegistration CreateRootLinkRegistration(INamedTypeSymbol operationType, string? rel, string url)
    {
        return new RootLinkRegistration(rel ?? GetDefaultRel(operationType), url);
    }

    /// <summary>
    /// Gives the key of a link whose attribute sets no <c>Rel</c>, root link or resource link alike, as legacy Blueprint
    /// gave it: <c>linkAttribute.Rel ?? descriptor.Name</c>, taken when the link was built and so before an app's
    /// conventions renamed the descriptor (Signature365 <c>8b40ecc961</c>,
    /// <c>Blueprint/Configuration/OperationScanner.cs:294-303</c>), with the name legacy gave the operation
    /// (<see cref="OperationNameHelpers.GetDescriptorName"/>). So an app's <c>[BlueprintAppOperationNames(Remove = ...)]</c>
    /// renames the operation but not this key.
    /// </summary>
    /// <remarks>
    /// The key is a resource's <c>$links</c> property and its <c>x-links</c> <c>rel</c>, which a consumer's client reads
    /// (Signature365's Portal calls <c>DeleteGroupCommand</c> through the <c>DeleteGroup</c> link). Only a missing
    /// <c>Rel</c> takes it: an empty one is kept as written, as legacy's <c>??</c> kept it.
    /// </remarks>
    private static string GetDefaultRel(INamedTypeSymbol operationType)
    {
        return OperationNameHelpers.GetDescriptorName(operationType.Name);
    }

    private static void LookForSelfLinks(DescriptorContributorContext context, List<(int Position, object[] Metadata)> registrations)
    {
        var operationType = context.GetOperationTypeSymbol();
        var selfLinkAttributes = SelfLinkAttributeDescriptor.Instance.ParseAll(operationType);

        // We also need access to the raw AttributeData to read named arguments (e.g. Rel)
        var rawSelfLinkAttributes = operationType.GetAttributes()
            .Where(static a => a.AttributeClass?.Name == "SelfLinkAttribute")
            .ToList();

        var parsedIndex = 0;

        foreach (var selfLinkAttribute in selfLinkAttributes)
        {
            var (_, urlLocation, url) = selfLinkAttribute.GetConstructorArgument(SelfLinkAttributeDescriptor.UrlArgument);
            var (_, resourceLocation, resourceType) = selfLinkAttribute.GetConstructorArgument(SelfLinkAttributeDescriptor.ResourceTypeArgument);

            // Resolve the effective rel: default is "self", but can be overridden via Rel = "..." on the attribute
            var effectiveRel = "self";
            var rawAttr = parsedIndex < rawSelfLinkAttributes.Count ? rawSelfLinkAttributes[parsedIndex] : null;

            if (rawAttr is not null)
            {

                foreach (var namedArg in rawAttr.NamedArguments)
                {
                    if (namedArg.Key == "Rel" && namedArg.Value.Value is string namedRel && !string.IsNullOrWhiteSpace(namedRel))
                    {
                        effectiveRel = namedRel;
                    }
                }
            }

            parsedIndex++;

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

            // Validate placeholders: route param name must exist on operation, and at least one
            // of (primary, fallback) must exist on resource for link URL generation.
            if (normalisedUrl.Contains("{"))
            {
                normalisedUrl = _mappedPlaceholderRegex.Replace(
                    normalisedUrl,
                    match =>
                    {
                        var routePropertyName = match.Groups["routeName"].Value;
                        var fallbackPropertyName = match.Groups["resourcePropName"]?.Value;

                        var argumentLocationInfo = urlLocation.WithSubRange(
                            match.Index + 2,
                            match.Length - 2);

                        // Route param must exist on the operation (for incoming request binding)
                        if (TryFindMember(
                                operationType,
                                routePropertyName,
                                argumentLocationInfo,
                                context))
                        {
                            if (string.IsNullOrEmpty(fallbackPropertyName))
                            {
                                // Simple {Prop}: must exist on resource
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
                                // Fallback {Prop:Alt}: try primary on resource, then fallback
                                var primaryExists = HasMember(resourceTypeSymbol, routePropertyName);
                                var fallbackExists = HasMember(resourceTypeSymbol, fallbackPropertyName!);

                                if (!primaryExists && !fallbackExists)
                                {
                                    // Neither exists — error on both
                                    context.AddDiagnostic(CreatePlaceholderMissingDiagnostic(
                                        resourceTypeSymbol, routePropertyName, argumentLocationInfo));
                                }
                                else
                                {
                                    // At least one exists. Use the first that exists as primary.
                                    var effectivePrimary = primaryExists ? routePropertyName : fallbackPropertyName!;
                                    var effectiveFallback = primaryExists && fallbackExists ? fallbackPropertyName : null;

                                    var primaryIsValue = IsResourcePropertyValueType(resourceTypeSymbol, effectivePrimary);
                                    var fallbackIsValue = effectiveFallback != null && IsResourcePropertyValueType(resourceTypeSymbol, effectiveFallback);

                                    placeholderMappings.Add(new LinkPlaceholderMapping(
                                        routePropertyName, effectivePrimary, primaryIsValue, effectiveFallback, fallbackIsValue));
                                }
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
            var linkRegistration = new LinkRegistration(normalisedUrl, effectiveRel, resourceTypeSymbol, urlLocation, new EquatableList<LinkPlaceholderMapping>(placeholderMappings.ToArray()));

            registrations.Add((
                DeclarationPosition(operationType, rawAttr),
                [urlRegistration, linkRegistration, HostExposureRegistration.HttpAt(urlLocation)]));
        }
    }

    /// <summary>
    /// Discovers <c>[Link(typeof(X), "/path", Rel = "relName")]</c> attributes from
    /// the runtime model <c>LinkAttribute</c> base class.
    /// These represent non-self HATEOAS links that also register an HTTP endpoint.
    /// </summary>
    private static void LookForLinks(DescriptorContributorContext context, List<(int Position, object[] Metadata)> registrations)
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
                !attrClass.IsInNamespace("Blueprint", "Model", "Attributes"))
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
                    HashSet<string>? memberNames = null;

                    foreach (var placeholder in placeholders.Cast<Match>())
                    {
                        var placeholderName = placeholder.Value.Trim('{', '}', '*');
                        memberNames ??= new HashSet<string>(operationType.GetAllMembers().Select(static m => m.Name), StringComparer.Ordinal);

                        if (!memberNames.Contains(placeholderName))
                        {
                            context.AddDiagnostic(CreatePlaceholderMissingDiagnostic(
                                operationType,
                                placeholderName,
                                locationInfo?.WithSubRange(placeholder.Index + 2, placeholder.Length - 2)));
                        }
                    }
                }

                // A link without a resource type was a root link (Blueprint/ApiDataModel.cs:212-215, ApiOperationLink.cs:176).
                registrations.Add((
                    DeclarationPosition(operationType, attribute),
                    [
                        new UrlRegistration(url!, locationInfo),
                        HostExposureRegistration.HttpAt(locationInfo),
                        CreateRootLinkRegistration(operationType, rel, url!),
                    ]));

                continue;
            }

            if (resourceTypeArg.Value is not INamedTypeSymbol resourceTypeSymbol || resourceTypeSymbol.TypeKind == TypeKind.Error)
            {
                continue;
            }

            rel ??= GetDefaultRel(operationType);

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
                var opMemberNames = BuildMemberNameSet(operationType);
                var resMemberCache = BuildMemberCache(resourceTypeSymbol);
                var resMemberNames = new HashSet<string>(resMemberCache.Keys, StringComparer.Ordinal);

                normalisedUrl = _mappedPlaceholderRegex.Replace(
                    normalisedUrl,
                    match =>
                    {
                        var routePropertyName = match.Groups["routeName"].Value;
                        var fallbackPropertyName = match.Groups["resourcePropName"]?.Value;

                        var argumentLocationInfo = locationInfo?.WithSubRange(
                            match.Index + 2,
                            match.Length - 2);

                        if (TryFindMember(operationType, routePropertyName, opMemberNames, argumentLocationInfo, context))
                        {
                            if (string.IsNullOrEmpty(fallbackPropertyName))
                            {
                                TryFindMember(resourceTypeSymbol, routePropertyName, resMemberNames, argumentLocationInfo, context);

                                var isValueType = IsResourcePropertyValueType(resMemberCache, routePropertyName);
                                placeholderMappings.Add(new LinkPlaceholderMapping(routePropertyName, routePropertyName, isValueType));
                            }
                            else
                            {
                                // Fallback semantics: try primary (route name) on resource, then fallback
                                var primaryExists = resMemberNames.Contains(routePropertyName);
                                var fallbackExists = resMemberNames.Contains(fallbackPropertyName!);

                                if (!primaryExists && !fallbackExists)
                                {
                                    context.AddDiagnostic(CreatePlaceholderMissingDiagnostic(
                                        resourceTypeSymbol, routePropertyName, argumentLocationInfo));
                                }
                                else
                                {
                                    var effectivePrimary = primaryExists ? routePropertyName : fallbackPropertyName!;
                                    var effectiveFallback = primaryExists && fallbackExists ? fallbackPropertyName : null;

                                    var primaryIsValue = IsResourcePropertyValueType(resMemberCache, effectivePrimary);
                                    var fallbackIsValue = effectiveFallback != null && IsResourcePropertyValueType(resMemberCache, effectiveFallback);

                                    placeholderMappings.Add(new LinkPlaceholderMapping(
                                        routePropertyName, effectivePrimary, primaryIsValue, effectiveFallback, fallbackIsValue));
                                }
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

            registrations.Add((
                DeclarationPosition(operationType, attribute),
                [urlRegistration, linkRegistration, HostExposureRegistration.HttpAt(locationInfo)]));
        }
    }

    /// <summary>
    /// Gives the position of a link attribute among the operation's attributes, so that what the three kinds of link
    /// attribute register can be put back in declaration order. An attribute that cannot be found goes last.
    /// </summary>
    private static int DeclarationPosition(INamedTypeSymbol operationType, AttributeData? attribute)
    {
        if (attribute is null)
        {
            return int.MaxValue;
        }

        var position = operationType.GetAttributes().IndexOf(attribute);

        return position < 0 ? int.MaxValue : position;
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
    /// Checks whether a member with the given name exists on a type without reporting diagnostics.
    /// Used for fallback validation where at least one of multiple candidates must exist.
    /// </summary>
    private static bool HasMember(INamedTypeSymbol typeSymbol, string memberName)
    {
        return typeSymbol.GetAllMembers().Any(m => m.Name == memberName);
    }

    /// <summary>
    /// Overload that uses a pre-built member name set to avoid repeated <see cref="RoslynExtensions.GetAllMembers"/>
    /// walks when validating multiple placeholders against the same type.
    /// </summary>
    private static bool TryFindMember(ITypeSymbol typeSymbol, string placeholderName, HashSet<string> memberNameCache, LocationInfo? argumentLocationInfo, DescriptorContributorContext context)
    {
        if (memberNameCache.Contains(placeholderName))
        {
            return true;
        }

        context.AddDiagnostic(
            CreatePlaceholderMissingDiagnostic(
                typeSymbol,
                placeholderName,
                argumentLocationInfo));

        return false;
    }

    /// <summary>
    /// Checks whether a named property on a resource type is a non-nullable value type, which
    /// affects the generated URL builder code (value types cannot use <c>?.</c> or be compared to null).
    /// </summary>
    private static bool IsResourcePropertyValueType(INamedTypeSymbol resourceType, string propertyName)
    {
        var member = resourceType.GetAllMembers().FirstOrDefault(m => m.Name == propertyName);

        return member is IPropertySymbol property
               && property.Type.IsValueType
               && property.Type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
               && property.Type.NullableAnnotation != NullableAnnotation.Annotated;
    }

    /// <summary>
    /// Overload that uses a pre-built member dictionary to avoid repeated <see cref="RoslynExtensions.GetAllMembers"/>
    /// walks when checking multiple properties on the same resource type.
    /// </summary>
    private static bool IsResourcePropertyValueType(Dictionary<string, ISymbol> memberCache, string propertyName)
    {
        if (memberCache.TryGetValue(propertyName, out var member) && member is IPropertySymbol property)
        {
            return property.Type.IsValueType
                   && property.Type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
                   && property.Type.NullableAnnotation != NullableAnnotation.Annotated;
        }

        return false;
    }

    /// <summary>
    /// Builds a name-to-symbol dictionary for all members of a type, including inherited members.
    /// Used to cache member lookups when multiple placeholders reference the same type.
    /// </summary>
    private static Dictionary<string, ISymbol> BuildMemberCache(ITypeSymbol type)
    {
        var cache = new Dictionary<string, ISymbol>(StringComparer.Ordinal);

        foreach (var member in type.GetAllMembers())
        {
            if (!cache.ContainsKey(member.Name))
            {
                cache[member.Name] = member;
            }
        }

        return cache;
    }

    /// <summary>
    /// Builds a name set for all members of a type, for fast existence checks.
    /// </summary>
    private static HashSet<string> BuildMemberNameSet(ITypeSymbol type)
    {
        return new HashSet<string>(type.GetAllMembers().Select(static m => m.Name), StringComparer.Ordinal);
    }

    private static DiagnosticInfo CreatePlaceholderMissingDiagnostic(ITypeSymbol namedType, string placeholderName, LocationInfo? argumentLocationInfo)
    {
        return new DiagnosticInfo(
            _missingPlaceholderError,
            argumentLocationInfo,
            [placeholderName, namedType.Name]);
    }

    /// <summary>
    /// Finds the linkable resource type the operation declares it returns, for the ambiguous self link diagnostic.
    /// </summary>
    private static INamedTypeSymbol? ResolveLinkableResourceType(PipelineContributorContext context, OperationHandlerModel handler)
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

        return resourceType.AllInterfaces.Contains(linkableResourceInterface, SymbolEqualityComparer.Default)
            ? resourceType
            : null;
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

        // DeferredResourceEventResult<T> → T (content resolved at pipeline time via SelfQuery)
        var deferredResourceEventResultGeneric = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.DeferredResourceEventResult`1");
        if (deferredResourceEventResultGeneric is not null
            && declaredResultType.IsGenericType
            && SymbolEqualityComparer.Default.Equals(declaredResultType.OriginalDefinition, deferredResourceEventResultGeneric))
        {
            return declaredResultType.TypeArguments[0] as INamedTypeSymbol;
        }

        // Legacy ResourceEvent<T> → T (Blueprint.Http.ResourceEvent<T> carries Data as the resource body)
        var legacyResourceEventGeneric = context.FindTypeByMetadataName("Blueprint.Http.ResourceEvent`1");
        if (legacyResourceEventGeneric is not null
            && declaredResultType.IsGenericType
            && SymbolEqualityComparer.Default.Equals(declaredResultType.OriginalDefinition, legacyResourceEventGeneric))
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

        var deferredResourceEventResultGeneric = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.DeferredResourceEventResult`1");
        if (deferredResourceEventResultGeneric is not null
            && member.IsGenericType
            && SymbolEqualityComparer.Default.Equals(member.OriginalDefinition, deferredResourceEventResultGeneric))
        {
            return member.TypeArguments[0] as INamedTypeSymbol;
        }

        var legacyResourceEventGeneric = context.FindTypeByMetadataName("Blueprint.Http.ResourceEvent`1");
        if (legacyResourceEventGeneric is not null
            && member.IsGenericType
            && SymbolEqualityComparer.Default.Equals(member.OriginalDefinition, legacyResourceEventGeneric))
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
        context.WriteStatement("await PopulateHateoasLinksAsync(context.ServiceProvider, context, handlerResult).ConfigureAwait(false);");
    }
}
