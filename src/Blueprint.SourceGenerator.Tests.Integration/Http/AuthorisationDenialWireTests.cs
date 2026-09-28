using System.Collections.Concurrent;
using System.Text.Json;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that an operation an authoriser denies, or an anonymous caller is refused, is answered with the body legacy
/// Blueprint wrote for it.
/// </summary>
/// <remarks>
/// <para>
/// Legacy's <c>AuthorisationMiddlewareBuilder</c> (Signature365 <c>8b40ecc961</c>,
/// <c>Blueprint/Authorisation/AuthorisationMiddlewareBuilder.cs</c>) threw, for an anonymous caller,
/// <c>new UnauthorizedException("Access denied. Anonymous access is not allowed.")</c> (:16, :75-79); for a denial whose
/// failure type was <c>Authorisation</c>, <c>new ForbiddenException(result, exposeSensitiveDetail)</c>, the detail exposed
/// only for a <c>ClaimsRequiredApiAuthoriser</c> in development (:40-46); and for any other,
/// <c>new UnauthorizedException(result.Message)</c> (:48). <c>ForbiddenException</c> was <c>ApiException(failure.Message,
/// "unauthorized", detail, 403)</c> (<c>Errors/ForbiddenException.cs:14-18</c>), <c>UnauthorizedException</c>
/// <c>ApiException(message, "security_failure", null, 401)</c> (<c>Errors/UnauthorizedException.cs:15-18</c>).
/// </para>
/// <para>
/// The pipeline's catch made each an <c>UnhandledExceptionOperationResult</c>, written as the exception's type, title,
/// status and detail (<c>Blueprint.Http/UnhandledExceptionOperationResultExecutor.cs:46-58</c>), with a <c>traceId</c>
/// extension when the operation had an activity (:35-38), in that order and leaving out a null member
/// (<c>ProblemDetailsJsonConverter.cs:107-139</c>). This framework has no <c>ClaimsRequiredApiAuthoriser</c>, so a denial's
/// reason never reaches the wire.
/// </para>
/// </remarks>
public sealed class AuthorisationDenialWireTests
{
    private const string AuthorisationReason = "internal: the caller lacks the widgets.write claim";

    [Fact]
    public async Task An_anonymous_caller_is_refused_with_legacys_anonymous_body()
    {
        await using var host = await DenialHost.StartAsync(withLogging: false);

        var (status, body) = await host.GetAsync("allow", user: null);

        status.Should().Be(401);
        body.Should().Be("""{"type":"security_failure","title":"Access denied. Anonymous access is not allowed.","status":401}""");
    }

    [Fact]
    public async Task An_authentication_denial_is_a_401_titled_with_the_authorisers_message()
    {
        await using var host = await DenialHost.StartAsync(withLogging: false);

        var (status, body) = await host.GetAsync("authentication");

        status.Should().Be(401);
        body.Should().Be("""{"type":"security_failure","title":"Your account has been deactivated","status":401}""");
    }

    [Fact]
    public async Task An_authorisation_denial_is_a_403_titled_with_the_authorisers_message_and_without_its_reason()
    {
        await using var host = await DenialHost.StartAsync(withLogging: false);

        var (status, body) = await host.GetAsync("authorisation");

        status.Should().Be(403);
        body.Should().Be("""{"type":"unauthorized","title":"You may not change this widget","status":403}""");
        body.Should().NotContain(AuthorisationReason, "legacy exposed the reason for a ClaimsRequiredApiAuthoriser in development only");
    }

    [Fact]
    public async Task A_denial_with_no_failure_type_is_a_401_as_legacy_made_every_denial_that_was_not_an_authorisation_one()
    {
        // AuthorisationMiddlewareBuilder.cs:40-48: only FailureType == Authorisation was a 403.
        await using var host = await DenialHost.StartAsync(withLogging: false);

        var (status, body) = await host.GetAsync("default");

        status.Should().Be(401);
        body.Should().Be("""{"type":"security_failure","status":401}""", "a default result has no message, and a null title is left out");
    }

    [Theory]
    [InlineData("async-authorisation")]
    [InlineData("async-allow-then-deny")]
    [InlineData("operation-authoriser")]
    public async Task A_denial_on_every_path_through_the_authorisers_is_written_the_same_way(string outcome)
    {
        // A denial that completes asynchronously, one after an asynchronous allow, and one from an IOperationAuthoriser.
        await using var host = await DenialHost.StartAsync(withLogging: false);

        var (status, body) = await host.GetAsync(outcome);

        status.Should().Be(403);
        body.Should().Be("""{"type":"unauthorized","title":"You may not change this widget","status":403}""");
    }

