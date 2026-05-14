using System.Text.RegularExpressions;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Determines compile-time property ownership (route, body, query, header, cookie, system) and generates
/// the Population pipeline stage that binds HTTP request data onto operation properties.
/// </summary>
public sealed class PropertyOwnershipModule : IBlueprintSourceGeneratorModule, IDescriptorContributor, IPipelineContributor
{
    private static readonly Regex PlaceholderRegex = new(@"\{\*{0,2}([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

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
        var routePlaceholders = ExtractRoutePlaceholders(operationType);
        ValidateRoutePlaceholderSyntax(operationType, context);
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

        var routePlaceholders = ExtractRoutePlaceholders(operationType);
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

        context.AddRenderStep(PipelineStage.Population, renderContext => RenderPopulation(renderContext, entries));

        // Compile-time claim mapping: when [FromAuthContext(ClaimType = "...")] is specified,
        // emit inline claim reading in the Authentication stage (after identity check).
        var claimMappedEntries = entries.Where(static e => e.Source == PropertyOwnershipSource.AuthContext && e.ClaimType is not null).ToList();
        if (claimMappedEntries.Count > 0)
        {
            context.AddRenderStep(PipelineStage.Authentication, renderContext => RenderClaimMappings(renderContext, claimMappedEntries));
        }

        // Generate [UnsafeAccessor] setter methods for init-only properties so the pipeline can
        // assign route/query/header/cookie/body values after construction.
        var initEntries = entries.Where(static e => e.IsInitOnly).ToList();
        if (initEntries.Count > 0)
        {
            var operationTypeFull = operationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            context.AddDeferredClassMember(pipelineClass =>
                WriteUnsafeAccessorSetters(pipelineClass, operationTypeFull, initEntries));
        }

        if (hasBodyProperties)
        {
            var bodyEntries = entries.Where(static e => e.Source == PropertyOwnershipSource.Body).ToArray();
            var operationTypeFull = operationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            context.AddDeferredClassMember(pipelineClass =>
                WritePopulateFromJsonBodyMethod(pipelineClass, operationTypeFull, bodyEntries));
        }
    }

