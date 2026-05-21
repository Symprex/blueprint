using System.Diagnostics.CodeAnalysis;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Helpers;
using JetBrains.Annotations;

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Mutable descriptor planning state for a single operation.
/// </summary>
public sealed class DescriptorPlan : IEquatable<DescriptorPlan>
{
    private readonly List<DiagnosticInfo> _diagnostics = [];
    private readonly List<object> _metadata = [];
    private readonly List<Action<ICodeWriter>> _renderSteps = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="DescriptorPlan"/> class.
    /// </summary>
    /// <param name="operation">The operation being planned.</param>
    public DescriptorPlan(OperationModel operation)
    {
        this.Operation = operation;
    }

    /// <summary>
    /// Gets the operation being planned.
    /// </summary>
    public OperationModel Operation { get; }

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
        T? candidate = default;
        var found = false;

        foreach (var item in this._metadata)
        {
            if (item is T typed)
            {
                if (found)
                {
                    // More than one match — ambiguous, return false.
                    metadata = default;
                    return false;
                }

                candidate = typed;
                found = true;
            }
        }

        metadata = candidate!;
        return found;
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

    /// <inheritdoc />
    public bool Equals(DescriptorPlan? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        // Cheap count checks before expensive SequenceEqual traversals
        if (this.Diagnostics.Count != other.Diagnostics.Count
            || this.Metadata.Count != other.Metadata.Count
            || this._renderSteps.Count != other._renderSteps.Count)
        {
            return false;
        }

        return EqualityComparer<OperationModel>.Default.Equals(this.Operation, other.Operation)
            && this.Diagnostics.SequenceEqual(other.Diagnostics)
            && this.Metadata.SequenceEqual(other.Metadata);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is DescriptorPlan other && this.Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        System.HashCode hash = default;
        hash.Add(this.Operation);

        foreach (var diagnostic in this.Diagnostics)
        {
            hash.Add(diagnostic);
        }

        foreach (var metadata in this.Metadata)
        {
            hash.Add(metadata);
        }

        hash.Add(this._renderSteps.Count);
        return hash.ToHashCode();
    }
}
