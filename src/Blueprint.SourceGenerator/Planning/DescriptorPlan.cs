using System.Diagnostics.CodeAnalysis;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Helpers;
using JetBrains.Annotations;

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Mutable descriptor planning state for a single operation.
/// </summary>
/// <param name="Operation">The operation being planned.</param>
public sealed record DescriptorPlan(OperationModel Operation)
{
    private readonly List<DiagnosticInfo> _diagnostics = [];
    private readonly List<object> _metadata = [];
    private readonly List<Action<ICodeWriter>> _renderSteps = [];

    /// <summary>
    /// Gets the accumulated diagnostics.
    /// </summary>
    public IReadOnlyList<DiagnosticInfo> Diagnostics => this._diagnostics;

    /// <summary>
    /// Gets the accumulated feature metadata.
    /// </summary>
    public IReadOnlyList<object> Metadata => this._metadata;

    /// <summary>
    /// Adds a diagnostic to the descriptor plan.
    /// </summary>
    /// <param name="diagnostic">The diagnostic to add.</param>
    public void AddDiagnostic(DiagnosticInfo diagnostic)
    {
        this._diagnostics.Add(diagnostic);
    }

    /// <summary>
    /// Adds feature metadata to the descriptor plan.
    /// </summary>
    /// <param name="metadata">The metadata to add.</param>
    public void AddMetadata(object metadata)
    {
        this._metadata.Add(metadata);
    }

    /// <summary>
    /// Tries to retrieve a single metadata instance of the requested type.
    /// </summary>
    /// <param name="metadata">The metadata, if present.</param>
    /// <typeparam name="T">The metadata type.</typeparam>
    /// <returns>Whether a metadata instance was found.</returns>
    public bool TryGetMetadata<T>([NotNullWhen(true)] out T? metadata)
    {
        using var metadataEnumerator = this.Metadata.OfType<T>().GetEnumerator();

        if (!metadataEnumerator.MoveNext())
        {
            metadata = default;
            return false;
        }

        var candidate = metadataEnumerator.Current;

        if (metadataEnumerator.MoveNext())
        {
            metadata = default;
            return false;
        }

        metadata = candidate!;
        return true;
    }

    /// <summary>
    /// Gets all metadata instances of the requested type.
    /// </summary>
    /// <typeparam name="T">The metadata type.</typeparam>
    /// <returns>The matching metadata instances.</returns>
    [ItemNotNull]
    public IEnumerable<T> GetMetadata<T>()
    {
        return this.Metadata.OfType<T>();
    }

    /// <summary>
    /// Adds a constrained render step used to emit the final descriptor code.
    /// </summary>
    /// <param name="renderStep">The render step.</param>
    public void AddRenderStep(Action<ICodeWriter> renderStep)
    {
        this._renderSteps.Add(renderStep);
    }

    /// <summary>
    /// Renders the descriptor plan using the supplied writer.
    /// </summary>
    /// <param name="codeWriter">The writer to render to.</param>
    public void Render(ICodeWriter codeWriter)
    {
        foreach (var renderStep in this._renderSteps)
        {
            renderStep(codeWriter);
        }
    }
}
