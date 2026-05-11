using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules.Authentication;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Authorization;

/// <summary>
/// Generates compile-time inline claims authorization checks for operations decorated with
/// <c>[Permission]</c> or <c>[PermissionFor]</c> attributes. Instead of relying on the runtime
/// <c>ClaimsRequiredApiAuthoriser</c> (which uses reflection to expand resource key templates),
/// this module emits direct property access code at compile time.
/// <para>
/// For <c>[Permission("ManageUsers")]</c> (no template), the generated code constructs a
/// <c>Claim</c> with a wildcard resource key and calls <c>IClaimInspector.IsDemandedClaimFulfilled</c>.
/// </para>
/// <para>
/// For <c>[PermissionFor("Edit", "Tenant/{TenantId}/Document/{DocumentId}")]</c>, the template
/// is expanded at compile time using direct property access (<c>operation.TenantId</c>) instead
/// of runtime reflection, then evaluated via <c>IClaimInspector</c>.
/// </para>
/// <para>
/// Reports BPAU008 when <c>[Permission]</c>/<c>[PermissionFor]</c> is combined with
/// <c>[AllowAnonymous]</c>, and BPAU009 when a template token references a property that
/// does not exist on the operation type.
/// </para>
/// </summary>
public sealed class ClaimsAuthorizationModule : IBlueprintSourceGeneratorModule, IDescriptorContributor, IPipelineContributor
{
    private const string PermissionAttributeName = "PermissionAttribute";
    private const string PermissionForAttributeName = "PermissionForAttribute";
    private const string AllowAnonymousAttributeName = "AllowAnonymousAttribute";
    private const string PermissionNamespace = "Blueprint.Authorization";
    private const string AllowAnonymousNamespace = "Blueprint.Model.Attributes";

    /// <inheritdoc />
    public string ModuleId => nameof(ClaimsAuthorizationModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(ClaimsAuthorizationModule);

    /// <inheritdoc />
    /// <remarks>
    /// Runs after <see cref="AuthorizationModule"/> (Order 60) and before
    /// <see cref="ApiAuthoriserModule"/> (Order 70). Inline claims checks are
    /// evaluated before the DI-based authoriser loop for earliest-possible denial.
    /// </remarks>
    public int Order => 65;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        var claimDemands = GetClaimDemands(operationType);

        if (claimDemands.Count == 0)
        {
            return;
        }

        // Check for conflict with [AllowAnonymous]
        if (HasAllowAnonymous(operationType))
        {
            context.AddDiagnostic(
                new DiagnosticInfo(
                    ClaimsAuthorizationDiagnosticDescriptors.ClaimsWithAllowAnonymous,
                    operationType.Locations.FirstOrDefault(),
                    [operationType.Name]));

            return;
        }

        // Claims-permissioned operations implicitly require authentication
        var alreadyHasAuthRegistration = context.GetMetadata<AuthenticationDescriptorRegistration>()
            .Any(static r => r.RequiresIdentityCheck);

        if (!alreadyHasAuthRegistration)
        {
            context.AddMetadata(new AuthenticationDescriptorRegistration(RequiresIdentityCheck: true));
        }

        // Project claims metadata into the descriptor for observability/manifest
        context.AddMetadata(new ClaimsAuthorizationDescriptorRegistration(claimDemands.Count));

        context.AddRenderStep(writer =>
        {
            if (!alreadyHasAuthRegistration)
            {
                writer.AppendLine(
                    "this.SetFeatureData(global::Blueprint.Model.WellKnownFeatureData.RequiresAuthentication);");
            }
        });
    }

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        var operationType = context.Operation.TypeSymbol;

        if (HasAllowAnonymous(operationType))
        {
            return;
        }

        if (!HasHttpHostExposure(operationType))
        {
            return;
        }

        var claimDemands = GetClaimDemands(operationType);

        if (claimDemands.Count == 0)
        {
            return;
        }

        // Validate template tokens exist on the operation type
        var hasValidationErrors = false;

