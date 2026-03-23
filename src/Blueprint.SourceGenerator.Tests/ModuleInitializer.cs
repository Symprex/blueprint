using System.Runtime.CompilerServices;

namespace Blueprint.SourceGenerator.Tests;

/// <summary>
/// Ensures Verify source-generator defaults are initialised once for the test assembly before any snapshot assertions run.
/// </summary>
public static class ModuleInitializer
{
    /// <summary>
    /// Boots the shared Verify configuration for this test assembly.
    /// </summary>
    [ModuleInitializer]
    public static void Init()
    {
        VerifySourceGenerators.Initialize();
    }
}
