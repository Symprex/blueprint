using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Blueprint.Http;
using Blueprint.Model;
using Microsoft.Extensions.Logging.Abstractions;
using MvcProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

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
/// <para>
/// A declared root is resolved strictly - by the context alone, so a nested type the context does not register throws -
/// except Blueprint's own open-by-design roots. <see cref="MvcProblemDetails.Extensions" />, and a
/// <see cref="ResourceEvent" />'s <c>Data</c>, <c>Metadata</c>, <c>SecureData</c> and <c>ChangedValues</c>, hold
/// whatever a caller puts in them, which no context can register, so such a root is resolved through
/// <see cref="GetFallbackOptions" />: the context's metadata first, and reflection only for what it does not cover. The
/// resource event envelope writer already wrote those bags that way; this covers an event returned as itself.
/// </para>
/// <para>
/// An app that sets <see cref="BlueprintApp.AllowRuntimeTypeFallback" /> has every declared root resolved that way, with
/// a reflection resolver of its own that logs a warning the first time it is asked for each type, so a runtime type the
/// context does not register is written instead of failing and the gap is still visible.
/// </para>
/// </remarks>
internal static class SerializerContextTypeInfoCache
{
    private const string RuntimeTypeFallbackLoggerCategory = "Blueprint.Runtime.Http.RuntimeTypeFallback";

