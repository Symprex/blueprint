using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules.Authentication;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Authorization;

/// <summary>
/// Generates pipeline code that invokes registered <c>IApiAuthoriser</c> implementations to
/// enforce business-level authorization rules (tenant isolation, feature flags, subscription checks).
/// <para>
/// The generated pipeline resolves all <c>IApiAuthoriser</c> instances from DI, calls
/// <c>AppliesTo(descriptor)</c> to filter, and invokes <c>CanExecuteOperationAsync</c> on each
/// applicable authorizer. The first denial short-circuits with either a 401 or 403 response
/// depending on the <c>ExecutionAllowedFailureType</c>.
/// </para>
/// <para>
/// Before invoking authorizers, the module resolves an optional <c>IUserAuthorisationContextFactory</c>
/// to populate <c>context.UserAuthorisationContext</c> from the authenticated identity. It also
/// populates APM activity tags with user identity metadata for observability.
/// </para>
/// <para>
/// Operations marked <c>[AllowAnonymous]</c> or exposed only on non-HTTP hosts skip all
/// authorization checks. The module respects <c>context.SkipAuthorisation</c> for nested operations.
/// </para>
/// </summary>
public sealed class ApiAuthoriserModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    private const string AllowAnonymousAttributeName = "AllowAnonymousAttribute";

    /// <inheritdoc />
    public string ModuleId => nameof(ApiAuthoriserModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(ApiAuthoriserModule);

    /// <inheritdoc />
    /// <remarks>
    /// Runs after <see cref="AuthenticationModule"/> (Order 50) and the resource-based
    /// <see cref="AuthorizationModule"/> (Order 60) to ensure identity is established
    /// and resource scope checks have already been applied.
    /// </remarks>
    public int Order => 70;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        var operationType = context.Operation.TypeSymbol;

        // Non-HTTP operations don't get user context loading or IApiAuthoriser checks
        if (!HttpHostExposureHelper.HasHttpHostExposure(operationType))
        {
            return;
        }

        context.RequireAsync();

        // User context loading runs in Authentication stage (after identity check).
        // This runs even for [AllowAnonymous] operations so that handler base-class
        // properties like Identity are populated when the caller IS authenticated.
        context.AddRenderStep(
            PipelineStage.Authentication,
            RenderUserContextLoading);

        // [AllowAnonymous] → skip authoriser invocation but still load user context
        if (HasAllowAnonymous(operationType))
        {
            return;
        }

        // Authoriser invocation runs in Authorisation stage
        context.AddRenderStep(
            PipelineStage.Authorisation,
            RenderAuthoriserInvocation);
    }

    private static bool HasAllowAnonymous(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes().Any(static a =>
        {
            var attrClass = a.AttributeClass;
            if (attrClass is null)
            {
                return false;
            }

            return attrClass.Name == AllowAnonymousAttributeName
                   && attrClass.IsInNamespace("Blueprint", "Model", "Attributes");
        });
    }

    private static void RenderUserContextLoading(PipelineRenderContext context)
    {
        var httpContextExpr = context.GetHttpContextExpression();

        context.WriteStatement(
            $"await Blueprint.Runtime.Exec.PipelineHelpers.LoadUserContextAsync(context, {httpContextExpr}, this.UserAuthorisationContextFactory).ConfigureAwait(false);");

        context.WriteBlankLine();
    }

    private static void RenderAuthoriserInvocation(PipelineRenderContext context)
    {
        context.DeclareVariable(
            "authoriserDenial",
            "await Blueprint.Runtime.Exec.PipelineHelpers.RunAuthorisersAsync(context, this.ApiAuthorisers, this.OperationAuthorisers).ConfigureAwait(false)");

        context.If(
            "authoriserDenial != null",
            branch =>
            {
                branch.Return("authoriserDenial");
            });

        context.WriteBlankLine();
    }
}
