using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Proves a typed client declared with a serializer context reads and writes with that context's metadata, with the
/// transport's own converters still applied.
/// </summary>
public sealed class BlueprintClientSerializerContextTests
{
    [Fact]
    public async Task The_request_body_is_written_with_the_context_s_metadata_and_the_options_converters()
    {
        string? body = null;
        var api = CreateApi(
            async request =>
            {
                body = await request.Content!.ReadAsStringAsync();
                return Json("""{"totalValue":"dec:1.5","quotedFor":"Ada"}""");
            });

        await api.QuotePriceAsync(new QuotePriceCommand { Amount = 12.5m, CustomerName = "Ada" });

        // "amountValue" is the context's name for it, "dec:" is the converter on the options, which the context's metadata
        // still honours, and "customerName" is the options' naming policy, which the context leaves to them.
        body.Should().Be("""{"amountValue":"dec:12.5","customerName":"Ada"}""");
    }

    [Fact]
    public async Task The_response_is_read_with_the_context_s_metadata_and_the_options_converters()
    {
        var api = CreateApi(_ => Task.FromResult(Json("""{"totalValue":"dec:99.25","quotedFor":"Ada"}""")));

        var quote = await api.QuotePriceAsync(new QuotePriceCommand { Amount = 1m });

        quote.Total.Should().Be(99.25m);
        quote.QuotedFor.Should().Be("Ada");
    }

    [Fact]
    public async Task A_clone_made_by_WithCredential_still_uses_the_context()
    {
        string? body = null;
        var api = CreateApi(
            async request =>
            {
                body = await request.Content!.ReadAsStringAsync();
                return Json("""{"totalValue":"dec:3","quotedFor":"Ada"}""");
            });

        var quote = await api.WithCredential(new BearerTokenCredential("t")).QuotePriceAsync(new QuotePriceCommand { Amount = 2m });

        body.Should().StartWith("""{"amountValue":"dec:2",""");
        quote.Total.Should().Be(3m);
    }

    [Fact]
    public void Options_derived_from_a_context_are_reused_for_the_same_read_only_options_and_context()
    {
        var options = CreateOptions();
        options.MakeReadOnly(populateMissingResolver: true);
        var first = new BlueprintHttpClient(new System.Net.Http.HttpClient(), options).WithSerializerContext(PricingSerializerContext.Default);
        var second = new BlueprintHttpClient(new System.Net.Http.HttpClient(), options).WithSerializerContext(PricingSerializerContext.Default);

        // A type's metadata is held by the options that resolved it, so the same instance means the same options.
        second.ResolveTypeInfo<QuotePriceCommand>().Should().BeSameAs(first.ResolveTypeInfo<QuotePriceCommand>());
    }

    [Fact]
    public void Options_still_open_to_change_are_derived_afresh_so_that_a_change_between_two_calls_is_seen()
    {
        var options = CreateOptions();
        var first = new BlueprintHttpClient(new System.Net.Http.HttpClient(), options).WithSerializerContext(PricingSerializerContext.Default);
        var before = first.JsonOptions.Converters.OfType<SecondDecimalConverter>().Any();

        options.Converters.Add(new SecondDecimalConverter());
        var second = new BlueprintHttpClient(new System.Net.Http.HttpClient(), options).WithSerializerContext(PricingSerializerContext.Default);

        before.Should().BeFalse();
        second.JsonOptions.Should().NotBeSameAs(first.JsonOptions);
        second.JsonOptions.Converters.OfType<SecondDecimalConverter>().Should().ContainSingle("the second client is derived from the options as they are now");
        first.JsonOptions.Converters.OfType<SecondDecimalConverter>().Should().BeEmpty("the first client keeps the options it was made with");
    }

    [Fact]
    public void Options_derived_from_a_context_are_not_shared_with_other_options()
    {
        var first = new BlueprintHttpClient(new System.Net.Http.HttpClient(), CreateOptions()).WithSerializerContext(PricingSerializerContext.Default);
        var second = new BlueprintHttpClient(new System.Net.Http.HttpClient(), CreateOptions()).WithSerializerContext(PricingSerializerContext.Default);

        second.ResolveTypeInfo<QuotePriceCommand>().Should().NotBeSameAs(first.ResolveTypeInfo<QuotePriceCommand>());
    }

