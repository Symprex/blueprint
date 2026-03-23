namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Provides shared reflection helpers for discovering tracked stage constants on generator stage containers.
/// </summary>
public static class StageHelpers
{
    /// <summary>
    /// Returns every non-empty string constant declared on the supplied stage container type.
    /// </summary>
    /// <param name="type">The stage container type whose constants should be read.</param>
    /// <returns>The tracked stage names declared on the supplied type.</returns>
    public static string[] FindAll(Type type)
    {
        return type
            .GetFields()
            .Where(fi => fi is { IsLiteral: true, IsInitOnly: false } && fi.FieldType == typeof(string))
            .Select(x => (string?)x.GetRawConstantValue())
            .Where(x => !string.IsNullOrEmpty(x))
            .OfType<string>()
            .ToArray();
    }
}