    [Fact]
    public async Task A_denial_in_an_operation_with_an_activity_carries_its_trace_id_and_is_logged_with_its_reason()
    {
        // The traceId extension (UnhandledExceptionOperationResultExecutor.cs:35-38), and legacy's log of the reason, the
        // only place it went: Information, "Permission check failed. reason={0} authoriser={1}", under
        // ILogger<AuthorisationMiddlewareBuilder> (AuthorisationMiddlewareBuilder.cs:26, :31-38).
        await using var host = await DenialHost.StartAsync(withLogging: true);

        var (status, body) = await host.GetAsync("authorisation");

        status.Should().Be(403);
        using var document = JsonDocument.Parse(body);
        document.RootElement.EnumerateObject().Select(static p => p.Name).Should().Equal("type", "title", "status", "traceId");
        document.RootElement.GetProperty("traceId").GetString().Should().Be(DenialHost.TraceId);
        host.Logged.Should().ContainSingle(static e => e.Category == "Blueprint.Authorisation.AuthorisationMiddlewareBuilder")
            .Which.Should().Be(new LoggedEntry(
                "Blueprint.Authorisation.AuthorisationMiddlewareBuilder",
                LogLevel.Information,
                $"Permission check failed. reason={AuthorisationReason} authoriser=OutcomeApiAuthoriser"));
    }

    [Fact]
    public async Task An_anonymous_caller_of_an_operation_with_an_activity_is_refused_with_its_trace_id()
    {
        // Legacy's anonymous refusal was an UnhandledExceptionOperationResult like any other denial, so it carried the
        // traceId too (UnhandledExceptionOperationResultExecutor.cs:35-38): the fixed body is only the no-activity case.
        // A deliberate departure: legacy's no-activity body could carry a traceId all the same. An ApiException made
        // without extensions shared one static mutable dictionary (ApiException.cs:14, :34), which AddExtension wrote the
        // traceId into (UnhandledExceptionOperationResultExecutor.cs:37, ProblemDetails.cs:73-74). So a later body with no
        // activity carried another request's traceId, and concurrent requests raced on the dictionary. Here a body without
        // an activity has no traceId; do not port the shared dictionary back.
        await using var host = await DenialHost.StartAsync(withLogging: true);

        var (status, body) = await host.GetAsync("allow", user: null);

        status.Should().Be(401);
        using var document = JsonDocument.Parse(body);
        document.RootElement.EnumerateObject().Select(static p => p.Name).Should().Equal("type", "title", "status", "traceId");
        document.RootElement.GetProperty("title").GetString().Should().Be("Access denied. Anonymous access is not allowed.");
        document.RootElement.GetProperty("traceId").GetString().Should().Be(DenialHost.TraceId);
    }

    [Fact]
    public async Task An_allowed_operation_is_not_denied()
    {
        await using var host = await DenialHost.StartAsync(withLogging: false);

        var (status, body) = await host.GetAsync("allow");

        status.Should().Be(200);
        body.Should().Be("\"allowed\"");
    }

    /// <summary>An entry logged at Information or above.</summary>
    /// <param name="Category">The logger's category.</param>
    /// <param name="Level">The entry's level.</param>
    /// <param name="Message">The formatted message.</param>
    private sealed record LoggedEntry(string Category, LogLevel Level, string Message);

    /// <summary>
    /// Decides by the operation's <see cref="GetAuthorisationDenialQuery.Outcome"/>, as a Signature365 authoriser decides by
    /// the user and operation (S365ClaimsApiAuthoriser's "Your account has been deactivated").
    /// </summary>
    public sealed class OutcomeApiAuthoriser : IApiAuthoriser
    {
        public bool AppliesTo(ApiOperationDescriptor descriptor)
        {
            return descriptor.OperationType == typeof(GetAuthorisationDenialQuery);
        }

        public Task<ExecutionAllowed> CanExecuteOperationAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object operation)
        {
            return ((GetAuthorisationDenialQuery)operation).Outcome switch
            {
                "authentication" => Task.FromResult(ExecutionAllowed.No("internal: inactive", "Your account has been deactivated", ExecutionAllowedFailureType.Authentication)),
                "authorisation" => Task.FromResult(Forbidden()),
                "default" => Task.FromResult(default(ExecutionAllowed)),
                "async-authorisation" => DenyAsync(),
                "async-allow-then-deny" => AllowAsync(),
                _ => ExecutionAllowed.YesTask,
            };
        }

