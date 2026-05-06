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
    private const string MustBeAuthenticatedAttributeName = "MustBeAuthenticatedAttribute";
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
        List<(string Noun, string Verb)> effectiveScopePairs;

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

            foreach (var (noun, verb) in effectiveScopePairs)
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
            effectiveScopePairs.Select(static sp => $"new global::Blueprint.Model.ScopePair(\"{sp.Noun}\", \"{sp.Verb}\")"));

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
        List<(string Noun, string Verb)> effectiveScopePairs;

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

        // Only add our own identity check if AuthenticationModule isn't already doing it
        if (!HasAttribute(operationType, MustBeAuthenticatedAttributeName))
        {
            context.AddRenderStep(
                PipelineStage.Authentication,
                RenderAuthenticationCheck);
        }

        // Add authorization check after authentication
        context.RequireAsync();
        context.AddRenderStep(
            PipelineStage.Authorisation,
            ctx => RenderAuthorizationCheck(ctx, effectiveScopePairs));
    }

    private static bool HasHttpHostExposure(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes()
            .Any(static a => a.AttributeClass?.Name is "RootLinkAttribute" or "SelfLinkAttribute");
    }

    private static void RenderAuthenticationCheck(PipelineRenderContext context)
    {
        context.If(
            "((Blueprint.Runtime.Http.IHttpOperationContext)context).HttpContext.User.Identity?.IsAuthenticated != true",
            branch =>
            {
                branch.Return(
                    context.IsPipelineAsync()
                        ? "new Blueprint.Runtime.Exec.UnauthenticatedResult()"
                        : "System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>(new Blueprint.Runtime.Exec.UnauthenticatedResult())");
            });

        context.WriteBlankLine();
    }

    private static void RenderAuthorizationCheck(PipelineRenderContext context, List<(string Noun, string Verb)> scopePairs)
    {
        // Resolve optional IResourceAuthorizationProvider — permissive when not registered
        context.DeclareVariable(
            "authorizationProvider",
            context.GetOptionalServiceExpression("Blueprint.Authorization.IResourceAuthorizationProvider"));

        context.If(
            "authorizationProvider != null",
            branch =>
            {
                // Build the scope pairs array expression
                var scopePairElements = string.Join(
                    ", ",
                    scopePairs.Select(static sp => $"new Blueprint.Model.ScopePair(\"{sp.Noun}\", \"{sp.Verb}\")"));

                branch.DeclareVariable(
                    "isAuthorized",
                    $"await authorizationProvider.IsAuthorizedAsync(((Blueprint.Runtime.Http.IHttpOperationContext)context).HttpContext.User, new Blueprint.Model.ScopePair[] {{ {scopePairElements} }}, ((Blueprint.Runtime.Http.IHttpOperationContext)context).HttpContext.RequestAborted)");

                branch.If(
                    "!isAuthorized",
                    innerBranch =>
                    {
                        innerBranch.Return(
                            context.IsPipelineAsync()
                                ? "new Blueprint.Runtime.Exec.ForbiddenResult()"
                                : "System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>(new Blueprint.Runtime.Exec.ForbiddenResult())");
                    });
            });

        context.WriteBlankLine();
    }

    private static List<(string Noun, string Verb)> GetResourceAttributes(INamedTypeSymbol operationType)
    {
        var result = new List<(string Noun, string Verb)>();

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

            result.Add((noun, verb));
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
