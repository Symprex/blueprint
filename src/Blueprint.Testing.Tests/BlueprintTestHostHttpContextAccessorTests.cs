using System.Net;
using Blueprint.Persistence;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Api;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.Testing.Tests;

/// <summary>
/// Proves that <see cref="BlueprintTestHost{TApp,TExec}"/> gives <see cref="IHttpContextAccessor"/> the HTTP context of the
/// operation it executes, as a real request does and as legacy Blueprint's test harness did, so that a dependency reading
/// the accessor (Signature365's <c>BusResourceEventAuditRepository</c> reads its user) sees the executing user.
/// </summary>
/// <remarks>
/// Legacy Signature365's <c>ApiFixture</c> gave every HTTP operation — one with a link attribute (Signature365
/// <c>8b40ecc961</c>, <c>Blueprint.Http/HttpHost.cs:47-50</c>) — an HTTP context configured by <c>ConfigureHttp</c>
/// (<c>backend/tests/Symprex.Signature365.TestHelpers/ApiFixture.cs:270-279</c>), with the current user
/// (<c>:275-278</c>). <c>ConfigureHttp</c> gave the connection a loopback remote address and set the context on the
/// scope's <see cref="IHttpContextAccessor"/> (<c>Blueprint.Testing/ApiOperationContextSetup.cs:46</c>, <c>:61</c>). An operation
/// that was not an HTTP operation was given no HTTP context at all.
/// </remarks>
public sealed class BlueprintTestHostHttpContextAccessorTests : IDisposable
{
    private readonly BlueprintTestHost<SampleBlueprintApp, SampleBlueprintAppExecutor> _host;

    public BlueprintTestHostHttpContextAccessorTests()
    {
        this._host = BlueprintTestHostBuilder<SampleBlueprintApp, SampleBlueprintAppExecutor>
            .Create()
            .ConfigureServices(static services =>
            {
                services.AddSingleton<TemplateStore>();
                services.AddSingleton<AccessorRecords>();
                services.AddScoped<IUnitOfWork, AccessorRecordingUnitOfWork>();
            })
            .Build();
    }

    [Fact]
    public async Task When_an_http_operation_runs_then_the_accessor_gives_its_http_context_and_user()
    {
        Microsoft.AspNetCore.Http.HttpContext? operationHttpContext = null;

        await this._host.ExecuteAsync(
            new ValidateTemplateCommand { Id = "t-1" },
            ctx =>
            {
                ctx.WithUser(static claims => claims.Add(new System.Security.Claims.Claim("sub", "user-1")));
                operationHttpContext = ctx.OperationContext.HttpContext;
            });

        var seen = this._host.Services.GetRequiredService<AccessorRecords>().Seen.Should().ContainSingle().Subject;
        seen.Should().NotBeNull("a dependency reading the accessor during the pipeline must see the request (ApiOperationContextSetup.cs:61)");
        seen.Should().BeSameAs(operationHttpContext);
        seen!.User.FindFirst("sub")!.Value.Should().Be("user-1", "the harness gave the context the current user (ApiFixture.cs:275-278)");
        seen.Connection.RemoteIpAddress.Should().Be(IPAddress.Loopback, "ConfigureHttp gave the connection a loopback address (ApiOperationContextSetup.cs:46)");
    }

    [Fact]
    public async Task When_an_operation_that_is_not_an_http_operation_runs_then_the_accessor_gives_no_http_context()
    {
        // ApiFixture.cs:270-279 configured HTTP only for an operation HttpHost supported, one with a link attribute.
        await this._host.ExecuteAsync(
            new SendTemplateSharedNotificationTask { TemplateId = "t-1", RecipientEmail = "recipient@example.com" },
            static ctx => ctx.WithUser(static claims => claims.Add(new System.Security.Claims.Claim("sub", "user-1"))));

        this._host.Services.GetRequiredService<AccessorRecords>().Seen.Should().ContainSingle().Which.Should().BeNull();
    }

    public void Dispose()
    {
        this._host.Dispose();
    }

    /// <summary>What the accessor gave each save.</summary>
    public sealed class AccessorRecords
    {
        public List<Microsoft.AspNetCore.Http.HttpContext?> Seen { get; } = [];
    }

    /// <summary>A unit of work, which the pipeline saves once its handler has run, that records what the accessor gives.</summary>
    public sealed class AccessorRecordingUnitOfWork(IHttpContextAccessor accessor, AccessorRecords records) : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            records.Seen.Add(accessor.HttpContext);
            return Task.CompletedTask;
        }
    }
}
