using System.Net;
using System.Net.Http.Headers;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// <see cref="BlueprintRequestOptions"/>: what <see cref="BlueprintHttpClient"/> tells a handler about each request it sends,
/// which a test double of the upstream (<c>Blueprint.Testing</c>'s <c>StubBlueprintClient</c>) dispatches and records by.
/// </summary>
public sealed class BlueprintRequestOptionsTests
{
    private static readonly Uri _baseAddress = new("https://upstream.example.test/api/");

    [Fact]
    public async Task A_typed_client_call_carries_the_operation_it_was_built_from()
    {
        var seen = new List<HttpRequestMessage>();
        await using var provider = BuildProvider(seen);
        var api = provider.GetRequiredService<ISampleContractApi>();
        var query = new GetTemplateQuery { Id = "t-1" };

        await api.GetTemplateAsync(query);

        seen.Should().ContainSingle()
            .Which.Options.TryGetValue(BlueprintRequestOptions.Operation, out var operation).Should().BeTrue();
        operation.Should().BeSameAs(query);
    }

    [Fact]
    public async Task A_forward_carries_the_inner_operation()
    {
        var seen = new List<HttpRequestMessage>();
        await using var provider = BuildProvider(seen);
        var inner = new GetTemplateQuery { Id = "t-2" };

        await BlueprintClientTransport.Create<SampleContractApi>(provider)
            .ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(inner);

        seen.Single().Options.TryGetValue(BlueprintRequestOptions.Operation, out var operation).Should().BeTrue();
        operation.Should().BeSameAs(inner);
    }

    [Fact]
    public async Task The_requests_own_authorization_is_told_apart_from_the_clients_default_header()
    {
        // HttpClient merges its DefaultRequestHeaders into the request before any handler sees it, so the header alone cannot
        // say whether the call set it.
        var seen = new List<HttpRequestMessage>();
        await using var provider = BuildProvider(seen, static client => client.DefaultRequestHeaders.Authorization = new("Bearer", "default"));
        var transport = BlueprintClientTransport.Create<SampleContractApi>(provider);

        await transport.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            new GetTemplateQuery { Id = "t-3" });
        await transport.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            new GetTemplateQuery { Id = "t-4" },
            new CallOptions { ModifyRequest = static r => r.Headers.Authorization = new("Bearer", "own") });

        seen[0].Headers.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "default"));
        seen[0].Options.TryGetValue(BlueprintRequestOptions.RequestAuthorization, out _).Should().BeFalse();

        seen[1].Options.TryGetValue(BlueprintRequestOptions.RequestAuthorization, out var own).Should().BeTrue();
        own.Should().Be(new AuthenticationHeaderValue("Bearer", "own"));
    }

    [Fact]
    public async Task A_raw_send_tells_the_requests_own_authorization_apart_from_the_clients_default_header()
    {
        // A streamed forward builds its request with CreateRequest and sends it with SendRawAsync, so a handler has to be told
        // the same things about it as about a request SendAsync sends.
        var seen = new List<HttpRequestMessage>();
        await using var provider = BuildProvider(seen, static client => client.DefaultRequestHeaders.Authorization = new("Bearer", "default"));
        var transport = BlueprintClientTransport.Create<SampleContractApi>(provider);
        var withoutOwn = new GetTemplateQuery { Id = "t-5" };
        var withOwn = new GetTemplateQuery { Id = "t-6" };

        using (var request = transport.CreateRequest(HttpMethod.Get, "/templates/t-5", withoutOwn))
        {
            (await transport.SendRawAsync(request)).Dispose();
        }

        using (var request = transport.CreateRequest(HttpMethod.Get, "/templates/t-6", withOwn))
        {
            request.Headers.Authorization = new("Bearer", "own");
            (await transport.SendRawAsync(request)).Dispose();
        }

        seen[0].Options.TryGetValue(BlueprintRequestOptions.Operation, out var first).Should().BeTrue();
        first.Should().BeSameAs(withoutOwn);
        seen[0].Options.TryGetValue(BlueprintRequestOptions.RequestAuthorization, out _).Should().BeFalse();

        seen[1].Options.TryGetValue(BlueprintRequestOptions.Operation, out var second).Should().BeTrue();
        second.Should().BeSameAs(withOwn);
        seen[1].Options.TryGetValue(BlueprintRequestOptions.RequestAuthorization, out var own).Should().BeTrue();
        own.Should().Be(new AuthenticationHeaderValue("Bearer", "own"));
    }

    private static ServiceProvider BuildProvider(List<HttpRequestMessage> seen, Action<System.Net.Http.HttpClient>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddBlueprintClient<ISampleContractApi, SampleContractApi>((_, client) =>
            {
                client.BaseAddress = _baseAddress;
                configure?.Invoke(client);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpHandler(request =>
            {
                seen.Add(request);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"id":"t","name":"n"}""", new MediaTypeHeaderValue("application/json")),
                };
            }));

        return services.BuildServiceProvider();
    }
}
