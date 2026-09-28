using System.Text.RegularExpressions;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Determines compile-time property ownership (route, body, query, header, cookie, system) and generates
/// the Population pipeline stage that binds HTTP request data onto operation properties.
/// </summary>
public sealed class PropertyOwnershipModule : IBlueprintSourceGeneratorModule, IDescriptorContributor, IPipelineContributor
{
    private static readonly Regex PlaceholderRegex = RoutePlaceholderSyntax.Placeholder;

    private sealed record RouteTemplateInfo(string Template, LocationInfo? Location);

    /// <inheritdoc />
    public string ModuleId => nameof(PropertyOwnershipModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(PropertyOwnershipModule);

    /// <inheritdoc />
    public int Order => 70;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        if (!HttpHostExposureHelper.HasHttpHostExposure(operationType))
        {
            return;
        }

        var routeTemplates = GetRouteTemplates(operationType);
        var routePlaceholders = ExtractRoutePlaceholders(routeTemplates);
        ValidateRoutePlaceholderSyntax(operationType, routeTemplates, context);
        ValidateRoutePlaceholdersResolveToProperties(operationType, routeTemplates, context);
        var httpMethods = ResolveHttpMethods(context, operationType);

        var diagnostics = new List<DiagnosticInfo>();
        var entries = AnalyzePropertyOwnership(operationType, routePlaceholders, httpMethods, diagnostics);

        foreach (var diagnostic in diagnostics)
        {
            context.AddDiagnostic(diagnostic);
        }

        if (diagnostics.Any(static d => d.Descriptor.DefaultSeverity == DiagnosticSeverity.Error))
        {
            return;
        }

        if (entries.Count == 0)
        {
            return;
        }

        var hasBodyBinding = entries.Any(static e => e.Source == PropertyOwnershipSource.Body);
        var hasHeaderOrCookieBinding = entries.Any(static e =>
            e.Source is PropertyOwnershipSource.Header or PropertyOwnershipSource.Cookie);

        context.AddMetadata(new BindingDescriptorRegistration(hasBodyBinding, hasHeaderOrCookieBinding));
        context.AddMetadata(new PropertyBindingEntriesMetadata(
            entries.Select(static e => new PropertyBindingEntriesMetadata.Entry(e.PropertyName, e.Source, e.BindingName)).ToList()));
        context.AddRenderStep(writer => RenderDescriptorFeatureData(writer, entries));
    }

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        var operationType = context.Operation.TypeSymbol;

        if (!HttpHostExposureHelper.HasHttpHostExposure(operationType))
        {
            return;
        }

        var routePlaceholders = ExtractRoutePlaceholders(GetRouteTemplates(operationType));
        var httpMethods = ResolveHttpMethods(operationType);
        var entries = AnalyzePropertyOwnership(operationType, routePlaceholders, httpMethods, diagnostics: null);

        if (entries.Count == 0)
        {
            return;
        }

        var hasBodyProperties = entries.Any(static e => e.Source == PropertyOwnershipSource.Body);
        var hasNonStringNonBodyProperties = entries.Any(static e =>
            e.Source is PropertyOwnershipSource.Query or PropertyOwnershipSource.Header or PropertyOwnershipSource.Cookie
            && !IsStringType(e.TypeSymbol));

        // Body reading requires async; typed non-body values with TryParse + early return also need async
        if (hasBodyProperties || hasNonStringNonBodyProperties)
        {
            context.RequireAsync();
        }

        // Cache TypeConverter instances as static readonly fields to avoid per-request
        // TypeDescriptor.GetConverter() lookup (~50-500ns per call depending on cache state).
        var converterFieldMap = EmitCachedTypeConverterFields(context, entries);

        context.AddRenderStep(PipelineStage.Population, renderContext => RenderPopulation(renderContext, entries, converterFieldMap));

