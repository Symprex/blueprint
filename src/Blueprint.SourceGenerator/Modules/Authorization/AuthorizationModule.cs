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
        var directResourceAttributes = GetResourceAttributes(operationType);
        var fromResourceResult = GetFromResourceTarget(operationType);

        // BPAU005: Both [FromResource] and [Resource] on the same operation
        if (fromResourceResult is not null && directResourceAttributes.Count > 0)
        {
            context.AddDiagnostic(
                new DiagnosticInfo(
                    AuthorizationDiagnosticDescriptors.FromResourceWithDirectResource,
                    operationType.Locations.FirstOrDefault(),
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
                        operationType.Locations.FirstOrDefault(),
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
                            operationType.Locations.FirstOrDefault(),
                            [operationType.Name, "noun"]));

                    hasValidationErrors = true;
                }

                if (string.IsNullOrWhiteSpace(verb))
                {
                    context.AddDiagnostic(
                        new DiagnosticInfo(
                            AuthorizationDiagnosticDescriptors.EmptyResourceScopePairValue,
                            operationType.Locations.FirstOrDefault(),
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
                            operationType.Locations.FirstOrDefault(),
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
                    operationType.Locations.FirstOrDefault(),
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

        context.AddRenderStep(writer =>
        {
            if (!alreadyHasAuthRegistration)
            {
                writer.AppendLine(
                    "this.SetFeatureData(global::Blueprint.Model.WellKnownFeatureData.RequiresAuthentication);");
            }

            writer.AppendLine(
                $"this.SetFeatureData(new global::Blueprint.Model.OperationAuthorizationFeatureData(global::System.Collections.Immutable.ImmutableArray.Create<global::Blueprint.Model.ScopePair>([{scopePairEntries}])));");
        });
    }

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        var operationType = context.Operation.TypeSymbol;
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
                    operationType.Locations.FirstOrDefault(),
                    [operationType.Name]));

            return;
        }

        // Validate template property tokens exist on the operation type
        var hasTemplateValidationErrors = false;
        var valueTypePropertyNames = new HashSet<string>();

        foreach (var (_, _, template) in effectiveScopePairs)
        {
            if (template is null)
            {
                continue;
            }

            var tokens = ResourceKeyTemplateParser.ParseTokens(template);

            foreach (var token in tokens)
            {
                if (token.IsClaimToken)
                {
                    continue;
                }

                // Property tokens must exist on the operation type
                var matchingProperty = operationType.GetAllMembers()
                    .OfType<IPropertySymbol>()
                    .FirstOrDefault(p => p.Name == token.Name);

                if (matchingProperty is null)
                {
                    context.AddDiagnostic(
                        new DiagnosticInfo(
                            AuthorizationDiagnosticDescriptors.ResourceKeyTemplatePropertyNotFound,
                            operationType.Locations.FirstOrDefault(),
                            [operationType.Name, token.Name]));

                    hasTemplateValidationErrors = true;
                }
                else if (matchingProperty.Type.IsValueType)
                {
                    valueTypePropertyNames.Add(token.Name);
                }
            }
        }

        // AuthenticationModule handles identity checks for all HTTP operations (default-on).
        // AuthorizationModule only needs to add its authorization check after that.

        // Add authorization check after authentication
        context.RequireAsync();

        // Cache the ScopePair[] as a static readonly field to avoid per-request allocation
        // (all values are compile-time string literals)
        var scopePairFieldName = "_scopePairs_" + context.Operation.TypeName.Replace(".", "_").Replace("+", "_");
        var scopePairArrayExpr = BuildScopePairArrayExpression(effectiveScopePairs);

        context.AddDeferredClassMember(classBuilder =>
        {
            classBuilder.AddProperty(scopePairFieldName, Accessibility.Private)
                .SetType("Blueprint.Model.ScopePair[]")
                .MakeStatic()
                .WithReadonlyValue($"[{scopePairArrayExpr}]");
        });

        context.AddRenderStep(
            PipelineStage.Authorisation,
            ctx => RenderAuthorizationCheck(ctx, effectiveScopePairs, hasTemplateValidationErrors, scopePairFieldName, valueTypePropertyNames));
    }

    private static void RenderAuthorizationCheck(
        PipelineRenderContext context,
        List<(string Noun, string Verb, string? ResourceKeyTemplate)> scopePairs,
        bool hasTemplateValidationErrors,
        string scopePairFieldName,
        HashSet<string> valueTypePropertyNames)
    {
        var hasTemplates = scopePairs.Any(static sp => sp.ResourceKeyTemplate is not null);

        // When templates are valid, prefer the resolved provider for zero-reflection expansion
        if (hasTemplates && !hasTemplateValidationErrors)
        {
            RenderResolvedAuthorizationCheck(context, scopePairs, scopePairFieldName, valueTypePropertyNames);
        }
        else
        {
            RenderLegacyAuthorizationCheck(context, scopePairFieldName);
        }
    }

    /// <summary>
    /// Generates pipeline code that uses the unified <c>PipelineHelpers.CheckResourceAuthorizationAsync</c>
    /// overload, which internally prefers <c>IResolvedResourceAuthorizationProvider</c> when available
    /// and falls back to <c>IResourceAuthorizationProvider</c>. This replaces the previous if/else
    /// pattern that emitted ~14 lines per pipeline with a single ~4-line helper call.
    /// </summary>
    private static void RenderResolvedAuthorizationCheck(
        PipelineRenderContext context,
        List<(string Noun, string Verb, string? ResourceKeyTemplate)> scopePairs,
        string scopePairFieldName,
        HashSet<string> valueTypePropertyNames)
    {
        var operationExpr = context.GetOperationExpression();
        var httpContextExpr = context.GetHttpContextExpression();
        var principalExpr = $"{httpContextExpr}.User";

        string resolvedScopeArg;

        if (scopePairs.Count == 1)
        {
            var sp = scopePairs[0];
            resolvedScopeArg = sp.ResourceKeyTemplate is not null
                ? BuildResolvedScopePairExpression(sp, operationExpr, principalExpr, valueTypePropertyNames)
                : $"new Blueprint.Model.ResolvedScopePair(\"{sp.Noun}\", \"{sp.Verb}\", null, null)";
        }
        else
        {
            var resolvedElements = string.Join(
                ", ",
                scopePairs.Select(sp => sp.ResourceKeyTemplate is not null
                    ? BuildResolvedScopePairExpression(sp, operationExpr, principalExpr, valueTypePropertyNames)
                    : $"new Blueprint.Model.ResolvedScopePair(\"{sp.Noun}\", \"{sp.Verb}\", null, null)"));

            resolvedScopeArg = $"[{resolvedElements}]";
        }

        context.DeclareVariable(
            "authorizationDenial",
            $"await Blueprint.Runtime.Exec.PipelineHelpers.CheckResourceAuthorizationAsync(context, {principalExpr}, this.ResolvedResourceAuthorizationProvider, this.ResourceAuthorizationProvider, {resolvedScopeArg}, {scopePairFieldName}, {httpContextExpr}.RequestAborted).ConfigureAwait(false)");

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
        HashSet<string> valueTypePropertyNames)
    {
        var tokens = ResourceKeyTemplateParser.ParseTokens(sp.ResourceKeyTemplate!);
        var expandedExpr = ResourceKeyTemplateParser.GenerateExpansionExpression(
            sp.ResourceKeyTemplate!, tokens, operationExpr, principalExpr, "context", valueTypePropertyNames);

        return $"new Blueprint.Model.ResolvedScopePair(\"{sp.Noun}\", \"{sp.Verb}\", \"{sp.ResourceKeyTemplate}\", {expandedExpr})";
    }

    /// <summary>
    /// Generates pipeline code that uses the legacy <c>IResourceAuthorizationProvider</c> with
    /// raw template strings (runtime expansion via regex/reflection). The helper handles the
    /// null-provider permissive fallback internally.
    /// </summary>
    private static void RenderLegacyAuthorizationCheck(
        PipelineRenderContext context,
        string scopePairFieldName)
    {
        RenderLegacyHelperCall(context, scopePairFieldName);
        context.WriteBlankLine();
    }

    /// <summary>
    /// Emits a single <c>PipelineHelpers.CheckResourceAuthorizationAsync</c> call for the
    /// legacy <c>IResourceAuthorizationProvider</c> path. Reused by both <see cref="RenderLegacyAuthorizationCheck"/>
    /// (standalone) and <see cref="RenderResolvedAuthorizationCheck"/> (inside the else branch).
    /// </summary>
    private static void RenderLegacyHelperCall(
        PipelineRenderContext context,
        string scopePairFieldName)
    {
        var httpContextExpr = context.GetHttpContextExpression();
        var principalExpr = $"{httpContextExpr}.User";

        context.DeclareVariable(
            "authorizationDenial",
            $"await Blueprint.Runtime.Exec.PipelineHelpers.CheckResourceAuthorizationAsync(context, {principalExpr}, this.ResourceAuthorizationProvider, {scopePairFieldName}, {httpContextExpr}.RequestAborted).ConfigureAwait(false)");

        context.If(
            "authorizationDenial != null",
            deniedBranch =>
            {
                deniedBranch.Return("authorizationDenial");
            });
    }

    private static string BuildScopePairArrayExpression(
        List<(string Noun, string Verb, string? ResourceKeyTemplate)> scopePairs)
    {
        return string.Join(
            ", ",
            scopePairs.Select(static sp => sp.ResourceKeyTemplate is not null
                ? $"new Blueprint.Model.ScopePair(\"{sp.Noun}\", \"{sp.Verb}\", \"{sp.ResourceKeyTemplate}\")"
                : $"new Blueprint.Model.ScopePair(\"{sp.Noun}\", \"{sp.Verb}\")"));
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
}