    private static List<string> ExtractRoutePlaceholders(INamedTypeSymbol operationType)
    {
        var placeholders = new List<string>();

        foreach (var url in HttpHostExposureHelper.GetAllRouteTemplates(operationType))
        {
            foreach (Match match in PlaceholderRegex.Matches(url))
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
    private static void ValidateRoutePlaceholderSyntax(INamedTypeSymbol operationType, DescriptorContributorContext context)
    {
        foreach (var url in HttpHostExposureHelper.GetAllRouteTemplates(operationType))
        {
            var openCount = 0;

            for (var i = 0; i < url.Length; i++)
            {
                switch (url[i])
                {
                    case '{':
                        openCount++;
                        if (openCount > 1)
                        {
                            context.AddDiagnostic(CreateRouteDiagnostic(operationType, url, "nested or consecutive opening braces"));
                            return;
                        }

                        break;

                    case '}':
                        if (openCount == 0)
                        {
                            context.AddDiagnostic(CreateRouteDiagnostic(operationType, url, "closing brace without matching opening brace"));
                            return;
                        }

                        openCount--;
                        break;
                }
            }

            if (openCount > 0)
            {
                context.AddDiagnostic(CreateRouteDiagnostic(operationType, url, "opening brace without matching closing brace"));
            }

            // Check for empty placeholders: "{}" with nothing inside.
            if (url.Contains("{}", StringComparison.Ordinal))
            {
                context.AddDiagnostic(CreateRouteDiagnostic(operationType, url, "empty placeholder name"));
            }
        }
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
            return explicitMethods.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        return IsCommand(operationType) ? ["POST"] : ["GET"];
    }

    private static bool IsCommand(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.ContainingNamespace.ToDisplayString() == "Blueprint.Model"
            && candidate.Name == "ICommand");
    }

    private static bool IsBodyMethod(string[] httpMethods)
    {
        return httpMethods.Any(static m =>
            string.Equals(m, "POST", StringComparison.OrdinalIgnoreCase)
            || string.Equals(m, "PUT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(m, "PATCH", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsQueryMethod(string[] httpMethods)
    {
        return httpMethods.Any(static m =>
            string.Equals(m, "GET", StringComparison.OrdinalIgnoreCase)
            || string.Equals(m, "DELETE", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns true when the operation is exclusively GET and/or DELETE — used for BPPO004 validation
    /// of explicit [FromBody], which is semantically invalid on these methods.
    /// </summary>
    private static bool IsGetOrDeleteMethod(string[] httpMethods)
    {
        return httpMethods.Length > 0 && httpMethods.All(static m =>
            string.Equals(m, "GET", StringComparison.OrdinalIgnoreCase)
            || string.Equals(m, "DELETE", StringComparison.OrdinalIgnoreCase));
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
        var isGetOrDelete = IsGetOrDeleteMethod(httpMethods);
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
            var fromRoute = FindAttribute(member, "FromRouteAttribute", "Blueprint.Model.Attributes");
            var fromQuery = FindAttribute(member, "FromQueryAttribute", "Blueprint.Model.Attributes");
            var fromBody = FindAttribute(member, "FromBodyAttribute", "Blueprint.Model.Attributes");
            var fromHeader = FindAttribute(member, "FromHeaderAttribute", "Blueprint.Model.Attributes");
            var fromCookie = FindAttribute(member, "FromCookieAttribute", "Blueprint.Model.Attributes");
            var fromAuthContext = FindAttribute(member, "FromAuthContextAttribute", "Blueprint.Model.Attributes");

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
                    HandleExplicitFromBody(member, operationType, httpMethods, isGetOrDelete, entries, diagnostics, isInitOnly);
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

                entries.Add(new PropertyOwnershipEntry(member.Name, member.Type, PropertyOwnershipSource.Route, member.Name, isInitOnly));
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

        entries.Add(new PropertyOwnershipEntry(member.Name, member.Type, PropertyOwnershipSource.Route, member.Name, isInitOnly));
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
    }

    private static void HandleExplicitFromBody(
        IPropertySymbol member,
        INamedTypeSymbol operationType,
        string[] httpMethods,
        bool isGetOrDelete,
        List<PropertyOwnershipEntry> entries,
        List<DiagnosticInfo>? diagnostics,
        bool isInitOnly)
    {
        if (isGetOrDelete)
        {
            var methodList = string.Join("/", httpMethods.Distinct(StringComparer.OrdinalIgnoreCase));
            diagnostics?.Add(CreatePropertyDiagnostic(
                PropertyOwnershipDiagnosticDescriptors.ExplicitBodyOnGetOrDelete,
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
    private static AttributeData? FindAttribute(IPropertySymbol property, string attributeName, string attributeNamespace)
    {
        return property.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name == attributeName
            && a.AttributeClass.ContainingNamespace?.ToDisplayString() == attributeNamespace);
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

    /// <summary>
    /// Creates a <see cref="DiagnosticInfo"/> for a route template diagnostic, locating
    /// the error on the operation type declaration.
    /// </summary>
    private static DiagnosticInfo CreateRouteDiagnostic(
        INamedTypeSymbol operationType,
        string url,
        string reason)
    {
        var location = operationType.Locations.FirstOrDefault(static l => l.IsInSource);
        return new DiagnosticInfo(
            PropertyOwnershipDiagnosticDescriptors.MalformedRoutePlaceholder,
            location is not null ? LocationInfo.CreateFrom(location) : null,
            new EquatableList<string>([url, operationType.Name, reason]));
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
        var typeName = property.Type.ToDisplayString();
        return typeName is "System.Threading.CancellationToken"
            or "Microsoft.Extensions.Logging.ILogger";
    }

    private static bool IsStringType(ITypeSymbol type)
    {
        return type.SpecialType == SpecialType.System_String;
    }

    private static bool IsSupportedRouteType(ITypeSymbol type)
    {
        // Strip nullable annotation for type checking
        var checkType = type.NullableAnnotation == NullableAnnotation.Annotated && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
            ? type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            : type;

        var displayName = checkType.ToDisplayString();
        return displayName is "string"
            or "int"
            or "long"
            or "double"
            or "float"
            or "System.Guid"
            or "System.DateTime"
            or "System.DateTimeOffset"
            or "bool"
            or "decimal"
            || type.TypeKind == TypeKind.Enum
            || IsStringParsable(type)
            || HasTypeConverterAttribute(type);
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
        var checkType = type.NullableAnnotation == NullableAnnotation.Annotated && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
            ? type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            : type;

        var displayName = checkType.ToDisplayString();
        return displayName is "string"
            or "int"
            or "long"
            or "double"
            or "float"
            or "System.Guid"
            or "System.DateTime"
            or "System.DateTimeOffset"
            or "bool"
            or "decimal"
            || type.TypeKind == TypeKind.Enum
            || IsStringParsable(type);
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
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name.Substring(1);
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
    /// Returns <see langword="true"/> when the type is supported for route/query binding
    /// only through a <c>[TypeConverter]</c> attribute (i.e. not via scalars, enums,
    /// <c>IParsable</c>, or <c>TryParse</c>). Used to select the exception-based
    /// conversion code path instead of the boolean TryParse path.
    /// </summary>
    private static bool IsTypeConverterOnlyType(ITypeSymbol type)
    {
        return !IsSupportedScalarType(type) && HasTypeConverterAttribute(type);
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
    private static void RenderPopulation(PipelineRenderContext context, List<PropertyOwnershipEntry> entries)
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

        // Body binding: delegate stream buffering, content-type check, and exception handling
        // to the shared PipelineHelpers.ReadJsonBodyAsync runtime method. The generated pipeline
        // only provides the operation-specific PopulateFromJsonBody call as a static delegate.
        if (bodyEntries is { Count: > 0 })
        {
            context.WriteStatement(
                $"var _bodyResult = await Blueprint.Runtime.Exec.PipelineHelpers.ReadJsonBodyAsync(httpContext, context.OperationCancelled, {operationExpr}, PopulateFromJsonBody, this.BodyJsonOptions).ConfigureAwait(false);");
            context.If("_bodyResult != null", earlyReturnCtx =>
            {
                earlyReturnCtx.Return("_bodyResult");
            });

            context.WriteBlankLine();
        }

        // Route binding
        if (routeEntries is not null)
        {
            foreach (var entry in routeEntries)
            {
                WriteRouteExtraction(context, operationExpr, entry);
            }
        }

        if (routeEntries is { Count: > 0 } && (queryEntries is { Count: > 0 } || headerEntries is { Count: > 0 } || cookieEntries is { Count: > 0 }))
        {
            context.WriteBlankLine();
        }

        // Query binding
        if (queryEntries is not null)
        {
            foreach (var entry in queryEntries)
            {
                WriteQueryExtraction(context, operationExpr, entry);
            }
        }

        // Header binding
        if (headerEntries is not null)
        {
            foreach (var entry in headerEntries)
            {
                WriteHeaderExtraction(context, operationExpr, entry);
            }
        }

        // Cookie binding
        if (cookieEntries is not null)
        {
            foreach (var entry in cookieEntries)
            {
                WriteCookieExtraction(context, operationExpr, entry);
            }
        }
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
        PropertyOwnershipEntry[] bodyEntries)
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
        var typeDisplay = unwrappedType.ToDisplayString();

        string readerExpr;
        switch (typeDisplay)
        {
            case "string":
                readerExpr = "reader.GetString()";
                break;
            case "int" when !isNullableValueType:
                readerExpr = "reader.GetInt32()";
                break;
            case "long" when !isNullableValueType:
                readerExpr = "reader.GetInt64()";
                break;
            case "double" when !isNullableValueType:
                readerExpr = "reader.GetDouble()";
                break;
            case "float" when !isNullableValueType:
                readerExpr = "reader.GetSingle()";
                break;
            case "bool" when !isNullableValueType:
                readerExpr = "reader.GetBoolean()";
                break;
            case "decimal" when !isNullableValueType:
                readerExpr = "reader.GetDecimal()";
                break;
            case "System.Guid" when !isNullableValueType:
                readerExpr = "reader.GetGuid()";
                break;
            case "System.DateTime" when !isNullableValueType:
                readerExpr = "reader.GetDateTime()";
                break;
            case "System.DateTimeOffset" when !isNullableValueType:
                readerExpr = "reader.GetDateTimeOffset()";
                break;
            default:
                var fullyQualifiedType = rawType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                readerExpr = $"System.Text.Json.JsonSerializer.Deserialize<{fullyQualifiedType}>(ref reader, jsonOptions)";
                break;
        }

        writer.AppendLine($"        {FormatPropertyAssignment("operation", entry, readerExpr)}");
    }

    private static void WriteRouteExtraction(PipelineRenderContext context, string operationExpr, PropertyOwnershipEntry entry)
    {
        var typeDisplay = entry.TypeSymbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();
        var routeValueExpr = $"httpContext.Request.RouteValues[\"{entry.BindingName}\"]";

        if (typeDisplay == "string")
        {
            context.WriteStatement(FormatPropertyAssignment(operationExpr, entry, $"(string){routeValueExpr}"));
        }
        else if (IsTypeConverterOnlyType(entry.TypeSymbol))
        {
            var fqType = entry.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            context.If($"{routeValueExpr} is string routeStr_{entry.PropertyName}", ifCtx =>
            {
                ifCtx.Try(tryCtx =>
                {
                    var assignment = FormatPropertyAssignment(
                        operationExpr,
                        entry,
                        $"({fqType})System.ComponentModel.TypeDescriptor.GetConverter(typeof({fqType})).ConvertFromInvariantString(routeStr_{entry.PropertyName})!");
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
            var lambda = TypeConversionHelpers.GetTryParseLambda(entry.TypeSymbol);
            var errVar = $"_routeErr_{entry.PropertyName}";
            var parsedVar = $"_parsed_{entry.PropertyName}";

            context.WriteStatement(
                $"var {errVar} = Blueprint.Runtime.Exec.PipelineHelpers.BindRouteValue(httpContext.Request.RouteValues, \"{entry.BindingName}\", out var {parsedVar}, {lambda});");

            context.If($"{errVar} != null", errCtx =>
            {
                errCtx.Return(errVar);
            });

            context.WriteStatement(FormatPropertyAssignment(operationExpr, entry, parsedVar));
        }
    }

    private static void WriteQueryExtraction(PipelineRenderContext context, string operationExpr, PropertyOwnershipEntry entry)
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

        var effectiveDisplay = effectiveType.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();

        if (effectiveDisplay == "string")
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

            context.WriteStatement($"var {queryVar} = httpContext.Request.Query[\"{queryParamName}\"];");

            context.If($"{queryVar}.Count > 0", ifCtx =>
            {
                ifCtx.Try(tryCtx =>
                {
                    var assignment = FormatPropertyAssignment(
                        operationExpr,
                        entry,
                        $"({fqType})System.ComponentModel.TypeDescriptor.GetConverter(typeof({fqType})).ConvertFromInvariantString({queryVar}[0])!");
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
                $"var {errVar} = Blueprint.Runtime.Exec.PipelineHelpers.BindQueryValue(httpContext.Request.Query, \"{queryParamName}\", out var {parsedVar}, out var {foundVar}, {lambda});");

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
        var elementDisplay = elementType.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();

        if (elementDisplay == "string")
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
                $"var {errVar} = Blueprint.Runtime.Exec.PipelineHelpers.BindQueryArray(httpContext.Request.Query, \"{queryParamName}\", out var {arrVar}, {lambda});");

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
                $"var {errVar} = Blueprint.Runtime.Exec.PipelineHelpers.BindHeaderValue(httpContext.Request.Headers, \"{headerName}\", out var {parsedVar}, out var {foundVar}, {lambda});");

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
                $"var {errVar} = Blueprint.Runtime.Exec.PipelineHelpers.BindCookieValue(httpContext.Request.Cookies, \"{cookieName}\", out var {parsedVar}, out var {foundVar}, {lambda});");

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
