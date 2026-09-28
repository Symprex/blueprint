using System.Collections.Immutable;
using System.Net;
using System.Text;
using Blueprint.Model;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Test-only operation with an array-typed query property. Declared top-level (not nested inside
/// the test class) and <c>partial</c> because <c>OperationDescriptorSourceGenerator</c> scans the
/// whole compilation for <see cref="IOperation"/> implementations and requires both; the generated
/// descriptor it produces is unused here — the tests below build their own
/// <see cref="ApiOperationDescriptor"/> to control which query-serialisation path is exercised.
/// </summary>
public sealed partial class ArrayQueryOperation : IOperation
{
    public string?[]? Ids { get; set; }
}

/// <summary>
/// Test-only operation with a <see langword="string"/> query property, used to prove a string
/// (itself <see cref="IEnumerable{T}"/> of <see langword="char"/>) is never exploded into repeated
/// query keys. See <see cref="ArrayQueryOperation"/> remarks for why it is top-level and partial.
/// </summary>
public sealed partial class StringQueryOperation : IOperation
{
    public string? Q { get; set; }
}

/// <summary>
/// Covers <c>BlueprintHttpClient.AppendQueryStringFromOwnership</c> and
/// <c>AppendQueryStringFallback</c>, which both serialise a non-string <see cref="IEnumerable{T}"/>
/// query property as one repeated <c>name=value</c> pair per element rather than a single
/// comma-joined value. Signature365 commit <c>19e2da72bf</c> fixed the same defect in
/// <c>ApiLinkGenerator</c>; these tests guard the equivalent Blueprint code path, which had no
/// coverage of its own even though the production logic is already correct.
/// <para>
/// Each descriptor is hand-built rather than reused from <c>SampleBlueprintApp</c> because the
/// source generator attaches <c>CompiledHttpRequestFeatureData</c> to every GET/DELETE operation
/// with a registered URL, and <c>BlueprintHttpClient.CreateRequest</c> prefers that compiled
/// delegate over both methods under test whenever it is present. Omitting
/// <c>CompiledHttpRequestFeatureData</c> here is what forces requests through the reflection-based
/// query builders instead.
/// </para>
/// </summary>
public sealed class QueryStringSerializationTests
{
    // ──────────────────────────────────────────────
    //  AppendQueryStringFromOwnership (:591-611)
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Ownership_array_property_emits_one_key_per_element_in_order()
    {
        var operation = new ArrayQueryOperation { Ids = ["a", "b", "c"] };

        var query = await SendWithOwnershipAsync(
            operation,
            new OperationPropertyBindingEntry(nameof(ArrayQueryOperation.Ids), PropertyBindingSource.Query, "ids", "string[]"));

        query.Should().Be("?ids=a&ids=b&ids=c");
    }

    [Fact]
    public async Task Ownership_string_property_is_not_exploded_into_repeated_keys()
    {
        // A string is IEnumerable<char> — it must be treated as a single scalar value, not a
        // collection of one-character elements.
        var operation = new StringQueryOperation { Q = "abc" };

        var query = await SendWithOwnershipAsync(
            operation,
            new OperationPropertyBindingEntry(nameof(StringQueryOperation.Q), PropertyBindingSource.Query, "q", "string"));

        query.Should().Be("?q=abc");
    }

    [Fact]
    public async Task Ownership_empty_collection_emits_no_query_string()
    {
        var operation = new ArrayQueryOperation { Ids = [] };

        var query = await SendWithOwnershipAsync(
            operation,
            new OperationPropertyBindingEntry(nameof(ArrayQueryOperation.Ids), PropertyBindingSource.Query, "ids", "string[]"));

        query.Should().BeEmpty();
    }

    [Fact]
    public async Task Ownership_null_elements_are_skipped()
    {
        var operation = new ArrayQueryOperation { Ids = ["a", null, "b"] };

        var query = await SendWithOwnershipAsync(
            operation,
            new OperationPropertyBindingEntry(nameof(ArrayQueryOperation.Ids), PropertyBindingSource.Query, "ids", "string[]"));

        query.Should().Be("?ids=a&ids=b");
    }

    [Fact]
    public async Task Ownership_elements_are_url_encoded_individually()
    {
        var operation = new ArrayQueryOperation { Ids = ["a b", "c&d"] };

        var query = await SendWithOwnershipAsync(
            operation,
            new OperationPropertyBindingEntry(nameof(ArrayQueryOperation.Ids), PropertyBindingSource.Query, "ids", "string[]"));

        query.Should().Be("?ids=a%20b&ids=c%26d");
    }

    // ──────────────────────────────────────────────
    //  AppendQueryStringFallback (:672-682)
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Fallback_array_property_emits_one_key_per_element_in_order()
    {
        var operation = new ArrayQueryOperation { Ids = ["a", "b", "c"] };

        var query = await SendWithFallbackAsync(operation);

        query.Should().Be("?Ids=a&Ids=b&Ids=c");
    }

    [Fact]
    public async Task Fallback_string_property_is_not_exploded_into_repeated_keys()
    {
        var operation = new StringQueryOperation { Q = "abc" };

        var query = await SendWithFallbackAsync(operation);

        query.Should().Be("?Q=abc");
    }

    [Fact]
    public async Task Fallback_empty_collection_emits_no_query_string()
    {
        var operation = new ArrayQueryOperation { Ids = [] };

        var query = await SendWithFallbackAsync(operation);

        query.Should().BeEmpty();
    }

    [Fact]
    public async Task Fallback_null_elements_are_skipped()
    {
        var operation = new ArrayQueryOperation { Ids = ["a", null, "b"] };

        var query = await SendWithFallbackAsync(operation);

        query.Should().Be("?Ids=a&Ids=b");
    }

    [Fact]
    public async Task Fallback_elements_are_url_encoded_individually()
    {
        var operation = new ArrayQueryOperation { Ids = ["a b", "c&d"] };

        var query = await SendWithFallbackAsync(operation);

        query.Should().Be("?Ids=a%20b&Ids=c%26d");
    }

    // ──────────────────────────────────────────────
    //  A DELETE: no query string, the operation as its body
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Fallback_delete_sends_no_query_string_and_the_operation_as_its_JSON_body()
    {
        // Legacy's client gave a GET alone a query string and sent every other method's operation as its JSON body
        // (Signature365 8b40ecc961, Common.Core/Api/BlueprintHttpClient.cs:79, :88-91).
        var (query, body) = await SendDeleteAsync(new ArrayQueryOperation { Ids = ["a", "b"] }, ownershipEntry: null);

        query.Should().BeEmpty();
        using var document = System.Text.Json.JsonDocument.Parse(body!);
        document.RootElement.GetProperty("ids").EnumerateArray().Select(static e => e.GetString()).Should().Equal("a", "b");
    }

    [Fact]
    public async Task Ownership_delete_sends_no_query_string_and_the_operation_as_its_JSON_body()
    {
        var (query, body) = await SendDeleteAsync(
            new ArrayQueryOperation { Ids = ["a", "b"] },
            new OperationPropertyBindingEntry(nameof(ArrayQueryOperation.Ids), PropertyBindingSource.Body, "ids", "string[]"));

        query.Should().BeEmpty();
        using var document = System.Text.Json.JsonDocument.Parse(body!);
        document.RootElement.GetProperty("ids").EnumerateArray().Select(static e => e.GetString()).Should().Equal("a", "b");
    }

    /// <summary>
    /// Sends <paramref name="operation"/> as a DELETE through a hand-built descriptor, with or without ownership data, and
    /// captures the request's query string and body.
    /// </summary>
    private static async Task<(string Query, string? Body)> SendDeleteAsync<TOperation>(
        TOperation operation, OperationPropertyBindingEntry? ownershipEntry)
        where TOperation : IOperation
    {
        var descriptor = BuildDescriptor<TOperation>("DELETE");

        if (ownershipEntry is not null)
        {
            descriptor.SetFeatureData(new OperationPropertyOwnershipFeatureData(ImmutableArray.Create(ownershipEntry)));
        }

        var app = new BlueprintApp();
        app.Register(descriptor);

        string? query = null;
        string? body = null;
        var handler = new FakeHttpHandler(req =>
        {
            query = req.RequestUri!.Query;
            body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        });

        var httpClient = new System.Net.Http.HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var client = new BlueprintHttpClient<BlueprintApp>(httpClient, app);

        await client.SendAsync<System.Text.Json.JsonElement>(operation);

        body.Should().NotBeNull("a DELETE carries the operation as its body");

        return (query!, body);
    }

    /// <summary>
    /// Sends <paramref name="operation"/> through a descriptor carrying
    /// <see cref="OperationPropertyOwnershipFeatureData"/> for the given <paramref name="queryEntry"/>,
    /// which routes <c>CreateRequest</c> through <c>AppendQueryStringFromOwnership</c>.
    /// </summary>
    private static async Task<string> SendWithOwnershipAsync<TOperation>(
        TOperation operation, OperationPropertyBindingEntry queryEntry)
        where TOperation : IOperation
    {
        var descriptor = BuildDescriptor<TOperation>();
        descriptor.SetFeatureData(new OperationPropertyOwnershipFeatureData(ImmutableArray.Create(queryEntry)));

        return await SendAndCaptureQueryAsync(descriptor, operation);
    }

    /// <summary>
    /// Sends <paramref name="operation"/> through a descriptor with no ownership feature data, which
    /// routes <c>CreateRequest</c> through the reflection-based <c>AppendQueryStringFallback</c>.
    /// </summary>
    private static async Task<string> SendWithFallbackAsync<TOperation>(TOperation operation)
        where TOperation : IOperation
    {
        var descriptor = BuildDescriptor<TOperation>();

        return await SendAndCaptureQueryAsync(descriptor, operation);
    }

    private static ApiOperationDescriptor BuildDescriptor<TOperation>(string httpMethod = "GET")
    {
        var descriptor = new ApiOperationDescriptor(typeof(TOperation), typeof(TOperation).Name, "test");
        descriptor.AddUrl(new ApiOperationUrl("/query-string-test"));
        descriptor.SetFeatureData(new OperationHttpMethodMetadataFeatureData(ImmutableArray.Create(httpMethod)));

        return descriptor;
    }

    private static async Task<string> SendAndCaptureQueryAsync<TOperation>(
        ApiOperationDescriptor descriptor, TOperation operation)
        where TOperation : IOperation
    {
        var app = new BlueprintApp();
        app.Register(descriptor);

        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        });

        var httpClient = new System.Net.Http.HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var client = new BlueprintHttpClient<BlueprintApp>(httpClient, app);

        await client.SendAsync<System.Text.Json.JsonElement>(operation);

        captured.Should().NotBeNull();

        return captured!.RequestUri!.Query;
    }

    // ──────────────────────────────────────────────
    //  R111: {Name(format)} route placeholders (ExtractPlaceholderNames :717, SubstitutePlaceholders :820)
    // ──────────────────────────────────────────────

    [Fact]
    public async Task A_route_placeholder_with_a_legacy_format_specifier_is_bound_from_the_route_and_excluded_from_the_query_string()
    {
        // {Name(format)} is route-owned via RoutePlaceholderSyntax (R97) — the bound name is whatever is before a `:` or
        // `(`. These two reflection-based parsers only stripped `:`, so "Q(f)" was taken whole as the property name: the
        // URL kept the literal placeholder unsubstituted (SubstitutePlaceholders returned null for the whole template,
        // so ResolveUrl fell through to no URL at all), and "Q" was not recognised as route-owned by
        // AppendQueryStringFallback, so it would also have been added to the query string.
        var descriptor = new ApiOperationDescriptor(typeof(StringQueryOperation), nameof(StringQueryOperation), "test");
        descriptor.AddUrl(new ApiOperationUrl("/x/{Q(f)}"));
        descriptor.SetFeatureData(new OperationHttpMethodMetadataFeatureData(ImmutableArray.Create("GET")));

        var app = new BlueprintApp();
        app.Register(descriptor);

        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        });

        var httpClient = new System.Net.Http.HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var client = new BlueprintHttpClient<BlueprintApp>(httpClient, app);

        await client.SendAsync<System.Text.Json.JsonElement>(new StringQueryOperation { Q = "abc" });

        captured.Should().NotBeNull();
        captured!.RequestUri!.AbsolutePath.Should().Be("/x/abc");
        captured.RequestUri.Query.Should().NotContain("Q=");
    }
}
