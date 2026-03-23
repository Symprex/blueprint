using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Blueprint.SourceGenerator.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests;

/// <summary>
/// The result of a compilation, from <see cref="TestHelpers.Compile{T}(string,bool)"/>.
/// </summary>
/// <param name="Inputs">The inputs to the compilation, from the tests.</param>
/// <param name="Diagnostics">The generated diagnostics.</param>
/// <param name="Output">The output of the compilation.</param>
public record GenerationResult(SyntaxTree[] Inputs, Diagnostic[] Diagnostics, SyntaxTree[] Output)
{
    /// <summary>
    /// Verifies that the compilation was successful, and that the output is as expected.
    /// </summary>
    /// <param name="snapshotsFolder">Where the snapshots are to be stored.</param>
    /// <param name="includeMarkerAttributes">Whether to include the source of marker attributes in the snapshot.</param>
    public async Task VerifySuccessAsync(string snapshotsFolder, bool includeMarkerAttributes = false)
    {
        if (this.Diagnostics.Length != 0)
        {
            throw new InvalidOperationException("The compilation failed with diagnostics:\n\n" + this.FormatErrors());
        }

        var outputText = new StringBuilder();

        foreach (var t in this.Output)
        {
            var source = t.ToString();

            if (includeMarkerAttributes == false && source.Contains("MarkerAttribute"))
            {
                continue;
            }

            outputText.AppendLine(t.FilePath + ":");
            outputText.AppendLine();
            outputText.AppendLine(source);
            outputText.AppendLine();
        }

        await Verify(outputText)
            .UseDirectory(snapshotsFolder);
    }

    /// <summary>
    /// Verifies that the compilation was successful, and that the output is as expected.
    /// </summary>
    /// <param name="snapshotsFolder">Where the snapshots are to be stored.</param>
    public async Task VerifyCompilationFailedAsync(string snapshotsFolder)
    {
        this.Diagnostics.Should().NotBeEmpty("we expected the input to generate diagnostic errors");

        await Verify(this.FormatErrors())
            .UseDirectory(snapshotsFolder);
    }

    private string FormatErrors()
    {
        var allSources = this.Inputs.Concat(this.Output)
            .ToDictionary(
                s => s.FilePath,
                s => s.ToString().Split(["\r\n", "\n"], StringSplitOptions.None));

        var result = new List<string>();
        var filesWithErrors = this.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(GetFilePath).Distinct().ToArray();

        foreach (var file in filesWithErrors)
        {
            if (!allSources.TryGetValue(file!, out var lines))
            {
                continue;
            }

            result.Add($"File: {file}");

            var diagnosticsByLine = this.Diagnostics
                .Where(d => GetFilePath(d) == file)
                .GroupBy(d => d.Location.GetLineSpan().StartLinePosition.Line)
                .ToDictionary(g => g.Key, g => g.ToList());

            for (var i = 0; i < lines.Length; i++)
            {
                result.Add(lines[i]);

                if (diagnosticsByLine.TryGetValue(i, out var lineDiagnostics))
                {
                    foreach (var diagnostic in lineDiagnostics.OrderBy(d => d.Location.GetLineSpan().StartLinePosition.Character))
                    {
                        var lineSpan = diagnostic.Location.GetLineSpan();

                        var startColumn = lineSpan.StartLinePosition.Character;
                        var endColumn = lineSpan.EndLinePosition.Character;
                        var pointer = new string(' ', startColumn) + new string('^', Math.Max(1, endColumn - startColumn));

                        result.Add(pointer + $" [{diagnostic.Severity}:{diagnostic.Id}] {diagnostic.GetMessage()}");
                    }
                }
            }

            result.Add(string.Empty);
        }

        return string.Join("\n", result);

        string GetFilePath(Diagnostic diagnostic)
        {
            return diagnostic.Location.GetLineSpan().Path;
        }
    }
}

public sealed record TrackedGeneratorDefinition(
    string Name,
    IIncrementalGenerator Generator,
    ImmutableArray<string> StageNames);

