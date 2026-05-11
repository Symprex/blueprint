namespace Blueprint.Model;

/// <summary>
/// Marks a class of having a "resource key", as used in the claims required API authoriser.
/// </summary>
public interface IHaveResourceKey
{
    /// <summary>
    /// The resource key of this resource.
    /// </summary>
    string ResourceKey { get; }
}
