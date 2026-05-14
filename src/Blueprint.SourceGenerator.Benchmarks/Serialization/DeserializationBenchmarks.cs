using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

namespace Blueprint.SourceGenerator.Benchmarks.Serialization;

/// <summary>
/// Measures request body deserialization cost — the Utf8JsonReader-based property population
/// that runs for every POST/PUT/PATCH request. Compares the source-generated property-by-property
/// reader (matching generated <c>PopulateFromJsonBody</c>) against full-object deserialization.
/// </summary>
[Config(typeof(InProcessBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class DeserializationBenchmarks
{
    private byte[] _smallBodyBytes = null!;
    private byte[] _mediumBodyBytes = null!;
    private byte[] _commandBodyBytes = null!;

    /// <summary>
    /// Pre-serializes representative JSON payloads.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        this._smallBodyBytes = JsonSerializer.SerializeToUtf8Bytes(
            new { name = "Alice", email = "alice@example.com" },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        this._mediumBodyBytes = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                name = "Template Beta with a longer name",
                description = "A medium-length description for the template that includes enough detail " +
                              "to exercise the parser on a realistic payload size.",
                tags = new[] { "production", "email", "signature", "template" },
                isActive = true,
                priority = 5,
            },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        this._commandBodyBytes = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                name = "New Template",
                description = "Full command body with multiple fields that exercises the " +
                              "generated property-by-property Utf8JsonReader path.",
                category = "marketing",
                isPublished = false,
                maxRecipients = 1000,
                expiresAt = DateTime.UtcNow.AddDays(90).ToString("O"),
                metadata = new { source = "api", version = "2.0" },
            },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    /// <summary>
    /// Property-by-property Utf8JsonReader — mirrors the generated PopulateFromJsonBody pattern.
    /// This is the production path: the source generator emits a static method that reads each
    /// property with u8 string literal matching and assigns directly.
    /// </summary>
    /// <returns>The populated command instance.</returns>
    [Benchmark(Baseline = true)]
    public DeserBenchmarkCommand SmallBody_PropertyByProperty()
    {
        var op = new DeserBenchmarkCommand();
        PopulateSmallBody(this._smallBodyBytes, op);
        return op;
    }

    /// <summary>
    /// Full JsonSerializer.Deserialize — the reflection-based alternative.
    /// </summary>
    /// <returns>The deserialized command instance.</returns>
    [Benchmark]
    public DeserBenchmarkCommand SmallBody_FullDeserialize()
    {
        return JsonSerializer.Deserialize<DeserBenchmarkCommand>(this._smallBodyBytes, _camelCaseOptions)!;
    }

    /// <summary>
    /// Full JsonSerializer.Deserialize with source-gen context.
    /// </summary>
    /// <returns>The deserialized command instance.</returns>
    [Benchmark]
    public DeserBenchmarkCommand SmallBody_SourceGenDeserialize()
    {
        return JsonSerializer.Deserialize(this._smallBodyBytes, DeserBenchmarkJsonContext.Default.DeserBenchmarkCommand)!;
    }

    /// <summary>
    /// Medium body (5 properties + array) using property-by-property reader.
    /// </summary>
    /// <returns>The populated command instance.</returns>
    [Benchmark]
    public DeserMediumCommand MediumBody_PropertyByProperty()
    {
        var op = new DeserMediumCommand();
        PopulateMediumBody(this._mediumBodyBytes, op);
        return op;
    }

    /// <summary>
    /// Medium body using full deserialization with source-gen context.
    /// </summary>
    /// <returns>The deserialized command instance.</returns>
    [Benchmark]
    public DeserMediumCommand MediumBody_SourceGenDeserialize()
    {
        return JsonSerializer.Deserialize(this._mediumBodyBytes, DeserBenchmarkJsonContext.Default.DeserMediumCommand)!;
    }

    // ─── Generated-style property population (mirrors source-gen output) ──────

    private static void PopulateSmallBody(ReadOnlySpan<byte> bodyBytes, DeserBenchmarkCommand op)
    {
        var reader = new Utf8JsonReader(bodyBytes);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("name"u8))
            {
                reader.Read();
                op.Name = reader.GetString()!;
            }
            else if (reader.ValueTextEquals("email"u8))
            {
                reader.Read();
                op.Email = reader.GetString()!;
            }
            else
            {
                reader.Skip();
            }
        }
    }

    private static void PopulateMediumBody(ReadOnlySpan<byte> bodyBytes, DeserMediumCommand op)
    {
        var reader = new Utf8JsonReader(bodyBytes);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("name"u8))
            {
                reader.Read();
                op.Name = reader.GetString()!;
            }
            else if (reader.ValueTextEquals("description"u8))
            {
                reader.Read();
                op.Description = reader.GetString();
            }
            else if (reader.ValueTextEquals("tags"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    var list = new System.Collections.Generic.List<string>();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        list.Add(reader.GetString()!);
                    }

                    op.Tags = list.ToArray();
                }
            }
            else if (reader.ValueTextEquals("isActive"u8))
            {
                reader.Read();
                op.IsActive = reader.GetBoolean();
            }
            else if (reader.ValueTextEquals("priority"u8))
            {
                reader.Read();
                op.Priority = reader.GetInt32();
            }
            else
            {
                reader.Skip();
            }
        }
    }

    private static readonly JsonSerializerOptions _camelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

// ─── Benchmark Models ──────────────────────────────────────

/// <summary>
/// Simple 2-property command — represents the smallest POST body.
/// </summary>
public sealed class DeserBenchmarkCommand
{
    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the email.</summary>
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// 5-property command with array — represents a medium POST body.
/// </summary>
public sealed class DeserMediumCommand
{
    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the tags.</summary>
    public string[]? Tags { get; set; }

    /// <summary>Gets or sets whether active.</summary>
    public bool IsActive { get; set; }

    /// <summary>Gets or sets the priority.</summary>
    public int Priority { get; set; }
}

/// <summary>
/// Source-gen serializer context for benchmark deserialization types.
/// </summary>
[JsonSerializable(typeof(DeserBenchmarkCommand))]
[JsonSerializable(typeof(DeserMediumCommand))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class DeserBenchmarkJsonContext : JsonSerializerContext;
