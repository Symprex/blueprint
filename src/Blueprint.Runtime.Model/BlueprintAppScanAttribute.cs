using System.Diagnostics;

namespace Blueprint.Model;

/// <summary>
/// Attribute to be added to a concrete instance of <See cref="BlueprintAppDataModel"/> to indicate types
/// to scan and register as operations
/// </summary>
[Conditional("CODE_ANALYSIS")]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class BlueprintAppScanAttribute : Attribute
{
    /// <summary>
    /// Set the assembly containing the given type as the source of types to register.
    /// If not specified, the assembly containing the method with this attribute will be used.
    /// </summary>
    public Type? FromAssemblyOf { get; set; }
}