        // Single-pass partition: collect claim-mapped, init-only, and body entries in one traversal
        // instead of three separate Where().ToList/ToArray() scans.
        List<PropertyOwnershipEntry>? claimMappedEntries = null;
        List<PropertyOwnershipEntry>? initEntries = null;
        List<PropertyOwnershipEntry>? bodyEntries = null;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Source == PropertyOwnershipSource.AuthContext && entry.ClaimType is not null)
            {
                (claimMappedEntries ??= []).Add(entry);
            }

            if (entry.IsInitOnly)
            {
                (initEntries ??= []).Add(entry);
            }

            if (entry.Source == PropertyOwnershipSource.Body)
            {
                (bodyEntries ??= []).Add(entry);
            }
        }

        // Compile-time claim mapping: when [FromAuthContext(ClaimType = "...")] is specified,
        // emit inline claim reading in the Authentication stage (after identity check).
        if (claimMappedEntries is not null)
        {
            context.AddRenderStep(PipelineStage.Authentication, renderContext => RenderClaimMappings(renderContext, claimMappedEntries));
        }

        // Generate [UnsafeAccessor] setter methods for init-only properties so the pipeline can
        // assign route/query/header/cookie/body values after construction.
        if (initEntries is not null)
        {
            var operationTypeFull = operationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            context.AddDeferredClassMember(pipelineClass =>
                WriteUnsafeAccessorSetters(pipelineClass, operationTypeFull, initEntries));
        }

        if (bodyEntries is not null)
        {
            var operationTypeFull = operationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            context.AddDeferredClassMember(pipelineClass =>
                WritePopulateFromJsonBodyMethod(pipelineClass, operationTypeFull, bodyEntries));
        }
    }

    private static List<string> ExtractRoutePlaceholders(IEnumerable<RouteTemplateInfo> routeTemplates)
    {
        var placeholders = new List<string>();

        foreach (var routeTemplate in routeTemplates)
        {
            foreach (Match match in PlaceholderRegex.Matches(routeTemplate.Template))
            {
                var name = match.Groups[1].Value;
                if (!placeholders.Contains(name, StringComparer.Ordinal))
                {
                    placeholders.Add(name);
                }
            }
        }

        return placeholders;
    }

    /// <summary>
    /// Validates route template syntax for unbalanced braces and empty placeholder names,
    /// reporting BPPO008 for each malformed segment.
    /// </summary>
    private static void ValidateRoutePlaceholderSyntax(
        INamedTypeSymbol operationType,
        IEnumerable<RouteTemplateInfo> routeTemplates,
        DescriptorContributorContext context)
    {
        foreach (var routeTemplate in routeTemplates)
        {
            var url = routeTemplate.Template;
            var openCount = 0;

            for (var i = 0; i < url.Length; i++)
            {
                switch (url[i])
                {
                    case '{':
                        openCount++;
                        if (openCount > 1)
                        {
                            context.AddDiagnostic(CreateRouteDiagnostic(operationType, url, "nested or consecutive opening braces", routeTemplate.Location));
                            return;
                        }

                        break;

                    case '}':
                        if (openCount == 0)
                        {
                            context.AddDiagnostic(CreateRouteDiagnostic(operationType, url, "closing brace without matching opening brace", routeTemplate.Location));
                            return;
                        }

                        openCount--;
                        break;
                }
            }

            if (openCount > 0)
            {
                context.AddDiagnostic(CreateRouteDiagnostic(operationType, url, "opening brace without matching closing brace", routeTemplate.Location));
            }

            // Check for empty placeholders: "{}" with nothing inside.
            if (url.Contains("{}", StringComparison.Ordinal))
            {
                context.AddDiagnostic(CreateRouteDiagnostic(operationType, url, "empty placeholder name", routeTemplate.Location));
            }
        }
    }

    /// <summary>
    /// Reports unresolved placeholders as soon as the route template and operation symbol disagree,
    /// preventing migrations from shipping a route segment that can never be populated at runtime.
    /// </summary>
    private static void ValidateRoutePlaceholdersResolveToProperties(
        INamedTypeSymbol operationType,
        IEnumerable<RouteTemplateInfo> routeTemplates,
        DescriptorContributorContext context)
    {
        var propertyNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in operationType.GetAllMembers())
        {
            if (member is IPropertySymbol prop)
            {
                propertyNames.Add(prop.Name);
            }
        }

        foreach (var routeTemplate in routeTemplates)
        {
            foreach (Match match in PlaceholderRegex.Matches(routeTemplate.Template))
            {
                var placeholderName = match.Groups[1].Value;
                if (propertyNames.Contains(placeholderName))
                {
                    continue;
                }

                context.AddDiagnostic(new DiagnosticInfo(
                    PropertyOwnershipDiagnosticDescriptors.UnresolvedRoutePlaceholder,
                    routeTemplate.Location ?? GetOperationLocation(operationType),
                    [placeholderName, operationType.Name, routeTemplate.Template]));
            }
        }
    }

    private static List<RouteTemplateInfo> GetRouteTemplates(INamedTypeSymbol operationType)
    {
        var routeTemplates = new List<RouteTemplateInfo>();

        foreach (var attribute in operationType.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null || !attributeClass.IsInNamespace("Blueprint", "Model", "Attributes"))
            {
                continue;
            }

            var (template, argumentIndex) = attributeClass.Name switch
            {
                "RootLinkAttribute" when attribute.ConstructorArguments.Length >= 1 => (attribute.ConstructorArguments[0].Value as string, 0),
                "SelfLinkAttribute" when attribute.ConstructorArguments.Length >= 2 => (attribute.ConstructorArguments[1].Value as string, 1),
                "LinkAttribute" when attribute.ConstructorArguments.Length >= 2 => (attribute.ConstructorArguments[1].Value as string, 1),
                _ => (null, -1),
            };

            if (string.IsNullOrWhiteSpace(template))
            {
                continue;
            }

            routeTemplates.Add(new RouteTemplateInfo(template!, GetRouteTemplateLocation(attribute, argumentIndex)));
        }

        return routeTemplates;
    }

    private static LocationInfo? GetRouteTemplateLocation(AttributeData attribute, int argumentIndex)
    {
        if (argumentIndex < 0 || attribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax attributeSyntax)
        {
            return null;
        }

        if (attributeSyntax.ArgumentList is { Arguments.Count: > 0 } argumentList
            && argumentIndex < argumentList.Arguments.Count)
        {
            return LocationInfo.CreateFrom(argumentList.Arguments[argumentIndex].Expression);
        }

        return LocationInfo.CreateFrom(attributeSyntax);
    }

    private static string[] ResolveHttpMethods(DescriptorContributorContext context, INamedTypeSymbol operationType)
    {
        var methods = context.GetMetadata<OperationHttpMethodMetadataRegistration>()
            .Select(static r => r.HttpMethod)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (methods.Length > 0)
        {
            return methods;
        }

        // Fallback to convention
        return IsCommand(operationType) ? ["POST"] : ["GET"];
    }

    private static string[] ResolveHttpMethods(INamedTypeSymbol operationType)
    {
        // Re-derive from attributes in pipeline context (no descriptor metadata available)
        var explicitMethods = new List<string>();

        foreach (var attribute in operationType.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;

            switch (name)
            {
                case "HttpGetAttribute":
                    explicitMethods.Add("GET");
                    break;
                case "HttpPostAttribute":
                    explicitMethods.Add("POST");
                    break;
                case "HttpPutAttribute":
                    explicitMethods.Add("PUT");
                    break;
                case "HttpPatchAttribute":
                    explicitMethods.Add("PATCH");
                    break;
                case "HttpDeleteAttribute":
                    explicitMethods.Add("DELETE");
                    break;
                case "HttpMethodAttribute" when attribute.ConstructorArguments.Length > 0:
                    if (attribute.ConstructorArguments[0].Value is string method)
                    {
                        explicitMethods.Add(method.ToUpperInvariant());
                    }

                    break;
            }
        }

        if (explicitMethods.Count > 0)
        {
            // Deduplicate without LINQ Distinct() iterator allocation
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduped = new List<string>(explicitMethods.Count);
            foreach (var m in explicitMethods)
            {
                if (seen.Add(m))
                {
                    deduped.Add(m);
                }
            }

            return [.. deduped];
        }

        return IsCommand(operationType) ? ["POST"] : ["GET"];
    }

    private static bool IsCommand(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.IsInNamespace("Blueprint", "Model")
            && candidate.Name == "ICommand");
    }

    /// <summary>
    /// Returns true when the operation answers any method but GET, whose unowned properties therefore come from the body.
    /// </summary>
    /// <remarks>
    /// Legacy Blueprint read the body of every request but a GET (Signature365 <c>8b40ecc961</c>,
    /// <c>Blueprint.Http/MessagePopulation/HttpBodyMessagePopulationSource.cs:145-147</c>), a DELETE's included, and the
    /// query string of a GET alone (<c>BlueprintApiBuilderExtensions.cs:68-73</c>).
    /// </remarks>
    private static bool IsBodyMethod(string[] httpMethods)
    {
        return httpMethods.Any(static m => !string.Equals(m, "GET", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsQueryMethod(string[] httpMethods)
    {
        return httpMethods.Any(static m => string.Equals(m, "GET", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns true when the operation is exclusively GET — used for BPPO004 validation
    /// of explicit [FromBody], which is semantically invalid on this method.
    /// </summary>
    private static bool IsGetOnlyMethod(string[] httpMethods)
    {
        return httpMethods.Length > 0 && httpMethods.All(static m => string.Equals(m, "GET", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Analyses property ownership for an operation type, returning entries for each bindable property.
    /// When <paramref name="diagnostics"/> is non-null (descriptor path), validation errors are collected
    /// into the list. When null (pipeline path), invalid properties are silently skipped so the generator
    /// still produces output for the valid subset.
    /// </summary>
    private static List<PropertyOwnershipEntry> AnalyzePropertyOwnership(
        INamedTypeSymbol operationType,
        List<string> routePlaceholders,
        string[] httpMethods,
        List<DiagnosticInfo>? diagnostics)
    {
        var entries = new List<PropertyOwnershipEntry>();
        var isGetOnly = IsGetOnlyMethod(httpMethods);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in operationType.GetAllMembers().OfType<IPropertySymbol>())
        {
            if (member.IsStatic || member.IsReadOnly || member.SetMethod is null || member.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            if (IsSystemProperty(member))
            {
                continue;
            }

            // Skip properties already seen in a derived type (handles new/override hiding)
            if (!seenNames.Add(member.Name))
            {
                continue;
            }

            var isInitOnly = member.SetMethod.IsInitOnly;

            // Step 1 — detect explicit [FromX] attributes (namespace-qualified to avoid MVC collisions)
            var fromRoute = FindBlueprintAttribute(member, "FromRouteAttribute");
            var fromQuery = FindBlueprintAttribute(member, "FromQueryAttribute");
            var fromBody = FindBlueprintAttribute(member, "FromBodyAttribute");
            var fromHeader = FindBlueprintAttribute(member, "FromHeaderAttribute");
            var fromCookie = FindBlueprintAttribute(member, "FromCookieAttribute");
            var fromAuthContext = FindBlueprintAttribute(member, "FromAuthContextAttribute");

            var httpBindingCount = (fromRoute != null ? 1 : 0) + (fromQuery != null ? 1 : 0) + (fromBody != null ? 1 : 0) + (fromHeader != null ? 1 : 0) + (fromCookie != null ? 1 : 0);

            // [FromAuthContext] conflicts with any HTTP binding attribute except [FromRoute]
            if (fromAuthContext != null && httpBindingCount > 0 && fromRoute == null)
            {
                if (diagnostics != null)
                {
                    var conflicting = BuildConflictingAttributeList(null, fromQuery, fromBody, fromHeader, fromCookie);
                    var location = member.Locations.FirstOrDefault(static l => l.IsInSource);
                    diagnostics.Add(new DiagnosticInfo(
                        PropertyOwnershipDiagnosticDescriptors.ConflictingFromAttributes,
                        location is not null ? LocationInfo.CreateFrom(location) : null,
                        [member.Name, operationType.Name, $"[FromAuthContext], {conflicting}"]));
                }

                continue;
            }

            // Step 2 — conflict: more than one HTTP [FromX] attribute on the same property
            if (httpBindingCount > 1)
            {
                if (diagnostics != null)
                {
                    var conflicting = BuildConflictingAttributeList(fromRoute, fromQuery, fromBody, fromHeader, fromCookie);
                    var location = member.Locations.FirstOrDefault(static l => l.IsInSource);
                    diagnostics.Add(new DiagnosticInfo(
                        PropertyOwnershipDiagnosticDescriptors.ConflictingFromAttributes,
                        location is not null ? LocationInfo.CreateFrom(location) : null,
                        [member.Name, operationType.Name, conflicting]));
                }

                continue;
            }

            // Step 3 — [FromAuthContext] without route: property excluded from HTTP binding,
            // recorded as AuthContext in descriptor metadata for OpenAPI visibility.
            if (fromAuthContext != null && fromRoute == null)
            {
                // Extract optional ClaimType named argument for compile-time claim mapping.
                var claimType = fromAuthContext.NamedArguments
                    .FirstOrDefault(static a => a.Key == "ClaimType").Value.Value as string;

                // Route placeholder match takes precedence even when [FromAuthContext] is present,
                // so check convention route fallback below. If there IS no route match, own it here.
                if (!routePlaceholders.Contains(member.Name, StringComparer.Ordinal))
                {
                    // Validate: ClaimType is only supported on string properties.
                    if (claimType != null && member.Type.SpecialType != SpecialType.System_String)
                    {
                        diagnostics?.Add(new DiagnosticInfo(
                            PropertyOwnershipDiagnosticDescriptors.ClaimTypeOnNonStringProperty,
                            member.Locations.FirstOrDefault(static l => l.IsInSource),
                            [member.Name, operationType.Name, claimType, member.Type.ToDisplayString()]));

                        // Still record as AuthContext (no HTTP binding), but without ClaimType.
                        entries.Add(new PropertyOwnershipEntry(member.Name, member.Type, PropertyOwnershipSource.AuthContext, null, isInitOnly));
                        continue;
                    }

                    entries.Add(new PropertyOwnershipEntry(member.Name, member.Type, PropertyOwnershipSource.AuthContext, null, isInitOnly, claimType));
                    continue;
                }

                // Route wins over auth-context — warn if ClaimType was specified.
                if (claimType != null)
                {
                    diagnostics?.Add(new DiagnosticInfo(
                        PropertyOwnershipDiagnosticDescriptors.ClaimTypeOnRouteOwnedProperty,
                        member.Locations.FirstOrDefault(static l => l.IsInSource),
                        [member.Name, operationType.Name, claimType]));
                }

                // Fall through to route binding below — route wins over auth-context
            }

            // A TenantId has no rule of its own here: it is bound from the request like any other property, and
            // TenantModule fills it from the caller's tenant only when the request left it unset, as legacy did
            // (TenantModule's remarks). [FromAuthContext] is how an operation keeps its TenantId out of the request.

            // Step 4 — exactly one explicit HTTP binding attribute: validate and add entry
            if (httpBindingCount == 1)
            {
                if (fromRoute != null)
                {
                    HandleExplicitFromRoute(member, operationType, routePlaceholders, entries, diagnostics, isInitOnly);
                }
                else if (fromQuery != null)
                {
                    HandleExplicitFromQuery(member, operationType, fromQuery, entries, diagnostics, isInitOnly);
                }
                else if (fromBody != null)
                {
                    HandleExplicitFromBody(member, operationType, httpMethods, isGetOnly, entries, diagnostics, isInitOnly);
                }
                else if (fromHeader != null)
                {
                    HandleExplicitFromHeader(member, operationType, fromHeader, entries, diagnostics, isInitOnly);
                }
                else if (fromCookie != null)
                {
                    HandleExplicitFromCookie(member, operationType, fromCookie, entries, diagnostics, isInitOnly);
                }

                continue;
            }

            // Step 4 — convention fallback (no explicit attribute)
            if (routePlaceholders.Contains(member.Name, StringComparer.Ordinal))
            {
                if (!IsSupportedRouteType(member.Type))
                {
                    diagnostics?.Add(CreatePropertyDiagnostic(
                        PropertyOwnershipDiagnosticDescriptors.UnsupportedRoutePropertyType,
                        member,
                        operationType,
                        member.Type.ToDisplayString()));
                    continue;
                }

                ReportNullableRouteParameter(member, operationType, diagnostics);
                entries.Add(new PropertyOwnershipEntry(member.Name, member.Type, PropertyOwnershipSource.Route, member.Name, isInitOnly));

                if (IsTypeConverterOnlyType(member.Type))
                {
                    diagnostics?.Add(CreatePropertyDiagnostic(
                        PropertyOwnershipDiagnosticDescriptors.TypeConverterFallbackBinding,
                        member,
                        operationType,
                        member.Type.ToDisplayString()));
                }

                continue;
            }

            if (IsBodyMethod(httpMethods))
            {
                entries.Add(new PropertyOwnershipEntry(
                    member.Name,
                    member.Type,
                    PropertyOwnershipSource.Body,
                    GetCamelCaseName(member.Name),
                    isInitOnly));
            }
            else if (IsQueryMethod(httpMethods))
            {
                if (!IsSupportedQueryType(member.Type))
                {
                    diagnostics?.Add(CreatePropertyDiagnostic(
                        PropertyOwnershipDiagnosticDescriptors.UnsupportedQueryPropertyType,
                        member,
                        operationType,
                        member.Type.ToDisplayString()));
                    continue;
                }

                entries.Add(new PropertyOwnershipEntry(
                    member.Name,
                    member.Type,
                    PropertyOwnershipSource.Query,
                    GetCamelCaseName(member.Name),
                    isInitOnly));

                if (IsTypeConverterOnlyType(member.Type))
                {
                    diagnostics?.Add(CreatePropertyDiagnostic(
                        PropertyOwnershipDiagnosticDescriptors.TypeConverterFallbackBinding,
                        member,
                        operationType,
                        member.Type.ToDisplayString()));
                }
            }
        }

        return entries;
    }

    private static void HandleExplicitFromRoute(
        IPropertySymbol member,
        INamedTypeSymbol operationType,
        List<string> routePlaceholders,
        List<PropertyOwnershipEntry> entries,
        List<DiagnosticInfo>? diagnostics,
        bool isInitOnly)
    {
        if (!routePlaceholders.Contains(member.Name, StringComparer.Ordinal))
        {
            diagnostics?.Add(CreatePropertyDiagnostic(
                PropertyOwnershipDiagnosticDescriptors.ExplicitRouteWithoutPlaceholder,
                member,
                operationType));
            return;
        }

        if (!IsSupportedRouteType(member.Type))
        {
            diagnostics?.Add(CreatePropertyDiagnostic(
                PropertyOwnershipDiagnosticDescriptors.UnsupportedRoutePropertyType,
                member,
                operationType,
                member.Type.ToDisplayString()));
            return;
        }

        ReportNullableRouteParameter(member, operationType, diagnostics);
        entries.Add(new PropertyOwnershipEntry(member.Name, member.Type, PropertyOwnershipSource.Route, member.Name, isInitOnly));

        if (IsTypeConverterOnlyType(member.Type))
        {
            diagnostics?.Add(CreatePropertyDiagnostic(
                PropertyOwnershipDiagnosticDescriptors.TypeConverterFallbackBinding,
                member,
                operationType,
                member.Type.ToDisplayString()));
        }
    }

    private static void HandleExplicitFromQuery(
        IPropertySymbol member,
        INamedTypeSymbol operationType,
        AttributeData attribute,
        List<PropertyOwnershipEntry> entries,
        List<DiagnosticInfo>? diagnostics,
        bool isInitOnly)
    {
        if (!IsSupportedQueryType(member.Type))
        {
            diagnostics?.Add(CreatePropertyDiagnostic(
                PropertyOwnershipDiagnosticDescriptors.UnsupportedQueryPropertyType,
                member,
                operationType,
                member.Type.ToDisplayString()));
            return;
        }

        // Constructor argument overrides first (e.g. [FromQuery("validationToken")])
        var nameOverride = attribute.ConstructorArguments.Length > 0
            ? attribute.ConstructorArguments[0].Value as string
            : null;

        // Fall back to named argument Name (e.g. [FromQuery(Name = "validationToken")])
        if (string.IsNullOrWhiteSpace(nameOverride))
        {
            nameOverride = attribute.NamedArguments
                .FirstOrDefault(static a => a.Key == "Name").Value.Value as string;
        }

        var bindingName = !string.IsNullOrWhiteSpace(nameOverride) ? nameOverride : GetCamelCaseName(member.Name);

        entries.Add(new PropertyOwnershipEntry(member.Name, member.Type, PropertyOwnershipSource.Query, bindingName, isInitOnly));

        if (IsTypeConverterOnlyType(member.Type))
        {
            diagnostics?.Add(CreatePropertyDiagnostic(
                PropertyOwnershipDiagnosticDescriptors.TypeConverterFallbackBinding,
                member,
                operationType,
                member.Type.ToDisplayString()));
        }
    }

    private static void HandleExplicitFromBody(
        IPropertySymbol member,
        INamedTypeSymbol operationType,
        string[] httpMethods,
        bool isGetOnly,
        List<PropertyOwnershipEntry> entries,
        List<DiagnosticInfo>? diagnostics,
        bool isInitOnly)
    {
        if (isGetOnly)
        {
            var methodList = string.Join("/", httpMethods.Distinct(StringComparer.OrdinalIgnoreCase));
            diagnostics?.Add(CreatePropertyDiagnostic(
                PropertyOwnershipDiagnosticDescriptors.ExplicitBodyOnGet,
                member,
                operationType,
                methodList));
            return;
        }

        entries.Add(new PropertyOwnershipEntry(
            member.Name,
            member.Type,
            PropertyOwnershipSource.Body,
            GetCamelCaseName(member.Name),
            isInitOnly));
    }

    private static void HandleExplicitFromHeader(
        IPropertySymbol member,
        INamedTypeSymbol operationType,
        AttributeData attribute,
        List<PropertyOwnershipEntry> entries,
        List<DiagnosticInfo>? diagnostics,
        bool isInitOnly)
    {
        if (!IsSupportedHeaderOrCookieType(member.Type))
        {
            diagnostics?.Add(CreatePropertyDiagnostic(
                PropertyOwnershipDiagnosticDescriptors.UnsupportedHeaderOrCookiePropertyType,
                member,
                operationType,
                "FromHeader",
                member.Type.ToDisplayString()));
            return;
        }

        // Header name is a required constructor argument
        var headerName = attribute.ConstructorArguments.Length > 0
            ? attribute.ConstructorArguments[0].Value as string
            : null;

        if (string.IsNullOrWhiteSpace(headerName))
        {
            // Defensive: broken or incomplete attribute — skip without crashing the generator
            return;
        }

        entries.Add(new PropertyOwnershipEntry(member.Name, member.Type, PropertyOwnershipSource.Header, headerName, isInitOnly));
    }

    private static void HandleExplicitFromCookie(
        IPropertySymbol member,
        INamedTypeSymbol operationType,
        AttributeData attribute,
        List<PropertyOwnershipEntry> entries,
        List<DiagnosticInfo>? diagnostics,
        bool isInitOnly)
    {
        if (!IsSupportedHeaderOrCookieType(member.Type))
        {
            diagnostics?.Add(CreatePropertyDiagnostic(
                PropertyOwnershipDiagnosticDescriptors.UnsupportedHeaderOrCookiePropertyType,
                member,
                operationType,
                "FromCookie",
                member.Type.ToDisplayString()));
            return;
        }

        // Cookie name is a required constructor argument
        var cookieName = attribute.ConstructorArguments.Length > 0
            ? attribute.ConstructorArguments[0].Value as string
            : null;

        if (string.IsNullOrWhiteSpace(cookieName))
        {
            return;
        }

        entries.Add(new PropertyOwnershipEntry(member.Name, member.Type, PropertyOwnershipSource.Cookie, cookieName, isInitOnly));
    }

    /// <summary>
    /// Finds a Blueprint attribute by name and namespace on the given property, returning null if not present.
    /// Uses full namespace matching to avoid collisions with MVC <c>[FromX]</c> attributes.
    /// </summary>
    private static AttributeData? FindBlueprintAttribute(IPropertySymbol property, string attributeName)
    {
        return property.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name == attributeName
            && a.AttributeClass.IsInNamespace("Blueprint", "Model", "Attributes"));
    }

    /// <summary>
    /// Creates a <see cref="DiagnosticInfo"/> for a property-level diagnostic, extracting the source location
    /// from the property symbol when available.
    /// </summary>
    private static DiagnosticInfo CreatePropertyDiagnostic(
        DiagnosticDescriptor descriptor,
        IPropertySymbol member,
        INamedTypeSymbol operationType,
        params string[] extraArgs)
    {
        var location = member.Locations.FirstOrDefault(static l => l.IsInSource);
        var args = new List<string> { member.Name, operationType.Name };
        args.AddRange(extraArgs);
        return new DiagnosticInfo(
            descriptor,
            location is not null ? LocationInfo.CreateFrom(location) : null,
            new EquatableList<string>(args.ToArray()));
    }

    private static void ReportNullableRouteParameter(
        IPropertySymbol member,
        INamedTypeSymbol operationType,
        List<DiagnosticInfo>? diagnostics)
    {
        if (diagnostics is null || !IsNullableType(member.Type))
        {
            return;
        }

        diagnostics.Add(CreatePropertyDiagnostic(
            PropertyOwnershipDiagnosticDescriptors.NullableRouteParameter,
            member,
            operationType,
            member.Type.ToDisplayString()));
    }

    /// <summary>
    /// Creates a <see cref="DiagnosticInfo"/> for a route template diagnostic, locating
    /// the error on the route template expression when available.
    /// </summary>
    private static DiagnosticInfo CreateRouteDiagnostic(
        INamedTypeSymbol operationType,
        string url,
        string reason,
        LocationInfo? location = null)
    {
        return new DiagnosticInfo(
            PropertyOwnershipDiagnosticDescriptors.MalformedRoutePlaceholder,
            location ?? GetOperationLocation(operationType),
            new EquatableList<string>([url, operationType.Name, reason]));
    }

    private static LocationInfo? GetOperationLocation(INamedTypeSymbol operationType)
    {
        var location = operationType.Locations.FirstOrDefault(static l => l.IsInSource);
        return location is not null ? LocationInfo.CreateFrom(location) : null;
    }

    /// <summary>
    /// Builds a human-readable list of conflicting attribute names for the BPPO006 diagnostic message.
    /// </summary>
    private static string BuildConflictingAttributeList(
        AttributeData? fromRoute,
        AttributeData? fromQuery,
        AttributeData? fromBody,
        AttributeData? fromHeader,
        AttributeData? fromCookie)
    {
        var names = new List<string>();
        if (fromRoute != null)
        {
            names.Add("[FromRoute]");
        }

        if (fromQuery != null)
        {
            names.Add("[FromQuery]");
        }

        if (fromBody != null)
        {
            names.Add("[FromBody]");
        }

        if (fromHeader != null)
        {
            names.Add("[FromHeader]");
        }

        if (fromCookie != null)
        {
            names.Add("[FromCookie]");
        }

        return string.Join(", ", names);
    }

    private static bool IsSystemProperty(IPropertySymbol property)
    {
        var type = property.Type;

        // CancellationToken: check Name first (cheap), then namespace
        if (type.Name == "CancellationToken" && type.IsInNamespace("System", "Threading"))
        {
            return true;
        }

        // ILogger: check Name first, then namespace
        if (type.Name == "ILogger" && type.IsInNamespace("Microsoft", "Extensions", "Logging"))
        {
            return true;
        }

        return false;
    }

    private static bool IsStringType(ITypeSymbol type)
    {
        return type.SpecialType == SpecialType.System_String;
    }

    private static bool IsSupportedRouteType(ITypeSymbol type)
    {
        var effectiveType = GetEffectiveBindingType(type);
        return IsSupportedScalarType(effectiveType) || HasTypeConverterAttribute(effectiveType);
    }

    private static bool IsSupportedQueryType(ITypeSymbol type)
    {
        // Arrays of supported scalar types
        if (type is IArrayTypeSymbol arrayType)
        {
            return IsSupportedScalarType(arrayType.ElementType);
        }

        // Nullable<T> where T is a supported scalar or has TypeConverter
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && type is INamedTypeSymbol namedType)
        {
            var innerType = namedType.TypeArguments[0];
            return IsSupportedScalarType(innerType) || HasTypeConverterAttribute(innerType);
        }

        return IsSupportedScalarType(type) || HasTypeConverterAttribute(type);
    }

    private static bool IsSupportedScalarType(ITypeSymbol type)
    {
        var effectiveType = GetEffectiveBindingType(type);

        // Use SpecialType for primitives (no allocation)
        if (effectiveType.SpecialType is SpecialType.System_String
            or SpecialType.System_Int32
            or SpecialType.System_Int64
            or SpecialType.System_Double
            or SpecialType.System_Single
            or SpecialType.System_Boolean
            or SpecialType.System_Decimal)
        {
            return true;
        }

        // Check Guid, DateTime, DateTimeOffset by Name + namespace (no allocation)
        if (effectiveType.Name is "Guid" or "DateTime" or "DateTimeOffset"
            && effectiveType.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true })
        {
            return true;
        }

        return effectiveType.TypeKind == TypeKind.Enum
            || IsStringParsable(effectiveType);
    }

    private static ITypeSymbol GetEffectiveBindingType(ITypeSymbol type)
    {
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && type is INamedTypeSymbol namedType)
        {
            return namedType.TypeArguments[0];
        }

        return type.NullableAnnotation == NullableAnnotation.Annotated
            ? type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            : type;
    }

    /// <summary>
    /// Headers and cookies support scalar types and <see cref="Nullable{T}"/> wrapping a scalar,
    /// but not arrays — multi-value headers need a dedicated pattern and cookies are inherently single-valued.
    /// </summary>
    private static bool IsSupportedHeaderOrCookieType(ITypeSymbol type)
    {
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && type is INamedTypeSymbol namedType)
        {
            return IsSupportedScalarType(namedType.TypeArguments[0]);
        }

        return IsSupportedScalarType(type);
    }

    private static string GetCamelCaseName(string name)
    {
        return name.Camelize();
    }

    private static bool ImplementsIParsable(ITypeSymbol type)
    {
        return TypeConversionHelpers.ImplementsIParsable(type);
    }

    private static bool HasStaticTryParseMethod(ITypeSymbol type)
    {
        return TypeConversionHelpers.HasStaticTryParseMethod(type);
    }

    private static bool IsStringParsable(ITypeSymbol type)
    {
        return TypeConversionHelpers.IsStringParsable(type);
    }

    private static bool HasTypeConverterAttribute(ITypeSymbol type)
    {
        return TypeConversionHelpers.HasTypeConverterAttribute(type);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the type (or its <c>Nullable&lt;T&gt;</c> inner type) is
    /// supported for route/query binding only through a <c>[TypeConverter]</c> attribute
    /// (i.e. not via scalars, enums, <c>IParsable</c>, or <c>TryParse</c>). Used to select the
    /// exception-based conversion code path instead of the boolean TryParse path.
    /// </summary>
    private static bool IsTypeConverterOnlyType(ITypeSymbol type)
    {
        var effectiveType = type;
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && type is INamedTypeSymbol namedType)
        {
            effectiveType = namedType.TypeArguments[0];
        }

        return !IsSupportedScalarType(effectiveType) && HasTypeConverterAttribute(effectiveType);
    }

    /// <summary>
    /// Registers static readonly <see cref="System.ComponentModel.TypeConverter"/> fields for types that
    /// require TypeConverter-based binding. This eliminates the per-request
    /// <c>TypeDescriptor.GetConverter(typeof(T))</c> call (~50-500ns depending on cache state).
    /// </summary>
    private static Dictionary<string, string> EmitCachedTypeConverterFields(
        PipelineContributorContext context,
        List<PropertyOwnershipEntry> entries)
    {
        var converterFieldMap = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (entry.Source is not (PropertyOwnershipSource.Route or PropertyOwnershipSource.Query))
            {
                continue;
            }

            var effectiveType = GetEffectiveBindingType(entry.TypeSymbol);
            if (!IsTypeConverterOnlyType(entry.TypeSymbol))
            {
                continue;
            }

            var fqType = effectiveType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (converterFieldMap.ContainsKey(fqType))
            {
                continue;
            }

            var fieldName = $"_tc_{entry.PropertyName}";

            // Ensure unique field names across different properties that share the same type
            if (converterFieldMap.ContainsValue(fieldName))
            {
                fieldName = $"_tc_{entry.PropertyName}_{converterFieldMap.Count}";
            }

            converterFieldMap[fqType] = fieldName;

            context.AddDeferredClassMember(classBuilder =>
            {
                classBuilder.AddProperty(fieldName, Accessibility.Private)
                    .SetType("System.ComponentModel.TypeConverter")
                    .MakeStatic()
                    .WithReadonlyValue($"System.ComponentModel.TypeDescriptor.GetConverter(typeof({fqType}))");
            });
        }

        return converterFieldMap;
    }

    private static void RenderDescriptorFeatureData(ICodeWriter writer, List<PropertyOwnershipEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var entryExpressions = entries
            .Select(static e =>
            {
                var sourceExpr = $"global::Blueprint.Model.PropertyBindingSource.{e.Source}";
                var bindingNameExpr = e.BindingName is not null
                    ? CSharpSymbolDisplay.FormatLiteral(e.BindingName, quote: true)
                    : "null";
                var propertyNameExpr = CSharpSymbolDisplay.FormatLiteral(e.PropertyName, quote: true);
                var typeNameExpr = e.Source is PropertyOwnershipSource.System or PropertyOwnershipSource.AuthContext
                    ? "null"
                    : CSharpSymbolDisplay.FormatLiteral(e.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), quote: true);
                var isNullable = IsNullableType(e.TypeSymbol);

                return $"new global::Blueprint.Model.OperationPropertyBindingEntry({propertyNameExpr}, {sourceExpr}, {bindingNameExpr}, {typeNameExpr}, {(isNullable ? "true" : "false")})";
            })
            .ToArray();

        writer.AppendLine(
            "this.SetFeatureData(new global::Blueprint.Model.OperationPropertyOwnershipFeatureData("
            + $"global::System.Collections.Immutable.ImmutableArray.Create({string.Join(", ", entryExpressions)})));");
    }

    /// <summary>
    /// Emits inline claim reading for <c>[FromAuthContext(ClaimType = "...")]</c> properties.
    /// Runs in the <see cref="PipelineStage.Authentication"/> stage after identity verification,
    /// providing compile-time claim mapping without runtime <c>IPropertyPopulator</c> overhead.
    /// Uses guarded assignment to avoid overwriting values already set by route binding.
    /// </summary>
    private static void RenderClaimMappings(PipelineRenderContext context, List<PropertyOwnershipEntry> claimEntries)
    {
        var operationExpr = context.GetOperationExpression();

        context.WriteBlankLine();
        context.WriteStatement("// Compile-time claim mapping ([FromAuthContext(ClaimType = \"...\")]), no reflection");

        foreach (var entry in claimEntries)
        {
            var claimType = entry.ClaimType!;
            var escapedClaimType = claimType.Replace("\"", "\\\"");

            // Guarded assignment: only set if claim is present and property is empty/null.
            // Uses context.UserPrincipal (canonical pipeline identity) rather than httpContext.User.
            context.If(
                $"string.IsNullOrEmpty({operationExpr}.{entry.PropertyName})",
                guardCtx =>
                {
                    guardCtx.DeclareVariable(
                        $"_claim_{entry.PropertyName}",
                        $"context.UserPrincipal?.FindFirst(\"{escapedClaimType}\")");

                    guardCtx.If(
                        $"_claim_{entry.PropertyName} is not null",
                        innerCtx =>
                        {
                            innerCtx.WriteStatement($"{operationExpr}.{entry.PropertyName} = _claim_{entry.PropertyName}.Value;");
                        });
                });
        }
    }

    /// <summary>
    /// Unified population renderer that emits extraction code for all binding sources (body, route, query,
    /// header, cookie) in a single pipeline stage. Blank lines separate logical sections for readability
    /// in the generated output.
    /// </summary>
    private static void RenderPopulation(PipelineRenderContext context, List<PropertyOwnershipEntry> entries, Dictionary<string, string> converterFieldMap)
    {
        // Single-pass partitioning by source avoids scanning the entries collection 5 times.
        List<PropertyOwnershipEntry>? bodyEntries = null;
        List<PropertyOwnershipEntry>? routeEntries = null;
        List<PropertyOwnershipEntry>? queryEntries = null;
        List<PropertyOwnershipEntry>? headerEntries = null;
        List<PropertyOwnershipEntry>? cookieEntries = null;

        foreach (var entry in entries)
        {
            switch (entry.Source)
            {
                case PropertyOwnershipSource.Body:
                    (bodyEntries ??= []).Add(entry);
                    break;
                case PropertyOwnershipSource.Route:
                    (routeEntries ??= []).Add(entry);
                    break;
                case PropertyOwnershipSource.Query:
                    (queryEntries ??= []).Add(entry);
                    break;
                case PropertyOwnershipSource.Header:
                    (headerEntries ??= []).Add(entry);
                    break;
                case PropertyOwnershipSource.Cookie:
                    (cookieEntries ??= []).Add(entry);
                    break;
            }
        }

        var operationExpr = context.GetOperationExpression();

        // Ensure httpContext variable is declared (shared across modules via GetHttpContextExpression).
        context.GetHttpContextExpression();
        context.WriteBlankLine();

        // Nested pipeline executions (e.g. SelfQuery resolution) provide a pre-populated
        // operation object. Skip HTTP binding entirely to avoid overwriting with route/query
        // values from the parent request context.
        context.If("!context.IsNested", bindingBranch =>
        {
            // Body binding: delegate stream buffering, content-type check, and exception handling
            // to the shared PipelineHelpers.ReadJsonBodyAsync runtime method. The generated pipeline
            // only provides the operation-specific PopulateFromJsonBody call as a static delegate.
            if (bodyEntries is { Count: > 0 })
            {
                bindingBranch.WriteStatement(
                    $"var _bodyResult = await ReadJsonBodyAsync(httpContext, context.OperationCancelled, {operationExpr}, PopulateFromJsonBody, this.BodyJsonOptions).ConfigureAwait(false);");
                bindingBranch.If("_bodyResult != null", earlyReturnCtx =>
                {
                    earlyReturnCtx.Return("_bodyResult");
                });

                bindingBranch.WriteBlankLine();
            }

            // Route binding
            if (routeEntries is not null)
            {
                foreach (var entry in routeEntries)
                {
                    WriteRouteExtraction(bindingBranch, operationExpr, entry, converterFieldMap);
                }
            }

            if (routeEntries is { Count: > 0 } && (queryEntries is { Count: > 0 } || headerEntries is { Count: > 0 } || cookieEntries is { Count: > 0 }))
            {
                bindingBranch.WriteBlankLine();
            }

            // Query binding
            if (queryEntries is not null)
            {
                foreach (var entry in queryEntries)
                {
                    WriteQueryExtraction(bindingBranch, operationExpr, entry, converterFieldMap);
                }
            }

            // Header binding
            if (headerEntries is not null)
            {
                foreach (var entry in headerEntries)
                {
                    WriteHeaderExtraction(bindingBranch, operationExpr, entry);
                }
            }

            // Cookie binding
            if (cookieEntries is not null)
            {
                foreach (var entry in cookieEntries)
                {
                    WriteCookieExtraction(bindingBranch, operationExpr, entry);
                }
            }
        });
    }

    /// <summary>
    /// Generates <c>[UnsafeAccessor]</c> setter stubs for <c>init</c>-only properties so the pipeline
    /// can assign values after the operation instance is created by the factory.
    /// </summary>
    private static void WriteUnsafeAccessorSetters(
        ClassBuilder pipelineClass,
        string operationTypeFull,
        List<PropertyOwnershipEntry> initEntries)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in initEntries)
        {
            if (!seen.Add(entry.PropertyName))
            {
                continue;
            }

            var propertyTypeFull = entry.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var methodName = $"_SetInit_{entry.PropertyName}";

            pipelineClass.AddMethod(methodName)
                .MakePrivateMethod()
                .MakeStaticMethod()
                .MakeExternMethod()
                .WithReturnType("void")
                .AddParameter(operationTypeFull, "instance")
                .AddParameter(propertyTypeFull, "value")
                .AddAttribute($"System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = \"set_{entry.PropertyName}\")");
        }
    }

    private static void WritePopulateFromJsonBodyMethod(
        ClassBuilder pipelineClass,
        string operationTypeFull,
        List<PropertyOwnershipEntry> bodyEntries)
    {
        var method = pipelineClass.AddMethod("PopulateFromJsonBody")
            .MakePrivateMethod()
            .MakeStaticMethod()
            .WithReturnType("void")
            .AddParameter("System.ReadOnlySpan<byte>", "bodyBytes")
            .AddParameter(operationTypeFull, "operation")
            .AddParameter("System.Text.Json.JsonSerializerOptions", "jsonOptions");

        method.WithBody(writer =>
        {
            writer.AppendLine("var reader = new System.Text.Json.Utf8JsonReader(bodyBytes);");
            writer.AppendLine("if (!reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.StartObject) return;");
            writer.NewLine();
            writer.AppendLine("while (reader.Read())");
            writer.AppendLine("{");
            writer.AppendLine("    if (reader.TokenType == System.Text.Json.JsonTokenType.EndObject) break;");
            writer.AppendLine("    if (reader.TokenType != System.Text.Json.JsonTokenType.PropertyName) continue;");
            writer.NewLine();

            var isFirst = true;
            foreach (var entry in bodyEntries)
            {
                var prefix = isFirst ? "    if" : "    else if";
                isFirst = false;

                var utf8Name = $"\"{entry.BindingName}\"u8";
                writer.AppendLine($"{prefix} (reader.ValueTextEquals({utf8Name}))");
                writer.AppendLine("    {");
                writer.AppendLine("        reader.Read();");
                WriteUtf8ReaderAssignment(writer, entry);
                writer.AppendLine("    }");
            }

            writer.AppendLine("    else");
            writer.AppendLine("    {");
            writer.AppendLine("        reader.Skip();");
            writer.AppendLine("    }");
            writer.AppendLine("}");
        });
    }

    private static void WriteUtf8ReaderAssignment(ICodeWriter writer, PropertyOwnershipEntry entry)
    {
        var rawType = entry.TypeSymbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        var isNullableValueType = rawType is INamedTypeSymbol { IsValueType: true, OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

        // For nullable value types, unwrap to the underlying type for the switch lookup.
        var unwrappedType = isNullableValueType
            ? ((INamedTypeSymbol)rawType).TypeArguments[0]
            : rawType;

        // Map primitive types to their fast Utf8JsonReader accessor via SpecialType (no allocation).
        // Nullable value types use the same accessor wrapped with a JsonTokenType.Null guard,
        // avoiding the much slower JsonSerializer.Deserialize<T?> path.
        var fastReaderExpr = GetFastJsonReaderExpression(unwrappedType);

        string readerExpr;
        if (fastReaderExpr is not null)
        {
            if (isNullableValueType)
            {
                var nullableCast = $"({unwrappedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}?)";
                readerExpr = $"reader.TokenType == System.Text.Json.JsonTokenType.Null ? null : {nullableCast}{fastReaderExpr}";
            }
            else
            {
                readerExpr = fastReaderExpr;
            }
        }
        else
        {
            var fullyQualifiedType = rawType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var deserializeExpr = $"System.Text.Json.JsonSerializer.Deserialize<{fullyQualifiedType}>(ref reader, jsonOptions)";

            // Reference types (classes, interfaces, arrays, etc.) can receive JSON null tokens.
            // Guard against calling Deserialize on a null token: the source-gen context would
            // throw NotSupportedException for types it doesn't recognise, and even the reflection
            // fallback allocates unnecessarily for what should be a trivial null assignment.
            if (!rawType.IsValueType)
            {
                readerExpr = $"reader.TokenType == System.Text.Json.JsonTokenType.Null ? null : {deserializeExpr}";
            }
            else
            {
                readerExpr = deserializeExpr;
            }
        }

        writer.AppendLine($"        {FormatPropertyAssignment("operation", entry, readerExpr)}");
    }

    private static void WriteRouteExtraction(PipelineRenderContext context, string operationExpr, PropertyOwnershipEntry entry, Dictionary<string, string> converterFieldMap)
    {
        var effectiveType = GetEffectiveBindingType(entry.TypeSymbol);
        var routeValueExpr = $"httpContext.Request.RouteValues[\"{entry.BindingName}\"]";

        if (effectiveType.SpecialType == SpecialType.System_String)
        {
            // Wrap in a null-guard so that when route values are absent (e.g. test-mode
            // direct execution) the operation's already-set property value is preserved.
            context.If($"{routeValueExpr} is string routeStr_{entry.PropertyName}", ifCtx =>
            {
                ifCtx.WriteStatement(FormatPropertyAssignment(operationExpr, entry, $"routeStr_{entry.PropertyName}"));
            });
        }
        else if (IsTypeConverterOnlyType(entry.TypeSymbol))
        {
            var fqType = effectiveType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var converterFieldName = converterFieldMap[fqType];

            context.If($"{routeValueExpr} is string routeStr_{entry.PropertyName}", ifCtx =>
            {
                ifCtx.Try(tryCtx =>
                {
                    var assignment = FormatPropertyAssignment(
                        operationExpr,
                        entry,
                        $"({fqType}){converterFieldName}.ConvertFromInvariantString(routeStr_{entry.PropertyName})!");
                    tryCtx.WriteStatement(assignment);
                });
                ifCtx.Catch("System.Exception", catchCtx =>
                {
                    catchCtx.Return($"new Blueprint.Runtime.Exec.BindingFailureResult(\"Invalid route parameter '{entry.BindingName}'\")");
                });
            });
        }
        else
        {
            var lambda = TypeConversionHelpers.GetTryParseLambda(effectiveType);
            var errVar = $"_routeErr_{entry.PropertyName}";
            var parsedVar = $"_parsed_{entry.PropertyName}";

            // As for a string: when the route value is absent (an operation executed in process, with no routing pass)
            // the operation keeps the value it has, as legacy Blueprint kept it (Signature365 8b40ecc961,
            // Blueprint.Http/MessagePopulation/HttpRouteMessagePopulationSource.cs:89-107).
            context.If($"httpContext.Request.RouteValues.ContainsKey(\"{entry.BindingName}\")", ifCtx =>
            {
                ifCtx.WriteStatement(
                    $"var {errVar} = BindRouteValue(httpContext.Request.RouteValues, \"{entry.BindingName}\", out var {parsedVar}, {lambda});");

                ifCtx.If($"{errVar} != null", errCtx =>
                {
                    errCtx.Return(errVar);
                });

                ifCtx.WriteStatement(FormatPropertyAssignment(operationExpr, entry, parsedVar));
            });
        }
    }

    private static void WriteQueryExtraction(PipelineRenderContext context, string operationExpr, PropertyOwnershipEntry entry, Dictionary<string, string> converterFieldMap)
    {
        var typeSymbol = entry.TypeSymbol;
        var queryParamName = entry.BindingName!;

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            WriteArrayQueryExtraction(context, operationExpr, entry, arrayType, queryParamName);
            return;
        }

        // Unwrap Nullable<T> to get the inner scalar type for parsing
        var effectiveType = typeSymbol;
        if (typeSymbol.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && typeSymbol is INamedTypeSymbol namedType)
        {
            effectiveType = namedType.TypeArguments[0];
        }

        var effectiveDisplay = effectiveType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

        if (effectiveDisplay.SpecialType == SpecialType.System_String)
        {
            var queryVar = $"_qv_{entry.PropertyName}";
            context.WriteStatement($"var {queryVar} = httpContext.Request.Query[\"{queryParamName}\"];");

            context.If($"{queryVar}.Count > 0", ifCtx =>
            {
                ifCtx.WriteStatement(FormatPropertyAssignment(operationExpr, entry, $"{queryVar}[0]"));
            });
        }
        else if (IsTypeConverterOnlyType(effectiveType))
        {
            var queryVar = $"_qv_{entry.PropertyName}";
            var fqType = effectiveType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var converterFieldName = converterFieldMap[fqType];

            context.WriteStatement($"var {queryVar} = httpContext.Request.Query[\"{queryParamName}\"];");

            context.If($"{queryVar}.Count > 0", ifCtx =>
            {
                ifCtx.Try(tryCtx =>
                {
                    var assignment = FormatPropertyAssignment(
                        operationExpr,
                        entry,
                        $"({fqType}){converterFieldName}.ConvertFromInvariantString({queryVar}[0])!");
                    tryCtx.WriteStatement(assignment);
                });
                ifCtx.Catch("System.Exception", catchCtx =>
                {
                    catchCtx.Return($"new Blueprint.Runtime.Exec.BindingFailureResult(\"Invalid query parameter '{entry.BindingName}'\")");
                });
            });
        }
        else
        {
            var lambda = TypeConversionHelpers.GetTryParseLambda(effectiveType);
            var errVar = $"_qvErr_{entry.PropertyName}";
            var parsedVar = $"_parsed_{entry.PropertyName}";
            var foundVar = $"_qvFound_{entry.PropertyName}";

            context.WriteStatement(
                $"var {errVar} = BindQueryValue(httpContext.Request.Query, \"{queryParamName}\", out var {parsedVar}, out var {foundVar}, {lambda});");

            context.If($"{errVar} != null", errCtx =>
            {
                errCtx.Return(errVar);
            });

            context.If(foundVar, foundCtx =>
            {
                foundCtx.WriteStatement(FormatPropertyAssignment(operationExpr, entry, parsedVar));
            });
        }
    }

    private static void WriteArrayQueryExtraction(
        PipelineRenderContext context,
        string operationExpr,
        PropertyOwnershipEntry entry,
        IArrayTypeSymbol arrayType,
        string queryParamName)
    {
        var elementType = arrayType.ElementType;

        if (elementType.WithNullableAnnotation(NullableAnnotation.NotAnnotated).SpecialType == SpecialType.System_String)
        {
            var queryVar = $"_qv_{entry.PropertyName}";
            context.WriteStatement($"var {queryVar} = httpContext.Request.Query[\"{queryParamName}\"];");

            context.If($"{queryVar}.Count > 0", ifCtx =>
            {
                ifCtx.WriteStatement(FormatPropertyAssignment(operationExpr, entry, $"{queryVar}.ToArray()"));
            });
        }
        else
        {
            var lambda = TypeConversionHelpers.GetTryParseLambda(elementType);
            var errVar = $"_qaErr_{entry.PropertyName}";
            var arrVar = $"_arr_{entry.PropertyName}";

            context.WriteStatement(
                $"var {errVar} = BindQueryArray(httpContext.Request.Query, \"{queryParamName}\", out var {arrVar}, {lambda});");

            context.If($"{errVar} != null", errCtx =>
            {
                errCtx.Return(errVar);
            });

            context.If($"{arrVar} != null", foundCtx =>
            {
                foundCtx.WriteStatement(FormatPropertyAssignment(operationExpr, entry, arrVar));
            });
        }
    }

    /// <summary>
    /// Emits extraction code for a header-bound property. Headers use <c>IHeaderDictionary</c> which
    /// returns <c>StringValues</c> — the same Count-based pattern as query parameters, taking the first value.
    /// </summary>
    private static void WriteHeaderExtraction(PipelineRenderContext context, string operationExpr, PropertyOwnershipEntry entry)
    {
        var headerName = entry.BindingName!;

        if (IsStringType(entry.TypeSymbol))
        {
            var headerVar = $"_hv_{entry.PropertyName}";
            context.WriteStatement($"var {headerVar} = httpContext.Request.Headers[\"{headerName}\"];");

            context.If($"{headerVar}.Count > 0", ifCtx =>
            {
                ifCtx.WriteStatement(FormatPropertyAssignment(operationExpr, entry, $"{headerVar}[0]"));
            });
        }
        else
        {
            var effectiveType = entry.TypeSymbol;
            if (effectiveType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && effectiveType is INamedTypeSymbol namedType)
            {
                effectiveType = namedType.TypeArguments[0];
            }

            var lambda = TypeConversionHelpers.GetTryParseLambda(effectiveType);
            var errVar = $"_hvErr_{entry.PropertyName}";
            var parsedVar = $"_parsed_{entry.PropertyName}";
            var foundVar = $"_hvFound_{entry.PropertyName}";

            context.WriteStatement(
                $"var {errVar} = BindHeaderValue(httpContext.Request.Headers, \"{headerName}\", out var {parsedVar}, out var {foundVar}, {lambda});");

            context.If($"{errVar} != null", errCtx =>
            {
                errCtx.Return(errVar);
            });

            context.If(foundVar, foundCtx =>
            {
                foundCtx.WriteStatement(FormatPropertyAssignment(operationExpr, entry, parsedVar));
            });
        }
    }

    /// <summary>
    /// Emits extraction code for a cookie-bound property. Cookies use <c>IRequestCookieCollection</c>
    /// which returns <c>string?</c> from the indexer — simpler null-check pattern than headers.
    /// </summary>
    private static void WriteCookieExtraction(PipelineRenderContext context, string operationExpr, PropertyOwnershipEntry entry)
    {
        var cookieName = entry.BindingName!;

        if (IsStringType(entry.TypeSymbol))
        {
            var cookieVar = $"_cv_{entry.PropertyName}";
            context.WriteStatement($"var {cookieVar} = httpContext.Request.Cookies[\"{cookieName}\"];");

            context.If($"{cookieVar} != null", ifCtx =>
            {
                ifCtx.WriteStatement(FormatPropertyAssignment(operationExpr, entry, cookieVar));
            });
        }
        else
        {
            var effectiveType = entry.TypeSymbol;
            if (effectiveType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && effectiveType is INamedTypeSymbol namedType)
            {
                effectiveType = namedType.TypeArguments[0];
            }

            var lambda = TypeConversionHelpers.GetTryParseLambda(effectiveType);
            var errVar = $"_cvErr_{entry.PropertyName}";
            var parsedVar = $"_parsed_{entry.PropertyName}";
            var foundVar = $"_cvFound_{entry.PropertyName}";

            context.WriteStatement(
                $"var {errVar} = BindCookieValue(httpContext.Request.Cookies, \"{cookieName}\", out var {parsedVar}, out var {foundVar}, {lambda});");

            context.If($"{errVar} != null", errCtx =>
            {
                errCtx.Return(errVar);
            });

            context.If(foundVar, foundCtx =>
            {
                foundCtx.WriteStatement(FormatPropertyAssignment(operationExpr, entry, parsedVar));
            });
        }
    }

    /// <summary>
    /// Returns a TryParse expression that avoids exception-handling overhead for malformed query values.
    /// The expression evaluates to <c>true</c> when parsing succeeds and writes the result into <paramref name="outVariable"/>.
    /// </summary>
    private static string GetScalarTryParseExpression(ITypeSymbol typeSymbol, string stringExpression, string outVariable)
    {
        return TypeConversionHelpers.GetTryParseExpression(typeSymbol, stringExpression, outVariable);
    }

    /// <summary>
    /// Determines whether a type symbol represents a nullable type (either a <c>Nullable&lt;T&gt;</c>
    /// value type or a reference type annotated with <c>?</c>).
    /// </summary>
    private static bool IsNullableType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return true;
        }

        if (typeSymbol is INamedTypeSymbol { IsGenericType: true } namedType
            && namedType.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Maps a type symbol to a fast <c>Utf8JsonReader.GetXxx()</c> expression using <see cref="SpecialType"/>
    /// and structural namespace checks — avoids <c>ToDisplayString()</c> allocation on every body property.
    /// Returns <c>null</c> when the type is not a known primitive and must fall through to
    /// <c>JsonSerializer.Deserialize</c>.
    /// </summary>
    private static string? GetFastJsonReaderExpression(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_String: return "reader.GetString()";
            case SpecialType.System_Int32: return "reader.GetInt32()";
            case SpecialType.System_Int64: return "reader.GetInt64()";
            case SpecialType.System_Double: return "reader.GetDouble()";
            case SpecialType.System_Single: return "reader.GetSingle()";
            case SpecialType.System_Boolean: return "reader.GetBoolean()";
            case SpecialType.System_Decimal: return "reader.GetDecimal()";
        }

        // Guid, DateTime, DateTimeOffset live in the System namespace
        if (type.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true })
        {
            return type.Name switch
            {
                "Guid" => "reader.GetGuid()",
                "DateTime" => "reader.GetDateTime()",
                "DateTimeOffset" => "reader.GetDateTimeOffset()",
                _ => null,
            };
        }

        return null;
    }

    /// <summary>
    /// Returns the assignment statement for setting an operation property value. For <c>init</c>-only
    /// properties, calls the generated <c>[UnsafeAccessor]</c> setter instead of a direct assignment.
    /// </summary>
    private static string FormatPropertyAssignment(string operationExpr, PropertyOwnershipEntry entry, string valueExpr)
    {
        if (entry.IsInitOnly)
        {
            return $"_SetInit_{entry.PropertyName}({operationExpr}, {valueExpr});";
        }

        return $"{operationExpr}.{entry.PropertyName} = {valueExpr};";
    }

    /// <summary>
    /// Internal model used during analysis. Not exposed to consumers — the feature data record uses string-based source names.
    /// </summary>
    internal sealed record PropertyOwnershipEntry(
        string PropertyName,
        ITypeSymbol TypeSymbol,
        PropertyOwnershipSource Source,
        string? BindingName,
        bool IsInitOnly = false,
        string? ClaimType = null);

    /// <summary>
    /// The binding source for a property during the ownership analysis phase.
    /// </summary>
    internal enum PropertyOwnershipSource
    {
        /// <summary>Property is populated from a route placeholder.</summary>
        Route,

        /// <summary>Property is populated from the JSON request body.</summary>
        Body,

        /// <summary>Property is populated from the query string.</summary>
        Query,

        /// <summary>Property is populated from an HTTP header.</summary>
        Header,

        /// <summary>Property is populated from an HTTP cookie.</summary>
        Cookie,

        /// <summary>Property is populated by the auth pipeline.</summary>
        AuthContext,

        /// <summary>Property is populated by the runtime.</summary>
        System,
    }
}
