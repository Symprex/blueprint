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
/// Captures one generator test run so assertions can inspect the original inputs, emitted diagnostics, and generated
/// output from the exact same Roslyn compilation.
/// </summary>
/// <param name="Inputs">The syntax trees that represented the test's handwritten input.</param>
/// <param name="Diagnostics">The diagnostics surfaced by either generation or the emitted compilation.</param>
/// <param name="Output">The generated syntax trees that were produced for the test run.</param>
public record GenerationResult(SyntaxTree[] Inputs, Diagnostic[] Diagnostics, SyntaxTree[] Output)
{
    /// <summary>
    /// Verifies a successful generation run against a reviewed snapshot so structural output changes stay intentional.
    /// </summary>
    /// <param name="snapshotsFolder">The snapshot folder that owns the reviewed baseline for this scenario.</param>
    /// <param name="includeMarkerAttributes">Set to <see langword="true"/> when the marker-attribute boilerplate is part of the behaviour under review.</param>
    /// <returns>A task that completes once the reviewed snapshot assertion has finished.</returns>
    public async Task VerifySuccessAsync(string snapshotsFolder, bool includeMarkerAttributes = false)
    {
        if (this.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error))
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
    /// Verifies a failing generation run against a reviewed diagnostic snapshot so validation regressions stay readable.
    /// </summary>
    /// <param name="snapshotsFolder">The snapshot folder that owns the reviewed diagnostic baseline for this scenario.</param>
    /// <returns>A task that completes once the reviewed diagnostic snapshot assertion has finished.</returns>
    public async Task VerifyCompilationFailedAsync(string snapshotsFolder)
    {
        this.Diagnostics.Should().NotBeEmpty("we expected the input to generate diagnostic errors");

        await Verify(this.FormatErrors())
            .UseDirectory(snapshotsFolder);
    }

    /// <summary>
    /// Verifies the generation produces informational or warning diagnostics (but no errors) and snapshot-matches them.
    /// </summary>
    /// <param name="snapshotsFolder">The snapshot folder that owns the reviewed baseline for this scenario.</param>
    /// <param name="expectedDiagnosticIds">One or more diagnostic IDs that must appear in the output.</param>
    /// <returns>A task that completes once the reviewed snapshot assertion has finished.</returns>
    public async Task VerifySuccessWithDiagnosticsAsync(string snapshotsFolder, params string[] expectedDiagnosticIds)
    {
        this.Diagnostics.Should().NotBeEmpty("we expected the input to generate diagnostics");
        this.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty("errors should not be present");

        foreach (var expectedId in expectedDiagnosticIds)
        {
            this.Diagnostics.Should().Contain(d => d.Id == expectedId, $"expected diagnostic {expectedId} to be emitted");
        }

        await Verify(this.FormatErrors(DiagnosticSeverity.Info))
            .UseDirectory(snapshotsFolder);
    }

