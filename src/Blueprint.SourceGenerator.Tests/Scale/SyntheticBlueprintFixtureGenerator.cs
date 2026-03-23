using System.Collections.Immutable;

namespace Blueprint.SourceGenerator.Tests.Scale;

public sealed record SyntheticBlueprintFixtureOptions
{
    public SyntheticBlueprintFixtureOptions(string namespaceName, string appName, int operationCount)
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
    }

    public string NamespaceName { get; }

    public string AppName { get; }

    public int OperationCount { get; }
}

public sealed record SyntheticBlueprintFixtureSourceFile(string Path, string Content);

public sealed record SyntheticBlueprintFixture(ImmutableArray<SyntheticBlueprintFixtureSourceFile> SourceFiles)
{
    public string CombinedSource => string.Join(
        Environment.NewLine + Environment.NewLine,
        this.SourceFiles.Select(static sourceFile => sourceFile.Content));

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

        return new SyntheticBlueprintFixture(sourceFiles.MoveToImmutable());
    }

    public SyntheticBlueprintFixture MutateOperation(int operationIndex, Func<string, string> mutation)
    {
        var operationPath = $@"Operations\SyntheticOperation{operationIndex:000}.cs";
        return this.MutateSourceFile(operationPath, mutation);
    }
}

public static class SyntheticBlueprintFixtureGenerator
{
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
            var operationSource = $$"""
                                  namespace {{options.NamespaceName}}
                                  {
                                      public partial class {{operationName}} : SyntheticOperationBase, global::Blueprint.Model.IQuery<string>
                                      {
                                          public static global::System.Threading.Tasks.Task<string> InvokeAsync()
                                          {
                                              return global::System.Threading.Tasks.Task.FromResult("{{operationName}}");
                                          }
                                     }
                                 }
                                 """;

            sourceFiles.Add(new SyntheticBlueprintFixtureSourceFile(
                $@"Operations\{operationName}.cs",
                operationSource));
        }

        return new SyntheticBlueprintFixture(sourceFiles.MoveToImmutable());
    }
}
