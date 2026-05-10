namespace Blueprint.SourceGenerator.Tests.Integration;

/// <summary>
/// Minimal <see cref="IServiceProvider"/> that returns <c>null</c> for all service requests.
/// Used as the root service provider when constructing generated executors in tests where
/// no constructor-time service resolution is needed.
/// </summary>
internal sealed class NullServiceProvider : IServiceProvider
{
    /// <summary>
    /// Gets the shared singleton instance.
    /// </summary>
    public static NullServiceProvider Instance { get; } = new();

    /// <inheritdoc />
    public object? GetService(Type serviceType)
    {
        return null;
    }
}
