using System.Diagnostics;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves the <c>BlueprintEmitOpenApi</c> build target end to end: building the sample gateway with it on writes the
/// gateway's document, and building again leaves the file untouched.
/// </summary>
/// <remarks>
/// Each test builds the gateway project alone (<c>--no-dependencies</c>), which this project's reference to it has
/// already built, so the build compiles nothing and only runs the target.
/// </remarks>
public sealed class OpenApiBuildEmissionTests : IDisposable
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    private static readonly DateTime LongAgo = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "blueprint-openapi-build-" + Guid.NewGuid().ToString("N"));

    private string OutputPath => Path.Combine(this._directory, "Spec", "openapi.json");

    [Fact]
    public async Task When_built_with_emission_on_then_the_gateway_document_is_written_and_a_second_build_leaves_it_untouched()
    {
        await BuildGatewayAsync("-p:BlueprintEmitOpenApi=true", $"-p:BlueprintOpenApiPath={this.OutputPath}");

        var document = File.ReadAllText(this.OutputPath);
        document.Should().Contain("\"Template Gateway API\"");
        document.Should().Contain("\"/templates/{Id}\"");

        File.SetLastWriteTimeUtc(this.OutputPath, LongAgo);

        await BuildGatewayAsync("-p:BlueprintEmitOpenApi=true", $"-p:BlueprintOpenApiPath={this.OutputPath}");

        File.GetLastWriteTimeUtc(this.OutputPath).Should().Be(LongAgo);
    }

    [Fact]
    public async Task When_built_without_emission_then_no_document_is_written()
    {
        await BuildGatewayAsync($"-p:BlueprintOpenApiPath={this.OutputPath}");

        File.Exists(this.OutputPath).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(this._directory))
        {
            Directory.Delete(this._directory, recursive: true);
        }
    }

    private static async Task BuildGatewayAsync(params string[] properties)
    {
        var project = Path.Combine(FindRepositoryRoot(), "src", "Blueprint.SourceGenerator.Sample.Gateway", "Blueprint.SourceGenerator.Sample.Gateway.csproj");

        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in new[] { "build", project, "-c", Configuration, "--no-restore", "--no-dependencies", "-nologo", "-v:q" }.Concat(properties))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // The build, and the app its target started, would otherwise outlive the test.
            process.Kill(entireProcessTree: true);
            throw;
        }

        process.ExitCode.Should().Be(0, "the build should succeed:{0}{1}{0}{2}", Environment.NewLine, await output, await error);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Blueprint.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Blueprint.sln was not found above " + AppContext.BaseDirectory);
    }
}
