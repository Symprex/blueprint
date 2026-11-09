using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// <see cref="BlueprintClientTransport.Create{TClient}"/>, which an <c>[Expose&lt;TInner&gt;]</c> operation's generated forward
/// calls: it reaches the upstream exactly as the typed client that includes the inner operation does.
/// </summary>
public sealed class BlueprintClientTransportTests
{
    private static readonly Uri _baseAddress = new("https://upstream.example.test/api/");

    [Fact]
    public async Task A_forward_goes_through_the_clients_registered_named_client_with_its_JSON_options()
    {
        var sent = new List<Uri>();
        var services = new ServiceCollection();
        services.AddBlueprintClient<ILocalOperationsApi, LocalOperationsApi>(static (_, client) => client.BaseAddress = _baseAddress)
            .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpHandler(request =>
            {
                sent.Add(request.RequestUri!);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", new MediaTypeHeaderValue("application/json")) };
            }));

        await using var provider = services.BuildServiceProvider();
        var client = (LocalOperationsApi)provider.GetRequiredService<ILocalOperationsApi>();

        var transport = BlueprintClientTransport.Create<LocalOperationsApi>(provider);
        var result = await transport.ForwardAsync<FindTemplateCopiesQuery, Blueprint.SourceGenerator.Sample.Contract.Api.TemplateApiResource[]>(
            new FindTemplateCopiesQuery { Id = "t-1" });

        result.Should().BeEmpty();
        sent.Should().ContainSingle().Which.ToString().Should().StartWith("https://upstream.example.test/api/templates/t-1/copies/search");
        transport.JsonOptions.Should().BeSameAs(client.Transport.JsonOptions);
    }

    [Fact]
    public async Task A_forward_through_a_client_with_a_serializer_context_reads_and_writes_with_that_context()
    {
        // The transport used to take the container's options alone, without the context the client's constructor applies.
        var services = new ServiceCollection();
        services.AddBlueprintClient<PricingApi, PricingApi>(static (_, client) => client.BaseAddress = _baseAddress);

        await using var provider = services.BuildServiceProvider();

        var transport = BlueprintClientTransport.Create<PricingApi>(provider);

        transport.JsonOptions.TypeInfoResolverChain.Should().Contain(PricingSerializerContext.Default);
    }

    [Fact]
    public async Task A_client_with_no_registration_is_refused_naming_it()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();

        await using var provider = services.BuildServiceProvider();

        var act = () => BlueprintClientTransport.Create<LocalOperationsApi>(provider);

        act.Should().Throw<InvalidOperationException>().WithMessage("*LocalOperationsApi*AddBlueprintClient*");
    }
}
