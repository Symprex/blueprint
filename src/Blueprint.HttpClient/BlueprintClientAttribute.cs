using System.Diagnostics;

namespace Blueprint.HttpClient;

/// <summary>
/// Marks a <see langword="partial"/> class as a typed client over a remote Blueprint API, for the source generator to
/// complete. The operations it calls are named with <see cref="BlueprintClientIncludeAttribute"/>.
/// </summary>
[Conditional("CODE_ANALYSIS")]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class BlueprintClientAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the <c>JsonSerializerContext</c> whose metadata the client reads and writes with, or
    /// <see langword="null"/> to use the JSON options' default reflection metadata.
    /// </summary>
    public Type? SerializerContext { get; set; }
}
