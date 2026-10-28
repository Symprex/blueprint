namespace Blueprint.SourceGenerator.Tests;

/// <summary>
/// Tests that run <c>dotnet build</c> against projects referencing the Blueprint source projects. They share a collection
/// so they never build the same referenced projects at the same time.
/// </summary>
[CollectionDefinition(Name)]
public class RealBuildCollection
{
    /// <summary>
    /// The collection's name.
    /// </summary>
    public const string Name = "Real builds";
}
