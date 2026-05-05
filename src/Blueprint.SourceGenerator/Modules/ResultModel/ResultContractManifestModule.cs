using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes <c>resultContract.type</c>, <c>resultContract.isUnion</c>, and <c>resultContract.members</c>
/// to the per-operation debug manifest, reflecting the declared result contract from the operation's generic
/// interface parameter.
/// </summary>
/// <remarks>
/// Uses <see cref="OperationResultContractHelper"/> to extract the declared result type and detect
/// <c>Results&lt;T1, T2&gt;</c> unions. Provides a quick overview of what each operation can return
/// without needing to inspect the source directly.
/// </remarks>
internal sealed class ResultContractManifestModule : IBlueprintSourceGeneratorModule, IDebugManifestContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(ResultContractManifestModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(ResultContractManifestModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn => [];

    /// <inheritdoc />
    public void ContributeToManifest(DebugManifestContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        var declaredContractType = OperationResultContractHelper.GetDeclaredOperationResultType(operationType);

        if (declaredContractType is null)
        {
            context.AddEntry("resultContract.type", new JValue("none"));
            return;
        }

        if (OperationResultContractHelper.TryGetResultsUnionMembers(declaredContractType, out var unionMembers))
        {
            context.AddEntry("resultContract.type", new JValue(GetTypeName(declaredContractType)));
            context.AddEntry("resultContract.isUnion", new JValue(true));
            context.AddEntry("resultContract.members", new JArray(
                unionMembers.Select(static m => new JValue(GetTypeName(m)))));
        }
        else
        {
            context.AddEntry("resultContract.type", new JValue(GetTypeName(declaredContractType)));
            context.AddEntry("resultContract.isUnion", new JValue(false));
        }
    }

    private static string GetTypeName(ITypeSymbol typeSymbol)
    {
        return typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }
}
