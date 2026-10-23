using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Blueprint.Http;
using Blueprint.Model;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Proves that <see cref="ListApiResource{T}"/>, <see cref="PagedApiResource{T}"/> and
/// <see cref="ContinuationPagedApiResource{T}"/> round-trip through System.Text.Json with every data property intact.
/// </summary>
/// <remarks>
/// Each type once declared a parameterless <c>[JsonConstructor]</c> next to get-only data properties, so System.Text.Json
/// built the resource through it and had nothing it could assign: a service-to-service call that returned one came back
/// empty. Legacy Blueprint had no parameterless constructor, so System.Text.Json bound the parameterised one. The values
/// are asserted against literals, not against what the serialiser wrote, so a shared bug cannot make them agree.
/// </remarks>
public sealed partial class ListResourceDeserialisationTests
{
    public static TheoryData<JsonMode> Modes => new() { JsonMode.Reflection, JsonMode.SourceGenerated, JsonMode.BlueprintHttpClient };

    public enum JsonMode
    {
        Reflection,
        SourceGenerated,
        BlueprintHttpClient,
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void A_list_resource_round_trips_its_values_and_total(JsonMode mode)
    {
        var list = new ListApiResource<Country>([new Country { Code = "GB" }, new Country { Code = "FR" }]);

        var result = RoundTrip(list, mode);

        result.Values.Select(static c => c.Code).Should().Equal("GB", "FR");
        result.Total.Should().Be(2);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void A_list_resource_reads_the_wire_shape_a_server_writes(JsonMode mode)
    {
        var result = Deserialize<ListApiResource<Country>>("""{"values":[{"code":"GB"},{"code":"FR"}],"total":2}""", mode);

        result.Values.Select(static c => c.Code).Should().Equal("GB", "FR");
        result.Total.Should().Be(2);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void A_paged_resource_round_trips_its_values_total_page_size_and_current_page(JsonMode mode)
    {
        var paged = new PagedApiResource<Country>([new Country { Code = "GB" }, new Country { Code = "FR" }], 42, 2, 3);

        var result = RoundTrip(paged, mode);

        result.Values.Select(static c => c.Code).Should().Equal("GB", "FR");
        result.Total.Should().Be(42);
        result.PageSize.Should().Be(2);
        result.CurrentPage.Should().Be(3);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void A_continuation_resource_with_page_numbers_round_trips_every_property(JsonMode mode)
    {
        var continuation = new ContinuationPagedApiResource<Country>([new Country { Code = "GB" }, new Country { Code = "FR" }], true, 2, 3);

        var result = RoundTrip(continuation, mode);

        result.Values.Select(static c => c.Code).Should().Equal("GB", "FR");
        result.HasMore.Should().BeTrue();
        result.PageSize.Should().Be(2);
        result.CurrentPage.Should().Be(3);
        result.ContinuationToken.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void A_continuation_resource_with_a_token_round_trips_every_property(JsonMode mode)
    {
        var continuation = new ContinuationPagedApiResource<Country>([new Country { Code = "GB" }, new Country { Code = "FR" }], "next-page-token");

        var result = RoundTrip(continuation, mode);

        result.Values.Select(static c => c.Code).Should().Equal("GB", "FR");
        result.ContinuationToken.Should().Be("next-page-token");
        result.HasMore.Should().BeTrue();
        result.PageSize.Should().Be(0);
        result.CurrentPage.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void A_continuation_resource_on_its_last_page_round_trips_without_more(JsonMode mode)
    {
        var continuation = new ContinuationPagedApiResource<Country>([new Country { Code = "GB" }], false, 5, 4);

        var result = RoundTrip(continuation, mode);

        result.Values.Select(static c => c.Code).Should().Equal("GB");
        result.HasMore.Should().BeFalse();
        result.PageSize.Should().Be(5);
        result.CurrentPage.Should().Be(4);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void A_list_subclass_with_its_own_single_constructor_round_trips(JsonMode mode)
    {
        var list = new TenantList<Country>("tenant-1", [new Country { Code = "GB" }, new Country { Code = "FR" }]);

        var result = RoundTrip(list, mode);

        result.TenantId.Should().Be("tenant-1");
        result.Values.Select(static c => c.Code).Should().Equal("GB", "FR");
        result.Total.Should().Be(2);
    }

    private static T RoundTrip<T>(T value, JsonMode mode)
    {
        var json = mode switch
        {
            JsonMode.SourceGenerated => JsonSerializer.Serialize(value, TypeInfo<T>()),
            _ => JsonSerializer.Serialize(value, Options(mode)),
        };

        return Deserialize<T>(json, mode);
    }

    private static T Deserialize<T>(string json, JsonMode mode)
    {
        var result = mode switch
        {
            JsonMode.SourceGenerated => JsonSerializer.Deserialize(json, TypeInfo<T>()),
            _ => JsonSerializer.Deserialize<T>(json, Options(mode)),
        };

        return result!;
    }

    private static JsonSerializerOptions Options(JsonMode mode)
    {
        // The HTTP client reads responses with the app's BlueprintJsonOptions, which default to this.
        return mode == JsonMode.BlueprintHttpClient
            ? BlueprintJsonOptions.CreateDefaultOptions()
            : new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    private static JsonTypeInfo<T> TypeInfo<T>()
    {
        return (JsonTypeInfo<T>)ListResourceJsonContext.Default.GetTypeInfo(typeof(T))!;
    }

    public sealed class Country
    {
        public string Code { get; set; } = string.Empty;
    }

    public sealed class TenantList<T> : ListApiResource<T>
    {
        public TenantList(string tenantId, IEnumerable<T> values)
            : base(values)
        {
            this.TenantId = tenantId;
        }

        public string TenantId { get; }
    }

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(ListApiResource<Country>))]
    [JsonSerializable(typeof(PagedApiResource<Country>))]
    [JsonSerializable(typeof(ContinuationPagedApiResource<Country>))]
    [JsonSerializable(typeof(TenantList<Country>))]
    internal sealed partial class ListResourceJsonContext : JsonSerializerContext;
}
