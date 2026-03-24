using System.Text.Json.Nodes;
using FluentAssertions;

namespace Blueprint.Codemod.Tests;

public sealed class CodemodArtifactWriterTests
{
    [Xunit.Fact]
    public async Task When_running_dry_run_with_an_output_directory_then_writes_json_and_markdown_artifacts()
    {
        var outputDirectory = CreateOutputDirectory();
        try
        {
            var exitCode = await Program.Main(
                [
                    "--solution",
                    GetFixtureSolutionPath(),
                    "--diagnostic-filter",
                    "BPMIG*",
                    "--output",
                    outputDirectory,
                ]);

            exitCode.Should().Be(0);

            var jsonPath = Path.Combine(outputDirectory, "codemod-report.json");
            var markdownPath = Path.Combine(outputDirectory, "codemod-report.md");
            File.Exists(jsonPath).Should().BeTrue();
            File.Exists(markdownPath).Should().BeTrue();

            var json = await File.ReadAllTextAsync(jsonPath);
            var document = JsonNode.Parse(json);
            document.Should().NotBeNull();
            document!["ReportType"]!.GetValue<string>().Should().Be("activation-diagnostics");

            var markdown = await File.ReadAllTextAsync(markdownPath);
            markdown.Should().Contain("# activation-diagnostics");
            markdown.IndexOf("### BPMIG001", StringComparison.Ordinal).Should().BeGreaterThanOrEqualTo(0);
            markdown.IndexOf("### BPMIG100", StringComparison.Ordinal).Should().BeGreaterThanOrEqualTo(0);
            markdown.IndexOf("### BPMIG101", StringComparison.Ordinal).Should().BeGreaterThanOrEqualTo(0);
            markdown.IndexOf("### BPMIG200", StringComparison.Ordinal).Should().BeGreaterThanOrEqualTo(0);
            markdown.IndexOf("### BPMIG201", StringComparison.Ordinal).Should().BeGreaterThanOrEqualTo(0);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Xunit.Fact]
    public async Task When_running_apply_mode_with_an_output_directory_then_writes_the_post_apply_json_and_markdown_artifacts()
    {
        var fixtureRoot = CreateWorkingFixtureRoot();
        var outputDirectory = CreateOutputDirectory();
        try
        {
            var solutionPath = Path.Combine(fixtureRoot, "SimpleBlueprintApp.sln");

            var exitCode = await Program.Main(
                [
                    "--solution",
                    solutionPath,
                    "--diagnostic-filter",
                    "BPMIG*",
                    "--apply",
                    "--output",
                    outputDirectory,
                ]);

            exitCode.Should().Be(0);

            var jsonPath = Path.Combine(outputDirectory, "codemod-report.json");
            var markdownPath = Path.Combine(outputDirectory, "codemod-report.md");
            File.Exists(jsonPath).Should().BeTrue();
            File.Exists(markdownPath).Should().BeTrue();

            var document = JsonNode.Parse(await File.ReadAllTextAsync(jsonPath));
            document.Should().NotBeNull();
            document!["Findings"]!.AsArray()
                .Select(static node => node!["DiagnosticId"]!.GetValue<string>())
                .Should()
                .Equal("BPMIG100", "BPMIG101", "BPMIG200", "BPMIG201");

            var markdown = await File.ReadAllTextAsync(markdownPath);
            markdown.Should().NotContain("### BPMIG001");
            markdown.Should().Contain("### BPMIG100");
            markdown.Should().Contain("### BPMIG101");
            markdown.Should().Contain("### BPMIG200");
            markdown.Should().Contain("### BPMIG201");
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }

            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    private static string CreateOutputDirectory()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"blueprint-codemod-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);
        return outputDirectory;
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
            $"SimpleBlueprintApp.Output.{Guid.NewGuid():N}");

        CopyDirectory(sourceFixtureRoot, workingFixtureRoot);
        return workingFixtureRoot;
    }

    private static string GetFixtureSolutionPath()
    {
        return Path.Combine(
            GetRepositoryRoot(),
            "src",
            "Blueprint.Codemod.Tests",
            "TestFixtures",
            "SimpleBlueprintApp",
            "SimpleBlueprintApp.sln");
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
            throw new InvalidOperationException("Could not locate the repository root for the codemod artifact tests.");
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
