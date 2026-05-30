using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

namespace Blueprint.SourceGenerator.Benchmarks.Serialization;

/// <summary>
/// Measures response serialization cost — the JSON writing path that runs on every HTTP
/// response. Exercises both the app-owned <see cref="JsonSerializerContext"/> fast-path
/// and the untyped fallback, plus the <c>ResourceEvent</c> envelope writer.
/// </summary>
[Config(typeof(InProcessBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class SerializationBenchmarks
{
    private readonly MemoryStream _outputStream = new(8192);

    private readonly SerBenchmarkResource _smallResource;
    private readonly SerBenchmarkResource _mediumResource;
    private readonly SerBenchmarkPagedResource _pagedResource;

    /// <summary>
    /// Initializes benchmark payloads matching typical S365 response sizes.
    /// </summary>
    public SerializationBenchmarks()
    {
        this._smallResource = new SerBenchmarkResource
        {
            Id = "res-001",
            Name = "Template Alpha",
            Description = "A short description",
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
        };

        this._mediumResource = new SerBenchmarkResource
        {
            Id = "res-002",
            Name = "Template Beta with a longer name for realism",
            Description = "A medium-length description that represents a typical API response body. " +
                          "This includes details about the resource, its purpose, and relevant metadata " +
                          "that a frontend client would render in a detail view.",
            CreatedAt = DateTime.UtcNow.AddDays(-30),
            IsActive = true,
            Tags = ["production", "template", "email", "signature"],
            UpdatedBy = "user-42@example.com",
        };

        var items = new SerBenchmarkResource[25];
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = new SerBenchmarkResource
            {
                Id = $"res-{i:D3}",
                Name = $"Template {i}",
                Description = $"Description for template {i}",
                CreatedAt = DateTime.UtcNow.AddDays(-i),
                IsActive = i % 3 != 0,
            };
        }

        this._pagedResource = new SerBenchmarkPagedResource { Items = items, TotalCount = 250 };
    }

    /// <summary>
    /// Serialize a small resource (5 properties) using app-owned JsonSerializerContext.
    /// This is the happy-path for S365: every type registered in the context.
    /// </summary>
    /// <returns>A task representing the asynchronous serialization operation.</returns>
    [Benchmark(Baseline = true)]
    public async Task SmallResource_SourceGenContext()
    {
        this._outputStream.Position = 0;
        this._outputStream.SetLength(0);
        await JsonSerializer.SerializeAsync(this._outputStream, this._smallResource, SerBenchmarkJsonContext.Default.SerBenchmarkResource).ConfigureAwait(false);
    }

    /// <summary>
    /// Serialize a medium resource (~300 bytes) using app-owned JsonSerializerContext.
    /// </summary>
    /// <returns>A task representing the asynchronous serialization operation.</returns>
    [Benchmark]
    public async Task MediumResource_SourceGenContext()
    {
        this._outputStream.Position = 0;
        this._outputStream.SetLength(0);
        await JsonSerializer.SerializeAsync(this._outputStream, this._mediumResource, SerBenchmarkJsonContext.Default.SerBenchmarkResource).ConfigureAwait(false);
    }

    /// <summary>
    /// Serialize a paged list of 25 resources — represents a typical list query response.
    /// </summary>
    /// <returns>A task representing the asynchronous serialization operation.</returns>
    [Benchmark]
    public async Task PagedList25Items_SourceGenContext()
    {
        this._outputStream.Position = 0;
        this._outputStream.SetLength(0);
        await JsonSerializer.SerializeAsync(this._outputStream, this._pagedResource, SerBenchmarkJsonContext.Default.SerBenchmarkPagedResource).ConfigureAwait(false);
    }

    /// <summary>
    /// Serialize a small resource using runtime reflection (no JsonSerializerContext).
    /// Measures the cost penalty of NOT registering a type in the context.
    /// </summary>
    /// <returns>A task representing the asynchronous serialization operation.</returns>
    [Benchmark]
    public async Task SmallResource_ReflectionFallback()
    {
        this._outputStream.Position = 0;
        this._outputStream.SetLength(0);
        await JsonSerializer.SerializeAsync<object>(this._outputStream, this._smallResource, _reflectionOptions).ConfigureAwait(false);
    }

    private static readonly JsonEncodedText _propObject = JsonEncodedText.Encode("$object");
    private static readonly JsonEncodedText _propEventId = JsonEncodedText.Encode("eventId");
    private static readonly JsonEncodedText _propChangeType = JsonEncodedText.Encode("changeType");
    private static readonly JsonEncodedText _propCreated = JsonEncodedText.Encode("created");
    private static readonly JsonEncodedText _propResourceObject = JsonEncodedText.Encode("resourceObject");
    private static readonly JsonEncodedText _propData = JsonEncodedText.Encode("data");
    private static readonly JsonEncodedText _valEvent = JsonEncodedText.Encode("ResourceEvent");
    private static readonly JsonEncodedText _valCreated = JsonEncodedText.Encode("Created");
    private static readonly JsonEncodedText _valResourceType = JsonEncodedText.Encode("SerBenchmarkResource");

    [ThreadStatic]
    private static System.Buffers.ArrayBufferWriter<byte>? _benchBuffer;

    [ThreadStatic]
    private static Utf8JsonWriter? _benchWriter;

    /// <summary>
    /// Serialize a ResourceEvent envelope using thread-static Utf8JsonWriter reuse,
    /// matching the production path in HttpOperationResultExecutor.
    /// </summary>
    [Benchmark]
    public void ResourceEventEnvelope_Utf8Writer()
    {
        var buffer = _benchBuffer ??= new System.Buffers.ArrayBufferWriter<byte>(512);
        buffer.Clear();

        var writer = _benchWriter;
        if (writer is null)
        {
            writer = new Utf8JsonWriter(buffer);
            _benchWriter = writer;
        }
        else
        {
            writer.Reset(buffer);
        }

        writer.WriteStartObject();
        writer.WriteString(_propObject, _valEvent);
        writer.WriteString(_propEventId, Guid.NewGuid().ToString("N"));
        writer.WriteString(_propChangeType, _valCreated);
        writer.WriteString(_propCreated, DateTimeOffset.UtcNow);
        writer.WriteString(_propResourceObject, _valResourceType);
        writer.WritePropertyName(_propData);
        JsonSerializer.Serialize(writer, this._smallResource, SerBenchmarkJsonContext.Default.SerBenchmarkResource);
        writer.WriteEndObject();
        writer.Flush();
    }

    private static readonly JsonSerializerOptions _reflectionOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

// ─── Benchmark Models ──────────────────────────────────────

/// <summary>
/// Represents a typical S365 API resource response payload.
/// </summary>
public sealed class SerBenchmarkResource
{
    /// <summary>Gets or sets the resource identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the resource name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the resource description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets whether the resource is active.</summary>
    public bool IsActive { get; set; }

    /// <summary>Gets or sets optional tags.</summary>
    public string[]? Tags { get; set; }

    /// <summary>Gets or sets the user who last updated the resource.</summary>
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Represents a paged list response matching S365's ListApiResource pattern.
/// </summary>
public sealed class SerBenchmarkPagedResource
{
    /// <summary>Gets or sets the items in this page.</summary>
    public SerBenchmarkResource[] Items { get; set; } = [];

    /// <summary>Gets or sets the total count across all pages.</summary>
    public int TotalCount { get; set; }
}

/// <summary>
/// App-owned serializer context for benchmark resources — mirrors
/// what S365 will configure per-service via [JsonSerializable].
/// </summary>
[JsonSerializable(typeof(SerBenchmarkResource))]
[JsonSerializable(typeof(SerBenchmarkPagedResource))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class SerBenchmarkJsonContext : JsonSerializerContext;
