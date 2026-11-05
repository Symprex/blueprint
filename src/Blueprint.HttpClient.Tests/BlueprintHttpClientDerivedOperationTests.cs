using System.Net;
using System.Text;
using System.Text.Json;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// A consumer's own operation can derive from a contract operation and send itself through that contract's client, as
/// Signature 365's gateway operations do (<c>GetOutlookAddInSettingsQuery : GetAddInSettingsQuery</c> calling
/// <c>api.GetAddInSettingsAsync(this)</c>). Only the contract operation is registered with the client's app, so the
/// client sends a derived operation as the nearest registered operation it derives from, as legacy's client did
/// (<c>d.OperationType.IsAssignableFrom(operationType)</c>). The exact-type lookup this used to be failed every such
/// call with "Could not find an operation of the type".
/// </summary>
/// <remarks>
/// The derived operations are declared in this assembly, so the generated <c>SampleBlueprintApp</c> of the contract's
/// assembly does not register them, as a contract's app does not register a consumer's operations.
/// </remarks>
public sealed class BlueprintHttpClientDerivedOperationTests : IClassFixture<SampleAppFixture>
{
    private static readonly Uri _baseAddress = new("https://localhost");

    private readonly SampleAppFixture _fixture;

    public BlueprintHttpClientDerivedOperationTests(SampleAppFixture fixture)
    {
        this._fixture = fixture;
    }

    [Fact]
    public async Task A_derived_query_through_an_apps_client_is_sent_to_its_contract_operations_url()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
        });

        await this._fixture.CreateClient(handler)
            .SendAsync<TemplateApiResource[]>(new GatewaySearchTemplatesQuery { Q = "derived", Page = 2, PageSize = 5 });

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri!.AbsolutePath.Should().Be("/templates/search");
        captured.RequestUri.Query.Should().Contain("q=derived").And.Contain("page=2").And.Contain("pageSize=5");
    }

    [Fact]
    public async Task A_derived_command_through_an_apps_client_is_sent_to_its_contract_operations_url_with_its_body()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        await this._fixture.CreateClient(handler).SendAsync(new GatewayCreateTemplateCommand { Name = "Derived" });

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.AbsolutePath.Should().Be("/templates");

        using var json = JsonDocument.Parse(body!);
        json.RootElement.GetProperty("name").GetString().Should().Be("Derived");
    }

    [Fact]
    public async Task A_derived_operation_bound_from_the_route_through_an_apps_client_is_sent_to_its_contract_operations_url()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        await this._fixture.CreateClient(handler).SendAsync(new GatewayArchiveTemplateCommand { Id = "t-42" });

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Delete);
        captured.RequestUri!.AbsolutePath.Should().Be("/templates/t-42");
    }

    [Fact]
    public async Task An_operation_deriving_from_nothing_registered_is_refused_naming_its_type()
    {
        var handler = new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK));

        var act = () => this._fixture.CreateClient(handler).SendAsync(new UnregisteredOperation());

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*{typeof(UnregisteredOperation).FullName}*");
    }

    [Fact]
    public async Task A_derived_query_through_a_typed_client_is_sent_to_its_contract_operations_url()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
        });

        await CreateTypedClient(handler)
            .SearchTemplatesAsync(new GatewaySearchTemplatesQuery { Q = "derived", Page = 2, PageSize = 5 });

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri!.AbsolutePath.Should().Be("/templates/search");
        captured.RequestUri.Query.Should().Contain("q=derived").And.Contain("page=2").And.Contain("pageSize=5");
    }

    [Fact]
    public async Task A_derived_command_through_a_typed_client_is_sent_to_its_contract_operations_url_with_its_body()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

            // The envelope the host writes for the created event the contract operation declares.
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """{"changeType":"Created","eventId":"template.created","data":{"id":"t-1","name":"Derived"}}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        await CreateTypedClient(handler).CreateTemplateAsync(new GatewayCreateTemplateCommand { Name = "Derived" });

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.AbsolutePath.Should().Be("/templates");

        using var json = JsonDocument.Parse(body!);
        json.RootElement.GetProperty("name").GetString().Should().Be("Derived");
    }

    private static SampleContractApi CreateTypedClient(FakeHttpHandler handler)
    {
        return new SampleContractApi(new BlueprintHttpClient(new System.Net.Http.HttpClient(handler) { BaseAddress = _baseAddress }));
    }
}
