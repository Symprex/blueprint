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
    private const string AttributeNamespace = "Blueprint.Model.Attributes";

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

        // [AllowAnonymous] → skip authorization entirely
        if (HasAllowAnonymous(operationType))
        {
            return;
        }

        // Non-HTTP operations don't get IApiAuthoriser checks
        if (!HasHttpHostExposure(operationType))
        {
            return;
        }

        context.RequireAsync();

        // User context loading runs in Authentication stage (after identity check)
        context.AddRenderStep(
            PipelineStage.Authentication,
            RenderUserContextLoading);

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

    private static void RenderUserContextLoading(PipelineRenderContext context)
    {
        // Resolve optional IUserAuthorisationContextFactory and populate context
        context.DeclareVariable(
            "userContextFactory",
            context.GetOptionalServiceExpression("Blueprint.Authorization.IUserAuthorisationContextFactory"));

        context.If(
            "userContextFactory != null",
            branch =>
            {
                branch.DeclareVariable(
                    "userIdentity",
                    "((Blueprint.Runtime.Http.IHttpOperationContext)context).HttpContext.User.Identity as System.Security.Claims.ClaimsIdentity");

                branch.If(
                    "userIdentity != null && userIdentity.IsAuthenticated",
                    innerBranch =>
                    {
                        innerBranch.WriteStatement(
                            "context.UserAuthorisationContext = await userContextFactory.CreateContextAsync(userIdentity, context.OperationCancelled);");
                    });
            });

        // Populate APM activity tags with user identity metadata when available
        context.If(
            "context.UserAuthorisationContext != null && context.Activity != null",
            branch =>
            {
                branch.WriteStatement(
                    "context.Activity.SetTag(\"enduser.id\", context.UserAuthorisationContext.Id);");
                branch.WriteStatement(
                    "context.Activity.SetTag(\"enduser.account_id\", context.UserAuthorisationContext.AccountId);");
                branch.WriteStatement(
                    "context.UserAuthorisationContext.PopulateMetadata(static (key, value) => System.Diagnostics.Activity.Current?.SetTag(key, value));");
            });

        context.WriteBlankLine();
    }

    private static void RenderAuthoriserInvocation(PipelineRenderContext context)
    {
        // Skip authorisation for nested operations that have already been authorised
        context.If(
            "!context.SkipAuthorisation",
            branch =>
            {
                // Resolve IEnumerable<IApiAuthoriser> from DI (combined execution + link-visibility).
                branch.DeclareVariable(
                    "apiAuthorisers",
                    $"Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<System.Collections.Generic.IEnumerable<Blueprint.Authorization.IApiAuthoriser>>({context.GetServiceProviderExpression()})");

                branch.If(
                    "apiAuthorisers != null",
                    innerBranch =>
                    {
                        innerBranch.WriteStatement("foreach (var authoriser in apiAuthorisers)");
                        innerBranch.WriteStatement("{");
                        innerBranch.If(
                            "authoriser.AppliesTo(context.Descriptor)",
                            appliesBody =>
                            {
                                RenderDenialCheck(appliesBody, "authoriser");
                            });

                        innerBranch.WriteStatement("}");
                    });

                // Resolve IEnumerable<IOperationAuthoriser> from DI (execution-only).
                branch.DeclareVariable(
                    "operationAuthorisers",
                    $"Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<System.Collections.Generic.IEnumerable<Blueprint.Authorization.IOperationAuthoriser>>({context.GetServiceProviderExpression()})");

                branch.If(
                    "operationAuthorisers != null",
                    innerBranch =>
                    {
                        innerBranch.WriteStatement("foreach (var authoriser in operationAuthorisers)");
                        innerBranch.WriteStatement("{");
                        innerBranch.If(
                            "authoriser.AppliesTo(context.Descriptor)",
                            appliesBody =>
                            {
                                RenderDenialCheck(appliesBody, "authoriser");
                            });

                        innerBranch.WriteStatement("}");
                    });
            });

        context.WriteBlankLine();
    }

    private static void RenderDenialCheck(PipelineRenderContext context, string authoriserVar)
    {
        context.DeclareVariable(
            "authoriseResult",
            $"await {authoriserVar}.CanExecuteOperationAsync(context, context.Descriptor, context.Operation)");

        context.If(
            "!authoriseResult.IsAllowed",
            deniedBody =>
            {
                deniedBody.If(
                    "authoriseResult.FailureType == Blueprint.Authorization.ExecutionAllowedFailureType.Authentication",
                    authBranch =>
                    {
                        authBranch.Return(
                            "new Blueprint.Runtime.Exec.UnauthenticatedResult()");
                    });

                deniedBody.Return(
                    "new Blueprint.Runtime.Exec.ForbiddenResult()");
            });
    }
}
