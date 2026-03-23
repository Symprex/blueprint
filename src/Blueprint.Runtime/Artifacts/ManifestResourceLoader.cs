using System.Reflection;
using System.Text;

namespace Blueprint.Runtime.Artifacts;

/// <summary>
/// Loads generated manifest artifacts from assembly resources so runtime inspection surfaces can reuse build-time
/// outputs instead of rediscovering the API shape.
/// </summary>
public static class ManifestResourceLoader
{
    /// <summary>
    /// Loads the required manifest resource as UTF-8 text.
    /// </summary>
    /// <param name="assembly">The assembly that should contain the embedded manifest resource.</param>
    /// <param name="resourceName">The exact manifest resource name to load.</param>
    /// <returns>The manifest JSON stored in the embedded resource.</returns>
    public static string LoadRequiredString(Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (string.IsNullOrWhiteSpace(resourceName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(resourceName));
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            var availableResources = string.Join(", ", assembly.GetManifestResourceNames().OrderBy(static name => name, StringComparer.Ordinal));
            throw new InvalidOperationException(
                $"Could not find manifest resource '{resourceName}' in assembly '{assembly.GetName().Name}'. Available resources: {availableResources}");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
