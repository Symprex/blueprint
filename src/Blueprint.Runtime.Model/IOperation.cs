namespace Blueprint.Model;

/// <summary>
/// Marker interface used to identify operations. Typically an operation will implement one of the
/// <see cref="ICommand"/> or <see cref="IQuery"/> interfaces that both extend this interface.
/// </summary>
public interface IOperation
{
    /// <summary>
    /// Configures the <see cref="ApiOperationDescriptor" /> for this operation.
    /// </summary>
    /// <returns>The configured <see cref="ApiOperationDescriptor"/>.</returns>
    static abstract ApiOperationDescriptor Configure();
}
