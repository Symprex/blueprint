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
    private const string ResourceAttributeName = "ResourceAttribute";
    private const string FromResourceAttributeName = "FromResourceAttribute";

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
            context.AddRenderStep(static writer => RenderDeclaredAccess(writer, "AnonymousAccess"));
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

        // Default-on authentication: HTTP operations require authentication unless [AllowAnonymous].
        // [MustBeAuthenticated] is optional/explicit — the behavior is the same with or without it.
        // This matches legacy Blueprint where MustBeAuthenticatedApiAuthoriser is registered by default.
        var isHttpExposedForAuth = context.GetMetadata<HostExposureRegistration>()
            .Any(static r => string.Equals(r.HostName, HostExposureRegistration.Http.HostName, StringComparison.Ordinal));

        if (isHttpExposedForAuth)
        {
            context.AddMetadata(new AuthenticationDescriptorRegistration(RequiresIdentityCheck: true));
            context.AddRenderStep(RenderAuthenticatedFeatureData);

            // An operation with scope pairs is permissioned, which AuthorizationModule records; [MustBeAuthenticated]
            // beside them adds nothing. Deciding it here, not by which module's render step runs last, keeps the
            // declared access mode independent of module order.
            if (hasMustBeAuthenticated
                && !HasAttribute(operationType, ResourceAttributeName)
                && !HasAttribute(operationType, FromResourceAttributeName))
            {
                context.AddRenderStep(static writer => RenderDeclaredAccess(writer, "AuthenticatedAccess"));
            }
        }
        else if (hasMustBeAuthenticated)
        {
            // [MustBeAuthenticated] on a non-HTTP operation is invalid
            context.AddDiagnostic(
                new DiagnosticInfo(
                    AuthenticationDiagnosticDescriptors.AuthAttributeOnNonHttpOperation,
                    operationType.Locations.FirstOrDefault(),
                    [operationType.Name]));
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

        // Non-HTTP operations don't get pipeline auth checks
        if (!HttpHostExposureHelper.HasHttpHostExposure(operationType))
        {
            return;
        }

        // Default-on: all HTTP operations get an auth check unless [AllowAnonymous]
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
                   && attrClass.IsInNamespace("Blueprint", "Model", "Attributes");
        });
    }

    private static void RenderAnonymousFeatureData(CodeWriter.ICodeWriter writer)
    {
        writer.AppendLine(
            "this.SetFeatureData(new global::Blueprint.Model.OperationAuthenticationFeatureData(global::Blueprint.Model.AuthenticationRequirement.Anonymous));");
        writer.AppendLine("this.AnonymousAccessAllowed = true;");
    }

    /// <summary>
    /// Records the access attribute the operation declares, from one of the shared <c>WellKnownFeatureData</c> instances.
    /// </summary>
    private static void RenderDeclaredAccess(CodeWriter.ICodeWriter writer, string wellKnownAccess)
    {
        writer.AppendLine($"this.SetFeatureData(global::Blueprint.Model.WellKnownFeatureData.{wellKnownAccess});");
    }

    private static void RenderAuthenticatedFeatureData(CodeWriter.ICodeWriter writer)
    {
        writer.AppendLine(
            "this.SetFeatureData(global::Blueprint.Model.WellKnownFeatureData.RequiresAuthentication);");
    }

    private static void RenderAuthenticationCheck(PipelineRenderContext context)
    {
        // Ensure the httpContext variable is declared before referencing it.
        context.GetHttpContextExpression();

        context.If(
            "CheckHttpAuthenticated(httpContext, context) is { } _authCheckResult",
            branch =>
            {
                branch.Return(
                    context.IsPipelineAsync()
                        ? "_authCheckResult"
                        : "System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>(_authCheckResult)");
            });

        context.WriteBlankLine();
    }
}
