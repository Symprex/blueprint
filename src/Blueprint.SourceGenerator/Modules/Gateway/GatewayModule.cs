using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Gateway;

/// <summary>
/// Gateway operations (Symprex/blueprint#59): checks that an operation marked <c>[Expose&lt;TInner&gt;]</c> declares its
/// authorisation with Blueprint's native attributes (D13).
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
        if (OperationPropertyModelBuilder.FindExposedType(operationType, out var exposeAttribute) is null)
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
