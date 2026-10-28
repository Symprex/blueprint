using System.Collections.Concurrent;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// Proves that a resource authorization provider's decision, not a bare yes or no, reaches the caller: its failure type
/// selects 401 or 403, its message titles the response, its reason is logged and never written, and every registered
/// provider must allow (Symprex/blueprint#77).
/// </summary>
public sealed class ResourceAuthorizationDecisionIntegrationTests
{
    private const string Reason = "internal: no widget::read scope";

    [Fact]
    public async Task An_authorisation_denial_is_a_403_titled_with_the_providers_message_and_without_its_reason()
    {
        var harness = Harness.Create(new OutcomeProvider());

        var response = await harness.GetAsync("authorisation");

        response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        response.Body.Should().Be("""{"type":"unauthorized","title":"You may not read this widget","status":403}""");
        response.Body.Should().NotContain(Reason);
    }

    [Fact]
    public async Task An_authentication_denial_is_a_401_titled_with_the_providers_message()
    {
        var harness = Harness.Create(new OutcomeProvider());

        var response = await harness.GetAsync("authentication");

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        response.Body.Should().Be("""{"type":"security_failure","title":"Your account has been deactivated","status":401}""");
    }

    [Fact]
    public async Task A_denial_completed_asynchronously_is_answered_the_same_way()
    {
        var harness = Harness.Create(new OutcomeProvider());

        var response = await harness.GetAsync("async-authorisation");

        response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        response.Body.Should().Be("""{"type":"unauthorized","title":"You may not read this widget","status":403}""");
    }

    [Fact]
    public async Task A_denial_is_logged_with_its_reason_and_the_provider_that_gave_it()
    {
        var harness = Harness.Create(new OutcomeProvider());

        await harness.GetAsync("authorisation");

        harness.Logged.Should().ContainSingle(static e => e.Category == "Blueprint.Authorisation.AuthorisationMiddlewareBuilder")
            .Which.Message.Should().Be($"Permission check failed. reason={Reason} authoriser={nameof(OutcomeProvider)}");
    }

    [Fact]
    public async Task The_provider_is_asked_to_authorise_execution_with_the_operations_scope_pairs()
    {
        var provider = new OutcomeProvider();
        var harness = Harness.Create(provider);

        await harness.GetAsync("allow");

        provider.Requests.Should().ContainSingle()
            .Which.Should().Match<ResourceAuthorizationRequest>(static r =>
                r.Mode == ResourceAuthorizationMode.Execute
                && r.Resource == null
                && r.ScopePairs.Count == 1
                && r.ScopePairs[0].Noun == "widget"
                && r.ScopePairs[0].Verb == "read");
    }

    [Theory]
    [InlineData("allow")]
    [InlineData("async-allow")]
    public async Task When_the_first_provider_allows_a_second_providers_denial_refuses_the_operation(string firstOutcome)
    {
        var first = new OutcomeProvider();
        var second = new FixedProvider(Forbidden());
        var harness = Harness.Create(first, second);

        var response = await harness.GetAsync(firstOutcome);

        response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        first.Requests.Should().HaveCount(1);
        second.Calls.Should().Be(1);
    }

    [Fact]
    public async Task When_the_first_provider_denies_the_second_is_not_asked()
    {
        var first = new OutcomeProvider();
        var second = new FixedProvider(ExecutionAllowed.Yes);
        var harness = Harness.Create(first, second);

        var response = await harness.GetAsync("authorisation");

        response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        second.Calls.Should().Be(0);
    }

    [Fact]
    public async Task When_every_provider_allows_the_operation_executes()
    {
        var first = new OutcomeProvider();
        var second = new FixedProvider(ExecutionAllowed.Yes);
        var harness = Harness.Create(first, second);

        var response = await harness.GetAsync("allow");

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        second.Calls.Should().Be(1);
    }

    private static ExecutionAllowed Forbidden()
    {
        return ExecutionAllowed.No(Reason, "You may not read this widget", ExecutionAllowedFailureType.Authorisation);
    }

    /// <summary>Decides by the operation's <see cref="GetResourceDecisionQuery.Outcome"/>.</summary>
    private sealed class OutcomeProvider : IResourceAuthorizationProvider
    {
        public ConcurrentQueue<ResourceAuthorizationRequest> Requests { get; } = new();

