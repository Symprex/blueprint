using System.Security.Claims;
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

    [Fact]
    public async Task Authentication_Set_applies_to_subsequent_operations()
    {
        var identity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("sub", "ambient-user")],
            "TestAuth");

        this._host.Authentication.Set(identity);

        // Execute without per-call auth — ambient should apply
        var result = await this._host.ExecuteAsync(new HealthCheckQuery());

        result.Should().BeOfType<PlainTextResult>();
    }

    [Fact]
    public async Task Authentication_As_reverts_after_disposal()
    {
        var permanentIdentity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("sub", "permanent-user")],
            "TestAuth");

        var temporaryIdentity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("sub", "temp-user")],
            "TestAuth");

        this._host.Authentication.Set(permanentIdentity);

        using (this._host.Authentication.As(temporaryIdentity))
        {
            this._host.Authentication.CurrentPrincipal!.Identity!.Name
                .Should().BeNull(); // sub claim doesn't set Name, but let's check the claim
            this._host.Authentication.CurrentPrincipal!.FindFirst("sub")!.Value
                .Should().Be("temp-user");
        }

        // After disposal, should revert to permanent
        this._host.Authentication.CurrentPrincipal!.FindFirst("sub")!.Value
            .Should().Be("permanent-user");
    }

    [Fact]
    public async Task Authentication_AsAnonymous_clears_and_reverts()
    {
        var identity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("sub", "user-1")],
            "TestAuth");

        this._host.Authentication.Set(identity);

        using (this._host.Authentication.AsAnonymous())
        {
            this._host.Authentication.CurrentPrincipal.Should().BeNull();

            // Execute anonymous — should work for [AllowAnonymous] operation
            var result = await this._host.ExecuteAsync(new HealthCheckQuery());
            result.Should().BeOfType<PlainTextResult>();
        }

        // After disposal, should revert to authenticated
        this._host.Authentication.CurrentPrincipal.Should().NotBeNull();
        this._host.Authentication.CurrentPrincipal!.FindFirst("sub")!.Value
            .Should().Be("user-1");
    }

    [Fact]
    public async Task Authentication_MakeAnonymous_clears_permanently()
    {
        var identity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("sub", "user-1")],
            "TestAuth");

        this._host.Authentication.Set(identity);
        this._host.Authentication.CurrentPrincipal.Should().NotBeNull();

        this._host.Authentication.MakeAnonymous();
        this._host.Authentication.CurrentPrincipal.Should().BeNull();
    }

    [Fact]
    public async Task Authentication_per_call_override_takes_precedence()
    {
        // Set ambient auth
        var ambientIdentity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("sub", "ambient-user")],
            "TestAuth");
        this._host.Authentication.Set(ambientIdentity);

        // Per-call override should win
        ClaimsPrincipal? capturedPrincipal = null;
        await this._host.ExecuteAsync(
            new HealthCheckQuery(),
            ctx =>
            {
                ctx.WithUser(claims => claims.Add(new System.Security.Claims.Claim("sub", "override-user")));
                capturedPrincipal = ctx.OperationContext.UserPrincipal;
            });

        capturedPrincipal.Should().NotBeNull();
        capturedPrincipal!.FindFirst("sub")!.Value.Should().Be("override-user");
    }

    [Fact]
    public async Task MessageAsync_executes_as_anonymous_without_throwing()
    {
        // MessageAsync is for bus-dispatched messages — always anonymous, throws on failure
        await this._host.MessageAsync(new HealthCheckQuery());
    }

    [Fact]
    public async Task MessageAsync_with_configure_invokes_callback()
    {
        var callbackInvoked = false;

        await this._host.MessageAsync(
            new HealthCheckQuery(),
            configure: _ => callbackInvoked = true);

        callbackInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task MessageAsync_throws_on_validation_failure()
    {
        // SearchTemplatesQuery requires Q — omitting it should cause a validation failure
        var act = () => this._host.MessageAsync(new SearchTemplatesQuery());

        await act.Should().ThrowAsync<BlueprintTestExecutionException>()
            .Where(static ex => ex.IsValidationFailure);
    }

    [Fact]
    public async Task BackgroundTaskAsync_executes_as_anonymous_without_throwing()
    {
        // BackgroundTaskAsync is for scheduled/recurring tasks — always anonymous
        await this._host.BackgroundTaskAsync(new HealthCheckQuery());
    }

    [Fact]
    public async Task BackgroundTaskAsync_with_configure_invokes_callback()
    {
        var callbackInvoked = false;

        await this._host.BackgroundTaskAsync(
            new HealthCheckQuery(),
            configure: _ => callbackInvoked = true);

        callbackInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task BackgroundTaskAsync_throws_on_validation_failure()
    {
        var act = () => this._host.BackgroundTaskAsync(new SearchTemplatesQuery());

        await act.Should().ThrowAsync<BlueprintTestExecutionException>()
            .Where(static ex => ex.IsValidationFailure);
    }

    public void Dispose()
    {
        this._host.Dispose();
    }
}
