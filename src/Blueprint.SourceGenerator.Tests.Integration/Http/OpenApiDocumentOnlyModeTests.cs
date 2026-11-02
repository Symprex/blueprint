using System.Net;
using System.Net.Sockets;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves the document-only mode the <c>BlueprintEmitOpenApi</c> build target starts an app in: the app starts as it
/// always does, writes its OpenAPI document only when it has changed, binds no port and stops itself.
/// </summary>
public sealed class OpenApiDocumentOnlyModeTests : IDisposable
{
    private static readonly DateTime LongAgo = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "blueprint-openapi-" + Guid.NewGuid().ToString("N"));

    private string OutputPath => Path.Combine(this._directory, "Spec", "openapi.json");

    [Fact]
    public async Task When_run_in_document_only_mode_then_the_document_is_written_and_the_app_stops_itself()
    {
        var document = await this.RunAsync();

        File.ReadAllText(this.OutputPath).Should().Be(document);
        document.Should().Contain("\"openapi\"");
    }

    [Fact]
    public async Task When_run_in_document_only_mode_then_no_port_is_bound()
    {
        // Kestrel would fail to start on a port another listener holds; the document-only mode never listens at all.
        using var occupant = new TcpListener(IPAddress.Loopback, 0);
        occupant.Start();
        var port = ((IPEndPoint)occupant.LocalEndpoint).Port;

        await this.RunAsync(url: $"http://127.0.0.1:{port}");

        File.Exists(this.OutputPath).Should().BeTrue();
    }

    [Fact]
    public async Task When_the_document_has_not_changed_then_the_file_is_left_untouched()
    {
        await this.RunAsync();
        File.SetLastWriteTimeUtc(this.OutputPath, LongAgo);

        await this.RunAsync();

        File.GetLastWriteTimeUtc(this.OutputPath).Should().Be(LongAgo);
    }

    [Fact]
    public async Task When_the_file_differs_from_the_document_only_in_its_line_endings_then_it_is_left_untouched()
    {
        // A spec committed with LF and checked out with CRLF (or the reverse) is the same document.
        var document = await this.RunAsync();
        var otherLineEndings = document.Contains("\r\n", StringComparison.Ordinal)
            ? document.Replace("\r\n", "\n", StringComparison.Ordinal)
            : document.Replace("\n", "\r\n", StringComparison.Ordinal);
        File.WriteAllText(this.OutputPath, otherLineEndings);
        File.SetLastWriteTimeUtc(this.OutputPath, LongAgo);

        await this.RunAsync();

        File.GetLastWriteTimeUtc(this.OutputPath).Should().Be(LongAgo);
        File.ReadAllText(this.OutputPath).Should().Be(otherLineEndings);
    }

    [Fact]
    public async Task When_the_file_is_stale_then_it_is_replaced_with_the_document()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(this.OutputPath)!);
        File.WriteAllText(this.OutputPath, "{}");

        var document = await this.RunAsync();

        File.ReadAllText(this.OutputPath).Should().Be(document);
    }

    [Fact]
    public async Task When_no_api_is_mapped_then_the_app_fails_to_start_and_writes_nothing()
    {
        var run = () => this.RunAsync(mapApi: false);

        (await run.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("MapBlueprintApi").And.Contain(this.OutputPath);
        File.Exists(this.OutputPath).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(this._directory))
        {
            Directory.Delete(this._directory, recursive: true);
        }
    }

    /// <summary>
    /// Builds and runs an app in document-only mode, as the build target runs it, and returns the document the app mapped.
    /// </summary>
    private async Task<string> RunAsync(bool mapApi = true, string? url = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(
            static blueprint => blueprint.AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>());
        BlueprintOpenApiDocumentMode.Enable(builder.Services, this.OutputPath);

        var app = builder.Build();

        if (url is not null)
        {
            app.Urls.Add(url);
        }

        if (mapApi)
        {
            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");
        }

        var document = mapApi ? app.Services.GetRequiredService<PreRenderedOpenApiDocumentHolder>().Document!.Json : string.Empty;

        var run = app.RunAsync();
        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(30)));

        if (finished != run)
        {
            await app.StopAsync();
            throw new TimeoutException("The app did not stop itself after writing the OpenAPI document.");
        }

        await run;

        return document;
    }
}
