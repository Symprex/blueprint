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
/// and BPAU006 when <c>[FromResource]</c> references a type that has no <c>[Resource]</c> attributes.
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
    private const string AttributeNamespace = "Blueprint.Model.Attributes";

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
                $"this.SetFeatureData(new global::Blueprint.Model.OperationAuthorizationFeatureData(global::System.Collections.Immutable.ImmutableArray.Create(new global::Blueprint.Model.ScopePair[] {{ {scopePairEntries} }})));");
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
            return;
        }

        // Validate template property tokens exist on the operation type
        var hasTemplateValidationErrors = false;

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
                var hasProperty = operationType.GetAllMembers()
                    .Any(m => m is IPropertySymbol && m.Name == token.Name);

                if (!hasProperty)
                {
                    context.AddDiagnostic(
                        new DiagnosticInfo(
                            AuthorizationDiagnosticDescriptors.ResourceKeyTemplatePropertyNotFound,
                            operationType.Locations.FirstOrDefault(),
                            [operationType.Name, token.Name]));

                    hasTemplateValidationErrors = true;
                }
            }
        }

        // AuthenticationModule handles identity checks for all HTTP operations (default-on).
        // AuthorizationModule only needs to add its authorization check after that.

        // Add authorization check after authentication
        context.RequireAsync();

        // Cache singleton IResourceAuthorizationProvider at pipeline construction time
        context.CacheService(
            "_resourceAuthorizationProvider",
            "Blueprint.Authorization.IResourceAuthorizationProvider",
            "Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Blueprint.Authorization.IResourceAuthorizationProvider>(serviceProvider)",
            isNullable: true);

        // When templates are present, also cache IResolvedResourceAuthorizationProvider to
        // avoid per-request DI resolution (auth providers are singletons).
        var hasTemplates = effectiveScopePairs.Any(static sp => sp.ResourceKeyTemplate is not null);

        if (hasTemplates && !hasTemplateValidationErrors)
        {
            context.CacheService(
                "_resolvedResourceAuthorizationProvider",
                "Blueprint.Authorization.IResolvedResourceAuthorizationProvider",
                "Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Blueprint.Authorization.IResolvedResourceAuthorizationProvider>(serviceProvider)",
                isNullable: true);
        }

        // Cache the ScopePair[] as a static readonly field to avoid per-request allocation
        // (all values are compile-time string literals)
        var scopePairFieldName = "_scopePairs_" + context.Operation.TypeName.Replace(".", "_").Replace("+", "_");
        var scopePairArrayExpr = BuildScopePairArrayExpression(effectiveScopePairs);

        context.AddDeferredClassMember(classBuilder =>
        {
            classBuilder.AddProperty(scopePairFieldName, Accessibility.Private)
                .SetType("Blueprint.Model.ScopePair[]")
                .MakeStatic()
                .WithReadonlyValue(
                    $"new Blueprint.Model.ScopePair[] {{ {scopePairArrayExpr} }}");
        });

        context.AddRenderStep(
            PipelineStage.Authorisation,
            ctx => RenderAuthorizationCheck(ctx, effectiveScopePairs, hasTemplateValidationErrors, scopePairFieldName));
    }

    private static void RenderAuthorizationCheck(
        PipelineRenderContext context,
        List<(string Noun, string Verb, string? ResourceKeyTemplate)> scopePairs,
        bool hasTemplateValidationErrors,
        string scopePairFieldName)
    {
        var hasTemplates = scopePairs.Any(static sp => sp.ResourceKeyTemplate is not null);

        // When templates are valid, prefer the resolved provider for zero-reflection expansion
        if (hasTemplates && !hasTemplateValidationErrors)
        {
            RenderResolvedAuthorizationCheck(context, scopePairs, scopePairFieldName);
        }
        else
        {
            RenderLegacyAuthorizationCheck(context, scopePairFieldName);
        }
    }

    /// <summary>
    /// Generates pipeline code that uses <c>IResolvedResourceAuthorizationProvider</c> with
    /// compile-time expanded resource keys. Falls back to <c>IResourceAuthorizationProvider</c>
    /// when the resolved provider is not registered.
    /// </summary>
    private static void RenderResolvedAuthorizationCheck(
        PipelineRenderContext context,
        List<(string Noun, string Verb, string? ResourceKeyTemplate)> scopePairs,
        string scopePairFieldName)
    {
        var operationExpr = context.GetOperationExpression();
        var httpContextExpr = context.GetHttpContextExpression();
        var principalExpr = $"{httpContextExpr}.User";

        // Use cached IResolvedResourceAuthorizationProvider (resolved at construction time)
        context.If(
            "this._resolvedResourceAuthorizationProvider != null",
            resolvedBranch =>
            {
                // Generate inline-expanded ResolvedScopePair array
                var resolvedElements = string.Join(
                    ", ",
                    scopePairs.Select(sp =>
                    {
                        if (sp.ResourceKeyTemplate is not null)
                        {
                            var tokens = ResourceKeyTemplateParser.ParseTokens(sp.ResourceKeyTemplate);
                            var expandedExpr = ResourceKeyTemplateParser.GenerateExpansionExpression(
                                sp.ResourceKeyTemplate, tokens, operationExpr, principalExpr, "context");

                            return $"new Blueprint.Model.ResolvedScopePair(\"{sp.Noun}\", \"{sp.Verb}\", \"{sp.ResourceKeyTemplate}\", {expandedExpr})";
                        }

                        return $"new Blueprint.Model.ResolvedScopePair(\"{sp.Noun}\", \"{sp.Verb}\", null, null)";
                    }));

                resolvedBranch.DeclareVariable(
                    "isAuthorized",
                    $"await this._resolvedResourceAuthorizationProvider.IsAuthorizedAsync(context, {principalExpr}, new Blueprint.Model.ResolvedScopePair[] {{ {resolvedElements} }}, {httpContextExpr}.RequestAborted).ConfigureAwait(false)");

                resolvedBranch.If(
                    "!isAuthorized",
                    deniedBranch =>
                    {
                        deniedBranch.Return(
                            context.IsPipelineAsync()
                                ? "new Blueprint.Runtime.Exec.ForbiddenResult()"
                                : "System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>(new Blueprint.Runtime.Exec.ForbiddenResult())");
                    });
            });

        // Fallback to legacy provider
        context.Else(elseBranch =>
        {
            RenderLegacyAuthorizationCheckBody(elseBranch, scopePairFieldName);
        });

        context.WriteBlankLine();
    }

    /// <summary>
    /// Generates pipeline code that uses the legacy <c>IResourceAuthorizationProvider</c> with
    /// raw template strings (runtime expansion via regex/reflection).
    /// </summary>
    private static void RenderLegacyAuthorizationCheck(
        PipelineRenderContext context,
        string scopePairFieldName)
    {
        // Use cached IResourceAuthorizationProvider — permissive when not registered
        context.If(
            "this._resourceAuthorizationProvider != null",
            branch =>
            {
                RenderLegacyAuthorizationCheckInner(branch, context, scopePairFieldName);
            });

        context.WriteBlankLine();
    }

    private static void RenderLegacyAuthorizationCheckBody(
        PipelineRenderContext context,
        string scopePairFieldName)
    {
        context.If(
            "this._resourceAuthorizationProvider != null",
            branch =>
            {
                branch.DeclareVariable(
                    "isAuthorized",
                    $"await this._resourceAuthorizationProvider.IsAuthorizedAsync(context, {context.GetHttpContextExpression()}.User, {scopePairFieldName}, {context.GetHttpContextExpression()}.RequestAborted).ConfigureAwait(false)");

                branch.If(
                    "!isAuthorized",
                    deniedBranch =>
                    {
                        deniedBranch.Return(
                            context.IsPipelineAsync()
                                ? "new Blueprint.Runtime.Exec.ForbiddenResult()"
                                : "System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>(new Blueprint.Runtime.Exec.ForbiddenResult())");
                    });
            });
    }

    private static void RenderLegacyAuthorizationCheckInner(
        PipelineRenderContext branch,
        PipelineRenderContext context,
        string scopePairFieldName)
    {
        branch.DeclareVariable(
            "isAuthorized",
            $"await this._resourceAuthorizationProvider.IsAuthorizedAsync(context, {context.GetHttpContextExpression()}.User, {scopePairFieldName}, {context.GetHttpContextExpression()}.RequestAborted).ConfigureAwait(false)");

        branch.If(
            "!isAuthorized",
            deniedBranch =>
            {
                deniedBranch.Return(
                    context.IsPipelineAsync()
                        ? "new Blueprint.Runtime.Exec.ForbiddenResult()"
                        : "System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>(new Blueprint.Runtime.Exec.ForbiddenResult())");
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
                !string.Equals(attrClass.ContainingNamespace?.ToDisplayString(), AttributeNamespace, StringComparison.Ordinal))
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
                !string.Equals(attrClass.ContainingNamespace?.ToDisplayString(), AttributeNamespace, StringComparison.Ordinal))
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
                   && string.Equals(
                       attrClass.ContainingNamespace?.ToDisplayString(),
                       AttributeNamespace,
                       StringComparison.Ordinal);
        });
    }
}