public sealed record GeneratedSourceSnapshot(string HintName, string SourceText);

public sealed record MultiGeneratorGenerationResult(
    Diagnostic[] Diagnostics,
    ImmutableDictionary<string, ImmutableArray<GeneratedSourceSnapshot>> GeneratedSources,
    ImmutableDictionary<string, ImmutableDictionary<string, ImmutableArray<IncrementalGeneratorRunStep>>> TrackedSteps)
{
    public ImmutableArray<GeneratedSourceSnapshot> GetGeneratedSources(string generatorName)
    {
        return this.GeneratedSources.TryGetValue(generatorName, out var generatedSources)
            ? generatedSources
            : [];
    }
}

public sealed record MultiGeneratorEditResult(
    MultiGeneratorGenerationResult Initial,
    MultiGeneratorGenerationResult Updated)
{
    public ImmutableArray<string> GetChangedGeneratedSourceHintNames(string generatorName)
    {
        var initialSources = this.Initial.GetGeneratedSources(generatorName)
            .ToDictionary(static source => source.HintName, static source => source.SourceText, StringComparer.Ordinal);

        return this.Updated.GetGeneratedSources(generatorName)
            .Where(source => !initialSources.TryGetValue(source.HintName, out var sourceText) || sourceText != source.SourceText)
            .Select(static source => source.HintName)
            .OrderBy(static hintName => hintName, StringComparer.Ordinal)
            .ToImmutableArray();
    }
}

/// <summary>
/// A set of helper methods for testing the generation of source code, including gathering of diagnostics and checking
/// the cacheability of the source generator to ensure it will be performant.
/// </summary>
/// <remarks>
/// This has been taken also as-is from https://andrewlock.net/creating-a-source-generator-part-10-testing-your-incremental-generator-pipeline-outputs-are-cacheable/
/// </remarks>
public static class TestHelpers
{
    public static GenerationResult Compile<T>(string source, bool assertCacheability = true)
        where T : IIncrementalGenerator, ITrackedSourceGenerator, new()
    {
        return Compile<T>([source], filePaths: null, assertCacheability);
    }

    public static TrackedGeneratorDefinition CreateTrackedGenerator<T>()
        where T : IIncrementalGenerator, ITrackedSourceGenerator, new()
    {
        var generator = new T();
        return new TrackedGeneratorDefinition(typeof(T).Name, generator, [.. generator.StageNames]);
    }

    public static MultiGeneratorGenerationResult CompileWithTrackedGenerators(
        TrackedGeneratorDefinition[] generators,
        string[] sources,
        string[] filePaths)
    {
        ArgumentNullException.ThrowIfNull(generators);

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CreateCompilation(sources, filePaths, parseOptions);
        var result = RunTrackedGenerators(generators, parseOptions, compilation);
        return result.Initial;
    }

    public static MultiGeneratorEditResult CompileWithTrackedGeneratorsAfterEdit(
        TrackedGeneratorDefinition[] generators,
        string[] initialSources,
        string[] updatedSources,
        string[] filePaths)
    {
        ArgumentNullException.ThrowIfNull(generators);

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var initialCompilation = CreateCompilation(initialSources, filePaths, parseOptions);
        var updatedCompilation = CreateCompilation(updatedSources, filePaths, parseOptions);
        return RunTrackedGenerators(generators, parseOptions, initialCompilation, updatedCompilation);
    }

    // You call this method passing in C# sources, and the list of stages you expect
    // It runs the generator, asserts the outputs are ok,
    private static GenerationResult Compile<T>(
        string[] sources, // C# source code
        string[]? filePaths,
        bool assertCacheability = true) // You can disable cacheability checking during dev
        where T : IIncrementalGenerator, ITrackedSourceGenerator, new() // T is your generator
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CreateCompilation(sources, filePaths, parseOptions);
        var syntaxTrees = compilation.SyntaxTrees.ToArray();

        // Run the generator, get the results, and assert cacheability if applicable
        var runResult = RunGeneratorAndAssertOutput<T>(
            parseOptions,
            compilation,
            assertCacheability);

