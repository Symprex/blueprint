using System.Reflection;
using FluentAssertions;

namespace Blueprint.Codemod.Tests;

public sealed class ApplyModeTests
{
    [Xunit.Fact]
    public async Task When_applying_against_a_fixture_copy_then_supported_auto_fixable_activations_are_removed_and_configuration_findings_remain()
    {
        var fixtureRoot = CreateWorkingFixtureRoot();
        try
        {
            var solutionPath = Path.Combine(fixtureRoot, "SimpleBlueprintApp.sln");
            var programPath = Path.Combine(fixtureRoot, "SimpleBlueprintApp", "Program.cs");
            var applier = new TransformationApplier();

            await applier.ApplyAsync(
                new ApplyModeOptions(
                    solutionPath: solutionPath,
                    diagnosticFilter: "BPMIG*"));

            var updatedSource = await File.ReadAllTextAsync(programPath);
            updatedSource.Should().NotContain("builder.AddOpenApi();");
            updatedSource.Should().Contain("builder.AddOpenApi(options => options.Title = \"Legacy\");");
            updatedSource.Should().Contain("builder.AddAuthentication(_ => { });");

            // BPMIG203: the pure auth-claim template is auto-fixed by dropping the resource key
            // template argument, while the non-auth {TenantId} template is left for manual review.
            updatedSource.Should().Contain("[Resource(\"Disclaimer\", \"Update\")]");
            updatedSource.Should().Contain("[Resource(\"Account\", \"Read\", \"/Tenants/{TenantId}\")]");

            // A mixed auth-claim + non-auth template must survive ApplyAsync's real batch fix-all
            // path with its arity untouched: every placeholder must be an auth claim before the
            // resource-key-template argument is dropped, and this template's first placeholder
            // alone would incorrectly pass an "any" predicate. BPMIG201 legitimately normalises the
            // old "{Auth:...}" colon syntax to "{Auth....}" dot syntax in the same pass, but BPMIG203
            // must still decline to drop the argument because of the trailing non-auth "{Id}".
            updatedSource.Should().NotContain("[Resource(\"User\", \"Read\")]");
            updatedSource.Should().Contain("[Resource(\"User\", \"Read\", \"/Tenants/{Auth.AccountId}/Users/{Id}\")]");

            var runner = new SolutionAnalysisRunner();
            var report = await runner.DryRunAsync(
                new SolutionAnalysisOptions(
                    solutionPath: solutionPath,
                    diagnosticFilter: "BPMIG*"));

            report.Report.Findings.Select(static finding => finding.DiagnosticId)
                .Should()
                .Equal("BPMIG100", "BPMIG101", "BPMIG200", "BPMIG203", "BPMIG302");
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    [Xunit.Fact]
    public async Task When_applying_twice_against_the_same_fixture_copy_then_the_second_run_is_idempotent()
    {
        var fixtureRoot = CreateWorkingFixtureRoot();
        try
        {
            var solutionPath = Path.Combine(fixtureRoot, "SimpleBlueprintApp.sln");
            var programPath = Path.Combine(fixtureRoot, "SimpleBlueprintApp", "Program.cs");
            var applier = new TransformationApplier();
            var options = new ApplyModeOptions(
                solutionPath: solutionPath,
                diagnosticFilter: "BPMIG*");

            await applier.ApplyAsync(options);
            var sourceAfterFirstApply = await File.ReadAllTextAsync(programPath);

            await applier.ApplyAsync(options);
            var sourceAfterSecondApply = await File.ReadAllTextAsync(programPath);

            sourceAfterSecondApply.Should().Be(sourceAfterFirstApply);
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    [Xunit.Fact]
    public async Task When_a_project_holds_many_independent_diagnostics_then_apply_mode_batches_them_instead_of_recompiling_per_fix()
    {
        // Apply mode's cost is compile-and-analyse passes, not fixes: each pass is a full
        // compilation plus a full analyzer run over the project. Recomputing that per individual
        // fix is what turns a large solution from minutes into hours - Signature365's 3,301
        // auto-fixable diagnostics would pay 3,301 passes. The fixture holds 41 independent
        // BPMIG200 sites in one project, so a batching applier needs a handful of passes to
        // converge while a per-fix applier needs one per site.
        var fixtureRoot = CreateWorkingFixtureRoot("ManyDiagnosticsApp");
        try
        {
            var solutionPath = Path.Combine(fixtureRoot, "ManyDiagnosticsApp.sln");
            var programPath = Path.Combine(fixtureRoot, "ManyDiagnosticsApp", "Program.cs");
            var siteCount = System.Text.RegularExpressions.Regex.Matches(
                await File.ReadAllTextAsync(programPath),
                @"\[FromResource\(typeof\([^)]*\), ""\{Auth:AccountId\}""\)\]").Count;
            siteCount.Should().BeGreaterThan(30, "the fixture must hold enough sites for the pass count to be meaningful");

            var applier = new TransformationApplier();
            await applier.ApplyAsync(
                new ApplyModeOptions(
                    solutionPath: solutionPath,
                    diagnosticFilter: "BPMIG*"));

            // Correctness first: batching must not lose or corrupt a fix.
            var updatedSource = await File.ReadAllTextAsync(programPath);
            updatedSource.Should().NotContain(
                @"[FromResource(typeof(AccountLookupQuery), ""{Auth:AccountId}"")]",
                "every auto-fixable BPMIG200 site must have its resource key template dropped");
            updatedSource.Should().Contain("[FromResource(typeof(AccountLookupQuery))]");

            var passes = (int)typeof(TransformationApplier)
                .GetProperty("AnalyzerPasses", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(applier)!;

            passes.Should().BeLessThan(
                siteCount,
                "apply mode must batch a diagnostic id across the project rather than recompiling once per fix");
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    [Xunit.Fact]
    public async Task When_one_document_holds_several_legacy_usings_then_each_is_replaced_exactly_once()
    {
        // Regression: batching a diagnostic id across a project computes every fix against one
        // snapshot and merges the resulting text changes. Where several fixes land in the SAME
        // document - and Blueprint.Http and Blueprint.OpenApi each expand one using into more
        // than one - a removal span can be dropped while its insertion still lands. The symptom
        // is a document that accumulates replacement usings while keeping the legacy one, and a
        // diagnostic that therefore never clears: the project plateaus at a constant fix count
        // per round until the round cap stops it, leaving it partially migrated.
        //
        // Observed on Signature365 before this was guarded: 80 files carrying 198 redundant
        // using lines, with the originals still present.
        var fixtureRoot = CreateWorkingFixtureRoot("SharedDocumentUsingsApp");
        try
        {
            var solutionPath = Path.Combine(fixtureRoot, "SharedDocumentUsingsApp.sln");
            var consumerPath = Path.Combine(fixtureRoot, "SharedDocumentUsingsApp", "Consumer.cs");

            var applier = new TransformationApplier();
            await applier.ApplyAsync(
                new ApplyModeOptions(solutionPath: solutionPath, diagnosticFilter: "BPMIG*"));

            var usings = (await File.ReadAllLinesAsync(consumerPath))
                .Where(static line => line.StartsWith("using ", StringComparison.Ordinal))
                .Select(static line => line.Trim())
                .ToArray();

            // No legacy namespace may survive its own rewrite.
            usings.Should().NotContain("using Blueprint.Http;");
            usings.Should().NotContain("using Blueprint.Authorisation;");
            usings.Should().NotContain("using Blueprint.OpenApi;");
            usings.Should().NotContain("using Blueprint.Properties;");

            // And nothing may be inserted twice. This is the assertion the first version of this
            // guard lacked, and the reason the defect reached a real corpus.
            usings.Should().OnlyHaveUniqueItems(
                "each replacement using must be inserted exactly once");

            // The replacements the map specifies must actually be present.
            usings.Should().Contain("using Blueprint.Model;");
            usings.Should().Contain("using Blueprint.Runtime.Http;");
            usings.Should().Contain("using Blueprint.Authorization;");
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    private static string CreateWorkingFixtureRoot(string fixtureName)
    {
        var repositoryRoot = GetRepositoryRoot();
        var sourceFixtureRoot = Path.Combine(
            repositoryRoot, "src", "Blueprint.Codemod.Tests", "TestFixtures", fixtureName);
        var workingFixtureRoot = Path.Combine(
            repositoryRoot,
            "src",
            "Blueprint.Codemod.Tests",
            "TestFixtures",
            $"{fixtureName}.Apply.{Guid.NewGuid():N}");

        CopyDirectory(sourceFixtureRoot, workingFixtureRoot);
        return workingFixtureRoot;
    }

    private static string CreateWorkingFixtureRoot()
    {
        var repositoryRoot = GetRepositoryRoot();
        var sourceFixtureRoot = Path.Combine(
            repositoryRoot,
            "src",
            "Blueprint.Codemod.Tests",
            "TestFixtures",
            "SimpleBlueprintApp");
        var workingFixtureRoot = Path.Combine(
            repositoryRoot,
            "src",
            "Blueprint.Codemod.Tests",
            "TestFixtures",
            $"SimpleBlueprintApp.Apply.{Guid.NewGuid():N}");

        CopyDirectory(sourceFixtureRoot, workingFixtureRoot);
        return workingFixtureRoot;
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Blueprint.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate the repository root for the apply-mode fixture.");
        }

        return directory.FullName;
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            var destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(file));
            File.Copy(file, destinationFile, overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDirectory))
        {
            var childDestination = Path.Combine(destinationDirectory, Path.GetFileName(directory));
            CopyDirectory(directory, childDestination);
        }
    }
}