        public Task<ExecutionAllowed> CanShowLinkAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object resource)
        {
            return ExecutionAllowed.YesTask;
        }

        private static async Task<ExecutionAllowed> DenyAsync()
        {
            await Task.Yield();

            return Forbidden();
        }

        private static async Task<ExecutionAllowed> AllowAsync()
        {
            await Task.Yield();

            return ExecutionAllowed.Yes;
        }
    }

    /// <summary>A second API authoriser, which denies what the first allowed asynchronously.</summary>
    public sealed class SecondApiAuthoriser : IApiAuthoriser
    {
        public bool AppliesTo(ApiOperationDescriptor descriptor)
        {
            return descriptor.OperationType == typeof(GetAuthorisationDenialQuery);
        }

        public Task<ExecutionAllowed> CanExecuteOperationAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object operation)
        {
            return ((GetAuthorisationDenialQuery)operation).Outcome == "async-allow-then-deny"
                ? Task.FromResult(Forbidden())
                : ExecutionAllowed.YesTask;
        }

        public Task<ExecutionAllowed> CanShowLinkAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object resource)
        {
            return ExecutionAllowed.YesTask;
        }
    }

    /// <summary>An operation authoriser, run after every API authoriser has allowed.</summary>
    public sealed class OutcomeOperationAuthoriser : IOperationAuthoriser
    {
        public bool AppliesTo(ApiOperationDescriptor descriptor)
        {
            return descriptor.OperationType == typeof(GetAuthorisationDenialQuery);
        }

        public Task<ExecutionAllowed> CanExecuteOperationAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object operation)
        {
            return ((GetAuthorisationDenialQuery)operation).Outcome == "operation-authoriser"
                ? Task.FromResult(Forbidden())
                : ExecutionAllowed.YesTask;
        }
    }

    private static ExecutionAllowed Forbidden()
    {
        return ExecutionAllowed.No(AuthorisationReason, "You may not change this widget", ExecutionAllowedFailureType.Authorisation);
    }

    /// <summary>The integration app with the authorisers above, served by Kestrel.</summary>
    private sealed class DenialHost : IAsyncDisposable
    {
        public const string TraceId = "5bf92f3577b34da6a3ce929d0e0e4736";

        private const string TestUserHeader = "X-Test-User";

        private readonly WebApplication _app;

        private DenialHost(WebApplication app, System.Net.Http.HttpClient client, ConcurrentQueue<LoggedEntry> logged)
        {
            this._app = app;
            this.Client = client;
            this.Logged = logged;
        }

        public System.Net.Http.HttpClient Client { get; }

        public ConcurrentQueue<LoggedEntry> Logged { get; }

        public static async Task<DenialHost> StartAsync(bool withLogging)
        {
            var logged = new ConcurrentQueue<LoggedEntry>();
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            // Logging enabled is also what makes ASP.NET Core start the request's activity, which the operation's is made
            // under; without it the operation has none, and legacy wrote no traceId.
            if (withLogging)
            {
                builder.Logging.AddProvider(new RecordingLoggerProvider(logged));
            }

            builder.Services.AddSingleton<IApiAuthoriser, OutcomeApiAuthoriser>();
            builder.Services.AddSingleton<IApiAuthoriser, SecondApiAuthoriser>();
            builder.Services.AddSingleton<IOperationAuthoriser, OutcomeOperationAuthoriser>();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

            var app = builder.Build();

            app.Use(static (httpContext, next) =>
            {
                if (httpContext.Request.Headers.TryGetValue(TestUserHeader, out var userName))
                {
                    httpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                        [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, userName.ToString())],
                        authenticationType: "Test"));
                }

                return next(httpContext);
            });

            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/") };

            return new DenialHost(app, client, logged);
        }

        public async Task<(int Status, string Body)> GetAsync(string outcome, string? user = "user-1")
        {
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "api/authorisation-denials/" + outcome);
            request.Headers.Add("traceparent", $"00-{TraceId}-00f067aa0ba902b7-01");

            if (user is not null)
            {
                request.Headers.Add(TestUserHeader, user);
            }

            using var response = await this.Client.SendAsync(request);

            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }

    private sealed class RecordingLoggerProvider(ConcurrentQueue<LoggedEntry> logged) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new RecordingLogger(categoryName, logged);
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<LoggedEntry> logged) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Information;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Information)
            {
                logged.Enqueue(new LoggedEntry(category, logLevel, formatter(state, exception)));
            }
        }
    }
}

/// <summary>An operation its authorisers allow or deny by <see cref="Outcome"/>.</summary>
[RootLink("/authorisation-denials/{Outcome}")]
public partial class GetAuthorisationDenialQuery : IQuery<string>
{
    public string Outcome { get; set; } = null!;

    public static string Handle(GetAuthorisationDenialQuery query)
    {
        return "allowed";
    }
}