    private string FormatErrors(DiagnosticSeverity minimumSeverity = DiagnosticSeverity.Error)
    {
        var allSources = this.Inputs.Concat(this.Output)
            .ToDictionary(
                s => s.FilePath,
                s => s.ToString().Split(["\r\n", "\n"], StringSplitOptions.None));

        var result = new List<string>();
        var matchingDiagnostics = this.Diagnostics.Where(d => d.Severity >= minimumSeverity).ToArray();
        var filesWithDiagnostics = matchingDiagnostics.Select(GetFilePath).Distinct().ToArray();

        foreach (var file in filesWithDiagnostics)
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

/// <summary>
/// Describes one tracked incremental generator inside the multi-generator harness.
/// </summary>
/// <param name="Name">The stable name used when reporting generated sources and tracked steps.</param>
/// <param name="Generator">The incremental generator instance to execute.</param>
/// <param name="StageNames">The stage names worth preserving when locality assertions compare tracked steps.</param>
public sealed record TrackedGeneratorDefinition(
    string Name,
    IIncrementalGenerator Generator,
    ImmutableArray<string> StageNames);

/// <summary>
/// Stores a Roslyn generated source as plain text so tests can compare outputs without depending on Roslyn wrappers.
/// </summary>
/// <param name="HintName">The generated hint name used as the stable file identifier.</param>
/// <param name="SourceText">The generated source text captured for comparison and snapshots.</param>
public sealed record GeneratedSourceSnapshot(string HintName, string SourceText);

/// <summary>
/// Keeps one complete multi-generator run together so locality tests can correlate diagnostics, generated text, and
/// tracked pipeline steps from the same driver execution.
/// </summary>
/// <param name="Diagnostics">The diagnostics surfaced by the run.</param>
/// <param name="GeneratedSources">The generated source snapshots grouped by generator name.</param>
/// <param name="TrackedSteps">The tracked incremental steps grouped by generator name and stage.</param>
public sealed record MultiGeneratorGenerationResult(
    Diagnostic[] Diagnostics,
    ImmutableDictionary<string, ImmutableArray<GeneratedSourceSnapshot>> GeneratedSources,
    ImmutableDictionary<string, ImmutableDictionary<string, ImmutableArray<IncrementalGeneratorRunStep>>> TrackedSteps)
{
    /// <summary>
    /// Returns the generated source snapshots for one generator without forcing each caller to duplicate dictionary
    /// fallback logic.
    /// </summary>
    /// <param name="generatorName">The tracked generator name to read generated sources for.</param>
    /// <returns>The generated sources for the requested generator, or an empty array when it produced none.</returns>
    public ImmutableArray<GeneratedSourceSnapshot> GetGeneratedSources(string generatorName)
    {
        return this.GeneratedSources.TryGetValue(generatorName, out var generatedSources)
            ? generatedSources
            : [];
    }
}

/// <summary>
/// Pairs the initial and updated multi-generator runs for edit-locality assertions.
/// </summary>
/// <param name="Initial">The baseline run before a source edit is applied.</param>
/// <param name="Updated">The run captured after the source edit is applied.</param>
public sealed record MultiGeneratorEditResult(
    MultiGeneratorGenerationResult Initial,
    MultiGeneratorGenerationResult Updated)
{
    /// <summary>
    /// Identifies which generated outputs changed for one generator after an edit so locality tests can stay focused on
    /// the affected artifacts instead of diffing every file manually.
    /// </summary>
    /// <param name="generatorName">The tracked generator name to diff.</param>
    /// <returns>The ordered hint names whose generated source changed between the initial and updated runs.</returns>
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
/// Central Roslyn harness for generator tests.
/// </summary>
/// <remarks>
/// These helpers intentionally compile real in-memory projects, emit the resulting assembly, and rerun the same driver
/// to prove cacheability. Tests should prefer this shared harness over ad-hoc Roslyn setup so every slice exercises the
/// same correctness and incremental-behaviour constraints. The original cacheability pattern was adapted from
/// https://andrewlock.net/creating-a-source-generator-part-10-testing-your-incremental-generator-pipeline-outputs-are-cacheable/.
/// </remarks>
public static class TestHelpers
{
    /// <summary>
    /// Runs a single tracked generator against one source string and verifies the same driver can replay its work from
    /// cache on a second run.
    /// </summary>
    /// <typeparam name="T">The tracked incremental generator under test.</typeparam>
    /// <param name="source">The handwritten source that should trigger the generator.</param>
    /// <param name="assertCacheability">Set to <see langword="false"/> only when debugging a failing test and you need to inspect the first run in isolation.</param>
    /// <returns>The captured diagnostics and generated syntax trees for the run.</returns>
    public static GenerationResult Compile<T>(string source, bool assertCacheability = true)
        where T : IIncrementalGenerator, ITrackedSourceGenerator, new()
    {
        return Compile<T>([source], filePaths: null, assertCacheability);
    }

    /// <summary>
    /// Creates a tracked generator definition that records the stage names a locality test is allowed to reason about.
    /// </summary>
    /// <typeparam name="T">The tracked incremental generator to instantiate.</typeparam>
    /// <returns>A tracked generator definition ready for the multi-generator harness.</returns>
    public static TrackedGeneratorDefinition CreateTrackedGenerator<T>()
        where T : IIncrementalGenerator, ITrackedSourceGenerator, new()
    {
        var generator = new T();
        return new TrackedGeneratorDefinition(typeof(T).Name, generator, [.. generator.StageNames]);
    }

    /// <summary>
    /// Creates the standard Roslyn generator driver used by Blueprint tests so manual driver-based scenarios can share
    /// the same parse options and incremental-tracking defaults as the main harness.
    /// </summary>
    /// <param name="generator">The incremental generator instance to execute.</param>
    /// <param name="parseOptions">Optional parse options; when omitted the shared preview-language defaults are used.</param>
    /// <returns>A generator driver configured with Blueprint's default incremental tracking behaviour.</returns>
    public static GeneratorDriver CreateBlueprintSourceGeneratorDriver(
        IIncrementalGenerator generator,
        CSharpParseOptions? parseOptions = null)
    {
        ArgumentNullException.ThrowIfNull(generator);

        var driverOptions = new GeneratorDriverOptions(
            disabledOutputs: IncrementalGeneratorOutputKind.None,
            trackIncrementalGeneratorSteps: true);

        return CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()],
            driverOptions: driverOptions,
            parseOptions: parseOptions ?? new CSharpParseOptions(LanguageVersion.Preview));
    }

    /// <summary>
    /// Executes several tracked generators against the same compilation so tests can observe how descriptor, app, and
    /// pipeline emitters behave together from one baseline input set.
    /// </summary>
    /// <param name="generators">The tracked generators that should run against the shared compilation.</param>
    /// <param name="sources">The source texts that make up the baseline compilation.</param>
    /// <param name="filePaths">The stable file paths to associate with each source so diagnostics stay deterministic.</param>
    /// <param name="additionalReferences">Extra metadata references to add to the compilation (e.g. for Marten/Rebus auto-detection).</param>
    /// <returns>The first multi-generator run captured from the shared driver execution.</returns>
    public static MultiGeneratorGenerationResult CompileWithTrackedGenerators(
        TrackedGeneratorDefinition[] generators,
        string[] sources,
        string[] filePaths,
        MetadataReference[]? additionalReferences = null)
    {
        ArgumentNullException.ThrowIfNull(generators);

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CreateCompilation(sources, filePaths, parseOptions);

        if (additionalReferences is { Length: > 0 })
        {
            compilation = compilation.AddReferences(additionalReferences);
        }

        var result = RunTrackedGenerators(generators, parseOptions, compilation);
        return result.Initial;
    }

    /// <summary>
    /// Reuses the same tracked-generator driver across an initial and updated compilation so locality tests can observe
    /// which generated files and incremental stages truly changed after a targeted edit.
    /// </summary>
    /// <param name="generators">The tracked generators that should participate in both runs.</param>
    /// <param name="initialSources">The baseline source texts before the edit.</param>
    /// <param name="updatedSources">The source texts after the edit is applied.</param>
    /// <param name="filePaths">The stable file paths shared by both compilations.</param>
    /// <returns>The paired baseline and post-edit run results.</returns>
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
        // Exclude Marten and Rebus assemblies from the default compilation references.
        // Auto-detecting modules (MartenScopeModule, RebusTransactionModule) activate when
        // these types are found in the compilation. Module-specific tests add them back via
        // their own CreateCompilation methods.
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Where(static assembly =>
            {
                var name = assembly.GetName().Name;
                return name is not null
                    && !name.Contains("Marten", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("Rebus", StringComparison.OrdinalIgnoreCase);
            })
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
        references.Add(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Options.IOptions<>).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(System.Diagnostics.Activity).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(System.Text.Json.Serialization.JsonSerializerContext).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Primitives.StringValues).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Http.IQueryCollection).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Http.HttpContext).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.HttpClient.BlueprintHttpClient<>).Assembly.Location));

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
