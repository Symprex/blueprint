using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Caches, per app-owned <see cref="JsonSerializerContext" />, the <see cref="JsonTypeInfo" /> each response type
/// resolves to, so the response writers look a type up at most once per context.
/// </summary>
/// <remarks>
/// <para>
/// The cache is static but must not decide how long a context lives. A process can build many apps - a test suite that
/// builds a host per test does - and a cache keyed by the context in an ordinary dictionary keeps every one of them,
/// with its options and metadata, until the process exits. A <see cref="ConditionalWeakTable{TKey, TValue}" /> holds
/// each context's entries only while something else holds the context, even though the cached metadata itself
/// references it.
/// </para>
/// <para>
/// Shared by <see cref="HttpOperationResultExecutor" /> and the generated pipelines' inline writer
/// (<see cref="Exec.PipelineHelpers.WriteOkJsonResponseAsync" />), which resolve the same pairs.
/// </para>
/// </remarks>
internal static class SerializerContextTypeInfoCache
{
    private static readonly ConditionalWeakTable<JsonSerializerContext, ConcurrentDictionary<Type, JsonTypeInfo?>> _typeInfos = new();

    /// <summary>
    /// Gets the metadata <paramref name="context" /> holds for <paramref name="type" />, or <see langword="null" /> when
    /// there is no context or it does not cover the type, in which case the caller falls back to runtime serialization.
    /// </summary>
    /// <param name="context">The app-owned serializer context, if the app has one.</param>
    /// <param name="type">The runtime type of the value to serialize.</param>
    /// <returns>The context's metadata for the type, or <see langword="null" />.</returns>
    public static JsonTypeInfo? Get(JsonSerializerContext? context, Type type)
    {
        if (context is null)
        {
            return null;
        }

        return _typeInfos
            .GetValue(context, static _ => new ConcurrentDictionary<Type, JsonTypeInfo?>())
            .GetOrAdd(type, static (t, c) => c.GetTypeInfo(t), context);
    }
}
