namespace Blueprint.SourceGenerator.Helpers;

public static class StageHelpers
{
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