        foreach (var demand in claimDemands)
        {
            if (demand.ResourceKeyTemplate is null or "*")
            {
                continue;
            }

            var tokens = ResourceKeyTemplateParser.ParseTokens(demand.ResourceKeyTemplate);

            foreach (var token in tokens)
            {
                if (token.IsClaimToken)
                {
                    continue;
                }

                var hasProperty = operationType.GetAllMembers()
                    .Any(m => m is IPropertySymbol && m.Name == token.Name);

                if (!hasProperty)
                {
                    context.AddDiagnostic(
                        new DiagnosticInfo(
                            ClaimsAuthorizationDiagnosticDescriptors.ClaimsTemplatePropertyNotFound,
                            operationType.Locations.FirstOrDefault(),
                            [operationType.Name, token.Name]));

                    hasValidationErrors = true;
                }
            }
        }

        if (hasValidationErrors)
        {
            return;
        }

        context.RequireAsync();

        // Cache singleton IClaimInspector at pipeline construction time
        context.CacheService(
            "_claimInspector",
            "Blueprint.Authorization.IClaimInspector",
            "Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Blueprint.Authorization.IClaimInspector>(serviceProvider)",
            isNullable: true);

        // Pre-register static readonly fields for claims with constant resource keys
        // (wildcard or no-token templates) to avoid per-request Claim allocations.
        var cachedClaimFields = new Dictionary<int, string>();

        foreach (var demand in claimDemands)
        {
            if (!IsConstantResourceKey(demand))
            {
                continue;
            }

            var resourceKeyLiteral = demand.ResourceKeyTemplate ?? "*";
            var fieldName = $"_claimDemand_{context.Operation.TypeSymbol.Name}_{demand.Index}";
            cachedClaimFields[demand.Index] = fieldName;

            context.AddDeferredClassMember(classBuilder =>
            {
                classBuilder.AddProperty(fieldName, Accessibility.Private)
                    .SetType("Blueprint.Authorization.Claim")
                    .MakeStatic()
                    .WithReadonlyValue(
                        $"new Blueprint.Authorization.Claim(\"{demand.ClaimType}\", \"{resourceKeyLiteral}\", \"{demand.ValueType}\")");
            });
        }

