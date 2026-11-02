using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.HttpClient.Tests.Clients;

/// <summary>
/// A typed client whose calls put <see cref="RecordingJsonContext"/> first in the resolver chain.
/// </summary>
[BlueprintClient(SerializerContext = typeof(RecordingJsonContext))]
#pragma warning disable BPCL007 // The recording context declares no types: it records what is asked of it and supplies nothing.
[BlueprintClientInclude(typeof(CreateTemplateCommand))]
#pragma warning restore BPCL007
public partial class RecordingContextApi;

/// <summary>
/// A serializer context that supplies no metadata and records each type it is asked for, so a test can see that a client's
/// calls consult it first, and how often.
/// </summary>
public sealed class RecordingJsonContext : JsonSerializerContext, IJsonTypeInfoResolver
{
    private RecordingJsonContext()
        : base(null)
    {
    }

    /// <summary>
    /// Gets the instance a generated client uses.
    /// </summary>
    public static RecordingJsonContext Default { get; } = new();

    /// <summary>
    /// Gets the types asked for, with how many times each was.
    /// </summary>
    public ConcurrentDictionary<Type, int> Requested { get; } = new();

    /// <inheritdoc />
    protected override JsonSerializerOptions? GeneratedSerializerOptions => null;

    /// <inheritdoc />
    public override JsonTypeInfo? GetTypeInfo(Type type)
    {
        return null;
    }

    /// <inheritdoc />
    JsonTypeInfo? IJsonTypeInfoResolver.GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        this.Requested.AddOrUpdate(type, 1, static (_, count) => count + 1);

        return null;
    }
}
