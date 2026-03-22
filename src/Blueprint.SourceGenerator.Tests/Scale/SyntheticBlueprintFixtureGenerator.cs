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
}

public static class SyntheticBlueprintFixtureGenerator
{
    public static SyntheticBlueprintFixture Create(SyntheticBlueprintFixtureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var sourceFiles = ImmutableArray.CreateBuilder<SyntheticBlueprintFixtureSourceFile>(options.OperationCount + 1);
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

        for (var operationIndex = 1; operationIndex <= options.OperationCount; operationIndex++)
        {
            var operationName = $"SyntheticOperation{operationIndex:000}";
            var operationSource = $$"""
                                namespace {{options.NamespaceName}}
                                {
                                    public partial class {{operationName}} : global::Blueprint.Model.IOperation
                                    {
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
