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
public sealed class BlueprintHttpClientDerivedOperationTests : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _fixture;

    public BlueprintHttpClientDerivedOperationTests(SampleAppFixture fixture)
    {
        this._fixture = fixture;
    }

    [Fact]
    public async Task A_derived_query_through_a_runtime_scanned_client_is_sent_to_its_contract_operations_url()
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

        var client = BlueprintHttpClient.From(CreateRuntimeScannedClient(handler));

        await client.CallAsync<TemplateApiResource[]>(new GatewaySearchTemplatesQuery { Q = "derived", Page = 2, PageSize = 5 });

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri!.AbsolutePath.Should().Be("/templates/search");
        captured.RequestUri.Query.Should().Contain("Q=derived").And.Contain("Page=2").And.Contain("PageSize=5");
    }

    [Fact]
    public async Task A_derived_command_through_a_runtime_scanned_client_is_sent_to_its_contract_operations_url_with_its_body()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var client = BlueprintHttpClient.From(CreateRuntimeScannedClient(handler));

        await client.SendVoidAsync((Blueprint.Model.IOperation)new GatewayCreateTemplateCommand { Name = "Derived" });

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.AbsolutePath.Should().Be("/templates");

        using var json = JsonDocument.Parse(body!);
        json.RootElement.GetProperty("name").GetString().Should().Be("Derived");
    }

    [Fact]
    public async Task A_derived_operation_through_a_generated_apps_client_is_sent_to_its_contract_operations_url()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var client = this._fixture.CreateClient(handler);

        await client.SendVoidAsync(new GatewayArchiveTemplateCommand { Id = "t-42" });

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Delete);
        captured.RequestUri!.AbsolutePath.Should().Be("/templates/t-42");
    }

    [Fact]
    public async Task An_operation_deriving_from_nothing_registered_is_refused_naming_its_type()
    {
        var handler = new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateRuntimeScannedClient(handler);

        var act = () => client.SendVoidAsync(new UnregisteredOperation());

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*{typeof(UnregisteredOperation).FullName}*");
    }

    private static BlueprintHttpClient<RuntimeScanBlueprintApp> CreateRuntimeScannedClient(FakeHttpHandler handler)
    {
        // AddBlueprintClient<TInterface, TImplementation> builds its app this way, from the contract assembly alone.
        var app = RuntimeScanBlueprintApp.ScanAssembly(typeof(SearchTemplatesQuery).Assembly);

        return new BlueprintHttpClient<RuntimeScanBlueprintApp>(
            new System.Net.Http.HttpClient(handler) { BaseAddress = new Uri("https://localhost") },
            app);
    }
}
