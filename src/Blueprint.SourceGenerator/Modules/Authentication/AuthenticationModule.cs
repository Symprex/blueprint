using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Authentication;

/// <summary>
/// Detects <c>[MustBeAuthenticated]</c> and <c>[AllowAnonymous]</c> on operations and emits both descriptor
/// feature data and a pipeline identity check in the <see cref="PipelineStage.Authentication"/> stage.
/// Unauthenticated HTTP requests are short-circuited with a <c>401 Unauthorized</c> response.
/// <c>[AllowAnonymous]</c> operations project <c>AuthenticationRequirement.Anonymous</c> metadata and skip checks.
/// </summary>
public sealed class AuthenticationModule : IBlueprintSourceGeneratorModule, IDescriptorContributor, IPipelineContributor
{
    private const string MustBeAuthenticatedAttributeName = "MustBeAuthenticatedAttribute";
    private const string AllowAnonymousAttributeName = "AllowAnonymousAttribute";
    private const string AttributeNamespace = "Blueprint.Model.Attributes";

    /// <inheritdoc />
    public string ModuleId => nameof(AuthenticationModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(AuthenticationModule);

    /// <inheritdoc />
    /// <remarks>
    /// Must run after <c>LinkGenerationModule</c> and <c>TasksHostModule</c> (both <c>Order = 0</c>)
    /// so that <see cref="HostExposureRegistration"/> metadata is available when checking whether
    /// the operation is HTTP-exposed.
    /// </remarks>
    public int Order => 50;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        var hasMustBeAuthenticated = HasAttribute(operationType, MustBeAuthenticatedAttributeName);
        var hasAllowAnonymous = HasAttribute(operationType, AllowAnonymousAttributeName);

        // Conflicting markers: report error and bail
        if (hasMustBeAuthenticated && hasAllowAnonymous)
        {
            context.AddDiagnostic(
                new DiagnosticInfo(
                    AuthenticationDiagnosticDescriptors.ConflictingAccessModeAttributes,
                    operationType.Locations.FirstOrDefault(),
                    [operationType.Name]));

            return;
        }

        // [AllowAnonymous]: project anonymous access mode metadata (host-agnostic)
        if (hasAllowAnonymous)
        {
            context.AddMetadata(new AuthenticationDescriptorRegistration(RequiresIdentityCheck: false));
            context.AddRenderStep(RenderAnonymousFeatureData);
            return;
        }

        // BackgroundTask convention: auto-project anonymous when no explicit auth attribute is present
        // and the operation is exposed on the tasks host. Background tasks execute server-side without
        // HTTP identity, matching legacy Blueprint's BackgroundTaskAllowAnonymousConvention behavior.
        if (!hasMustBeAuthenticated)
        {
            var isTaskExposed = context.GetMetadata<HostExposureRegistration>()
                .Any(static r => string.Equals(r.HostName, HostExposureRegistration.Tasks.HostName, StringComparison.Ordinal));

            if (isTaskExposed)
            {
                context.AddMetadata(new AuthenticationDescriptorRegistration(RequiresIdentityCheck: false));
                context.AddRenderStep(RenderAnonymousFeatureData);
                return;
            }
        }

        // [MustBeAuthenticated]: requires HTTP host for enforcement
        if (hasMustBeAuthenticated)
        {
            var isHttpExposed = context.GetMetadata<HostExposureRegistration>()
                .Any(static r => string.Equals(r.HostName, HostExposureRegistration.Http.HostName, StringComparison.Ordinal));

            if (!isHttpExposed)
            {
                context.AddDiagnostic(
                    new DiagnosticInfo(
                        AuthenticationDiagnosticDescriptors.AuthAttributeOnNonHttpOperation,
                        operationType.Locations.FirstOrDefault(),
                        [operationType.Name]));

                return;
            }

            context.AddMetadata(new AuthenticationDescriptorRegistration(RequiresIdentityCheck: true));
            context.AddRenderStep(RenderAuthenticatedFeatureData);
        }
    }

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        var operationType = context.Operation.TypeSymbol;

        // [AllowAnonymous] → no pipeline check needed
        if (HasAttribute(operationType, AllowAnonymousAttributeName))
        {
            return;
        }

        if (!HasAttribute(operationType, MustBeAuthenticatedAttributeName))
        {
            return;
        }

        if (!HasHttpHostExposure(operationType))
        {
            return;
        }

        context.AddRenderStep(
            PipelineStage.Authentication,
            RenderAuthenticationCheck);
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

    private static bool HasHttpHostExposure(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes()
            .Any(static a => a.AttributeClass?.Name is "RootLinkAttribute" or "SelfLinkAttribute");
    }

    private static void RenderAnonymousFeatureData(CodeWriter.ICodeWriter writer)
    {
        writer.AppendLine(
            "this.SetFeatureData(new global::Blueprint.Model.OperationAuthenticationFeatureData(global::Blueprint.Model.AuthenticationRequirement.Anonymous));");
    }

    private static void RenderAuthenticatedFeatureData(CodeWriter.ICodeWriter writer)
    {
        writer.AppendLine(
            "this.SetFeatureData(global::Blueprint.Model.WellKnownFeatureData.RequiresAuthentication);");
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
}