        public ValueTask<ExecutionAllowed> AuthorizeAsync(
            ApiOperationContext operationContext,
            System.Security.Claims.ClaimsPrincipal principal,
            ResourceAuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            this.Requests.Enqueue(request);

            return ((GetResourceDecisionQuery)operationContext.Operation).Outcome switch
            {
                "authorisation" => new ValueTask<ExecutionAllowed>(Forbidden()),
                "authentication" => new ValueTask<ExecutionAllowed>(
                    ExecutionAllowed.No("internal: inactive", "Your account has been deactivated", ExecutionAllowedFailureType.Authentication)),
                "async-authorisation" => DenyAsync(),
                "async-allow" => AllowAsync(),
                _ => new ValueTask<ExecutionAllowed>(ExecutionAllowed.Yes),
            };
        }

        private static async ValueTask<ExecutionAllowed> DenyAsync()
        {
            await Task.Yield();

            return Forbidden();
        }

        private static async ValueTask<ExecutionAllowed> AllowAsync()
        {
            await Task.Yield();

            return ExecutionAllowed.Yes;
        }
    }

    /// <summary>Always gives the same decision, counting how often it is asked.</summary>
    private sealed class FixedProvider(ExecutionAllowed decision) : IResourceAuthorizationProvider
    {
        private int _calls;

        public int Calls => this._calls;

        public ValueTask<ExecutionAllowed> AuthorizeAsync(
            ApiOperationContext operationContext,
            System.Security.Claims.ClaimsPrincipal principal,
            ResourceAuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this._calls);

            return new ValueTask<ExecutionAllowed>(decision);
        }
    }

    private sealed record LoggedEntry(string Category, LogLevel Level, string Message);

    private sealed record TestHttpResponse(int StatusCode, string Body);

    /// <summary>The integration app with the given providers, its endpoints invoked in memory.</summary>
    private sealed class Harness
    {
        private readonly TestEndpointRouteBuilder _endpoints;

        private Harness(TestEndpointRouteBuilder endpoints, ConcurrentQueue<LoggedEntry> logged)
        {
            this._endpoints = endpoints;
            this.Logged = logged;
        }

        public ConcurrentQueue<LoggedEntry> Logged { get; }

        public static Harness Create(params IResourceAuthorizationProvider[] providers)
        {
            var logged = new ConcurrentQueue<LoggedEntry>();

            var services = new ServiceCollection()
                .AddLogging(b => b.AddProvider(new RecordingLoggerProvider(logged)))
                .AddSingleton<IntegrationTestBlueprintApp>()
                .AddSingleton<IntegrationTestBlueprintAppExecutor>();

            foreach (var provider in providers)
            {
                services.AddSingleton(provider);
            }

            var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());
            endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            return new Harness(endpoints, logged);
        }

        public async Task<TestHttpResponse> GetAsync(string outcome)
        {
            var endpoint = this._endpoints.DataSources
                .SelectMany(static ds => ds.Endpoints)
                .OfType<RouteEndpoint>()
                .Single(static ep => string.Equals(ep.RoutePattern.RawText, "api/resource-decisions/{Outcome}", StringComparison.Ordinal));

            await using var body = new System.IO.MemoryStream();
            var context = new DefaultHttpContext
            {
                RequestServices = this._endpoints.ServiceProvider,
                Response = { Body = body },
            };

            context.SetEndpoint(endpoint);
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/resource-decisions/" + outcome;
            context.Request.RouteValues["Outcome"] = outcome;
            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "user-1")],
                    "TestAuth"));

            await endpoint.RequestDelegate!(context);

            return new TestHttpResponse(context.Response.StatusCode, System.Text.Encoding.UTF8.GetString(body.ToArray()));
        }
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public IApplicationBuilder CreateApplicationBuilder()
        {
            return new ApplicationBuilder(this.ServiceProvider);
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

/// <summary>A permissioned operation whose resource authorization providers decide by <see cref="Outcome"/>.</summary>
[RootLink("/resource-decisions/{Outcome}")]
[Resource("widget", "read")]
public partial class GetResourceDecisionQuery : IQuery<string>
{
    public string Outcome { get; set; } = null!;

    public static string Handle(GetResourceDecisionQuery query)
    {
        return "allowed";
    }
}
