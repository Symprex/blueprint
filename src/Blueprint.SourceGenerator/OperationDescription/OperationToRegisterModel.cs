using System.Diagnostics.CodeAnalysis;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.OperationDescription;

/// <summary>
/// Represents an in-flight registration of an operation, used to associate diagnostics with registrations that
/// are incomplete or invalid.
/// </summary>
/// <param name="TypeSymbol">The symbol representing the type being registered.</param>
/// <param name="OperationToRegisterModel">The model representing the registration of the operation.</param>
public record OperationConfigurationModel(
    INamedTypeSymbol TypeSymbol,
    OperationToRegisterModel OperationToRegisterModel)
{
    private readonly List<DiagnosticInfo> _diagnostics = [];

    /// <summary>
    /// Gets all diagnostics that have been added to this model.
    /// </summary>
    public IReadOnlyList<DiagnosticInfo> Diagnostics => this._diagnostics;

    /// <summary>
    /// Adds the given diagnostic to this model, meaning code will not be able to be generated due a misconfiguration
    /// of the operation.
    /// </summary>
    /// <param name="diagnostic">The diagnostic to add.</param>
    public void AddDiagnostic(DiagnosticInfo diagnostic)
    {
        this._diagnostics.Add(diagnostic);
    }
}

/// <summary>
/// A model representing the registration of an operation, used to configure the ApiOperationDescriptor.
/// </summary>
/// <param name="TypeName">The name of the type being registered.</param>
/// <param name="Namespace">The namespace this operation belongs to</param>
public record OperationToRegisterModel(string TypeName, string Namespace)
{
    private readonly List<object> _metadata = [];

    /// <summary>
    /// Gets all metadata that has been added to this model.
    /// </summary>
    public IReadOnlyList<object> Metadata => this._metadata;

    /// <summary>
    /// A safe version of the <see cref="TypeName"/> that can be used in code generation.
    /// </summary>
    public string SyntaxSafeTypeName => this.TypeName.Replace(".", "_");

    /// <summary>
    /// Adds the given metadata to this model, used to stash data between code parsing and code generation.
    /// </summary>
    /// <param name="model">The metadata to add.</param>
    public void AddMetadata(object model)
    {
        this._metadata.Add(model);
    }

    /// <summary>
    /// Tries to get the given metadata from this model, returning <c>true</c> if it exists.
    /// </summary>
    /// <param name="metadata">The metadata, if it exists.</param>
    /// <typeparam name="T">The type of the metadata.</typeparam>
    /// <returns>Whether the metadata exists.</returns>
    public bool TryGetMetadata<T>([NotNullWhen(true)] out T? metadata)
    {
        metadata = this.Metadata.OfType<T>().SingleOrDefault();

        return metadata != null;
    }

    /// <summary>
    /// Gets all metadata instances of the given type.
    /// </summary>
    /// <typeparam name="T">The type of the metadata.</typeparam>
    /// <returns>A list, possibly empty, of metadata instances.</returns>
    public IEnumerable<T> GetMetadatas<T>()
    {
        return this.Metadata.OfType<T>();
    }
}
