using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Blueprint.Http;
using Blueprint.HttpClient.Readers;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Proves a resource event's <c>metadata</c>, <c>secureData</c> and <c>changedValues</c> are read when the transport's
/// options resolve no metadata for <see cref="object"/>, as a source-generated context's do unless the user names it: a nested
/// object or array value needs none, for it is read as a <see cref="JsonElement"/> whatever the options resolve.
/// </summary>
public sealed class BlueprintHttpClientResourceEventContextOnlyTests
{
    private const string Body = """
        {"changeType":"Created","eventId":"template.created","resourceObject":"template","data":{"id":"t-1","name":"N"},
         "metadata":{"Nested":{"a":1},"List":[1,2]},
         "secureData":{"Nested":{"b":"x"},"List":["y"]},
         "changedValues":{"Nested":{"c":true},"List":[{"d":null}]}}
        """;

    [Fact]
    public async Task A_resource_event_result_reads_nested_dictionary_values_from_options_that_resolve_no_object_metadata()
    {
        var client = CreateClient();

        var result = await SendAsync(client, new ResourceEventResultResponseReader<TemplateApiResource>(client.ResolveTypeInfo<TemplateApiResource>()));

        AssertNested(result.Metadata, """{"a":1}""", "[1,2]");
        AssertNested(result.SecureData, """{"b":"x"}""", """["y"]""");
        AssertNested(result.ChangedValues!, """{"c":true}""", """[{"d":null}]""");
    }

    [Fact]
    public async Task A_legacy_resource_event_reads_nested_dictionary_values_from_options_that_resolve_no_object_metadata()
    {
        var client = CreateClient();

        var result = await SendAsync(client, new ResourceEventResponseReader<ResourceCreated<TemplateApiResource>, TemplateApiResource>(client.ResolveTypeInfo<TemplateApiResource>()));

        AssertNested(result.Metadata, """{"a":1}""", "[1,2]");
        AssertNested(result.SecureData, """{"b":"x"}""", """["y"]""");
        AssertNested(result.ChangedValues!, """{"c":true}""", """[{"d":null}]""");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task A_nested_object_read_as_a_JsonNode_keeps_the_options_property_name_case_sensitivity(bool caseInsensitive, bool found)
    {
        var client = CreateClient(options =>
        {
            options.UnknownTypeHandling = JsonUnknownTypeHandling.JsonNode;
            options.PropertyNameCaseInsensitive = caseInsensitive;
        });

        var result = await SendAsync(client, new ResourceEventResultResponseReader<TemplateApiResource>(client.ResolveTypeInfo<TemplateApiResource>()));

        var nested = result.Metadata!["Nested"].Should().BeAssignableTo<JsonObject>().Subject;
        (nested["A"] is not null).Should().Be(found, "the body names it \"a\"");
    }

    private static void AssertNested(IReadOnlyDictionary<string, object>? dictionary, string nestedObject, string list)
    {
        dictionary!["Nested"].Should().BeOfType<JsonElement>().Which.GetRawText().Should().Be(nestedObject);
        dictionary["List"].Should().BeOfType<JsonElement>().Which.GetRawText().Should().Be(list);
    }

    private static BlueprintHttpClient CreateClient(Action<JsonSerializerOptions>? configure = null)
    {
        // No resolver for object, and none that falls back to reflection, as a source-generated context's options are.
        var options = BlueprintJsonOptions.CreateDefaultOptions();
        options.TypeInfoResolver = new NoObjectResolver();
        configure?.Invoke(options);
        var handler = new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Body, new MediaTypeHeaderValue("application/json")),
        });

        return new BlueprintHttpClient(new System.Net.Http.HttpClient(handler) { BaseAddress = new Uri("https://localhost") }, options);
    }

    private static Task<TResult> SendAsync<TResult>(BlueprintHttpClient client, ResponseReader<TResult> reader)
    {
        return client.SendAsync(HttpMethod.Delete, "/templates/t-1", new ArchiveTemplateCommand { Id = "t-1" }, operationTypeInfo: null, reader, "ArchiveTemplateCommand", options: null);
    }

    /// <summary>
    /// Resolves the metadata of every type but <see cref="object"/>, as a source-generated context does for the types it
    /// covers: the request and the resource, with what they reach, and nothing the user did not name.
    /// </summary>
    private sealed class NoObjectResolver : IJsonTypeInfoResolver
    {
        private static readonly DefaultJsonTypeInfoResolver _inner = new();

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            return type == typeof(object) ? null : _inner.GetTypeInfo(type, options);
        }
    }
}
