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
    public async Task When_the_app_targets_several_frameworks_then_only_its_first_framework_runs_the_app()
    {
        // The outer build of a project with TargetFrameworks has no TargetPath, and used to run `dotnet exec ""`, which
        // failed the build after the framework builds had emitted the document. Framework builds may run in parallel, so
        // each running the app would race to write one file, and the last to finish would decide what it held.
        var appProject = this.CreateMultiTargetedApp();

        var output = await RunDotnetAsync("build", appProject, "-nologo", "-p:BlueprintEmitOpenApi=true", $"-p:BlueprintOpenApiPath={this.OutputPath}");

        File.ReadAllText(this.OutputPath).Should().Be(".NETCoreApp,Version=v10.0");
        CountOf($"MultiTargetedApp -> {this.OutputPath}", output).Should().Be(1, "one framework's build runs the app:{0}{1}", Environment.NewLine, output);
    }

    [Fact]
    public async Task When_the_app_names_the_framework_to_emit_from_then_that_framework_runs_the_app()
    {
        var appProject = this.CreateMultiTargetedApp();

        var output = await RunDotnetAsync("build", appProject, "-nologo", "-p:BlueprintEmitOpenApi=true", $"-p:BlueprintOpenApiPath={this.OutputPath}", "-p:BlueprintOpenApiTargetFramework=net8.0");

        File.ReadAllText(this.OutputPath).Should().Be(".NETCoreApp,Version=v8.0");
        CountOf($"MultiTargetedApp -> {this.OutputPath}", output).Should().Be(1, "one framework's build runs the app:{0}{1}", Environment.NewLine, output);
    }

    [Fact]
    public async Task When_the_app_lists_its_frameworks_over_several_lines_then_the_named_framework_runs_the_app()
    {
        // The check that the named framework is one the app targets used to remove only spaces, so a framework after a
        // line break never matched.
        var appProject = this.CreateMultiTargetedApp("<TargetFrameworks>\n      net10.0;\n      net8.0\n    </TargetFrameworks>");

        var output = await RunDotnetAsync("build", appProject, "-nologo", "-p:BlueprintEmitOpenApi=true", $"-p:BlueprintOpenApiPath={this.OutputPath}", "-p:BlueprintOpenApiTargetFramework=net8.0");

        File.ReadAllText(this.OutputPath).Should().Be(".NETCoreApp,Version=v8.0");
        CountOf($"MultiTargetedApp -> {this.OutputPath}", output).Should().Be(1, "one framework's build runs the app:{0}{1}", Environment.NewLine, output);
    }

    [Fact]
    public async Task When_only_another_framework_is_built_then_the_named_framework_is_built_and_runs_the_app()
    {
        // A build of one framework (dotnet build -f) that is not the one named used to write nothing and succeed, leaving
        // an earlier spec in place for a CI check to accept.
        var appProject = this.CreateMultiTargetedApp();

        var output = await RunDotnetAsync("build", appProject, "-nologo", "-f", "net8.0", "-p:BlueprintEmitOpenApi=true", $"-p:BlueprintOpenApiPath={this.OutputPath}");

        File.ReadAllText(this.OutputPath).Should().Be(".NETCoreApp,Version=v10.0");
        CountOf($"MultiTargetedApp -> {this.OutputPath}", output).Should().Be(1, "one framework's build runs the app:{0}{1}", Environment.NewLine, output);
    }

    [Fact]
    public async Task When_the_named_framework_is_not_one_the_app_targets_then_the_build_fails()
    {
        var appProject = this.CreateMultiTargetedApp();

        var (exitCode, output) = await StartDotnetAsync("build", appProject, "-nologo", "-p:BlueprintEmitOpenApi=true", $"-p:BlueprintOpenApiPath={this.OutputPath}", "-p:BlueprintOpenApiTargetFramework=net9.0");

        exitCode.Should().NotBe(0, "the build should fail:{0}{1}", Environment.NewLine, output);
        output.Should().Contain("BlueprintOpenApiTargetFramework names net9.0");
        File.Exists(this.OutputPath).Should().BeFalse();
    }

    [Fact]
    public async Task When_a_single_framework_app_names_another_framework_then_the_build_fails()
    {
        var appProject = this.CreateMultiTargetedApp("<TargetFramework>net10.0</TargetFramework>");

        var (exitCode, output) = await StartDotnetAsync("build", appProject, "-nologo", "-p:BlueprintEmitOpenApi=true", $"-p:BlueprintOpenApiPath={this.OutputPath}", "-p:BlueprintOpenApiTargetFramework=net8.0");

        exitCode.Should().NotBe(0, "the build should fail:{0}{1}", Environment.NewLine, output);
        output.Should().Contain("BlueprintOpenApiTargetFramework names net8.0");
        File.Exists(this.OutputPath).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(this._directory))
        {
            Directory.Delete(this._directory, recursive: true);
        }
    }

    private static int CountOf(string value, string text)
    {
        var count = 0;

        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
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

        // A build leaves MSBuild nodes and the compiler server running after it exits, and they inherit its redirected
        // output. Reading that output to its end then waited for them to go idle and exit, fifteen minutes later, and the
        // test passed after that wait. Keep every process the build starts inside the build.
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        startInfo.Environment["UseSharedCompilation"] = "false";

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
            // The output's end, not only the exit, so anything left holding it fails the test instead of stalling it.
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(output, error).WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // The build, and the app its target started, would otherwise outlive the test.
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, await output + Environment.NewLine + await error);
    }

    /// <summary>
    /// Creates an app that targets two frameworks, unless told otherwise, and does what the document mode does when it
    /// succeeds: it writes the document, here the name of the framework it was built for, and records completion.
    /// </summary>
    /// <remarks>
    /// It rolls forward to a later major runtime, so that its .NET 8 build runs where only .NET 10 is installed.
    /// </remarks>
    private string CreateMultiTargetedApp(string frameworks = "<TargetFrameworks>net10.0;net8.0</TargetFrameworks>")
    {
        var appDirectory = Path.Combine(this._directory, "MultiTargetedApp");
        var appProject = Path.Combine(appDirectory, "MultiTargetedApp.csproj");
        var targets = Path.Combine(FindRepositoryRoot(), "src", "Blueprint.Runtime", "buildTransitive", "Blueprint.Runtime.targets");
        Directory.CreateDirectory(appDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(this.OutputPath)!);
        var appProjectXml = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                {frameworks}
                <RollForward>Major</RollForward>
              </PropertyGroup>
              <Import Project="{targets}" />
            </Project>
            """;
        File.WriteAllText(appProject, appProjectXml);
        const string Program = """
            System.IO.File.WriteAllText(System.Environment.GetEnvironmentVariable("BLUEPRINT_OPENAPI_OUTPUT_PATH")!, System.AppContext.TargetFrameworkName);
            System.IO.File.WriteAllText(System.Environment.GetEnvironmentVariable("BLUEPRINT_OPENAPI_COMPLETION_PATH")!, "");
            """;
        File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), Program);

        return appProject;
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
