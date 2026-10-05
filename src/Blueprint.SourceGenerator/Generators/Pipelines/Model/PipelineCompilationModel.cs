using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Model representing all the operations found in a BlueprintApp and their handlers.
/// </summary>
/// <param name="AppTypeName">The simple name of the BlueprintApp type.</param>
/// <param name="AppNamespace">The namespace of the BlueprintApp.</param>
/// <param name="Handlers">All discovered operation handlers.</param>
public sealed record PipelineCompilationModel(
    string AppTypeName,
    string AppNamespace,
    EquatableList<OperationHandlerModel> Handlers) : IEquatable<PipelineCompilationModel>
{
    /// <summary>
    /// Gets the fully qualified name of the only <c>TenantId</c> type the app has filled from the caller's tenant, from its
    /// <c>[BlueprintAppTenancy(TenantIdType = ...)]</c>, or <see langword="null"/> when it declares none.
    /// </summary>
    public string? TenantIdTypeName { get; init; }

    /// <inheritdoc />
    public bool Equals(PipelineCompilationModel? other)
    {
        return other is null
            ? false
            : ReferenceEquals(this, other)
            ? true
            : this.AppTypeName == other.AppTypeName &&
               this.AppNamespace == other.AppNamespace &&
               this.Handlers == other.Handlers &&
               this.TenantIdTypeName == other.TenantIdTypeName;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return System.HashCode.Combine(this.AppTypeName, this.AppNamespace, this.Handlers, this.TenantIdTypeName);
    }
}
