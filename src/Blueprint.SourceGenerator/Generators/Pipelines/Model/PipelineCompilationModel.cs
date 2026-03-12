using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

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
    /// <inheritdoc />
    public bool Equals(PipelineCompilationModel? other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return this.AppTypeName == other.AppTypeName &&
               this.AppNamespace == other.AppNamespace &&
               this.Handlers == other.Handlers;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return System.HashCode.Combine(this.AppTypeName, this.AppNamespace, this.Handlers);
    }
}
