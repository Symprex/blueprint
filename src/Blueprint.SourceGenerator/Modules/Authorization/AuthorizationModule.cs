using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules.Authentication;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Authorization;

/// <summary>
/// Detects <c>[Resource("noun", "verb")]</c> attributes on operations and projects
/// authorization scope-pair metadata into descriptors. Multiple <c>[Resource]</c> attributes
/// on a single operation use any-of semantics.
/// <para>
/// Reports BPAU003 when <c>[Resource]</c> is combined with <c>[AllowAnonymous]</c>
/// and BPAU004 when noun or verb values are empty or whitespace.
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
        var resourceAttributes = GetResourceAttributes(operationType);

        if (resourceAttributes.Count == 0)
        {
            return;
        }

        // Validate noun/verb values
        var hasValidationErrors = false;

        foreach (var (noun, verb) in resourceAttributes)
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
            resourceAttributes.Select(static sp => $"new global::Blueprint.Model.ScopePair(\"{sp.Noun}\", \"{sp.Verb}\")"));

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
        var resourceAttributes = GetResourceAttributes(operationType);

        if (resourceAttributes.Count == 0)
        {
            return;
        }

        // Conflicting or invalid attributes → no pipeline contribution
        if (HasAttribute(operationType, AllowAnonymousAttributeName))
        {
            return;
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
