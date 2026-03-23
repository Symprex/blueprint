using System.Collections.Immutable;

namespace Blueprint.SourceGenerator.Tests.Scale;

/// <summary>
/// Describes the minimum knobs needed to synthesise a repeatable Blueprint app for scale and invalidation tests.
/// </summary>
/// <remarks>
/// The options stay intentionally small so benchmark and locality diffs reflect the behaviour under test rather than a
/// large amount of fixture configuration noise.
/// </remarks>
public sealed record SyntheticBlueprintFixtureOptions
{
    /// <summary>
    /// Initialises fixture options for a synthetic Blueprint app.
    /// </summary>
    /// <param name="namespaceName">The namespace shared by the synthetic app and generated operations.</param>
    /// <param name="appName">The synthetic Blueprint app type name.</param>
    /// <param name="operationCount">The number of generated operations the fixture should emit.</param>
    /// <param name="includeRootLinks">A value indicating whether every synthetic operation should include a unique root link.</param>
    public SyntheticBlueprintFixtureOptions(string namespaceName, string appName, int operationCount, bool includeRootLinks = false)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            throw new ArgumentException("Fixture namespace name is required.", nameof(namespaceName));
        }

        if (string.IsNullOrWhiteSpace(appName))
        {
            throw new ArgumentException("Fixture app name is required.", nameof(appName));
        }

        if (operationCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(operationCount), operationCount, "Fixture operation count must be at least 1.");
        }

        this.NamespaceName = namespaceName;
        this.AppName = appName;
        this.OperationCount = operationCount;
        this.IncludeRootLinks = includeRootLinks;
    }

    /// <summary>
    /// Gets the namespace shared by the synthetic app and its generated operations.
    /// </summary>
    public string NamespaceName { get; }

    /// <summary>
    /// Gets the synthetic Blueprint app type name.
    /// </summary>
    public string AppName { get; }

    /// <summary>
    /// Gets the number of generated operations the fixture should emit.
    /// </summary>
    public int OperationCount { get; }

    /// <summary>
    /// Gets a value indicating whether every synthetic operation should include a unique root link.
    /// </summary>
    public bool IncludeRootLinks { get; }
}

/// <summary>
/// Represents one physical source file within a synthetic Blueprint fixture.
/// </summary>
/// <param name="Path">The stable relative path used when compiling or mutating the fixture.</param>
/// <param name="Content">The source text that should be compiled for the file.</param>
public sealed record SyntheticBlueprintFixtureSourceFile(string Path, string Content);

/// <summary>
/// Represents a complete synthetic Blueprint app as a set of independently addressable source files plus the small
/// amount of scenario metadata that performance artifacts need to describe what the fixture actually contains.
/// </summary>
/// <param name="Options">The stable options used to generate the fixture.</param>
/// <param name="SourceFiles">The source files that make up the synthetic Blueprint app.</param>
public sealed record SyntheticBlueprintFixture(
    SyntheticBlueprintFixtureOptions Options,
    ImmutableArray<SyntheticBlueprintFixtureSourceFile> SourceFiles)
{
    /// <summary>
    /// Gets the namespace shared by the synthetic app and generated operations.
    /// </summary>
    public string NamespaceName => this.Options.NamespaceName;

    /// <summary>
    /// Gets the synthetic Blueprint app type name.
    /// </summary>
    public string AppName => this.Options.AppName;

    /// <summary>
    /// Gets the number of generated operations in the fixture.
    /// </summary>
    public int OperationCount => this.Options.OperationCount;

    /// <summary>
    /// Gets the legacy concatenated view of the fixture for older tests that still compile from one combined source blob.
    /// </summary>
    public string CombinedSource => string.Join(
        Environment.NewLine + Environment.NewLine,
        this.SourceFiles.Select(static sourceFile => sourceFile.Content));

    /// <summary>
    /// Creates a new fixture with one named source file rewritten.
    /// </summary>
    /// <param name="path">The relative source-file path to mutate.</param>
    /// <param name="mutation">The transformation to apply to the selected file contents.</param>
    /// <returns>A new fixture whose selected source file has been rewritten.</returns>
    public SyntheticBlueprintFixture MutateSourceFile(string path, Func<string, string> mutation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(mutation);

        var sourceFiles = ImmutableArray.CreateBuilder<SyntheticBlueprintFixtureSourceFile>(this.SourceFiles.Length);
        var found = false;

        foreach (var sourceFile in this.SourceFiles)
        {
            if (string.Equals(sourceFile.Path, path, StringComparison.Ordinal))
            {
                sourceFiles.Add(sourceFile with { Content = mutation(sourceFile.Content) });
                found = true;
                continue;
            }

            sourceFiles.Add(sourceFile);
        }

        if (!found)
        {
            throw new InvalidOperationException($"Could not find synthetic fixture source file '{path}'.");
        }

        return new SyntheticBlueprintFixture(this.Options, sourceFiles.MoveToImmutable());
    }

    /// <summary>
    /// Creates a new fixture with one generated operation file rewritten.
    /// </summary>
    /// <param name="operationIndex">The one-based synthetic operation index to mutate.</param>
    /// <param name="mutation">The transformation to apply to the selected operation file.</param>
    /// <returns>A new fixture whose selected operation source file has been rewritten.</returns>
    public SyntheticBlueprintFixture MutateOperation(int operationIndex, Func<string, string> mutation)
    {
        var operationPath = $@"Operations\SyntheticOperation{operationIndex:000}.cs";
        return this.MutateSourceFile(operationPath, mutation);
    }
}

