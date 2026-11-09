using System.Runtime.ExceptionServices;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves the document-only mode the <c>BlueprintEmitOpenApi</c> build target runs an app in: as soon as the app's host is
/// built, the document the app would serve is written, only when it has changed, and the app goes no further, so none of
/// its hosted services, middleware or start-up code after the host is built ever runs.
/// </summary>
/// <remarks>
/// The build target's app exits the process once the document is written. Here the mode is handed a finish that throws
/// <see cref="HostAbortedException" /> out of <c>Build()</c> instead, which stops the app at the same point.
/// </remarks>
public sealed class OpenApiDocumentOnlyModeTests : IDisposable
{
    private const string Title = "Document mode API";

    private static readonly DateTime LongAgo = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "blueprint-openapi-" + Guid.NewGuid().ToString("N"));

    private string OutputPath => Path.Combine(this._directory, "Spec", "openapi.json");

    private string CompletionPath => Path.Combine(this._directory, "obj", "openapi.done");

    [Fact]
    public void When_the_host_is_built_then_the_document_the_app_serves_is_written_before_anything_starts()
    {
        var hostedService = new StartRecordingService();

        this.Build(configureAfter: services => services.AddSingleton<IHostedService>(hostedService));

        var document = File.ReadAllText(this.OutputPath);
        document.Should().Contain($"\"{Title}\"");
        document.Should().Be(ServedDocument());
        hostedService.Started.Should().BeFalse();
    }

    [Fact]
    public void When_a_startup_class_host_is_built_then_the_document_is_written_and_configure_never_runs()
    {
        // A Startup class's ConfigureServices, where AddBlueprintApi is called, runs inside Build(); its Configure, where
        // the middleware is set up, runs only when the host starts.
        var builder = new HostBuilder().ConfigureWebHost(web => web
            .UseKestrel()
            .UseStartup(_ => new DocumentModeStartup(this.OutputPath, this.CompletionPath)));

        var build = () => builder.Build();

        build.Should().Throw<HostAbortedException>();
        File.ReadAllText(this.OutputPath).Should().Be(ServedDocument());
    }

    [Fact]
    public void When_the_document_has_not_changed_then_the_file_is_left_untouched()
    {
        this.Build();
        File.SetLastWriteTimeUtc(this.OutputPath, LongAgo);

        this.Build();

        File.GetLastWriteTimeUtc(this.OutputPath).Should().Be(LongAgo);
    }

    [Fact]
    public void When_the_file_differs_from_the_document_only_in_its_line_endings_then_it_is_left_untouched()
    {
        // A spec committed with LF and checked out with CRLF (or the reverse) is the same document.
        this.Build();
        var document = File.ReadAllText(this.OutputPath);
        var otherLineEndings = document.Contains("\r\n", StringComparison.Ordinal)
            ? document.Replace("\r\n", "\n", StringComparison.Ordinal)
            : document.Replace("\n", "\r\n", StringComparison.Ordinal);
        File.WriteAllText(this.OutputPath, otherLineEndings);
        File.SetLastWriteTimeUtc(this.OutputPath, LongAgo);

        this.Build();

        File.GetLastWriteTimeUtc(this.OutputPath).Should().Be(LongAgo);
        File.ReadAllText(this.OutputPath).Should().Be(otherLineEndings);
    }

    [Fact]
    public void When_the_file_is_stale_then_it_is_replaced_with_the_document()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(this.OutputPath)!);
        File.WriteAllText(this.OutputPath, "{}");

        this.Build();

        File.ReadAllText(this.OutputPath).Should().Be(ServedDocument());
    }

    [Fact]
    public void When_the_document_is_written_or_found_unchanged_then_completion_is_recorded_for_the_build_target()
    {
        // The target cannot tell from the exit code alone: an app that returns before building its host exits with 0.
        this.Build();
        File.Exists(this.CompletionPath).Should().BeTrue();

        File.Delete(this.CompletionPath);
        this.Build();
        File.Exists(this.CompletionPath).Should().BeTrue("an unchanged document is a successful emission too");
    }

    [Fact]
    public void When_no_base_path_is_declared_with_AddBlueprintApi_then_the_build_fails_and_writes_nothing()
    {
        // MapBlueprintApi is never reached, so a base path passed only to it cannot be known.
        var build = this.CreateBuild(declare: static _ => { });

        build.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("UseBasePath").And.Contain(this.OutputPath);
        File.Exists(this.OutputPath).Should().BeFalse();
        File.Exists(this.CompletionPath).Should().BeFalse("the build target fails when the app does not record that it wrote the document");
    }

    [Fact]
    public void When_another_host_is_built_in_the_same_process_then_it_is_left_alone()
    {
        var build = this.CreateBuild();

        var other = () => WebApplication.CreateSlimBuilder().Build().DisposeAsync().AsTask().GetAwaiter().GetResult();

        other.Should().NotThrow();
        File.Exists(this.OutputPath).Should().BeFalse();
        build.Should().Throw<HostAbortedException>();
        File.Exists(this.OutputPath).Should().BeTrue();
    }

    [Fact]
    public async Task When_the_host_starts_without_having_been_seen_built_then_it_fails_to_start()
    {
        // A host whose builder does not announce that it was built, as the generic host's builders do, would otherwise
        // run the app as usual until the build target's timeout ended it.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(DeclareApi);
        BlueprintOpenApiDocumentMode.Enable<IntegrationTestBlueprintApp>(services, this.OutputPath, this.CompletionPath, Abort);
        await using var provider = services.BuildServiceProvider();

        var start = () => Task.WhenAll(provider.GetServices<IHostedService>().Select(static service => service.StartAsync(CancellationToken.None)));

        (await start.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("BlueprintEmitOpenApi");
    }

    public void Dispose()
    {
        if (Directory.Exists(this._directory))
        {
            Directory.Delete(this._directory, recursive: true);
        }
    }

    private static void DeclareApi(BlueprintServiceBuilder blueprint)
    {
        blueprint.AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>();
        blueprint.UseBasePath("api/");
        blueprint.ConfigureOpenApi(static info => info.Title = Title);
    }

    /// <summary>
    /// Stands in for the build target's exit, by throwing out of <c>Build()</c> as the process exit would leave it.
    /// </summary>
    private static void Abort(IHost host, Exception? failure)
    {
        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }

        throw new HostAbortedException();
    }

    /// <summary>
    /// Returns the document an app registered as these tests register it serves, from an app started as usual.
    /// </summary>
    private static string ServedDocument()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(DeclareApi);
        using var app = builder.Build();
        app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

        return app.Services.GetRequiredService<PreRenderedOpenApiDocumentHolder>().Document!.Json;
    }

    /// <summary>
    /// Builds an app in document-only mode, as the build target runs it, and expects it to stop at the build.
    /// </summary>
    private void Build(Action<IServiceCollection>? configureAfter = null)
    {
        this.CreateBuild(configureAfter: configureAfter).Should().Throw<HostAbortedException>();
    }

    /// <summary>
    /// Registers an app in document-only mode and returns its build, so that a test decides how the build ends.
    /// </summary>
    private Action CreateBuild(Action<BlueprintServiceBuilder>? declare = null, Action<IServiceCollection>? configureAfter = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(declare ?? DeclareApi);
        BlueprintOpenApiDocumentMode.Enable<IntegrationTestBlueprintApp>(builder.Services, this.OutputPath, this.CompletionPath, Abort);
        configureAfter?.Invoke(builder.Services);

        return () => builder.Build();
    }

    /// <summary>
    /// A hosted service that records whether it was started.
    /// </summary>
    private sealed class StartRecordingService : IHostedService
    {
        public bool Started { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            this.Started = true;

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A Startup class that registers the app in document-only mode and fails if its middleware is ever set up.
    /// </summary>
    private sealed class DocumentModeStartup(string outputPath, string completionPath)
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(DeclareApi);
            BlueprintOpenApiDocumentMode.Enable<IntegrationTestBlueprintApp>(services, outputPath, completionPath, Abort);
        }

        public void Configure(IApplicationBuilder app)
        {
            throw new InvalidOperationException("Configure ran, so the host was started.");
        }
    }
}
