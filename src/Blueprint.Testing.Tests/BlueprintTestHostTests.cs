using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Api;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.Testing.Tests;

/// <summary>
/// Proves that <see cref="BlueprintTestHost{TApp,TExec}"/> correctly wires up the source-generated
/// pipeline and can execute operations end-to-end without an HTTP server.
/// </summary>
public class BlueprintTestHostTests : IDisposable
{
    private readonly BlueprintTestHost<SampleBlueprintApp, SampleBlueprintAppExecutor> _host;

    public BlueprintTestHostTests()
    {
        this._host = BlueprintTestHostBuilder<SampleBlueprintApp, SampleBlueprintAppExecutor>
            .Create()
            .ConfigureServices(services =>
            {
                // Register the sample app's handler dependencies so pipelines can resolve
                services.AddSingleton<TemplateStore>();
                services.AddScoped<InMemoryUnitOfWork>();
            })
            .Build();
    }

    [Fact]
    public void Host_resolves_app_and_executor()
    {
        this._host.App.Should().NotBeNull();
        this._host.Executor.Should().NotBeNull();
    }

    [Fact]
    public void Host_app_contains_registered_operations()
    {
        // HealthCheckQuery should be registered from the scanned contract assembly
        var descriptor = this._host.App.FindOperation(typeof(HealthCheckQuery));
        descriptor.Should().NotBeNull("HealthCheckQuery should be discovered by [BlueprintAppScan]");
    }

    [Fact]
    public async Task ExecuteAsync_returns_raw_result()
    {
        var result = await this._host.ExecuteAsync(
            new HealthCheckQuery(),
            ctx => ctx.SkipAuthorisation());

        result.Should().BeOfType<PlainTextResult>();
    }

    [Fact]
    public async Task ExecuteAsync_generic_unwraps_result()
    {
        var result = await this._host.ExecuteAsync<PlainTextResult>(
            new HealthCheckQuery(),
            ctx => ctx.SkipAuthorisation());

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_validation_failure_for_invalid_input()
    {
        // SearchTemplatesQuery requires Q (search term) — omitting it should fail validation.
        var result = await this._host.ExecuteAsync(
            new SearchTemplatesQuery(),
            ctx => ctx.SkipAuthorisation());

        result.Should().BeOfType<ValidationFailedResult>();
    }

    [Fact]
    public async Task ExecuteExpectingValidationFailureAsync_returns_validation_result()
    {
        var validationResult = await this._host.ExecuteExpectingValidationFailureAsync(
            new SearchTemplatesQuery(),
            ctx => ctx.SkipAuthorisation());

        validationResult.ValidationResults.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_generic_throws_on_unexpected_validation_failure()
    {
        // Trying to unwrap a validation failure as PlainTextResult should throw
        var act = () => this._host.ExecuteAsync<PlainTextResult>(
            new SearchTemplatesQuery(),
            ctx => ctx.SkipAuthorisation());

        await act.Should().ThrowAsync<BlueprintTestExecutionException>()
            .Where(static ex => ex.ActualResult is ValidationFailedResult);
    }

    [Fact]
    public async Task WithUser_sets_claims_principal()
    {
        // Execute with a user configured via claims builder
        var result = await this._host.ExecuteAsync(
            new HealthCheckQuery(),
            ctx => ctx.WithUser(claims =>
            {
                claims.Add(new System.Security.Claims.Claim("sub", "test-user-1"));
                claims.Add(new System.Security.Claims.Claim("email", "test@example.com"));
            }));

        // HealthCheckQuery is [AllowAnonymous] so it should succeed regardless
        result.Should().BeOfType<PlainTextResult>();
    }

    [Fact]
    public async Task WithTenant_creates_authenticated_user_with_tenant_claim()
    {
        var result = await this._host.ExecuteAsync(
            new HealthCheckQuery(),
            ctx => ctx.WithTenant("tenant-123", "templates:read"));

        result.Should().BeOfType<PlainTextResult>();
    }

    public void Dispose()
    {
        this._host.Dispose();
    }
}