/// <summary>
/// Creates the repeatable synthetic Blueprint app used by scale and invalidation harnesses.
/// </summary>
public static class SyntheticBlueprintFixtureGenerator
{
    /// <summary>
    /// Builds a synthetic Blueprint app whose structure stays simple enough for benchmark and locality tests to attribute
    /// changes to the infrastructure under test instead of fixture complexity.
    /// </summary>
    /// <param name="options">The options that define the synthetic app shape.</param>
    /// <returns>The generated synthetic fixture.</returns>
    public static SyntheticBlueprintFixture Create(SyntheticBlueprintFixtureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var sourceFiles = ImmutableArray.CreateBuilder<SyntheticBlueprintFixtureSourceFile>(options.OperationCount + 2);
        var appSource = $$"""
                           namespace {{options.NamespaceName}}
                           {
                               [global::Blueprint.Model.BlueprintAppScan]
                               public partial class {{options.AppName}} : global::Blueprint.Model.BlueprintApp
                               {
                               }
                           }
                           """;

        sourceFiles.Add(new SyntheticBlueprintFixtureSourceFile(
            $"{options.AppName}.cs",
            appSource));

        var sharedBaseSource = $$"""
                                 namespace {{options.NamespaceName}}
                                 {
                                     public abstract class SyntheticOperationBase
                                     {
                                         [global::System.ComponentModel.DataAnnotations.Required]
                                         public string? Value { get; init; }
                                     }
                                 }
                                 """;

        sourceFiles.Add(new SyntheticBlueprintFixtureSourceFile(
            @"Shared\SyntheticOperationBase.cs",
            sharedBaseSource));

        for (var operationIndex = 1; operationIndex <= options.OperationCount; operationIndex++)
        {
            var operationName = $"SyntheticOperation{operationIndex:000}";
            var operationSource = CreateOperationSource(options.NamespaceName, operationName, operationIndex, options.IncludeRootLinks);

            sourceFiles.Add(new SyntheticBlueprintFixtureSourceFile(
                $@"Operations\{operationName}.cs",
                operationSource));
        }

        return new SyntheticBlueprintFixture(options, sourceFiles.MoveToImmutable());
    }

    private static string CreateOperationSource(string namespaceName, string operationName, int operationIndex, bool includeRootLinks)
    {
        var rootLinkAttribute = string.Empty;
        var idProperty = string.Empty;

        if (includeRootLinks)
        {
            var rootLinkUrl = $"/synthetic/operations/{operationIndex:000}/{{Id}}";
            rootLinkAttribute = $$"""
                                  [global::Blueprint.Model.Attributes.RootLink("{{rootLinkUrl}}")]
                                  """;
            idProperty = """
                                           public int Id { get; init; }

                                """;
        }

        return $$"""
                 namespace {{namespaceName}}
                 {
                     {{rootLinkAttribute}}
                     public partial class {{operationName}} : SyntheticOperationBase, global::Blueprint.Model.IQuery<string>
                     {
                         {{idProperty}}        public static global::System.Threading.Tasks.Task<string> InvokeAsync()
                         {
                             return global::System.Threading.Tasks.Task.FromResult("{{operationName}}");
                         }
                    }
                }
                """;
    }
}
