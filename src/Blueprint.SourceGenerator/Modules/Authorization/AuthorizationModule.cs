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
        var fromResourceTarget = GetFromResourceTarget(operationType);

        // BPAU005: Both [FromResource] and [Resource] on the same operation
        if (fromResourceTarget is not null && directResourceAttributes.Count > 0)
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

        if (fromResourceTarget is not null)
        {
            // [FromResource(typeof(X))] — inherit from the referenced type's direct [Resource] attrs
            var targetResourceAttributes = GetResourceAttributes(fromResourceTarget);

            if (targetResourceAttributes.Count == 0)
            {
                context.AddDiagnostic(
                    new DiagnosticInfo(
                        AuthorizationDiagnosticDescriptors.FromResourceTargetHasNoResource,
                        operationType.Locations.FirstOrDefault(),
                        [operationType.Name, fromResourceTarget.Name]));

                return;
            }

            effectiveScopePairs = targetResourceAttributes;
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
        if (fromResourceTarget is null)
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

        context.AddMetadata(new AuthorizationDescriptorRegistration(RequiresPermissionCheck: true));

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
        var fromResourceTarget = GetFromResourceTarget(operationType);

        // Determine if this operation has effective resource authorization
        var hasResourceAuthorization = directResourceAttributes.Count > 0 || fromResourceTarget is not null;

        if (!hasResourceAuthorization)
        {
            return;
        }

        // BPAU005 conflict → no pipeline contribution
        if (fromResourceTarget is not null && directResourceAttributes.Count > 0)
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
        if (fromResourceTarget is not null)
        {
            var targetResourceAttributes = GetResourceAttributes(fromResourceTarget);

            if (targetResourceAttributes.Count == 0)
            {
                return;
            }

            effectiveScopePairs = targetResourceAttributes;
        }
        else
        {
            effectiveScopePairs = directResourceAttributes;
        }

        if (!HasHttpHostExposure(operationType))
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

        context.AddRenderStep(
            PipelineStage.Authorisation,
            ctx => RenderAuthorizationCheck(ctx, effectiveScopePairs, hasTemplateValidationErrors));
    }

    private static bool HasHttpHostExposure(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes()
            .Any(static a => a.AttributeClass?.Name is "RootLinkAttribute" or "SelfLinkAttribute");
    }

    private static void RenderAuthorizationCheck(
        PipelineRenderContext context,
        List<(string Noun, string Verb, string? ResourceKeyTemplate)> scopePairs,
        bool hasTemplateValidationErrors)
    {
        var hasTemplates = scopePairs.Any(static sp => sp.ResourceKeyTemplate is not null);

        // When templates are valid, prefer the resolved provider for zero-reflection expansion
        if (hasTemplates && !hasTemplateValidationErrors)
        {
            RenderResolvedAuthorizationCheck(context, scopePairs);
        }
        else
        {
            RenderLegacyAuthorizationCheck(context, scopePairs);
        }
    }

    /// <summary>
    /// Generates pipeline code that uses <c>IResolvedResourceAuthorizationProvider</c> with
    /// compile-time expanded resource keys. Falls back to <c>IResourceAuthorizationProvider</c>
    /// when the resolved provider is not registered.
    /// </summary>
    private static void RenderResolvedAuthorizationCheck(
        PipelineRenderContext context,
        List<(string Noun, string Verb, string? ResourceKeyTemplate)> scopePairs)
    {
        var operationExpr = context.GetOperationExpression();
        var httpContextExpr = context.GetHttpContextExpression();
        var principalExpr = $"{httpContextExpr}.User";

        // Try resolved provider first
        context.DeclareVariable(
            "resolvedAuthProvider",
            context.GetOptionalServiceExpression("Blueprint.Authorization.IResolvedResourceAuthorizationProvider"));

        context.If(
            "resolvedAuthProvider != null",
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
                                sp.ResourceKeyTemplate, tokens, operationExpr, principalExpr);

                            return $"new Blueprint.Model.ResolvedScopePair(\"{sp.Noun}\", \"{sp.Verb}\", \"{sp.ResourceKeyTemplate}\", {expandedExpr})";
                        }

                        return $"new Blueprint.Model.ResolvedScopePair(\"{sp.Noun}\", \"{sp.Verb}\", null, null)";
                    }));

                resolvedBranch.DeclareVariable(
                    "isAuthorized",
                    $"await resolvedAuthProvider.IsAuthorizedAsync(context, {principalExpr}, new Blueprint.Model.ResolvedScopePair[] {{ {resolvedElements} }}, {httpContextExpr}.RequestAborted)");

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
            RenderLegacyAuthorizationCheckBody(elseBranch, scopePairs);
        });

        context.WriteBlankLine();
    }

    /// <summary>
    /// Generates pipeline code that uses the legacy <c>IResourceAuthorizationProvider</c> with
    /// raw template strings (runtime expansion via regex/reflection).
    /// </summary>
    private static void RenderLegacyAuthorizationCheck(
        PipelineRenderContext context,
        List<(string Noun, string Verb, string? ResourceKeyTemplate)> scopePairs)
    {
        // Use cached IResourceAuthorizationProvider — permissive when not registered
        context.If(
            "this._resourceAuthorizationProvider != null",
            branch =>
            {
                RenderLegacyAuthorizationCheckInner(branch, context, scopePairs);
            });

        context.WriteBlankLine();
    }

    private static void RenderLegacyAuthorizationCheckBody(
        PipelineRenderContext context,
        List<(string Noun, string Verb, string? ResourceKeyTemplate)> scopePairs)
    {
        context.If(
            "this._resourceAuthorizationProvider != null",
            branch =>
            {
                var scopePairElements = BuildScopePairArrayExpression(scopePairs);

                branch.DeclareVariable(
                    "isAuthorized",
                    $"await this._resourceAuthorizationProvider.IsAuthorizedAsync(context, {context.GetHttpContextExpression()}.User, new Blueprint.Model.ScopePair[] {{ {scopePairElements} }}, {context.GetHttpContextExpression()}.RequestAborted)");

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
        List<(string Noun, string Verb, string? ResourceKeyTemplate)> scopePairs)
    {
        var scopePairElements = BuildScopePairArrayExpression(scopePairs);

        branch.DeclareVariable(
            "isAuthorized",
            $"await this._resourceAuthorizationProvider.IsAuthorizedAsync(context, {context.GetHttpContextExpression()}.User, new Blueprint.Model.ScopePair[] {{ {scopePairElements} }}, {context.GetHttpContextExpression()}.RequestAborted)");

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
    /// Resolves the <c>[FromResource(typeof(X))]</c> attribute on the operation, returning the
    /// referenced <see cref="INamedTypeSymbol"/> or <c>null</c> if the attribute is not present
    /// or the type argument cannot be resolved.
    /// </summary>
    private static INamedTypeSymbol? GetFromResourceTarget(INamedTypeSymbol operationType)
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

            return targetType;
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
