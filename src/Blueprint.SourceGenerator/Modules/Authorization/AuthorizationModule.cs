using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules.Authentication;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Modules.Authorization;

/// <summary>
/// Detects <c>[Resource("noun", "verb")]</c> and <c>[FromResource(typeof(X))]</c> attributes on
/// operations and projects authorization scope-pair metadata into descriptors.
/// Multiple <c>[Resource]</c> attributes on a single operation use any-of semantics.
/// <c>[FromResource]</c> inherits scope pairs from the referenced operation's direct
/// <c>[Resource]</c> declarations (not transitive).
/// <para>
/// Reports BPAU003 when resource authorization is combined with <c>[AllowAnonymous]</c>,
/// BPAU004 when noun or verb values are empty or whitespace,
/// BPAU005 when both <c>[FromResource]</c> and <c>[Resource]</c> are used on the same operation,
/// BPAU006 when <c>[FromResource]</c> references a type that has no <c>[Resource]</c> attributes,
/// and BPAU008 when resource authorization is declared on a non-HTTP operation.
/// </para>
/// <para>
/// Permissioned operations implicitly require authentication; this module coordinates
/// with <see cref="AuthenticationModule"/> via the shared
/// <see cref="AuthenticationDescriptorRegistration"/> metadata.
/// </para>
/// </summary>
public sealed class AuthorizationModule : IBlueprintSourceGeneratorModule, IDescriptorContributor, IPipelineContributor
{
    private const string ResourceAttributeName = "ResourceAttribute";
    private const string FromResourceAttributeName = "FromResourceAttribute";
    private const string AllowAnonymousAttributeName = "AllowAnonymousAttribute";
    private const string LinkAttributeName = "LinkAttribute";
    private const string SelfLinkAttributeName = "SelfLinkAttribute";

    /// <summary>
    /// Whether a property path exists on a linked resource type and, if it does, whether generated code can read it.
    /// </summary>
    private enum PropertyPathState
    {
        /// <summary>A segment is not a public instance property of the type.</summary>
        Missing,

        /// <summary>Every segment exists, but one has no public getter.</summary>
        Unreadable,

        /// <summary>Every segment is a public instance property with a public getter.</summary>
        Readable,
    }