    private static readonly Action<ILogger, string, string, Exception?> _logRuntimeTypeFallback =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(34, "RuntimeTypeFallback"),
            "{SerializerContext} does not register {Type}, so it was written with reflection metadata because the app allows the runtime type fallback. Register it with [JsonSerializable] to write it with the context's own metadata");

    private static readonly ConditionalWeakTable<JsonSerializerContext, ContextEntry> _entries = new();

    // Keyed weakly for the same reason: the key is an app's context options, and this static must not keep a dropped
    // app's options alive.
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> _fallbackOptions = new();

    // Shared singleton avoids allocating a new resolver each time the fallback cache is populated.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Only the fallback options use it, for types the app-owned serializer context does not cover.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "DefaultJsonTypeInfoResolver requires dynamic code. This is only used as a fallback when the app-owned serializer context does not cover a type.")]
    private static readonly DefaultJsonTypeInfoResolver _sharedDefaultTypeInfoResolver = new();

    /// <summary>
    /// Gets the metadata <paramref name="type" /> is written with when <paramref name="app" />'s serializer context
    /// declares it, or <see langword="null" /> when the app has no context or it does not cover the type, in which case
    /// the caller falls back to runtime serialization.
    /// </summary>
    /// <param name="app">The app whose serializer context and runtime type fallback apply.</param>
    /// <param name="type">The runtime type of the value to serialize.</param>
    /// <param name="services">The request's services, which the runtime type fallback's warning is logged through.</param>
    /// <returns>The metadata to write the type with, or <see langword="null" />.</returns>
    public static JsonTypeInfo? Get(BlueprintApp app, Type type, IServiceProvider? services)
    {
        if (app.AppOwnedSerializerContext is not { } context)
        {
            return null;
        }

        var entry = _entries.GetValue(context, static _ => new ContextEntry());

        // Partitioned by policy: two apps may share one context instance while choosing differently, and neither may get
        // the other's metadata - strict metadata would fail the opted-in app, fallback metadata would let the strict one
        // write an unregistered object through reflection.
        var typeInfos = app.AllowRuntimeTypeFallback ? entry.FallbackTypeInfos : entry.StrictTypeInfos;

        if (typeInfos.TryGetValue(type, out var typeInfo))
        {
            return typeInfo;
        }

        var runtimeFallbackOptions = app.AllowRuntimeTypeFallback ? entry.GetRuntimeFallbackOptions(context, services) : null;

        return typeInfos.GetOrAdd(type, Resolve(context, type, runtimeFallbackOptions));
    }

    /// <summary>
    /// Gets a copy of <paramref name="serializerOptions" /> (made once per options instance) that resolves through their
    /// own resolver first and reflection second, so an app's converters and naming apply to a type its context does not
    /// cover.
    /// </summary>
    /// <param name="serializerOptions">The app-owned context's options, or empty options when the app has no context.</param>
    /// <returns>The fallback options.</returns>
    public static JsonSerializerOptions GetFallbackOptions(JsonSerializerOptions serializerOptions)
    {
        return _fallbackOptions.GetValue(serializerOptions, static opts =>
            new JsonSerializerOptions(opts)
            {
                TypeInfoResolver = opts.TypeInfoResolver is null
                    ? _sharedDefaultTypeInfoResolver
                    : JsonTypeInfoResolver.Combine(opts.TypeInfoResolver, _sharedDefaultTypeInfoResolver),
            });
    }

    private static JsonTypeInfo? Resolve(JsonSerializerContext context, Type type, JsonSerializerOptions? runtimeFallbackOptions)
    {
        // A problem's extensions and a resource event's payload and bags are open by design, so they are written
        // through reflection without the runtime type fallback's warning: no context could register what they hold.
        // That holds for a subclass the context does not register as well, so both writers write it the same way.
        if (typeof(MvcProblemDetails).IsAssignableFrom(type) || typeof(ResourceEvent).IsAssignableFrom(type))
        {
            return GetFallbackOptions(context.Options).GetTypeInfo(type);
        }

        var typeInfo = context.GetTypeInfo(type);

        if (typeInfo is null)
        {
            // An uncovered root - a registered root returned as a subclass the context does not register - gets no
            // metadata, so each writer serializes it asynchronously, which an async stream anywhere in it needs, through
            // GetUncoveredRootOptions.
            return null;
        }

        return runtimeFallbackOptions?.GetTypeInfo(type) ?? typeInfo;
    }

    /// <summary>
    /// Gets the options a writer serializes an uncovered root with - one <see cref="Get" /> returned no metadata for - when
    /// the app allows the runtime type fallback: the fallback's own, which warn once per unregistered type written.
    /// </summary>
    /// <param name="app">The app whose serializer context and runtime type fallback apply.</param>
    /// <param name="services">The request's services, which the warning is logged through.</param>
    /// <returns>The runtime type fallback's options, or <see langword="null" /> when the app has not opted in.</returns>
    public static JsonSerializerOptions? GetUncoveredRootOptions(BlueprintApp app, IServiceProvider? services)
    {
        if (!app.AllowRuntimeTypeFallback || app.AppOwnedSerializerContext is not { } context)
        {
            return null;
        }

        return _entries.GetValue(context, static _ => new ContextEntry()).GetRuntimeFallbackOptions(context, services);
    }

    /// <summary>
    /// What one serializer context's response writing has resolved: each type's metadata and, for an app that allows the
    /// runtime type fallback, the options that fallback resolves through.
    /// </summary>
    private sealed class ContextEntry
    {
        private JsonSerializerOptions? _runtimeFallbackOptions;

        public ConcurrentDictionary<Type, JsonTypeInfo?> StrictTypeInfos { get; } = new();

        public ConcurrentDictionary<Type, JsonTypeInfo?> FallbackTypeInfos { get; } = new();

        public JsonSerializerOptions GetRuntimeFallbackOptions(JsonSerializerContext context, IServiceProvider? services)
        {
            if (this._runtimeFallbackOptions is { } existing)
            {
                return existing;
            }

            var logger = services?.GetService<ILoggerFactory>()?.CreateLogger(RuntimeTypeFallbackLoggerCategory) ?? NullLogger.Instance;
            var options = new JsonSerializerOptions(context.Options)
            {
                TypeInfoResolver = JsonTypeInfoResolver.Combine(context, new LoggingFallbackResolver(context.GetType().FullName!, logger)),
            };

            return Interlocked.CompareExchange(ref this._runtimeFallbackOptions, options, null) ?? options;
        }
    }

    /// <summary>
    /// The runtime type fallback's reflection resolver, asked only for what the context does not register, which warns
    /// once per type that is actually written.
    /// </summary>
    /// <remarks>
    /// Being asked is not being written: resolving any type's metadata also asks for its interfaces and base classes, to
    /// find inherited polymorphism configuration, so a registered <see cref="string" /> brings a request for
    /// <see cref="IComparable" />, and an ancestor's element types follow (<see cref="char" /> for
    /// <see cref="IEnumerable{T}" />). A value written is never of an interface or abstract type, so those are never
    /// warned about. An object's warning waits for its first instance written (<see cref="JsonTypeInfo.OnSerializing" />),
    /// and a value-shaped type's - a <see cref="JsonElement" />, a type with its own converter - for its converter's first
    /// write (or first key written), since only objects have that callback. Numbers and enums are written as they would
    /// be by the context and are not warned about, nullable ones included. A collection is not warned about itself, only
    /// what it holds.
    /// </remarks>
    private sealed class LoggingFallbackResolver(string contextName, ILogger logger) : IJsonTypeInfoResolver
    {
        private static readonly System.Reflection.MethodInfo _createValueInfo =
            typeof(LoggingFallbackResolver).GetMethod(nameof(CreateLoggingValueInfo), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        private readonly ConcurrentDictionary<Type, byte> _logged = new();

        [UnconditionalSuppressMessage(
            "AOT",
            "IL3050",
            Justification = "The runtime type fallback is reflection by definition; an app that opts in has given up AOT.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2060",
            Justification = "CreateLoggingValueInfo has no generic constraints or annotations, and the runtime type fallback is reflection by definition; an app that opts in has given up trimming.")]
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            var typeInfo = _sharedDefaultTypeInfoResolver.GetTypeInfo(type, options);

            if (typeInfo is null || type.IsInterface || type.IsAbstract)
            {
                return typeInfo;
            }

            switch (typeInfo.Kind)
            {
                case JsonTypeInfoKind.Object:
                    var onSerializing = typeInfo.OnSerializing;
                    typeInfo.OnSerializing = value =>
                    {
                        this.LogOnce(type);
                        onSerializing?.Invoke(value);
                    };

                    return typeInfo;

                case JsonTypeInfoKind.None when IsNumberLike(type):
                    // Not wrapped, and so not warned about: a built-in number converter applies the options' number
                    // handling only when the serializer dispatches to it, never through its public Write. A number is
                    // also the commonest ancestor probe (an IEnumerable<int> brings int), and reflection writes it
                    // exactly as the context would.
                    return typeInfo;

                case JsonTypeInfoKind.None:
                    return (JsonTypeInfo)_createValueInfo.MakeGenericMethod(type).Invoke(null, [typeInfo, options, this])!;

                default:
                    // A collection is not warned about itself - it has no callback, and resolving it is not writing it
                    // (a null list left out still has metadata). What it holds is warned about when written.
                    return typeInfo;
            }
        }

        private static bool IsNumberLike(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            return type.IsPrimitive
                || type.IsEnum
                || type == typeof(decimal)
                || type == typeof(Half)
                || type == typeof(Int128)
                || type == typeof(UInt128);
        }

        private static JsonTypeInfo CreateLoggingValueInfo<T>(JsonTypeInfo inner, JsonSerializerOptions options, LoggingFallbackResolver owner)
        {
            if (inner.Converter is JsonConverter<T> converter)
            {
                return JsonMetadataServices.CreateValueInfo<T>(options, new LoggingValueConverter<T>(converter, owner));
            }

            // A base type's converter accepting this type through CanConvert, which System.Text.Json adapts internally and
            // which cannot be wrapped as this type's: keep its metadata, and warn as it is resolved instead.
            owner.LogOnce(typeof(T));
            return inner;
        }

        private void LogOnce(Type type)
        {
            if (this._logged.TryAdd(type, 0))
            {
                _logRuntimeTypeFallback(logger, contextName, type.FullName ?? type.Name, null);
            }
        }

        /// <summary>
        /// A value-shaped type's own converter, which warns the first time it writes.
        /// </summary>
        private sealed class LoggingValueConverter<T>(JsonConverter<T> inner, LoggingFallbackResolver owner) : JsonConverter<T>
        {
            public override bool HandleNull => inner.HandleNull;

            public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                return inner.Read(ref reader, typeToConvert, options);
            }

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            {
                owner.LogOnce(typeof(T));
                inner.Write(writer, value, options);
            }

            // A dictionary keyed by the type writes its keys through these, which the base class does not support.
            public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                return inner.ReadAsPropertyName(ref reader, typeToConvert, options);
            }

            public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] T value, JsonSerializerOptions options)
            {
                owner.LogOnce(typeof(T));
                inner.WriteAsPropertyName(writer, value, options);
            }
        }
    }
}
