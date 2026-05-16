using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Blueprint.Utilities;

/// <summary>
/// Extension methods for reading embedded resources from assemblies and types, using
/// namespace-relative naming conventions to locate resources co-located with the owning type.
/// </summary>
public static class EmbeddedResourceExtensions
{
    /// <summary>
    /// Gets the fully-qualified embedded resource name by combining the type's namespace with a file name.
    /// </summary>
    /// <param name="type">The type whose namespace provides the root.</param>
    /// <param name="fileName">The file name (dots separate folder segments).</param>
    /// <returns>The fully-qualified resource name.</returns>
    public static string GetRelativeResourceName(this Type type, string fileName)
    {
        return type.Namespace + "." + fileName;
    }

    /// <summary>
    /// Reads an embedded resource as a string from the given assembly.
    /// </summary>
    /// <param name="assembly">The assembly containing the resource.</param>
    /// <param name="name">The fully-qualified resource name.</param>
    /// <returns>The resource content, or <c>null</c> if not found.</returns>
    [RequiresUnreferencedCode("Embedded resource lookup uses reflection.")]
    public static string? GetEmbeddedResourceAsString(this Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name);

        if (stream == null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    /// <summary>
    /// Reads an embedded resource as a string from the assembly containing the given type.
    /// </summary>
    /// <param name="type">A type in the assembly containing the resource.</param>
    /// <param name="name">The fully-qualified resource name.</param>
    /// <returns>The resource content, or <c>null</c> if not found.</returns>
    [RequiresUnreferencedCode("Embedded resource lookup uses reflection.")]
    public static string? GetEmbeddedResourceAsString(this Type type, string name)
    {
        return GetEmbeddedResourceAsString(type.Assembly, name);
    }

    /// <summary>
    /// Reads an embedded resource as a string, using the type's namespace to resolve the name.
    /// </summary>
    /// <param name="type">A type whose namespace provides the root.</param>
    /// <param name="name">The relative resource name (e.g. "Templates.Default.html").</param>
    /// <returns>The resource content, or <c>null</c> if not found.</returns>
    [RequiresUnreferencedCode("Embedded resource lookup uses reflection.")]
    public static string? GetRelativeEmbeddedResourceAsString(this Type type, string name)
    {
        return GetEmbeddedResourceAsString(type.Assembly, type.GetRelativeResourceName(name));
    }

    /// <summary>
    /// Reads an embedded resource as a byte array from the given assembly.
    /// </summary>
    /// <param name="assembly">The assembly containing the resource.</param>
    /// <param name="name">The fully-qualified resource name.</param>
    /// <returns>The resource bytes, or <c>null</c> if not found.</returns>
    [RequiresUnreferencedCode("Embedded resource lookup uses reflection.")]
    public static byte[]? GetEmbeddedResourceAsByteArray(this Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name);

        if (stream == null)
        {
            return null;
        }

        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);

        return bytes;
    }

    /// <summary>
    /// Reads an embedded resource as a byte array from the assembly containing the given type.
    /// </summary>
    /// <param name="type">A type in the assembly containing the resource.</param>
    /// <param name="name">The fully-qualified resource name.</param>
    /// <returns>The resource bytes, or <c>null</c> if not found.</returns>
    [RequiresUnreferencedCode("Embedded resource lookup uses reflection.")]
    public static byte[]? GetEmbeddedResourceAsByteArray(this Type type, string name)
    {
        return GetEmbeddedResourceAsByteArray(type.Assembly, name);
    }

    /// <summary>
    /// Reads an embedded resource as a byte array, using the type's namespace to resolve the name.
    /// </summary>
    /// <param name="type">A type whose namespace provides the root.</param>
    /// <param name="name">The relative resource name (e.g. "Assets.logo.png").</param>
    /// <returns>The resource bytes, or <c>null</c> if not found.</returns>
    [RequiresUnreferencedCode("Embedded resource lookup uses reflection.")]
    public static byte[]? GetRelativeEmbeddedResourceAsByteArray(this Type type, string name)
    {
        return GetEmbeddedResourceAsByteArray(type.Assembly, type.GetRelativeResourceName(name));
    }
}
