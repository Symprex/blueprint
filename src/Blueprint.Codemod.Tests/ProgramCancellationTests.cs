using Blueprint.Migration.Analyzers;
using FluentAssertions;

namespace Blueprint.Codemod.Tests;

public sealed class ProgramCancellationTests
{
    [Xunit.Fact]
    public async Task When_running_dry_run_with_output_then_the_provided_cancellation_token_reaches_analysis_and_artifact_writing()
    {
        var report = MigrationReport.CreateMigrationDiagnostics();
        var cancellationToken = new CancellationTokenSource().Token;
        CancellationToken? applyToken = null;
        CancellationToken? dryRunToken = null;
        CancellationToken? artifactToken = null;
        string? outputDirectory = null;
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();

        var exitCode = await Program.RunAsync(
            [
                "--solution",
                "fixture.sln",
                "--diagnostic-filter",
                "BPMIG*",
                "--output",
                "artifacts",
            ],
            (options, token) =>
            {
                applyToken = token;
                return Task.CompletedTask;
            },
            (options, token) =>
            {
                dryRunToken = token;
                return Task.FromResult(report);
            },
            (emittedReport, directory, token) =>
            {
                emittedReport.Should().BeSameAs(report);
                outputDirectory = directory;
                artifactToken = token;
                return Task.CompletedTask;
            },
            standardOutput,
            standardError,
            cancellationToken);

        exitCode.Should().Be(0);
        applyToken.Should().BeNull();
        dryRunToken.Should().Be(cancellationToken);
        artifactToken.Should().Be(cancellationToken);
        outputDirectory.Should().Be("artifacts");
    }

    [Xunit.Fact]
    public async Task When_running_apply_mode_then_the_provided_cancellation_token_reaches_apply_and_analysis()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        CancellationToken? applyToken = null;
        CancellationToken? dryRunToken = null;
        var artifactWriterCalled = false;
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();

        var exitCode = await Program.RunAsync(
            [
                "--solution",
                "fixture.sln",
                "--diagnostic-filter",
                "BPMIG*",
                "--apply",
            ],
            (options, token) =>
            {
                applyToken = token;
                return Task.CompletedTask;
            },
            (options, token) =>
            {
                dryRunToken = token;
                return Task.FromResult(MigrationReport.CreateMigrationDiagnostics());
            },
            (report, directory, token) =>
            {
                artifactWriterCalled = true;
                return Task.CompletedTask;
            },
            standardOutput,
            standardError,
            cancellationToken);

        exitCode.Should().Be(0);
        applyToken.Should().Be(cancellationToken);
        dryRunToken.Should().Be(cancellationToken);
        artifactWriterCalled.Should().BeFalse();
    }
}
