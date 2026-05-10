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
        if (!HasHttpHostExposure(context))
        {
            return;
        }

        var operationType = context.GetOperationTypeSymbol();
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

        if (!HasHttpHostExposure(operationType))
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

    private static bool HasHttpHostExposure(DescriptorContributorContext context)
    {
        return context.GetMetadata<HostExposureRegistration>()
            .Any(static r => string.Equals(r.HostName, HostExposureRegistration.Http.HostName, StringComparison.Ordinal));
    }

    private static bool HasHttpHostExposure(INamedTypeSymbol operationType)
    {
        // In the pipeline context we don't have descriptor metadata.
        // Detect HTTP exposure by checking for RootLink or SelfLink attributes.
        return operationType.GetAttributes().Any(static a =>
            a.AttributeClass?.Name is "RootLinkAttribute" or "SelfLinkAttribute");
    }

    private static List<string> ExtractRoutePlaceholders(INamedTypeSymbol operationType)
    {
        var placeholders = new List<string>();

        foreach (var attribute in operationType.GetAttributes())
        {
            var url = attribute.AttributeClass?.Name switch
            {
                "RootLinkAttribute" when attribute.ConstructorArguments.Length > 0 => attribute.ConstructorArguments[0].Value as string,
                "SelfLinkAttribute" when attribute.ConstructorArguments.Length > 1 => attribute.ConstructorArguments[1].Value as string,
                _ => null,
            };

            if (url is null)
            {
                continue;
            }

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
        foreach (var attribute in operationType.GetAttributes())
        {
            var url = attribute.AttributeClass?.Name switch
            {
                "RootLinkAttribute" when attribute.ConstructorArguments.Length > 0 => attribute.ConstructorArguments[0].Value as string,
                "SelfLinkAttribute" when attribute.ConstructorArguments.Length > 1 => attribute.ConstructorArguments[1].Value as string,
                _ => null,
            };

            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var openCount = 0;

            for (var i = 0; i < url!.Length; i++)
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

        foreach (var member in operationType.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.IsStatic || member.IsReadOnly || member.SetMethod is null || member.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            if (IsSystemProperty(member))
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

            var explicitCount = (fromRoute != null ? 1 : 0) + (fromQuery != null ? 1 : 0) + (fromBody != null ? 1 : 0) + (fromHeader != null ? 1 : 0) + (fromCookie != null ? 1 : 0);

            // Step 2 — conflict: more than one [FromX] attribute on the same property
            if (explicitCount > 1)
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

            // Step 3 — exactly one explicit attribute: validate and add entry
            if (explicitCount == 1)
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

        // Named argument Name overrides the default camelCase property name
        var nameOverride = attribute.NamedArguments
            .FirstOrDefault(static a => a.Key == "Name").Value.Value as string;

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
        return type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString() == "string";
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
            || ImplementsIParsable(type);
    }

    private static bool IsSupportedQueryType(ITypeSymbol type)
    {
        // Arrays of supported scalar types
        if (type is IArrayTypeSymbol arrayType)
        {
            return IsSupportedScalarType(arrayType.ElementType);
        }

        // Nullable<T> where T is a supported scalar
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && type is INamedTypeSymbol namedType)
        {
            return IsSupportedScalarType(namedType.TypeArguments[0]);
        }

        return IsSupportedScalarType(type);
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
            || ImplementsIParsable(type);
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

    /// <summary>
    /// Checks whether a type implements <c>IParsable&lt;T&gt;</c> (System.IParsable),
    /// which ASP.NET Core uses for binding route/query values from strings.
    /// </summary>
    private static bool ImplementsIParsable(ITypeSymbol type)
    {
        return type.AllInterfaces.Any(
            static i => i is { IsGenericType: true, MetadataName: "IParsable`1", ContainingNamespace.Name: "System" });
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
    /// Unified population renderer that emits extraction code for all binding sources (body, route, query,
    /// header, cookie) in a single pipeline stage. Blank lines separate logical sections for readability
    /// in the generated output.
    /// </summary>
    private static void RenderPopulation(PipelineRenderContext context, List<PropertyOwnershipEntry> entries)
    {
        var bodyEntries = entries.Where(static e => e.Source == PropertyOwnershipSource.Body).ToArray();
        var routeEntries = entries.Where(static e => e.Source == PropertyOwnershipSource.Route).ToArray();
        var queryEntries = entries.Where(static e => e.Source == PropertyOwnershipSource.Query).ToArray();
        var headerEntries = entries.Where(static e => e.Source == PropertyOwnershipSource.Header).ToArray();
        var cookieEntries = entries.Where(static e => e.Source == PropertyOwnershipSource.Cookie).ToArray();
        var operationExpr = context.GetOperationExpression();

        // Ensure httpContext variable is declared (shared across modules via GetHttpContextExpression).
        context.GetHttpContextExpression();
        context.WriteBlankLine();

        // Body binding: buffer stream async, then parse with Utf8JsonReader
        if (bodyEntries.Length > 0)
        {
            context.If(
                "httpContext.Request.ContentLength > 0 || httpContext.Request.ContentType != null",
                bodyContext =>
                {
                    bodyContext.If(
                        "httpContext.Request.ContentType != null && !httpContext.Request.ContentType.Contains(\"json\", System.StringComparison.OrdinalIgnoreCase)",
                        unsupportedCtx =>
                        {
                            unsupportedCtx.Return("new Blueprint.Runtime.Exec.UnsupportedMediaTypeResult()");
                        });

                    bodyContext.WriteBlankLine();

                    bodyContext.WriteStatement("using var bodyStream = httpContext.Request.ContentLength.HasValue");
                    bodyContext.WriteStatement("    ? new System.IO.MemoryStream((int)httpContext.Request.ContentLength.Value)");
                    bodyContext.WriteStatement("    : new System.IO.MemoryStream();");
                    bodyContext.WriteStatement("await httpContext.Request.Body.CopyToAsync(bodyStream, context.OperationCancelled);");
                    bodyContext.WriteBlankLine();

                    bodyContext.If("bodyStream.Length > 0", lengthCtx =>
                    {
                        lengthCtx.Try(tryCtx =>
                        {
                            tryCtx.WriteStatement($"PopulateFromJsonBody(new System.ReadOnlySpan<byte>(bodyStream.GetBuffer(), 0, (int)bodyStream.Length), {operationExpr});");
                        });

                        lengthCtx.Catch("System.Text.Json.JsonException", catchCtx =>
                        {
                            catchCtx.Return("new Blueprint.Runtime.Exec.BindingFailureResult(\"Invalid JSON body\")");
                        });

                        lengthCtx.Catch("System.InvalidOperationException", catchCtx =>
                        {
                            catchCtx.Return("new Blueprint.Runtime.Exec.BindingFailureResult(\"Invalid JSON body\")");
                        });

                        lengthCtx.Catch("System.NotSupportedException", catchCtx =>
                        {
                            catchCtx.Return("new Blueprint.Runtime.Exec.BindingFailureResult(\"Invalid JSON body\")");
                        });
                    });
                });

            context.WriteBlankLine();
        }

        // Route binding
        foreach (var entry in routeEntries)
        {
            WriteRouteExtraction(context, operationExpr, entry);
        }

        if (routeEntries.Length > 0 && (queryEntries.Length > 0 || headerEntries.Length > 0 || cookieEntries.Length > 0))
        {
            context.WriteBlankLine();
        }

        // Query binding
        foreach (var entry in queryEntries)
        {
            WriteQueryExtraction(context, operationExpr, entry);
        }

        // Header binding
        foreach (var entry in headerEntries)
        {
            WriteHeaderExtraction(context, operationExpr, entry);
        }

        // Cookie binding
        foreach (var entry in cookieEntries)
        {
            WriteCookieExtraction(context, operationExpr, entry);
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
            .AddParameter(operationTypeFull, "operation");

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
                readerExpr = $"System.Text.Json.JsonSerializer.Deserialize<{fullyQualifiedType}>(ref reader)";
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
        else
        {
            var tryParseExpr = GetScalarTryParseExpression(entry.TypeSymbol, $"routeStr_{entry.PropertyName}", $"_parsed_{entry.PropertyName}");

            context.If($"{routeValueExpr} is string routeStr_{entry.PropertyName}", ifCtx =>
            {
                ifCtx.If(tryParseExpr, parsedCtx =>
                {
                    parsedCtx.WriteStatement(FormatPropertyAssignment(operationExpr, entry, $"_parsed_{entry.PropertyName}"));
                });
                ifCtx.Else(elseCtx =>
                {
                    elseCtx.Return($"new Blueprint.Runtime.Exec.BindingFailureResult(\"Invalid route parameter '{entry.BindingName}'\")");
                });
            });
        }
    }

    private static void WriteQueryExtraction(PipelineRenderContext context, string operationExpr, PropertyOwnershipEntry entry)
    {
        var typeSymbol = entry.TypeSymbol;
        var queryParamName = entry.BindingName!;
        var queryVar = $"_qv_{entry.PropertyName}";

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            WriteArrayQueryExtraction(context, operationExpr, entry, arrayType, queryVar, queryParamName);
            return;
        }

        // Unwrap Nullable<T> to get the inner scalar type for parsing
        var effectiveType = typeSymbol;
        if (typeSymbol.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && typeSymbol is INamedTypeSymbol namedType)
        {
            effectiveType = namedType.TypeArguments[0];
        }

        var effectiveDisplay = effectiveType.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();

        context.WriteStatement($"var {queryVar} = httpContext.Request.Query[\"{queryParamName}\"];");

        if (effectiveDisplay == "string")
        {
            context.If($"{queryVar}.Count > 0", ifCtx =>
            {
                ifCtx.WriteStatement(FormatPropertyAssignment(operationExpr, entry, $"{queryVar}[0]"));
            });
        }
        else
        {
            var tryParseExpr = GetScalarTryParseExpression(effectiveType, $"{queryVar}[0]", $"_parsed_{entry.PropertyName}");

            context.If($"{queryVar}.Count > 0", ifCtx =>
            {
                ifCtx.If(tryParseExpr, parsedCtx =>
                {
                    parsedCtx.WriteStatement(FormatPropertyAssignment(operationExpr, entry, $"_parsed_{entry.PropertyName}"));
                });
                ifCtx.Else(elseCtx =>
                {
                    elseCtx.Return($"new Blueprint.Runtime.Exec.BindingFailureResult(\"Invalid query parameter '{entry.BindingName}'\")");
                });
            });
        }
    }

    private static void WriteArrayQueryExtraction(
        PipelineRenderContext context,
        string operationExpr,
        PropertyOwnershipEntry entry,
        IArrayTypeSymbol arrayType,
        string queryVar,
        string queryParamName)
    {
        var elementType = arrayType.ElementType;
        var elementDisplay = elementType.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();

        context.WriteStatement($"var {queryVar} = httpContext.Request.Query[\"{queryParamName}\"];");

        context.If($"{queryVar}.Count > 0", ifCtx =>
        {
            if (elementDisplay == "string")
            {
                ifCtx.WriteStatement(FormatPropertyAssignment(operationExpr, entry, $"{queryVar}.ToArray()"));
            }
            else
            {
                var tryParseExpr = GetScalarTryParseExpression(elementType, $"{queryVar}[_i]", $"_parsed_{entry.PropertyName}");

                ifCtx.WriteStatement($"var _arr_{entry.PropertyName} = new {elementDisplay}[{queryVar}.Count];");
                ifCtx.Block($"for (var _i = 0; _i < {queryVar}.Count; _i++)", forCtx =>
                {
                    forCtx.If($"{tryParseExpr}", parsedCtx =>
                    {
                        parsedCtx.WriteStatement($"_arr_{entry.PropertyName}[_i] = _parsed_{entry.PropertyName};");
                    });

                    forCtx.Else(elseCtx =>
                    {
                        elseCtx.Return($"new Blueprint.Runtime.Exec.BindingFailureResult(\"Invalid query parameter '{entry.BindingName}'\")");
                    });
                });
                ifCtx.WriteStatement(FormatPropertyAssignment(operationExpr, entry, $"_arr_{entry.PropertyName}"));
            }
        });
    }

    /// <summary>
    /// Emits extraction code for a header-bound property. Headers use <c>IHeaderDictionary</c> which
    /// returns <c>StringValues</c> — the same Count-based pattern as query parameters, taking the first value.
    /// </summary>
    private static void WriteHeaderExtraction(PipelineRenderContext context, string operationExpr, PropertyOwnershipEntry entry)
    {
        var headerName = entry.BindingName!;
        var headerVar = $"_hv_{entry.PropertyName}";

        context.WriteStatement($"var {headerVar} = httpContext.Request.Headers[\"{headerName}\"];");

        if (IsStringType(entry.TypeSymbol))
        {
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

            var tryParseExpr = GetScalarTryParseExpression(effectiveType, $"{headerVar}[0]", $"_parsed_{entry.PropertyName}");

            context.If($"{headerVar}.Count > 0", ifCtx =>
            {
                ifCtx.If(tryParseExpr, parsedCtx =>
                {
                    parsedCtx.WriteStatement(FormatPropertyAssignment(operationExpr, entry, $"_parsed_{entry.PropertyName}"));
                });
                ifCtx.Else(elseCtx =>
                {
                    elseCtx.Return($"new Blueprint.Runtime.Exec.BindingFailureResult(\"Invalid header '{entry.BindingName}'\")");
                });
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
        var cookieVar = $"_cv_{entry.PropertyName}";

        context.WriteStatement($"var {cookieVar} = httpContext.Request.Cookies[\"{cookieName}\"];");

        if (IsStringType(entry.TypeSymbol))
        {
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

            var tryParseExpr = GetScalarTryParseExpression(effectiveType, cookieVar, $"_parsed_{entry.PropertyName}");

            context.If($"{cookieVar} != null", ifCtx =>
            {
                ifCtx.If(tryParseExpr, parsedCtx =>
                {
                    parsedCtx.WriteStatement(FormatPropertyAssignment(operationExpr, entry, $"_parsed_{entry.PropertyName}"));
                });
                ifCtx.Else(elseCtx =>
                {
                    elseCtx.Return($"new Blueprint.Runtime.Exec.BindingFailureResult(\"Invalid cookie '{entry.BindingName}'\")");
                });
            });
        }
    }

    /// <summary>
    /// Returns a TryParse expression that avoids exception-handling overhead for malformed query values.
    /// The expression evaluates to <c>true</c> when parsing succeeds and writes the result into <paramref name="outVariable"/>.
    /// </summary>
    private static string GetScalarTryParseExpression(ITypeSymbol typeSymbol, string stringExpression, string outVariable)
    {
        var typeDisplay = typeSymbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();

        return typeDisplay switch
        {
            "int" => $"int.TryParse({stringExpression}, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var {outVariable})",
            "long" => $"long.TryParse({stringExpression}, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var {outVariable})",
            "double" => $"double.TryParse({stringExpression}, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var {outVariable})",
            "float" => $"float.TryParse({stringExpression}, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var {outVariable})",
            "bool" => $"bool.TryParse({stringExpression}, out var {outVariable})",
            "decimal" => $"decimal.TryParse({stringExpression}, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var {outVariable})",
            "System.Guid" => $"System.Guid.TryParse({stringExpression}, out var {outVariable})",
            "System.DateTime" => $"System.DateTime.TryParse({stringExpression}, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var {outVariable})",
            "System.DateTimeOffset" => $"System.DateTimeOffset.TryParse({stringExpression}, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var {outVariable})",
            _ when typeSymbol.TypeKind == TypeKind.Enum =>
                $"System.Enum.TryParse<{typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>({stringExpression}, out var {outVariable})",
            _ when ImplementsIParsable(typeSymbol) =>
                $"{typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.TryParse({stringExpression}, null, out var {outVariable})",
            _ => $"int.TryParse({stringExpression}, out var {outVariable})", // fallback — should not be reached for supported types
        };
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
        bool IsInitOnly = false);

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