        context.AddRenderStep(
            PipelineStage.Authorisation,
            ctx => RenderClaimsCheck(ctx, claimDemands, cachedClaimFields));
    }

    private static void RenderClaimsCheck(
        PipelineRenderContext context,
        List<ClaimDemand> claimDemands,
        Dictionary<int, string> cachedClaimFields)
    {
        var operationExpr = context.GetOperationExpression();

        // Use cached IClaimInspector (optional — if not registered, claims checks are skipped)
        context.If(
            "this._claimInspector != null && context.UserAuthorisationContext is Blueprint.Authorization.IClaimsHolder claimsHolder",
            branch =>
            {
                foreach (var demand in claimDemands)
                {
                    string claimExpr;

                    if (cachedClaimFields.TryGetValue(demand.Index, out var fieldName))
                    {
                        // Use the pre-cached static readonly field
                        claimExpr = fieldName;
                    }
                    else
                    {
                        // Dynamic resource key — must allocate per-request
                        var resourceKeyExpr = GenerateResourceKeyExpression(
                            demand, operationExpr, context);

                        branch.DeclareVariable(
                            $"claimDemand_{demand.Index}",
                            $"new Blueprint.Authorization.Claim(\"{demand.ClaimType}\", {resourceKeyExpr}, \"{demand.ValueType}\")");

                        claimExpr = $"claimDemand_{demand.Index}";
                    }

                    branch.If(
                        $"!this._claimInspector.IsDemandedClaimFulfilled(claimsHolder, {claimExpr})",
                        deniedBranch =>
                        {
                            deniedBranch.Return(
                                "new Blueprint.Runtime.Exec.ForbiddenResult()");
                        });
                }
            });

        context.WriteBlankLine();
    }

    private static string GenerateResourceKeyExpression(
        ClaimDemand demand, string operationExpr, PipelineRenderContext context)
    {
        if (demand.ResourceKeyTemplate is null or "*")
        {
            return $"\"{demand.ResourceKeyTemplate ?? "*"}\"";
        }

        var tokens = ResourceKeyTemplateParser.ParseTokens(demand.ResourceKeyTemplate);

        if (tokens.Count == 0)
        {
            return $"\"{demand.ResourceKeyTemplate}\"";
        }

        // Use ResourceKeyTemplateParser for compiled expansion (no reflection)
        return ResourceKeyTemplateParser.GenerateExpansionExpression(
            demand.ResourceKeyTemplate,
            tokens,
            operationExpr,
            $"{context.GetHttpContextExpression()}.User");
    }

    /// <summary>
    /// Returns <see langword="true" /> when the demand's resource key is fully known at compile
    /// time (wildcard, null, or a template with no <c>{Property}</c> / <c>{Auth:…}</c> tokens),
    /// enabling the <c>Claim</c> to be cached as a <c>static readonly</c> field.
    /// </summary>
    private static bool IsConstantResourceKey(ClaimDemand demand)
    {
        if (demand.ResourceKeyTemplate is null or "*")
        {
            return true;
        }

        return ResourceKeyTemplateParser.ParseTokens(demand.ResourceKeyTemplate).Count == 0;
    }

    private static List<ClaimDemand> GetClaimDemands(INamedTypeSymbol operationType)
    {
        var demands = new List<ClaimDemand>();
        var index = 0;

        foreach (var attribute in operationType.GetAttributes())
        {
            var attrClass = attribute.AttributeClass;

            if (attrClass is null)
            {
                continue;
            }

            // Check for [Permission("name")] — namespace Blueprint.Authorization, extends ClaimRequiredAttribute
            if (attrClass.Name == PermissionAttributeName &&
                IsInNamespace(attrClass, PermissionNamespace))
            {
                if (attribute.ConstructorArguments.Length >= 1)
                {
                    var permission = attribute.ConstructorArguments[0].Value as string ?? string.Empty;

                    demands.Add(new ClaimDemand(
                        Index: index++,
                        ClaimType: "urn:claims/permission",
                        ResourceKeyTemplate: "*",
                        ValueType: permission));
                }
            }

            // Check for [PermissionFor("verb", "template")] — namespace Blueprint.Authorization
            else if (attrClass.Name == PermissionForAttributeName &&
                     IsInNamespace(attrClass, PermissionNamespace))
            {
                if (attribute.ConstructorArguments.Length >= 2)
                {
                    var permission = attribute.ConstructorArguments[0].Value as string ?? string.Empty;
                    var template = attribute.ConstructorArguments[1].Value as string ?? string.Empty;

                    demands.Add(new ClaimDemand(
                        Index: index++,
                        ClaimType: "urn:claims/permission",
                        ResourceKeyTemplate: template,
                        ValueType: permission));
                }
            }

            // Check for base [ClaimRequired("type", "template", "valueType")]
            else if (attrClass.Name == "ClaimRequiredAttribute" &&
                     IsInNamespace(attrClass, PermissionNamespace))
            {
                if (attribute.ConstructorArguments.Length >= 3)
                {
                    var claimType = attribute.ConstructorArguments[0].Value as string ?? string.Empty;
                    var template = attribute.ConstructorArguments[1].Value as string ?? string.Empty;
                    var valueType = attribute.ConstructorArguments[2].Value as string ?? string.Empty;

                    demands.Add(new ClaimDemand(
                        Index: index++,
                        ClaimType: claimType,
                        ResourceKeyTemplate: template,
                        ValueType: valueType));
                }
            }
        }

        return demands;
    }

    private static bool HasAllowAnonymous(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes().Any(static a =>
        {
            var attrClass = a.AttributeClass;
            return attrClass is not null
                   && attrClass.Name == AllowAnonymousAttributeName
                   && string.Equals(
                       attrClass.ContainingNamespace?.ToDisplayString(),
                       AllowAnonymousNamespace,
                       StringComparison.Ordinal);
        });
    }

    private static bool HasHttpHostExposure(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes()
            .Any(static a => a.AttributeClass?.Name is "RootLinkAttribute" or "SelfLinkAttribute");
    }

    private static bool IsInNamespace(INamedTypeSymbol symbol, string ns)
    {
        return string.Equals(symbol.ContainingNamespace?.ToDisplayString(), ns, StringComparison.Ordinal);
    }

    /// <summary>
    /// Represents a claim demand extracted from a <c>[Permission]</c>, <c>[PermissionFor]</c>,
    /// or <c>[ClaimRequired]</c> attribute at compile time.
    /// </summary>
    private sealed record ClaimDemand(int Index, string ClaimType, string? ResourceKeyTemplate, string ValueType);
}

/// <summary>
/// Descriptor metadata indicating that an operation has compile-time claims authorization checks.
/// </summary>
internal sealed record ClaimsAuthorizationDescriptorRegistration(int DemandCount);