    /// <inheritdoc />
    public string ModuleId => nameof(AuthorizationModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(AuthorizationModule);

    /// <inheritdoc />
    /// <remarks>
    /// Must run after <see cref="AuthenticationModule"/> (Order 50) so that authentication
    /// metadata is already projected when we check for conflicts.
    /// </remarks>
    public int Order => 60;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        var operationLocation = operationType.Locations.FirstOrDefault();
        var directResourceAttributes = GetResourceAttributes(operationType);
        var fromResourceResult = GetFromResourceTarget(operationType);

        // BPAU005: Both [FromResource] and [Resource] on the same operation
        if (fromResourceResult is not null && directResourceAttributes.Count > 0)
        {
            context.AddDiagnostic(
                new DiagnosticInfo(
                    AuthorizationDiagnosticDescriptors.FromResourceWithDirectResource,
                    operationLocation,
                    [operationType.Name]));

            return;
        }

        // Resolve the effective scope pairs — either direct or inherited
        List<(string Noun, string Verb, string? ResourceKeyTemplate)> effectiveScopePairs;

        if (fromResourceResult is not null)
        {
            var (fromTarget, templateOverride) = fromResourceResult.Value;

            // [FromResource(typeof(X))] — inherit from the referenced type's direct [Resource] attrs
            var targetResourceAttributes = GetResourceAttributes(fromTarget);

            if (targetResourceAttributes.Count == 0)
            {
                context.AddDiagnostic(
                    new DiagnosticInfo(
                        AuthorizationDiagnosticDescriptors.FromResourceTargetHasNoResource,
                        operationLocation,
                        [operationType.Name, fromTarget.Name]));

                return;
            }

            // When [FromResource] specifies a template override, apply it to all inherited pairs
            if (templateOverride is not null)
            {
                effectiveScopePairs = targetResourceAttributes
                    .Select(sp => (sp.Noun, sp.Verb, (string?)templateOverride))
                    .ToList();
            }
            else
            {
                effectiveScopePairs = targetResourceAttributes;
            }
        }
        else if (directResourceAttributes.Count > 0)
        {
            effectiveScopePairs = directResourceAttributes;
        }
        else
        {
            return;
        }

        // Validate noun/verb values (only for direct [Resource]; inherited values were validated on the source)
        if (fromResourceResult is null)
        {
            var hasValidationErrors = false;

            foreach (var (noun, verb, _) in effectiveScopePairs)
            {
                if (string.IsNullOrWhiteSpace(noun))
                {
                    context.AddDiagnostic(
                        new DiagnosticInfo(
                            AuthorizationDiagnosticDescriptors.EmptyResourceScopePairValue,
                            operationLocation,
                            [operationType.Name, "noun"]));

                    hasValidationErrors = true;
                }

                if (string.IsNullOrWhiteSpace(verb))
                {
                    context.AddDiagnostic(
                        new DiagnosticInfo(
                            AuthorizationDiagnosticDescriptors.EmptyResourceScopePairValue,
                            operationLocation,
                            [operationType.Name, "verb"]));

                    hasValidationErrors = true;
                }
            }

            if (hasValidationErrors)
            {
                return;
            }

            // Check for duplicate noun+verb pairs — same scope pair declared twice is almost always a copy-paste error
            var seen = new HashSet<(string Noun, string Verb)>();

            foreach (var (noun, verb, _) in effectiveScopePairs)
            {
                if (!seen.Add((noun.ToUpperInvariant(), verb.ToUpperInvariant())))
                {
                    context.AddDiagnostic(
                        new DiagnosticInfo(
                            AuthorizationDiagnosticDescriptors.DuplicateResourceScopePair,
                            operationLocation,
                            [operationType.Name, noun, verb]));
                }
            }
        }

        // Check for conflict with [AllowAnonymous]
        var hasAllowAnonymous = HasAttribute(operationType, AllowAnonymousAttributeName);

        if (hasAllowAnonymous)
        {
            context.AddDiagnostic(
                new DiagnosticInfo(
                    AuthorizationDiagnosticDescriptors.ResourceWithAllowAnonymous,
                    operationLocation,
                    [operationType.Name]));

            return;
        }

        // Permissioned operations implicitly require authentication.
        // Only add auth metadata if AuthenticationModule hasn't already projected it
        // (e.g. when [MustBeAuthenticated] is also present).
        var alreadyHasAuthRegistration = context.GetMetadata<AuthenticationDescriptorRegistration>()
            .Any(static r => r.RequiresIdentityCheck);

        if (!alreadyHasAuthRegistration)
        {
            context.AddMetadata(new AuthenticationDescriptorRegistration(RequiresIdentityCheck: true));
        }

        context.AddMetadata(new AuthorizationDescriptorRegistration(RequiresResourceAuthorization: true));

        // Project scope pairs and authentication feature data
        var scopePairEntries = string.Join(
            ", ",
            effectiveScopePairs.Select(static sp => sp.ResourceKeyTemplate is not null
                ? $"new global::Blueprint.Model.ScopePair(\"{sp.Noun}\", \"{sp.Verb}\", \"{sp.ResourceKeyTemplate}\")"
                : $"new global::Blueprint.Model.ScopePair(\"{sp.Noun}\", \"{sp.Verb}\")"));

        var linkResolverBranches = BuildLinkScopeResolverBranches(operationType, operationLocation, effectiveScopePairs, context);
        var listingPairs = string.Join(", ", effectiveScopePairs.Select(static sp => sp.ResourceKeyTemplate is null
            ? $"new global::Blueprint.Model.ResolvedScopePair(\"{sp.Noun}\", \"{sp.Verb}\", null, null)"
            : $"new global::Blueprint.Model.ResolvedScopePair(\"{sp.Noun}\", \"{sp.Verb}\", \"{sp.ResourceKeyTemplate}\", {BuildListingPrefixExpression(sp.ResourceKeyTemplate)})"));

        context.AddRenderStep(writer =>
        {
            if (!alreadyHasAuthRegistration)
            {
                writer.AppendLine(
                    "this.SetFeatureData(global::Blueprint.Model.WellKnownFeatureData.RequiresAuthentication);");
            }

            writer.AppendLine(
                $"this.SetFeatureData(new global::Blueprint.Model.OperationAuthorizationFeatureData(global::System.Collections.Immutable.ImmutableArray.Create<global::Blueprint.Model.ScopePair>([{scopePairEntries}])));");

            if (linkResolverBranches.Count > 0)
            {
                RenderLinkScopeResolver(writer, linkResolverBranches);
            }

            writer.AppendLine(
                $"this.SetFeatureData(new global::Blueprint.Model.OperationListingAuthorizationFeatureData(static callerValue => [{listingPairs}]));");
        });
    }

