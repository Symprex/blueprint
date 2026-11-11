using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Gateway;

/// <summary>
/// Gateway operations (Symprex/blueprint#59): checks that an operation marked <c>[Expose&lt;TInner&gt;]</c> or
/// <c>[ForwardsTo&lt;TInner&gt;]</c> declares its authorisation with Blueprint's native attributes (D13), that any
/// operation's upstream credential and error translations are valid (D7, D8; <see cref="UpstreamForwardPolicyAnalysis"/>),
/// and that only a declared-mode operation renames a property with <c>[ForwardAs]</c> (D10).
/// </summary>
/// <remarks>
/// The rest of minimal mode lives where the generator that needs it runs: the property model and its emission in
/// <see cref="OperationPropertyModelBuilder"/> and the descriptor generator (D25), the class-level checks in
/// <see cref="ExposedOperationAnalysis"/>, and the forwarding handler in <see cref="GatewayForward"/>, which the pipeline
/// generator's handler discovery falls back to (D12).
/// </remarks>
public sealed class GatewayModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(GatewayModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(GatewayModule);

    /// <inheritdoc />
    public int Order => 5;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();

        // Generated and hand-written forwards read the same attributes, so every operation is checked.
        foreach (var diagnostic in UpstreamForwardPolicyAnalysis.AnalyseOperation(operationType))
        {
            context.AddDiagnostic(diagnostic);
        }

        if (DeclaredForward.FindForwardedType(operationType, out _) is null)
        {
            ReportRenamesWithoutForwardsTo(context, operationType);
        }

        if (OperationPropertyModelBuilder.FindGatewayInnerType(operationType, out var exposeAttribute) is null)
        {
            return;
        }

        if (HasNativeAuthorisationDeclaration(operationType))
        {
            return;
        }

        var location = exposeAttribute?.ApplicationSyntaxReference is { } reference
            ? LocationInfo.CreateFrom(reference.GetSyntax())
            : LocationInfo.CreateFrom(operationType.Locations.First());

        context.AddDiagnostic(new DiagnosticInfo(GatewayDiagnosticDescriptors.MissingAuthorisationDeclaration, location, [operationType.Name]));
    }

    /// <summary>
    /// BPGW027: a <c>[ForwardAs]</c> on an operation that no declared-mode forward reads, so the rename would do nothing.
    /// </summary>
    private static void ReportRenamesWithoutForwardsTo(DescriptorContributorContext context, INamedTypeSymbol operationType)
    {
        foreach (var member in operationType.GetMembers())
        {
            if (member is IPropertySymbol property && DeclaredForward.FindForwardAs(property, out _) is { } forwardAs)
            {
                var location = forwardAs.ApplicationSyntaxReference is { } reference
                    ? LocationInfo.CreateFrom(reference.GetSyntax())
                    : LocationInfo.CreateFrom(property.Locations.First());

                context.AddDiagnostic(new DiagnosticInfo(GatewayDiagnosticDescriptors.ForwardAsWithoutForwardsTo, location, [operationType.Name, property.Name]));
            }
        }
    }

    /// <summary>
    /// Whether the operation declares one of D13's recognised attributes from <c>Blueprint.Model.Attributes</c>:
    /// <c>[AllowAnonymous]</c>, <c>[MustBeAuthenticated]</c>, <c>[Resource]</c> or <c>[FromResource]</c>. Only the
    /// operation's own attributes count; an inner operation's authorisation is never copied.
    /// </summary>
    private static bool HasNativeAuthorisationDeclaration(INamedTypeSymbol operationType)
    {
        foreach (var attribute in operationType.GetAttributes())
        {
            if (attribute.AttributeClass is { } attributeClass
                && attributeClass.Name is "AllowAnonymousAttribute" or "MustBeAuthenticatedAttribute" or "ResourceAttribute" or "FromResourceAttribute"
                && attributeClass.IsInNamespace("Blueprint", "Model", "Attributes"))
            {
                return true;
            }
        }

        return false;
    }
}
