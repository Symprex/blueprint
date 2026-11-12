using System.Net;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// <c>AddCallerBlueprintClient</c> with a client's own extension points: the caller's credential is applied after every
/// request modifier, so a clone made through <c>WithRequestModifier</c> (a region, say) keeps it and cannot override it.
/// </summary>
public sealed class CallerBlueprintClientCloneTests
{
    private static readonly Uri _baseAddress = new("https://default.example.test/");

    [Fact]
    public async Task A_clone_made_by_a_request_modifier_keeps_the_callers_credential()
    {
        var sent = new List<HttpRequestMessage>();
        await using var provider = BuildProvider(sent, "Bearer alice");

        await provider.GetRequiredService<IRegionalOperationsApi>()
            .ForRegion("eu")
            .FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });

        var request = sent.Should().ContainSingle().Subject;
        request.RequestUri!.Host.Should().Be("eu.example.test");
        request.Headers.Authorization!.ToString().Should().Be("Bearer alice");
    }

    [Fact]
    public async Task The_callers_credential_is_applied_after_a_modifier_that_sets_authorization()
    {
        var sent = new List<HttpRequestMessage>();
        await using var provider = BuildProvider(sent, "Bearer alice");

        await provider.GetRequiredService<IRegionalOperationsApi>()
            .WithAuthorizationHeader("Bearer from-modifier")
            .FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });

        sent.Should().ContainSingle().Which.Headers.GetValues("Authorization").Should().Equal("Bearer alice");
    }

    private static ServiceProvider BuildProvider(List<HttpRequestMessage> sent, string authorization)
    {
        var endpoints = new RegionEndpoints();
        endpoints.Regions["eu"] = new Uri("https://eu.example.test/");

        var services = new ServiceCollection();
        services.AddSingleton(endpoints);
        services.AddCallerBlueprintClient<IRegionalOperationsApi, RegionalOperationsApi>(static (_, client) => client.BaseAddress = _baseAddress)
            .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpHandler(request =>
            {
                sent.Add(request);

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json") };
            }));

        var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = authorization;
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;

        return provider;
    }
}
