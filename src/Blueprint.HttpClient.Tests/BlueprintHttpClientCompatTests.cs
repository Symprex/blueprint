using System.Net;
using System.Text;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Verifies the non-generic <see cref="BlueprintHttpClient"/> compatibility surface used by
/// legacy Signature 365 generated partial clients.
/// </summary>
public sealed class BlueprintHttpClientCompatTests : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _fixture;

    public BlueprintHttpClientCompatTests(SampleAppFixture fixture)
    {
        this._fixture = fixture;
    }

    [Fact]
    public async Task CallAsync_typed_without_options_dispatches_through_non_generic_client()
    {
        var handler = new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                [{"id":"t-1","name":"Template 1","htmlSource":"<p>Hi</p>","plainTextSource":"Hi"}]
                """,
                Encoding.UTF8,
                "application/json"),
        });

        var client = BlueprintHttpClient.From(this._fixture.CreateClient(handler));

        var result = await client.CallAsync<TemplateApiResource[]>(
            new SearchTemplatesQuery { Q = "compat", Page = 1, PageSize = 5 });

        result.Should().NotBeNull();
        result!.Should().ContainSingle();
        result[0].Id.Should().Be("t-1");
    }

    [Fact]
    public async Task CallAsync_typed_with_options_modifies_request_through_non_generic_client()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    [{"id":"t-2","name":"Template 2","htmlSource":"<p>Hi</p>","plainTextSource":"Hi"}]
                    """,
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        var client = BlueprintHttpClient.From(this._fixture.CreateClient(handler));

        await client.CallAsync<TemplateApiResource[]>(
            new SearchTemplatesQuery { Q = "compat", Page = 1, PageSize = 5 },
            new CallOptions
            {
                ModifyRequest = req => req.Headers.Add("X-Compat", "yes"),
            });

        captured.Should().NotBeNull();
        captured!.Headers.Contains("X-Compat").Should().BeTrue();
    }

    [Fact]
    public async Task SendVoidAsync_without_options_dispatches_through_non_generic_client()
    {
        var handler = new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = BlueprintHttpClient.From(this._fixture.CreateClient(handler));

        Func<Task> act = () => client.SendVoidAsync((Blueprint.Model.IOperation)new ArchiveTemplateCommand { Id = "t-9" });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendVoidAsync_with_options_modifies_request_through_non_generic_client()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var client = BlueprintHttpClient.From(this._fixture.CreateClient(handler));

        await client.SendVoidAsync(
            (Blueprint.Model.IOperation)new ArchiveTemplateCommand { Id = "t-10" },
            new CallOptions
            {
                ModifyRequest = req => req.Headers.Add("X-Compat", "void"),
            });

        captured.Should().NotBeNull();
        captured!.Headers.Contains("X-Compat").Should().BeTrue();
    }
}