    [Fact]
    public void Options_derived_from_a_context_leave_the_original_options_unchanged()
    {
        var options = CreateOptions();

        _ = new BlueprintHttpClient(new System.Net.Http.HttpClient(), options).WithSerializerContext(PricingSerializerContext.Default);

        options.IsReadOnly.Should().BeFalse("the options the caller gave are copied, never locked or changed");
        options.TypeInfoResolverChain.Should().NotContain(PricingSerializerContext.Default);
    }

    [Fact]
    public async Task Options_that_already_resolve_with_the_context_are_used_as_they_are()
    {
        // The context is the options' own resolver, so putting it in front of itself would throw.
        string? body = null;
        var options = CreateOptions();
        options.TypeInfoResolver = PricingSerializerContext.Default;
        var handler = new AsyncHandler(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            return Json("""{"totalValue":"dec:4","quotedFor":"Ada"}""");
        });
        var api = new PricingApi(new BlueprintHttpClient(new System.Net.Http.HttpClient(handler) { BaseAddress = new Uri("https://localhost") }, options));

        var quote = await api.QuotePriceAsync(new QuotePriceCommand { Amount = 2m });

        body.Should().StartWith("""{"amountValue":"dec:2",""");
        quote.Total.Should().Be(4m);
    }

    [Fact]
    public void Options_that_hold_the_context_in_their_resolver_chain_are_used_as_they_are()
    {
        var options = CreateOptions();
        options.TypeInfoResolverChain.Add(PricingSerializerContext.Default);
        options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());

        var client = new BlueprintHttpClient(new System.Net.Http.HttpClient(), options).WithSerializerContext(PricingSerializerContext.Default);

        client.ResolveTypeInfo<QuotePriceCommand>().Type.Should().Be<QuotePriceCommand>();
    }

    [Fact]
    public async Task A_client_with_a_result_that_cannot_be_configured_is_made_and_only_that_call_fails()
    {
        var handler = new AsyncHandler(request => Task.FromResult(Json(request.RequestUri!.AbsolutePath == "/faulty" ? """{"value":"x"}""" : "[]")));
        var api = new FaultyResultApi(new BlueprintHttpClient(new System.Net.Http.HttpClient(handler) { BaseAddress = new Uri("https://localhost") }, CreateOptions()));

        var copies = await api.FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });
        var act = () => api.GetUnconfigurableAsync(new GetUnconfigurableQuery());

        copies.Should().BeEmpty();
        await act.Should().ThrowAsync<InvalidOperationException>("the options' own metadata is asked for at the call, which fails for this type only");
    }

    [Fact]
    public void A_type_the_context_does_not_cover_is_still_resolved_from_the_options()
    {
        var client = new BlueprintHttpClient(new System.Net.Http.HttpClient(), CreateOptions()).WithSerializerContext(PricingSerializerContext.Default);

        client.ResolveTypeInfo<UncoveredBody>().Type.Should().Be<UncoveredBody>();
    }

    private static PricingApi CreateApi(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var handler = new AsyncHandler(respond);

        return new PricingApi(new BlueprintHttpClient(new System.Net.Http.HttpClient(handler) { BaseAddress = new Uri("https://localhost") }, CreateOptions()));
    }

    private static HttpResponseMessage Json(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, new MediaTypeHeaderValue("application/json")) };
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new PrefixedDecimalConverter());

        return options;
    }

    private sealed class UncoveredBody
    {
        public string? Name { get; set; }
    }

    /// <summary>
    /// Writes a decimal as <c>dec:{value}</c> text, a wire format only a converter on the options can have given it.
    /// </summary>
    private sealed class PrefixedDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return decimal.Parse(reader.GetString()!["dec:".Length..], CultureInfo.InvariantCulture);
        }

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        {
            writer.WriteStringValue("dec:" + value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private sealed class SecondDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.GetDecimal();
        }

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }

    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return respond(request);
        }
    }
}
