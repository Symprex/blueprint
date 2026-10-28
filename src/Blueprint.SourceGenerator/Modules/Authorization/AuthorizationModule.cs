using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules.Authentication;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

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
/// and BPAU008 when resource authorization is declared on a background task, whose descriptor allows anonymous access
/// by convention, so the declaration is never enforced.
/// </para>
/// <para>
/// An HTTP-exposed operation's check takes its principal from the request's <c>HttpContext</c>. An operation with no
/// link is executed in process only (unlinked, a Rebus message, <c>ExecuteWithNewScopeAsync</c>, nested), so its check
/// takes the principal from <c>context.UserPrincipal</c> and is skipped, as the authoriser step is, when the context
/// skips authorisation or the descriptor allows anonymous access.
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
    private const string MustBeAuthenticatedAttributeName = "MustBeAuthenticatedAttribute";
    private const string BackgroundTaskInterfaceName = "IBackgroundTask";

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
            var (fromTarget, templateOverride, _) = fromResourceResult.Value;

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

        // A [FromResource] operation records where its pairs came from, and any template it overrode them with, so an
        // app can read what the attributes declared without reflecting over them.
        var inheritanceInitializer = fromResourceResult is { } inherited
            ? inherited.DeclaredOverride is { } declaredOverride
                ? $" {{ InheritedFrom = typeof({inherited.Target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), InheritedTemplateOverride = {Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(declaredOverride, quote: true)} }}"
                : $" {{ InheritedFrom = typeof({inherited.Target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}) }}"
            : string.Empty;

        context.AddRenderStep(writer =>
        {
            if (!alreadyHasAuthRegistration)
            {
                writer.AppendLine(
                    "this.SetFeatureData(global::Blueprint.Model.WellKnownFeatureData.RequiresAuthentication);");
            }

            writer.AppendLine(
                $"this.SetFeatureData(new global::Blueprint.Model.OperationAuthorizationFeatureData(global::System.Collections.Immutable.ImmutableArray.Create<global::Blueprint.Model.ScopePair>([{scopePairEntries}])){inheritanceInitializer});");
            writer.AppendLine("this.SetFeatureData(global::Blueprint.Model.WellKnownFeatureData.PermissionedAccess);");
        });
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
            var (fromTarget, templateOverride, _) = fromResourceResult.Value;
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

        // An operation with no link is executed in process only, so its check skips a descriptor allowing anonymous access,
        // as its authoriser step does. An exposed one is checked even so, as MapBlueprintApi routes it whatever its descriptor.
        var isHttpExposed = HttpHostExposureHelper.HasHttpHostExposure(operationType);

        if (!isHttpExposed && IsBackgroundTask(operationType) && !HasAttribute(operationType, MustBeAuthenticatedAttributeName))
        {
            // BPAU008: a background task's descriptor allows anonymous access by convention, so the in-process check is
            // skipped at runtime and the declaration is never enforced. The check is still generated. [MustBeAuthenticated]
            // turns the convention off (AuthenticationModule), so such a task's declaration is enforced.
            context.AddDiagnostic(
                new DiagnosticInfo(
                    AuthorizationDiagnosticDescriptors.ResourceOnBackgroundTask,
                    operationLocation,
                    [operationType.Name]));
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
                            context.AddDiagnostic(
                                new DiagnosticInfo(
                                    AuthorizationDiagnosticDescriptors.ResourceKeyTemplateFallbackPropertyNotFound,
                                    operationLocation,
                                    [operationType.Name, fallbackSegment]));

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
            ctx => RenderAuthorizationCheck(ctx, effectiveScopePairs, hasTemplateValidationErrors, isHttpExposed, valueTypePropertyNames, fallbackOnlyTokenNames, invalidFallbackTokenNames));
    }

    private static bool IsBackgroundTask(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.Name == BackgroundTaskInterfaceName
            && candidate.IsInNamespace("Blueprint", "Tasks"));
    }

    private static void RenderAuthorizationCheck(
        PipelineRenderContext context,
        List<(string Noun, string Verb, string? ResourceKeyTemplate)> scopePairs,
        bool hasTemplateValidationErrors,
        bool isHttpExposed,
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

        RenderResolvedAuthorizationCheck(context, scopePairs, isHttpExposed, valueTypePropertyNames, fallbackOnlyTokenNames, invalidFallbackTokenNames);
    }

    /// <summary>
    /// Generates pipeline code that calls <c>PipelineHelpers.CheckOperationResourceAuthorizationAsync</c> with compile-time–
    /// expanded <c>ResolvedScopePair</c> instances. It is the same call over HTTP and in process: the helper takes the caller
    /// and cancellation from the context, which over HTTP are the request's, and the operation's exposure from the call.
    /// </summary>
    private static void RenderResolvedAuthorizationCheck(
        PipelineRenderContext context,
        List<(string Noun, string Verb, string? ResourceKeyTemplate)> scopePairs,
        bool isHttpExposed,
        HashSet<string> valueTypePropertyNames,
        HashSet<string> fallbackOnlyTokenNames,
        HashSet<string> invalidFallbackTokenNames)
    {
        var operationExpr = context.GetOperationExpression();

        // Over HTTP the context's principal is the request's user. In process it may be null, and the check refuses a caller
        // with none: the trailing '?' makes the template's claim reads null-safe, so expanding the key never fails before then.
        const string principalExpr = "context.UserPrincipal?";

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

        var checkExpr = $"CheckOperationResourceAuthorizationAsync(context, this.ResourceAuthorizationProviders, {resolvedScopeArg}, isHttpExposed: {(isHttpExposed ? "true" : "false")})";

        context.DeclareVariable(
            "authorizationDenial",
            $"await {checkExpr}.ConfigureAwait(false)");

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
    private static (INamedTypeSymbol Target, string? TemplateOverride, string? DeclaredOverride)? GetFromResourceTarget(INamedTypeSymbol operationType)
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

            // Check for optional second constructor argument (resource key template override). The override as written is
            // the declaration; a blank one is not a template, so it does not replace the source's.
            var declaredOverride = attribute.ConstructorArguments.Length >= 2
                ? attribute.ConstructorArguments[1].Value as string
                : null;
            var templateOverride = string.IsNullOrWhiteSpace(declaredOverride) ? null : declaredOverride;

            return (targetType, templateOverride, declaredOverride);
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
