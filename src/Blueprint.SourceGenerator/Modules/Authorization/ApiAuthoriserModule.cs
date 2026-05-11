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

        // Cache singleton authoriser arrays at pipeline construction time
        context.CacheService(
            "_apiAuthorisers",
            "Blueprint.Authorization.IApiAuthoriser[]",
            "(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<System.Collections.Generic.IEnumerable<Blueprint.Authorization.IApiAuthoriser>>(serviceProvider) is { } aa ? System.Linq.Enumerable.ToArray(aa) : System.Array.Empty<Blueprint.Authorization.IApiAuthoriser>())",
            isNullable: false);

        context.CacheService(
            "_operationAuthorisers",
            "Blueprint.Authorization.IOperationAuthoriser[]",
            "(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<System.Collections.Generic.IEnumerable<Blueprint.Authorization.IOperationAuthoriser>>(serviceProvider) is { } oa ? System.Linq.Enumerable.ToArray(oa) : System.Array.Empty<Blueprint.Authorization.IOperationAuthoriser>())",
            isNullable: false);

        context.RequireAsync();

        // Cache singleton IUserAuthorisationContextFactory at pipeline construction time
        // (registered as singleton per ServiceCollectionExtensions.AddUserAuthorisationContextFactory)
        context.CacheService(
            "_userContextFactory",
            "Blueprint.Authorization.IUserAuthorisationContextFactory",
            "Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Blueprint.Authorization.IUserAuthorisationContextFactory>(serviceProvider)",
            isNullable: true);

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
        // Use cached singleton IUserAuthorisationContextFactory field
        context.If(
            "this._userContextFactory != null",
            branch =>
            {
                branch.DeclareVariable(
                    "userIdentity",
                    $"{context.GetHttpContextExpression()}.User.Identity as System.Security.Claims.ClaimsIdentity");

                branch.If(
                    "userIdentity != null && userIdentity.IsAuthenticated",
                    innerBranch =>
                    {
                        innerBranch.WriteStatement(
                            "context.UserAuthorisationContext = await this._userContextFactory.CreateContextAsync(userIdentity, context.OperationCancelled).ConfigureAwait(false);");
                    });
            });

        // Populate APM activity tags with user identity metadata when available
        context.If(
            "context.UserAuthorisationContext != null && context.Activity != null",
            branch =>
            {
                branch.DeclareVariable("_activity", "context.Activity");
                branch.WriteStatement(
                    "_activity.SetTag(\"enduser.id\", context.UserAuthorisationContext.Id);");
                branch.WriteStatement(
                    "_activity.SetTag(\"enduser.account_id\", context.UserAuthorisationContext.AccountId);");
                branch.WriteStatement(
                    "context.UserAuthorisationContext.PopulateMetadata((key, value) => _activity.SetTag(key, value));");
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
                // Use cached IApiAuthoriser[] (combined execution + link-visibility).
                branch.If(
                    "this._apiAuthorisers.Length > 0",
                    innerBranch =>
                    {
                        innerBranch.ForEach("var authoriser", "this._apiAuthorisers", foreachBody =>
                        {
                            foreachBody.If(
                                "authoriser.AppliesTo(context.Descriptor)",
                                appliesBody =>
                                {
                                    RenderDenialCheck(appliesBody, "authoriser");
                                });
                        });
                    });

                // Use cached IOperationAuthoriser[] (execution-only).
                branch.If(
                    "this._operationAuthorisers.Length > 0",
                    innerBranch =>
                    {
                        innerBranch.ForEach("var authoriser", "this._operationAuthorisers", foreachBody =>
                        {
                            foreachBody.If(
                                "authoriser.AppliesTo(context.Descriptor)",
                                appliesBody =>
                                {
                                    RenderDenialCheck(appliesBody, "authoriser");
                                });
                        });
                    });
            });

        context.WriteBlankLine();
    }

    private static void RenderDenialCheck(PipelineRenderContext context, string authoriserVar)
    {
        context.DeclareVariable(
            "authoriseResult",
            $"await {authoriserVar}.CanExecuteOperationAsync(context, context.Descriptor, context.Operation).ConfigureAwait(false)");

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