        // Return the generator diagnostics and generated sources
        return new GenerationResult(syntaxTrees, runResult.Diagnostics, runResult.GeneratedSources.ToArray());
    }

    private static CSharpCompilation CreateCompilation(
        string[] sources,
        string[]? filePaths,
        CSharpParseOptions parseOptions)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (filePaths is not null && filePaths.Length != sources.Length)
        {
            throw new ArgumentException("Source file path count must match source count.", nameof(filePaths));
        }

        var syntaxTrees = sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, parseOptions, filePaths?[index] ?? $"Input{index}.cs")).ToArray();
        var references = CreateMetadataReferences();

        return CSharpCompilation.Create(
            "Tests",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static List<MetadataReference> CreateMetadataReferences()
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(HttpRequestMessage).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(System.Net.Http.Headers.AuthenticationHeaderValue).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.ICommand).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.OperationResult).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Runtime.Exec.ApiOperationContext).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(System.ComponentModel.DataAnnotations.ValidationResult).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.ILogger).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.LoggerMessage).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(System.Diagnostics.Activity).Assembly.Location));
        return references;
    }

    private static MultiGeneratorEditResult RunTrackedGenerators(
        TrackedGeneratorDefinition[] generators,
        CSharpParseOptions parseOptions,
        CSharpCompilation initialCompilation,
        CSharpCompilation? updatedCompilation = null)
    {
        var driver = CreateTrackedGeneratorDriver(generators, parseOptions);
        var initialRun = ExecuteTrackedGenerators(driver, initialCompilation, generators);

        if (updatedCompilation is null)
        {
            return new MultiGeneratorEditResult(initialRun.Result, initialRun.Result);
        }

        var updatedRun = ExecuteTrackedGenerators(initialRun.Driver, updatedCompilation, generators);
        return new MultiGeneratorEditResult(initialRun.Result, updatedRun.Result);
    }

    private static GeneratorDriver CreateTrackedGeneratorDriver(
        TrackedGeneratorDefinition[] generators,
        CSharpParseOptions parseOptions)
    {
        var sourceGenerators = generators.Select(static generator => generator.Generator.AsSourceGenerator()).ToArray();
        var driverOptions = new GeneratorDriverOptions(
            disabledOutputs: IncrementalGeneratorOutputKind.None,
            trackIncrementalGeneratorSteps: true);

        return CSharpGeneratorDriver.Create(sourceGenerators, driverOptions: driverOptions, parseOptions: parseOptions);
    }

    private static (GeneratorDriver Driver, MultiGeneratorGenerationResult Result) ExecuteTrackedGenerators(
        GeneratorDriver driver,
        CSharpCompilation compilation,
        TrackedGeneratorDefinition[] generators)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        var runResult = driver.GetRunResult();

        foreach (var result in runResult.Results)
        {
            if (result.Exception is not null)
            {
                ExceptionDispatchInfo.Throw(result.Exception);
            }
        }

        var emitResult = outputCompilation.Emit(new MemoryStream());
        var diagnostics = emitResult.Success
            ? runResult.Diagnostics.ToArray()
            : emitResult.Diagnostics.Concat(runResult.Diagnostics).ToArray();

        return (driver, CreateMultiGeneratorGenerationResult(runResult, diagnostics, generators));
    }

    private static MultiGeneratorGenerationResult CreateMultiGeneratorGenerationResult(
        GeneratorDriverRunResult runResult,
        Diagnostic[] diagnostics,
        TrackedGeneratorDefinition[] generators)
    {
        var generatedSources = ImmutableDictionary.CreateRange(
            generators.Select(
                (generator, index) => new KeyValuePair<string, ImmutableArray<GeneratedSourceSnapshot>>(
                    generator.Name,
                    runResult.Results[index].GeneratedSources
                        .Select(static source => new GeneratedSourceSnapshot(source.HintName, source.SourceText.ToString()))
                        .OrderBy(static source => source.HintName, StringComparer.Ordinal)
                        .ToImmutableArray())));
        var trackedSteps = ImmutableDictionary.CreateRange(
            generators.Select(
                (generator, index) => new KeyValuePair<string, ImmutableDictionary<string, ImmutableArray<IncrementalGeneratorRunStep>>>(
                    generator.Name,
                    runResult.Results[index].TrackedSteps
                        .Where(step => generator.StageNames.Contains(step.Key, StringComparer.Ordinal))
                        .ToImmutableDictionary(static step => step.Key, static step => step.Value, StringComparer.Ordinal))));

        return new MultiGeneratorGenerationResult(diagnostics, generatedSources, trackedSteps);
    }

    private static (Diagnostic[] Diagnostics, IEnumerable<SyntaxTree> GeneratedSources) RunGeneratorAndAssertOutput<T>(
        CSharpParseOptions parseOptions,
        CSharpCompilation compilation,
        bool assertCacheability = true)
        where T : IIncrementalGenerator, ITrackedSourceGenerator, new()
    {
        var trackedSourceGenerator = new T();
        var trackingNames = trackedSourceGenerator.StageNames;
        var generator = trackedSourceGenerator.AsSourceGenerator();

        // ⚠ Tell the driver to track all the incremental generator outputs
        // without this, you'll have no tracked outputs!
        var opts = new GeneratorDriverOptions(
            disabledOutputs: IncrementalGeneratorOutputKind.None,
            trackIncrementalGeneratorSteps: true);

        GeneratorDriver driver = CSharpGeneratorDriver.Create([generator], driverOptions: opts, parseOptions: parseOptions);

        // Create a clone of the compilation that we will use later
        var clone = compilation.Clone();

        // Do the initial run
        // Note that we store the returned driver value, as it contains cached previous outputs
        // We want the compilation to actually be emitted below to ensure we can actually compile, it's not enough
        // to just verify the source code generator outputs a verified source text
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outCompilation, out var outDiagnostics);
        var runResult = driver.GetRunResult();

        if (runResult.Results[0].Exception is not null)
        {
            ExceptionDispatchInfo.Throw(runResult.Results[0].Exception!);
        }

        // First run an actual compilation with the newly generated sources, ensure we have a compileable output
        var emitResult = outCompilation.Emit(new MemoryStream());

        if (emitResult.Success == false)
        {
            // We have compilation errors in original source, or generated code.
            return (emitResult.Diagnostics.Concat(runResult.Diagnostics).ToArray(), runResult.GeneratedTrees);
        }

        // Then, optionally, ensure the generator is using only cacheable stages
        if (assertCacheability)
        {
            // Run again, using the same driver, with a clone of the compilation
            var runResult2 = driver
                .RunGenerators(clone)
                .GetRunResult();

            runResult2.Results[0]
                .TrackedOutputSteps
                .Should().NotBeEmpty();

            // verify the second run only generated cached source outputs
            runResult2.Results[0]
                .TrackedOutputSteps
                .SelectMany(x => x.Value) // step executions
                .SelectMany(x => x.Outputs) // execution results
                .Should()
                .OnlyContain(x => x.Reason == IncrementalStepRunReason.Cached, "all outputs should be cacheable");

            // Compare all the tracked outputs, throw if there's a failure
            AssertRunsEqual(runResult, runResult2, trackingNames);
        }

        return (runResult.Diagnostics.ToArray(), runResult.GeneratedTrees);
    }

    private static void AssertRunsEqual(
        GeneratorDriverRunResult runResult1,
        GeneratorDriverRunResult runResult2,
        string[] trackingNames)
    {
        // We're given all the tracking names, but not all the
        // stages will necessarily execute, so extract all the
        // output steps, and filter to ones we know about
        var trackedSteps1 = GetTrackedSteps(runResult1, trackingNames);
        var trackedSteps2 = GetTrackedSteps(runResult2, trackingNames);

        // Both runs should have the same tracked steps
        trackedSteps1.Should()
            .NotBeEmpty()
            .And.HaveSameCount(trackedSteps2)
            .And.ContainKeys(trackedSteps2.Keys);

        // Get the IncrementalGeneratorRunStep collection for each run
        foreach (var (trackingName, runSteps1) in trackedSteps1)
        {
            // Assert that both runs produced the same outputs
            var runSteps2 = trackedSteps2[trackingName];
            AssertEqual(runSteps1, runSteps2, trackingName);
        }

        return;

        // Local function that extracts the tracked steps
        static Dictionary<string, ImmutableArray<IncrementalGeneratorRunStep>> GetTrackedSteps(
            GeneratorDriverRunResult runResult,
            string[] trackingNames)
        {
            return runResult
                .Results[0] // We're only running a single generator, so this is safe
                .TrackedSteps // Get the pipeline outputs
                .Where(step => trackingNames.Contains(step.Key)) // filter to known steps
                .ToDictionary(x => x.Key, x => x.Value);
        }
    }

    private static void AssertEqual(
        ImmutableArray<IncrementalGeneratorRunStep> runSteps1,
        ImmutableArray<IncrementalGeneratorRunStep> runSteps2,
        string stepName)
    {
        runSteps1.Should().HaveSameCount(runSteps2);

        for (var i = 0; i < runSteps1.Length; i++)
        {
            var runStep1 = runSteps1[i];
            var runStep2 = runSteps2[i];

            // The outputs should be equal between different runs
            var outputs1 = runStep1.Outputs.Select(x => x.Value);
            var outputs2 = runStep2.Outputs.Select(x => x.Value);

            outputs1.Should()
                .Equal(outputs2, $"because {stepName} should produce cacheable outputs");

            // Therefore, on the second run the results should always be cached or unchanged!
            // - Unchanged is when the _input_ has changed, but the output hasn't
            // - Cached is when the the input has not changed, so the cached output is used
            runStep2.Outputs
                .Should()
                .OnlyContain(
                    x => x.Reason == IncrementalStepRunReason.Cached || x.Reason == IncrementalStepRunReason.Unchanged,
                    $"{stepName} expected to have reason {IncrementalStepRunReason.Cached} or {IncrementalStepRunReason.Unchanged}");

            // Make sure we're not using anything we shouldn't
            AssertObjectGraph(runStep1, stepName);
        }
    }

    private static void AssertObjectGraph(IncrementalGeneratorRunStep runStep, string stepName)
    {
        // Including the stepName in error messages to make it easy to isolate issues
        var because = $"{stepName} shouldn't contain banned symbols";
        var visited = new HashSet<object?>();

        // Check all of the outputs - probably overkill, but why not
        foreach (var (obj, _) in runStep.Outputs)
        {
            Visit(obj);
        }

        void Visit(object? node)
        {
            // If we've already seen this object, or it's null, stop.
            if (node is null || !visited.Add(node))
            {
                return;
            }

            // Known types that we can bail early as we know they are not going to be banned.
            if (node is DiagnosticInfo or string or int or bool or double or float or long or short or byte or sbyte or ushort or uint or ulong or char or decimal or TimeSpan or DateTime or DateTimeOffset or Guid or Uri)
            {
                return;
            }

            // Make sure it's not a banned type
            node.Should()
                .NotBeOfType<Compilation>(because)
                .And.NotBeOfType<ISymbol>(because)
                .And.NotBeOfType<SyntaxNode>(because);

            // Examine the object
            var type = node.GetType();
            if (type.IsEnum)
            {
                return;
            }

            // If the object is a collection, check each of the values
            if (node is IEnumerable collection and not string)
            {
                try
                {
                    foreach (var element in collection)
                    {
                        // recursively check each element in the collection
                        Visit(element);
                    }
                }
                catch (InvalidOperationException) when (node.GetType().Name.Contains("ImmutableArray"))
                {
                    // Ignore this, it means the node is typed ImmutableArray that is empty
                }
                catch (NullReferenceException)
                {
                    // Ignore this, it can happen when node tries to create an inner collection
                }

                return;
            }

            // Recursively check each field in the object
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var fieldValue = field.GetValue(node);
                Visit(fieldValue);
            }
        }
    }
}
