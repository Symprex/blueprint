using System.Net.Http.Headers;
using Blueprint.HttpClient;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Blueprint.Testing.Tests;

/// <summary>
/// <c>AddCallerBlueprintClient</c>: a typed client resolved already carrying the <c>Authorization</c> header of the caller
/// the app is serving, as a per-call credential, so a gateway sets nothing on the shared named client. Asserted on
/// <see cref="BlueprintClientStubRequest.RequestAuthorization"/>, which a client's default headers cannot fake.
/// </summary>
public sealed class CallerBlueprintClientTests
{
    private static readonly Uri _registeredAddress = new("https://templates.example.test/api/");

    [Fact]
    public async Task Each_caller_in_turn_sends_its_own_token_and_no_caller_sends_none()
    {
        await using var provider = BuildProvider(out var upstream, out var accessor);

        await CallAsAsync(provider, accessor, "Bearer alice");
        await CallAsAsync(provider, accessor, "Bearer bob");
        await CallAsAsync(provider, accessor, authorization: null);

        upstream.Requests.Select(static r => r.RequestAuthorization).Should().Equal(
            new AuthenticationHeaderValue("Bearer", "alice"),
            new AuthenticationHeaderValue("Bearer", "bob"),
            null);
    }

    [Fact]
    public async Task Outside_a_request_nothing_is_sent()
    {
        await using var provider = BuildProvider(out var upstream, out var accessor);
        accessor.HttpContext = null;

        await provider.GetRequiredService<IStubbedTemplatesApi>().GetTemplatesAsync(new GetTemplatesQuery());

        upstream.Requests.Should().ContainSingle().Which.RequestAuthorization.Should().BeNull();
    }

    [Fact]
    public async Task The_credential_is_read_once_when_the_client_is_resolved()
    {
        await using var provider = BuildProvider(out var upstream, out var accessor);
        accessor.HttpContext = ContextWith("Bearer alice");
        var api = provider.GetRequiredService<IStubbedTemplatesApi>();

        accessor.HttpContext = ContextWith("Bearer mallory");
        await api.GetTemplatesAsync(new GetTemplatesQuery());

        upstream.Requests.Should().ContainSingle().Which.RequestAuthorization.Should().Be(new AuthenticationHeaderValue("Bearer", "alice"));
    }

    [Fact]
    public async Task The_callers_token_wins_over_the_clients_default_header()
    {
        var services = new ServiceCollection();
        services.AddCallerBlueprintClient<IStubbedTemplatesApi, StubbedTemplatesApi>(static (_, client) =>
        {
            client.BaseAddress = _registeredAddress;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "default");
        });
        var upstream = services.StubBlueprintClient<StubbedTemplatesApi>();
        upstream.Respond<GetTemplatesQuery>(static _ => Array.Empty<TemplateApiResource>());
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = ContextWith("Bearer alice");

        await provider.GetRequiredService<IStubbedTemplatesApi>().GetTemplatesAsync(new GetTemplatesQuery());

        upstream.Requests.Should().ContainSingle().Which.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "alice"));
    }

    [Fact]
    public async Task A_client_registered_beforehand_stands()
    {
        var services = new ServiceCollection();
        var stand = Substitute.For<IStubbedTemplatesApi>();
        services.AddSingleton<IStubbedTemplatesApi>(stand);
        services.AddCallerBlueprintClient<IStubbedTemplatesApi, StubbedTemplatesApi>(static (_, client) => client.BaseAddress = _registeredAddress);
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStubbedTemplatesApi>().Should().BeSameAs(stand);
    }

    [Fact]
    public void A_client_already_registered_by_AddBlueprintClient_is_refused()
    {
        var services = new ServiceCollection();
        services.AddBlueprintClient<IStubbedTemplatesApi, StubbedTemplatesApi>(static (_, client) => client.BaseAddress = _registeredAddress);

        var act = () => services.AddCallerBlueprintClient<IStubbedTemplatesApi, StubbedTemplatesApi>(static (_, client) => client.BaseAddress = _registeredAddress);

        act.Should().Throw<InvalidOperationException>().WithMessage("*already registered*");
    }

    private static async Task CallAsAsync(IServiceProvider provider, IHttpContextAccessor accessor, string? authorization)
    {
        accessor.HttpContext = ContextWith(authorization);

        await provider.GetRequiredService<IStubbedTemplatesApi>().GetTemplatesAsync(new GetTemplatesQuery());
    }

    private static DefaultHttpContext ContextWith(string? authorization)
    {
        var context = new DefaultHttpContext();

        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        return context;
    }

    private static ServiceProvider BuildProvider(out BlueprintClientStub<StubbedTemplatesApi> upstream, out IHttpContextAccessor accessor)
    {
        var services = new ServiceCollection();
        services.AddCallerBlueprintClient<IStubbedTemplatesApi, StubbedTemplatesApi>(static (_, client) => client.BaseAddress = _registeredAddress);
        upstream = services.StubBlueprintClient<StubbedTemplatesApi>();
        upstream.Respond<GetTemplatesQuery>(static _ => Array.Empty<TemplateApiResource>());

        var provider = services.BuildServiceProvider();
        accessor = provider.GetRequiredService<IHttpContextAccessor>();

        return provider;
    }
}
