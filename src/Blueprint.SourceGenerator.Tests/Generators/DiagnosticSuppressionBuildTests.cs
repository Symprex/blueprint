using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators;

/// <summary>
/// Proves <c>[SuppressMessage]</c> silences one occurrence of a generator warning in a real build. The in-memory
/// harness behind <see cref="DiagnosticSuppressionTests"/> cannot prove it: the compiler's analyser driver applies
/// <c>[SuppressMessage]</c>, and the harness runs generators without one.
/// </summary>
[Collection(RealBuildCollection.Name)]
public class DiagnosticSuppressionBuildTests
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    private static readonly Regex Bpau010Warning = new(@"(?<file>[^\\/]+\.cs)\(\d+,\d+\): warning BPAU010", RegexOptions.Compiled);

    [Fact]
    public async Task When_SuppressMessage_marks_one_operation_then_a_real_build_reports_BPAU010_only_for_the_unmarked_one()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectDirectory = Path.Combine(AppContext.BaseDirectory, "DiagnosticSuppressionBuild", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDirectory);

        try
        {
            var projectPath = WriteProject(projectDirectory, repositoryRoot);

            var (exitCode, output) = await RunDotnetBuildAsync(projectPath);

            exitCode.Should().Be(0, output);

            Bpau010Warning.Matches(output)
                .Select(static match => match.Groups["file"].Value)
                .Distinct(StringComparer.Ordinal)
                .Should().Equal(["ReportedQuery.cs"], output);
        }
        finally
        {
            TryDeleteDirectory(projectDirectory);
        }
    }

    private static string WriteProject(string projectDirectory, string repositoryRoot)
    {
        var sourceDirectory = Path.Combine(repositoryRoot, "src");

        // An empty props/targets pair stops the build inheriting the repository's settings from above the test output
        // directory, which would turn every warning into an error.
        File.WriteAllText(Path.Combine(projectDirectory, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(projectDirectory, "Directory.Build.targets"), "<Project />");

        var projectPath = Path.Combine(projectDirectory, "SuppressionApp.csproj");
        var project =
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 <TargetFramework>net10.0</TargetFramework>
                 <ImplicitUsings>enable</ImplicitUsings>
                 <Nullable>disable</Nullable>
                 <LangVersion>preview</LangVersion>
               </PropertyGroup>
               <ItemGroup>
                 <ProjectReference Include="{Path.Combine(sourceDirectory, "Blueprint.Runtime.Model", "Blueprint.Runtime.Model.csproj")}" />
                 <ProjectReference Include="{Path.Combine(sourceDirectory, "Blueprint.Runtime", "Blueprint.Runtime.csproj")}" />
                 <ProjectReference Include="{Path.Combine(sourceDirectory, "Blueprint.SourceGenerator", "Blueprint.SourceGenerator.csproj")}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
               </ItemGroup>
             </Project>
             """;

        File.WriteAllText(projectPath, project);

        const string appSource =
            """
            using System.Text.Json.Serialization;
            using Blueprint.Model;
            using Microsoft.AspNetCore.Mvc;

            namespace SuppressionApp;

            [BlueprintAppScan]
            [BlueprintAppSerializerContext(JsonSerializerContextType = typeof(SuppressionJsonContext))]
            public partial class SuppressionBlueprintApp : BlueprintApp
            {
            }

            [JsonSerializable(typeof(string))]
            [JsonSerializable(typeof(ProblemDetails))]
            [JsonSerializable(typeof(ValidationProblemDetails))]
            public partial class SuppressionJsonContext : JsonSerializerContext
            {
            }
            """;

        File.WriteAllText(Path.Combine(projectDirectory, "SuppressionApp.cs"), appSource);

        // Each operation's [Resource] fallback names a property it lacks, so each would report BPAU010. Only the one
        // without [SuppressMessage] should.
        File.WriteAllText(Path.Combine(projectDirectory, "SuppressedQuery.cs"), OperationSource("SuppressedQuery", "[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Blueprint\", \"BPAU010\", Justification = \"Test\")]"));
        File.WriteAllText(Path.Combine(projectDirectory, "ReportedQuery.cs"), OperationSource("ReportedQuery", string.Empty));

        return projectPath;
    }

    private static string OperationSource(string name, string suppression)
    {
        return $$"""
                 using Blueprint.Model;
                 using Blueprint.Model.Attributes;

                 namespace SuppressionApp;

                 {{suppression}}
                 [RootLink("/{{name}}/{TenantId}")]
                 [Resource("tenant", "read", "/Tenants/{TenantId:MissingOn{{name}}}")]
                 public partial class {{name}} : IQuery<string>
                 {
                     public string TenantId { get; set; }

                     public static Task<string> HandleAsync({{name}} operation)
                     {
                         return Task.FromResult("ok");
                     }
                 }
                 """;
    }

    private static async Task<(int ExitCode, string Output)> RunDotnetBuildAsync(string projectPath)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in new[] { "build", projectPath, "-c", Configuration, "--nologo", "-v:minimal", "-clp:NoSummary" })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        var output = new StringBuilder();

        process.OutputDataReceived += (_, e) => AppendLine(output, e.Data);
        process.ErrorDataReceived += (_, e) => AppendLine(output, e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancelling the wait leaves dotnet and its MSBuild nodes running, and they would still be writing the
            // referenced projects' outputs while the next test in the collection builds them
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }

            throw;
        }

        lock (output)
        {
            return (process.ExitCode, output.ToString());
        }
    }

    private static void AppendLine(StringBuilder output, string? line)
    {
        if (line == null)
        {
            return;
        }

        lock (output)
        {
            output.AppendLine(line);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Blueprint.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find Blueprint.sln above the test output directory.");
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A build server can hold files briefly; the directory sits under the test output and is cleaned with it
        }
        catch (UnauthorizedAccessException)
        {
            // As above
        }
    }
}
