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
/// Every operation gets both steps, whether or not it has a <c>[RootLink]</c>, <c>[SelfLink]</c> or <c>[Link]</c>, as
/// legacy Blueprint's <c>UserContextLoaderMiddlewareBuilder</c> and <c>AuthorisationMiddlewareBuilder</c> matched every
/// operation it executed. An operation with a link takes its user from the request's <c>HttpContext</c>. One without is
/// executed in process only (a background task, a Rebus message, <c>ExecuteWithNewScopeAsync</c>), with no
/// <c>HttpContext</c>, so it takes its user from <c>context.UserPrincipal</c>, and runs its authorisers through
/// <c>RunUnlinkedOperationAuthorisersAsync</c>, which skips them when its descriptor allows anonymous access or there is
/// no user.
/// </para>
/// <para>
/// Operations marked <c>[AllowAnonymous]</c> skip the authoriser invocation but still load the user context. The
/// module respects <c>context.SkipAuthorisation</c> for nested operations.
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

        // An operation with no link is executed in process only, with no HttpContext to read the user from (casting to
        // IHttpOperationContext would throw), so its steps read the context's UserPrincipal instead.
        var isHttpExposed = HttpHostExposureHelper.HasHttpHostExposure(operationType);

        context.RequireAsync();

        // User context loading runs in Authentication stage (after identity check).
        // This runs even for [AllowAnonymous] operations so that handler base-class
        // properties like Identity are populated when the caller IS authenticated.
        if (isHttpExposed)
        {
            context.AddRenderStep(PipelineStage.Authentication, RenderUserContextLoading);
        }
        else
        {
            context.AddRenderStep(PipelineStage.Authentication, RenderUnlinkedUserContextLoading);
        }

        // [AllowAnonymous] → skip authoriser invocation but still load user context
        if (HasAllowAnonymous(operationType))
        {
            return;
        }

        // Authoriser invocation runs in Authorisation stage
        if (isHttpExposed)
        {
            context.AddRenderStep(PipelineStage.Authorisation, RenderAuthoriserInvocation);
        }
        else
        {
            context.AddRenderStep(PipelineStage.Authorisation, RenderUnlinkedAuthoriserInvocation);
        }
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

        // Inline null check avoids method call overhead when no factory is registered.
        context.WriteStatement(
            "if (this.UserAuthorisationContextFactory != null)");
        context.WriteStatement("{");
        context.WriteStatement(
            $"    await LoadUserContextAsync(context, {httpContextExpr}, this.UserAuthorisationContextFactory).ConfigureAwait(false);");
        context.WriteStatement("}");

        context.WriteBlankLine();
    }

    private static void RenderUnlinkedUserContextLoading(PipelineRenderContext context)
    {
        context.WriteStatement(
            "if (this.UserAuthorisationContextFactory != null)");
        context.WriteStatement("{");
        context.WriteStatement(
            "    await LoadUserContextAsync(context, this.UserAuthorisationContextFactory).ConfigureAwait(false);");
        context.WriteStatement("}");

        context.WriteBlankLine();
    }

    private static void RenderAuthoriserInvocation(PipelineRenderContext context)
    {
        RenderAuthoriserInvocation(context, "RunPreFilteredAuthorisersAsync");
    }

    private static void RenderUnlinkedAuthoriserInvocation(PipelineRenderContext context)
    {
        RenderAuthoriserInvocation(context, "RunUnlinkedOperationAuthorisersAsync");
    }

    private static void RenderAuthoriserInvocation(PipelineRenderContext context, string helperName)
    {
        context.DeclareVariable(
            "authoriserDenial",
            $"await {helperName}(context, this.GetApplicableApiAuthorisers(context.Descriptor), this.GetApplicableOperationAuthorisers(context.Descriptor)).ConfigureAwait(false)");

        context.If(
            "authoriserDenial != null",
            branch =>
            {
                branch.Return("authoriserDenial");
            });

        context.WriteBlankLine();
    }
}