    /// <summary>
    /// Builds the interpolated string of a key's literal prefix for listing an operation before any resource exists: caller
    /// (<c>Auth</c>) tokens resolve as an execution resolves them, and the key is cut at its first property token, whose
    /// value cannot be known.
    /// </summary>
    /// <remarks>
    /// A caller token's property fallback has no resource to read, so only a caller fallback is kept: a caller with no
    /// value then gives an empty segment, as at execution.
    /// </remarks>
    private static string BuildListingPrefixExpression(string template)
    {
        var prefix = new System.Text.StringBuilder();
        var position = 0;

        foreach (var token in ResourceKeyTemplateParser.ParseTokens(template))
        {
            var tokenIndex = template.IndexOf(token.RawToken, position, StringComparison.Ordinal);

            prefix.Append(template, position, tokenIndex - position);

            if (!token.IsAuthPath)
            {
                return $"$\"{prefix}\"";
            }

            var parts = new List<string> { CallerValueExpression(token.Name) };

            if (token.FallbackPropertyPath is { } fallback && ResourceKeyToken.IsAuthPropertyPath(fallback))
            {
                parts.Add(CallerValueExpression(fallback));
            }

            parts.Add("\"\"");
            prefix.Append('{').Append(string.Join(" ?? ", parts)).Append('}');
            position = tokenIndex + token.RawToken.Length;
        }

        prefix.Append(template, position, template.Length - position);

        return $"$\"{prefix}\"";
    }

    /// <summary>
    /// Builds, for each resource type the operation links from, the statement that resolves the operation's scope pairs
    /// against a resource of that type, most-derived type first so that a resource is resolved against its own type.
    /// </summary>
    /// <remarks>
    /// Each token is resolved on its own, by its complete property path. A <c>{A:B}</c> token uses <c>A</c> when the whole
    /// of path <c>A</c> exists on the resource type as public properties, and <c>B</c> only when it does not, as Signature
    /// 365's link authorisation chose it. Reports BPAU011 for a token that resolves to neither, and builds no statement for
    /// that type.
    /// </remarks>
    private static List<string> BuildLinkScopeResolverBranches(
        INamedTypeSymbol operationType,
        Location? operationLocation,
        List<(string Noun, string Verb, string? ResourceKeyTemplate)> scopePairs,
        DescriptorContributorContext context)
    {
        var branches = new List<string>();
        var linkedResourceTypes = GetLinkedResourceTypes(operationType);

        for (var index = 0; index < linkedResourceTypes.Count; index++)
        {
            var resourceType = linkedResourceTypes[index];
            var resourceExpression = $"resource{index}";
            var unresolvedTokens = new List<string>();
            var resolvedPairs = new List<string>();

            foreach (var (noun, verb, template) in scopePairs)
            {
                if (template is null)
                {
                    resolvedPairs.Add($"new global::Blueprint.Model.ResolvedScopePair(\"{noun}\", \"{verb}\", null, null)");

                    continue;
                }

                var keyExpression = BuildLinkKeyExpression(template, resourceType, resourceExpression, unresolvedTokens);

                resolvedPairs.Add($"new global::Blueprint.Model.ResolvedScopePair(\"{noun}\", \"{verb}\", \"{template}\", {keyExpression})");
            }

            foreach (var unresolved in unresolvedTokens.Distinct(StringComparer.Ordinal))
            {
                context.AddDiagnostic(
                    new DiagnosticInfo(
                        AuthorizationDiagnosticDescriptors.ResourceKeyTemplateTokenNotFoundOnLinkedResource,
                        operationLocation,
                        [operationType.Name, unresolved, resourceType.Name]));
            }

            if (unresolvedTokens.Count > 0)
            {
                continue;
            }

            var resourceTypeName = resourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            branches.Add($"if (linkResource is {resourceTypeName} {resourceExpression}) {{ return [{string.Join(", ", resolvedPairs)}]; }}");
        }

        return branches;
    }

