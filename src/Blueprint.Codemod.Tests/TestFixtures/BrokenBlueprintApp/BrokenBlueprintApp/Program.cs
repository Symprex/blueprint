/// <summary>
/// Entry point for the broken fixture solution used to prove the dry-run runner rejects unrelated build failures.
/// Contains only a compile error with no legacy Blueprint API usage (no BPMIG diagnostics should appear).
/// </summary>
public static class Program
{
    /// <summary>
    /// Contains an unresolved symbol that produces a CS0103 compile error.
    /// </summary>
    public static void Main()
    {
        _ = missingSymbol;
    }
}
