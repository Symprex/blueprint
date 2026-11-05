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

    [Fact]
    public async Task When_a_spec_is_at_the_configured_path_in_the_project_then_it_is_not_published_with_the_app()
    {
        // The web SDK publishes every .json in an app's folder. A referenced app's Spec/openapi.json was published beside
        // the referencing app's own, and the two at one relative path failed the publish (NETSDK1152).
        var name = Guid.NewGuid().ToString("N");
        var relativePath = Path.Combine("Spec", "test-" + name + ".json");
        var spec = Path.Combine(GatewayDirectory, relativePath);
        var otherJson = Path.Combine(GatewayDirectory, "Spec", "other-" + name + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(spec)!);
        File.WriteAllText(spec, "{}");
        File.WriteAllText(otherJson, "{}");

        try
        {
            var content = await RunDotnetAsync("msbuild", GatewayProject, "-getItem:Content", "-nologo", $"-p:Configuration={Configuration}", $"-p:BlueprintOpenApiPath={relativePath}");

            content.Should().Contain(Path.GetFileName(otherJson), "any other .json beside it is still published");
            content.Should().NotContain(Path.GetFileName(spec));
        }
        finally
        {
            File.Delete(spec);
            File.Delete(otherJson);
        }
    }

    [Fact]
    public async Task When_the_app_exits_without_starting_its_host_then_the_build_fails_and_the_earlier_spec_is_not_accepted()
    {
        // Such an app exits with 0 and writes nothing, so its exit code alone would let a stale spec pass for a current one.
        var appDirectory = Path.Combine(this._directory, "EarlyExitApp");
        var appProject = Path.Combine(appDirectory, "EarlyExitApp.csproj");
        var targets = Path.Combine(FindRepositoryRoot(), "src", "Blueprint.Runtime", "buildTransitive", "Blueprint.Runtime.targets");
        Directory.CreateDirectory(appDirectory);
        var appProjectXml = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <Import Project="{targets}" />
            </Project>
            """;
        File.WriteAllText(appProject, appProjectXml);
        File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "return;");

        const string Stale = "{\"stale\":true}";
        Directory.CreateDirectory(Path.GetDirectoryName(this.OutputPath)!);
        File.WriteAllText(this.OutputPath, Stale);

        var (exitCode, output) = await StartDotnetAsync("build", appProject, "-nologo", "-p:BlueprintEmitOpenApi=true", $"-p:BlueprintOpenApiPath={this.OutputPath}");

        exitCode.Should().NotBe(0, "the build should fail:{0}{1}", Environment.NewLine, output);
        output.Should().Contain("exited without writing its OpenAPI document");
        File.ReadAllText(this.OutputPath).Should().Be(Stale);
    }

    [Fact]
    public async Task When_the_app_targets_several_frameworks_then_only_its_framework_builds_run_the_app()
    {
        // The outer build of a project with TargetFrameworks has no TargetPath, and used to run `dotnet exec ""`, which
        // failed the build after every framework's own build had emitted the document. This app does what the document
        // mode does when it succeeds: it records completion.
        var appDirectory = Path.Combine(this._directory, "MultiTargetedApp");
        var appProject = Path.Combine(appDirectory, "MultiTargetedApp.csproj");
        var targets = Path.Combine(FindRepositoryRoot(), "src", "Blueprint.Runtime", "buildTransitive", "Blueprint.Runtime.targets");
        Directory.CreateDirectory(appDirectory);
        var appProjectXml = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFrameworks>net10.0</TargetFrameworks>
              </PropertyGroup>
              <Import Project="{targets}" />
            </Project>
            """;
        File.WriteAllText(appProject, appProjectXml);
        File.WriteAllText(
            Path.Combine(appDirectory, "Program.cs"),
            "System.IO.File.WriteAllText(System.Environment.GetEnvironmentVariable(\"BLUEPRINT_OPENAPI_COMPLETION_PATH\")!, \"\");");

        var output = await RunDotnetAsync("build", appProject, "-nologo", "-p:BlueprintEmitOpenApi=true", $"-p:BlueprintOpenApiPath={this.OutputPath}");

        output.Should().Contain($"MultiTargetedApp -> {this.OutputPath}", "the framework's own build still runs the app");
    }

    public void Dispose()
    {
        if (Directory.Exists(this._directory))
        {
            Directory.Delete(this._directory, recursive: true);
        }
    }

    private static string GatewayDirectory => Path.Combine(FindRepositoryRoot(), "src", "Blueprint.SourceGenerator.Sample.Gateway");

    private static string GatewayProject => Path.Combine(GatewayDirectory, "Blueprint.SourceGenerator.Sample.Gateway.csproj");

    private static Task BuildGatewayAsync(params string[] properties)
    {
        return RunDotnetAsync(new[] { "build", GatewayProject, "-c", Configuration, "--no-restore", "--no-dependencies", "-nologo", "-v:q" }.Concat(properties).ToArray());
    }

    /// <summary>
    /// Runs the .NET CLI, asserts that it succeeds and returns what it wrote to standard output.
    /// </summary>
    private static async Task<string> RunDotnetAsync(params string[] arguments)
    {
        var (exitCode, output) = await StartDotnetAsync(arguments);

        exitCode.Should().Be(0, "the command should succeed:{0}{1}", Environment.NewLine, output);

        return output;
    }

    /// <summary>
    /// Runs the .NET CLI and returns its exit code and what it wrote, standard output first.
    /// </summary>
    private static async Task<(int ExitCode, string Output)> StartDotnetAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
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

        return (process.ExitCode, await output + Environment.NewLine + await error);
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