    /// <summary>
    /// Builds the interpolated string that expands <paramref name="template"/> against a resource of
    /// <paramref name="resourceType"/>, adding to <paramref name="unresolvedTokens"/> each token that resolves to nothing.
    /// </summary>
    /// <remarks>
    /// A caller (<c>Auth</c>) path calls the runtime's <c>callerValue</c> lookup and always resolves; a property fallback
    /// after it is used when the caller has no value and the path exists on the type. A property path is used alone when it
    /// exists, whatever its value, and its fallback only when it does not.
    /// </remarks>
    private static string BuildLinkKeyExpression(
        string template,
        INamedTypeSymbol resourceType,
        string resourceExpression,
        List<string> unresolvedTokens)
    {
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        if (tokens.Count == 0)
        {
            return $"\"{template}\"";
        }

        var result = template;

        foreach (var token in tokens)
        {
            var parts = new List<string>();
            var fallback = token.FallbackPropertyPath;

            var (primaryState, primaryExpression) = LinkPathExpression(token.Name, resourceType, resourceExpression);

            if (token.IsAuthPath)
            {
                parts.Add(primaryExpression!);

                if (fallback is not null)
                {
                    var (fallbackState, fallbackExpression) = LinkPathExpression(fallback, resourceType, resourceExpression);

                    if (fallbackState == PropertyPathState.Unreadable)
                    {
                        unresolvedTokens.Add(token.RawToken.Trim('{', '}'));

                        continue;
                    }

                    if (fallbackState == PropertyPathState.Readable)
                    {
                        parts.Add(fallbackExpression!);
                    }
                }
            }
            else if (primaryState == PropertyPathState.Readable)
            {
                parts.Add(primaryExpression!);
            }
            else if (primaryState == PropertyPathState.Missing
                     && fallback is not null
                     && LinkPathExpression(fallback, resourceType, resourceExpression) is (PropertyPathState.Readable, { } fallbackExpression))
            {
                parts.Add(fallbackExpression);
            }
            else
            {
                // Neither path can be read. A primary that exists but cannot be read is not treated as missing, which
                // would silently switch the key to the fallback.
                unresolvedTokens.Add(token.RawToken.Trim('{', '}'));

                continue;
            }

            parts.Add("\"\"");
            result = result.Replace(token.RawToken, "{" + string.Join(" ?? ", parts) + "}");
        }

        return $"$\"{result}\"";
    }

    /// <summary>
    /// Gives the expression of a caller or property path against the resource, and whether the path can be read: a caller
    /// path always can; a property path can when every segment is a public instance property with a public getter.
    /// </summary>
    private static (PropertyPathState State, string? Expression) LinkPathExpression(string path, INamedTypeSymbol resourceType, string resourceExpression)
    {
        if (ResourceKeyToken.IsAuthPropertyPath(path))
        {
            return (PropertyPathState.Readable, CallerValueExpression(path));
        }

        var (state, properties) = ResolvePublicPropertyPath(resourceType, path);

        if (state != PropertyPathState.Readable)
        {
            return (state, null);
        }

        // Each step reads the next property with ?. only where the value before it can be null (a reference or a
        // Nullable<T>), so a struct's members and a non-nullable value are read with . and the expression compiles. A name
        // that is a C# keyword is written verbatim (@event), as its declaration was.
        var expression = new System.Text.StringBuilder(resourceExpression);
        var receiverCanBeNull = false;

        foreach (var property in properties)
        {
            expression.Append(receiverCanBeNull ? "?." : ".");

            if (SyntaxFacts.GetKeywordKind(property.Name) != SyntaxKind.None)
            {
                expression.Append('@');
            }

            expression.Append(property.Name);
            receiverCanBeNull = CanBeNull(property.Type);
        }

        expression.Append(receiverCanBeNull ? "?.ToString()" : ".ToString()");

        return (PropertyPathState.Readable, expression.ToString());
    }

    private static string CallerValueExpression(string authPath)
    {
        return $"callerValue(\"{ResourceKeyToken.GetAuthPropertyName(authPath)}\")";
    }

    private static bool CanBeNull(ITypeSymbol type)
    {
        return !type.IsValueType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }

    /// <summary>
    /// Writes the operation's <c>OperationLinkAuthorizationFeatureData</c>: a resolver that tries each linked resource type
    /// in turn and gives <see langword="null"/> for a resource of none of them.
    /// </summary>
    private static void RenderLinkScopeResolver(ICodeWriter writer, List<string> branches)
    {
        writer.AppendLine("this.SetFeatureData(new global::Blueprint.Model.OperationLinkAuthorizationFeatureData(static (linkResource, callerValue) =>");
        writer.AppendLine("{");

        foreach (var branch in branches)
        {
            writer.AppendLine("    " + branch);
        }

        writer.AppendLine("    return null;");
        writer.AppendLine("}));");
    }

    /// <summary>
    /// Gives the resource types the operation links from through <c>[Link]</c> and <c>[SelfLink]</c>, each once, ordered
    /// so that a type comes before any of its base types.
    /// </summary>
    private static List<INamedTypeSymbol> GetLinkedResourceTypes(INamedTypeSymbol operationType)
    {
        var types = new List<INamedTypeSymbol>();

        foreach (var attribute in operationType.GetAttributes())
        {
            var attrClass = attribute.AttributeClass;

            if (attrClass is null
                || (attrClass.Name != LinkAttributeName && attrClass.Name != SelfLinkAttributeName)
                || !attrClass.IsInNamespace("Blueprint", "Model", "Attributes")
                || attribute.ConstructorArguments.Length < 1)
            {
                continue;
            }

            var resourceTypeArgument = attribute.ConstructorArguments[0];

            if (resourceTypeArgument.Kind != TypedConstantKind.Type
                || resourceTypeArgument.Value is not INamedTypeSymbol resourceType
                || resourceType.TypeKind == TypeKind.Error
                || !resourceType.AllInterfaces.Any(static i => i.Name == "ILinkableResource")
                || types.Contains(resourceType, SymbolEqualityComparer.Default))
            {
                continue;
            }

            types.Add(resourceType);
        }

        return types.OrderByDescending(static t => InheritanceDepth(t)).ToList();
    }

    private static int InheritanceDepth(INamedTypeSymbol type)
    {
        var depth = 0;

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            depth++;
        }

        return depth;
    }

    /// <summary>
    /// Resolves every segment of a dotted property path to a public instance property, starting at
    /// <paramref name="type"/>, matching names exactly, as the generated access needs it and as Signature 365's link
    /// authorisation looked them up. An interface's members include those it inherits from other interfaces, and a
    /// <see cref="Nullable{T}"/> segment is navigated through its underlying type.
    /// </summary>
    /// <returns>
    /// <see cref="PropertyPathState.Missing"/> when a segment does not exist; <see cref="PropertyPathState.Unreadable"/>
    /// when one exists but has no public getter; otherwise <see cref="PropertyPathState.Readable"/> with the properties.
    /// </returns>
    private static (PropertyPathState State, List<IPropertySymbol> Properties) ResolvePublicPropertyPath(ITypeSymbol type, string propertyPath)
    {
        var properties = new List<IPropertySymbol>();
        var current = type;

        foreach (var segment in propertyPath.Split('.'))
        {
            if (current is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            {
                current = nullable.TypeArguments[0];
            }

            var candidates = current.GetAllMembers().OfType<IPropertySymbol>();

            if (current.TypeKind == TypeKind.Interface)
            {
                candidates = candidates.Concat(current.AllInterfaces.SelectMany(static i => i.GetMembers().OfType<IPropertySymbol>()));
            }

            var property = candidates.FirstOrDefault(p =>
                p.Name == segment && !p.IsStatic && !p.IsIndexer && p.DeclaredAccessibility == Accessibility.Public);

            if (property is null)
            {
                return (PropertyPathState.Missing, properties);
            }

            if (property.GetMethod is not { DeclaredAccessibility: Accessibility.Public })
            {
                return (PropertyPathState.Unreadable, properties);
            }

            properties.Add(property);
            current = property.Type;
        }

        return (PropertyPathState.Readable, properties);
    }

    /// <summary>
    /// Whether some resource type the operation links from lacks <paramref name="token"/>'s primary path and can read its
    /// fallback path, so that a link from it uses the fallback.
    /// </summary>
    private static bool IsFallbackUsedByALinkedResourceType(INamedTypeSymbol operationType, ResourceKeyToken token)
    {
        return GetLinkedResourceTypes(operationType).Any(resourceType =>
            ResolvePublicPropertyPath(resourceType, token.Name).State == PropertyPathState.Missing
            && ResolvePublicPropertyPath(resourceType, token.FallbackPropertyPath!).State == PropertyPathState.Readable);
    }

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        var operationType = context.Operation.TypeSymbol;
        var operationLocation = operationType.Locations.FirstOrDefault();
        var directResourceAttributes = GetResourceAttributes(operationType);
        var fromResourceResult = GetFromResourceTarget(operationType);

        // Determine if this operation has effective resource authorization
        var hasResourceAuthorization = directResourceAttributes.Count > 0 || fromResourceResult is not null;

        if (!hasResourceAuthorization)
        {
            return;
        }

        // BPAU005 conflict → no pipeline contribution
        if (fromResourceResult is not null && directResourceAttributes.Count > 0)
        {
            return;
        }

        // Conflicting or invalid attributes → no pipeline contribution
        if (HasAttribute(operationType, AllowAnonymousAttributeName))
        {
            return;
        }

        // Resolve effective scope pairs for the pipeline authorization check
        List<(string Noun, string Verb, string? ResourceKeyTemplate)> effectiveScopePairs;

        // For [FromResource], property tokens are resolved against the current operation type
        // (which may differ from the template source operation)
        if (fromResourceResult is not null)
        {
            var (fromTarget, templateOverride) = fromResourceResult.Value;
            var targetResourceAttributes = GetResourceAttributes(fromTarget);

            if (targetResourceAttributes.Count == 0)
            {
                return;
            }

            // When [FromResource] specifies a template override, apply it to all inherited pairs
            if (templateOverride is not null)
            {
                effectiveScopePairs = targetResourceAttributes
                    .Select(sp => (sp.Noun, sp.Verb, (string?)templateOverride))
                    .ToList();
            }
            else
            {
                effectiveScopePairs = targetResourceAttributes;
            }
        }
        else
        {
            effectiveScopePairs = directResourceAttributes;
        }

        if (!HttpHostExposureHelper.HasHttpHostExposure(operationType))
        {
            // BPAU008: Resource authorization requires an HTTP context to extract the principal.
            // Descriptor metadata is still projected above, but the pipeline check is absent.
            context.AddDiagnostic(
                new DiagnosticInfo(
                    AuthorizationDiagnosticDescriptors.ResourceOnNonHttpOperation,
                    operationLocation,
                    [operationType.Name]));

            return;
        }

        // Validate template property tokens exist on the operation type
        var hasTemplateValidationErrors = false;
        var valueTypePropertyNames = new HashSet<string>();
        var fallbackOnlyTokenNames = new HashSet<string>();
        var invalidFallbackTokenNames = new HashSet<string>();

        foreach (var (_, _, template) in effectiveScopePairs)
        {
            if (template is null)
            {
                continue;
            }

            var tokens = ResourceKeyTemplateParser.ParseTokens(template);

            foreach (var token in tokens)
            {
                // Auth paths don't reference operation properties — skip validation
                if (token.IsAuthPath)
                {
                    // But validate fallback if it's a non-auth path
                    if (token.FallbackPropertyPath is not null && !ResourceKeyToken.IsAuthPropertyPath(token.FallbackPropertyPath))
                    {
                        ValidatePropertyPath(
                            token.FallbackPropertyPath,
                            operationType,
                            operationLocation,
                            context,
                            ref hasTemplateValidationErrors,
                            valueTypePropertyNames);
                    }

                    continue;
                }

                // Validate the primary property path (first segment must exist on operation)
                var primaryExists = PropertyPathExists(token.Name, operationType);

                if (!primaryExists)
                {
                    // Primary path not found — check if a valid fallback exists.
                    // When the fallback is an auth path, we can still generate code using
                    // only the fallback expression (no operation property needed).
                    if (token.FallbackPropertyPath is not null && ResourceKeyToken.IsAuthPropertyPath(token.FallbackPropertyPath))
                    {
                        fallbackOnlyTokenNames.Add(token.Name);
                    }
                    else
                    {
                        context.AddDiagnostic(
                            new DiagnosticInfo(
                                AuthorizationDiagnosticDescriptors.ResourceKeyTemplatePropertyNotFound,
                                operationLocation,
                                [operationType.Name, token.Name.Split('.')[0]]));

                        hasTemplateValidationErrors = true;
                    }
                }
                else
                {
                    if (IsValueTypeProperty(token.Name, operationType))
                    {
                        valueTypePropertyNames.Add(token.Name.Split('.')[0]);
                    }
                }

                // Validate fallback path if present and not an auth path.
                // When the primary exists, an invalid fallback should not suppress code
                // generation — just emit primary-only code and warn about the dead fallback.
                if (token.FallbackPropertyPath is not null && !ResourceKeyToken.IsAuthPropertyPath(token.FallbackPropertyPath))
                {
                    if (primaryExists)
                    {
                        // Primary is valid; fallback is optional. Validate but don't block code gen.
                        var fallbackSegment = token.FallbackPropertyPath.Split('.')[0];
                        var fallbackProperty = operationType.GetAllMembers()
                            .OfType<IPropertySymbol>()
                            .FirstOrDefault(p => p.Name == fallbackSegment);

                        if (fallbackProperty is null)
                        {
                            // Dead only if no linked resource type uses it either: a link from a type that lacks the
                            // primary resolves to the fallback
                            if (!IsFallbackUsedByALinkedResourceType(operationType, token))
                            {
                                context.AddDiagnostic(
                                    new DiagnosticInfo(
                                        AuthorizationDiagnosticDescriptors.ResourceKeyTemplateFallbackPropertyNotFound,
                                        operationLocation,
                                        [operationType.Name, fallbackSegment]));
                            }

                            // Track as invalid so code gen skips the fallback expression
                            invalidFallbackTokenNames.Add(token.Name);
                        }
                        else if (fallbackProperty.Type.IsValueType)
                        {
                            valueTypePropertyNames.Add(fallbackSegment);
                        }
                    }
                    else
                    {
                        // Primary doesn't exist — fallback failure is a hard error
                        ValidatePropertyPath(
                            token.FallbackPropertyPath,
                            operationType,
                            operationLocation,
                            context,
                            ref hasTemplateValidationErrors,
                            valueTypePropertyNames);
                    }
                }
            }
        }

        // AuthenticationModule handles identity checks for all HTTP operations (default-on).
        // AuthorizationModule only needs to add its authorization check after that.

        // Add authorization check after authentication
        context.RequireAsync();

        context.AddRenderStep(
            PipelineStage.Authorisation,
            ctx => RenderAuthorizationCheck(ctx, effectiveScopePairs, hasTemplateValidationErrors, valueTypePropertyNames, fallbackOnlyTokenNames, invalidFallbackTokenNames));
    }

    private static void RenderAuthorizationCheck(
        PipelineRenderContext context,
        List<(string Noun, string Verb, string? ResourceKeyTemplate)> scopePairs,
        bool hasTemplateValidationErrors,
        HashSet<string> valueTypePropertyNames,
        HashSet<string> fallbackOnlyTokenNames,
        HashSet<string> invalidFallbackTokenNames)
    {
        // hasTemplateValidationErrors means the primary property path is missing AND there's
        // no valid fallback. We cannot generate correct property access, so skip code emission.
        // The build will fail on the diagnostic (BPAU007 on the primary path).
        if (hasTemplateValidationErrors)
        {
            return;
        }

        RenderResolvedAuthorizationCheck(context, scopePairs, valueTypePropertyNames, fallbackOnlyTokenNames, invalidFallbackTokenNames);
    }

    /// <summary>
    /// Generates pipeline code that calls <c>PipelineHelpers.CheckResourceAuthorizationAsync</c>
    /// with compile-time–expanded <c>ResolvedScopePair</c> instances.
    /// </summary>
    private static void RenderResolvedAuthorizationCheck(
        PipelineRenderContext context,
        List<(string Noun, string Verb, string? ResourceKeyTemplate)> scopePairs,
        HashSet<string> valueTypePropertyNames,
        HashSet<string> fallbackOnlyTokenNames,
        HashSet<string> invalidFallbackTokenNames)
    {
        var operationExpr = context.GetOperationExpression();
        var httpContextExpr = context.GetHttpContextExpression();
        var principalExpr = $"{httpContextExpr}.User";

        string resolvedScopeArg;

        if (scopePairs.Count == 1)
        {
            var sp = scopePairs[0];
            resolvedScopeArg = sp.ResourceKeyTemplate is not null
                ? BuildResolvedScopePairExpression(sp, operationExpr, principalExpr, valueTypePropertyNames, fallbackOnlyTokenNames, invalidFallbackTokenNames)
                : $"new Blueprint.Model.ResolvedScopePair(\"{sp.Noun}\", \"{sp.Verb}\", null, null)";
        }
        else
        {
            var resolvedElements = string.Join(
                ", ",
                scopePairs.Select(sp => sp.ResourceKeyTemplate is not null
                    ? BuildResolvedScopePairExpression(sp, operationExpr, principalExpr, valueTypePropertyNames, fallbackOnlyTokenNames, invalidFallbackTokenNames)
                    : $"new Blueprint.Model.ResolvedScopePair(\"{sp.Noun}\", \"{sp.Verb}\", null, null)"));

            resolvedScopeArg = $"[{resolvedElements}]";
        }

        context.DeclareVariable(
            "authorizationDenial",
            $"await CheckResourceAuthorizationAsync(context, {principalExpr}, this.ResourceAuthorizationProviders, {resolvedScopeArg}, {httpContextExpr}.RequestAborted).ConfigureAwait(false)");

        context.If(
            "authorizationDenial != null",
            deniedBranch =>
            {
                deniedBranch.Return("authorizationDenial");
            });

        context.WriteBlankLine();
    }

    private static string BuildResolvedScopePairExpression(
        (string Noun, string Verb, string? ResourceKeyTemplate) sp,
        string operationExpr,
        string principalExpr,
        HashSet<string> valueTypePropertyNames,
        HashSet<string> fallbackOnlyTokenNames,
        HashSet<string> invalidFallbackTokenNames)
    {
        var tokens = ResourceKeyTemplateParser.ParseTokens(sp.ResourceKeyTemplate!);
        var expandedExpr = ResourceKeyTemplateParser.GenerateExpansionExpression(
            sp.ResourceKeyTemplate!, tokens, operationExpr, principalExpr, "context", valueTypePropertyNames, fallbackOnlyTokenNames, invalidFallbackTokenNames);

        return $"new Blueprint.Model.ResolvedScopePair(\"{sp.Noun}\", \"{sp.Verb}\", \"{sp.ResourceKeyTemplate}\", {expandedExpr})";
    }

    private static List<(string Noun, string Verb, string? ResourceKeyTemplate)> GetResourceAttributes(INamedTypeSymbol operationType)
    {
        var result = new List<(string Noun, string Verb, string? ResourceKeyTemplate)>();

        foreach (var attribute in operationType.GetAttributes())
        {
            var attrClass = attribute.AttributeClass;

            if (attrClass is null)
            {
                continue;
            }

            if (attrClass.Name != ResourceAttributeName ||
                !attrClass.IsInNamespace("Blueprint", "Model", "Attributes"))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length < 2)
            {
                continue;
            }

            var noun = attribute.ConstructorArguments[0].Value as string ?? string.Empty;
            var verb = attribute.ConstructorArguments[1].Value as string ?? string.Empty;
            var resourceKeyTemplate = attribute.ConstructorArguments.Length >= 3
                ? attribute.ConstructorArguments[2].Value as string
                : null;

            result.Add((noun, verb, resourceKeyTemplate));
        }

        return result;
    }

    /// <summary>
    /// Resolves the <c>[FromResource(typeof(X))]</c> or <c>[FromResource(typeof(X), "template")]</c>
    /// attribute on the operation, returning the referenced <see cref="INamedTypeSymbol"/> and an
    /// optional resource key template override, or <c>null</c> if the attribute is not present.
    /// </summary>
    private static (INamedTypeSymbol Target, string? TemplateOverride)? GetFromResourceTarget(INamedTypeSymbol operationType)
    {
        foreach (var attribute in operationType.GetAttributes())
        {
            var attrClass = attribute.AttributeClass;

            if (attrClass is null)
            {
                continue;
            }

            if (attrClass.Name != FromResourceAttributeName ||
                !attrClass.IsInNamespace("Blueprint", "Model", "Attributes"))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length < 1)
            {
                continue;
            }

            var typeArg = attribute.ConstructorArguments[0];

            // typeof(X) in attribute arguments: Kind == TypedConstantKind.Type, Value is ITypeSymbol
            if (typeArg.Kind != TypedConstantKind.Type || typeArg.Value is not INamedTypeSymbol targetType)
            {
                continue;
            }

            // Reject error types (unresolved references)
            if (targetType.TypeKind == TypeKind.Error)
            {
                continue;
            }

            // Check for optional second constructor argument (resource key template override)
            string? templateOverride = null;

            if (attribute.ConstructorArguments.Length >= 2 &&
                attribute.ConstructorArguments[1].Value is string template &&
                !string.IsNullOrWhiteSpace(template))
            {
                templateOverride = template;
            }

            return (targetType, templateOverride);
        }

        return null;
    }

    private static bool HasAttribute(INamedTypeSymbol operationType, string attributeName)
    {
        return operationType.GetAttributes().Any(a =>
        {
            var attrClass = a.AttributeClass;

            if (attrClass is null)
            {
                return false;
            }

            return attrClass.Name == attributeName
                   && attrClass.IsInNamespace("Blueprint", "Model", "Attributes");
        });
    }

    /// <summary>
    /// Validates that the first segment of a dot-separated property path exists on the operation type.
    /// Tracks value-type first segments for code-generation null-safety.
    /// </summary>
    private static void ValidatePropertyPath(
        string propertyPath,
        INamedTypeSymbol operationType,
        Location? operationLocation,
        PipelineContributorContext context,
        ref bool hasTemplateValidationErrors,
        HashSet<string> valueTypePropertyNames)
    {
        // Only validate the first segment — deeper segments can't be checked without type flow
        var firstSegment = propertyPath.Split('.')[0];

        var matchingProperty = operationType.GetAllMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => p.Name == firstSegment);

        if (matchingProperty is null)
        {
            context.AddDiagnostic(
                new DiagnosticInfo(
                    AuthorizationDiagnosticDescriptors.ResourceKeyTemplatePropertyNotFound,
                    operationLocation,
                    [operationType.Name, firstSegment]));

            hasTemplateValidationErrors = true;
        }
        else if (matchingProperty.Type.IsValueType)
        {
            valueTypePropertyNames.Add(firstSegment);
        }
    }

    /// <summary>
    /// Checks whether a property path's first segment exists on the operation type.
    /// Used for non-blocking validation when fallback paths are available.
    /// </summary>
    private static bool PropertyPathExists(string propertyPath, INamedTypeSymbol operationType)
    {
        var firstSegment = propertyPath.Split('.')[0];

        return operationType.GetAllMembers()
            .OfType<IPropertySymbol>()
            .Any(p => p.Name == firstSegment);
    }

    /// <summary>
    /// Checks whether a property path's first segment is a value type on the operation.
    /// </summary>
    private static bool IsValueTypeProperty(string propertyPath, INamedTypeSymbol operationType)
    {
        var firstSegment = propertyPath.Split('.')[0];

        var matchingProperty = operationType.GetAllMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => p.Name == firstSegment);

        return matchingProperty?.Type.IsValueType == true;
    }
}
