using System.Reflection;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;

namespace Blueprint.HttpClient;

/// <summary>
/// Chooses the <see cref="BlueprintResponseReaders"/> reader for a result type known only at run time, as
/// <see cref="BlueprintHttpClient{TApp}"/>'s callers name it, once per type: a nested <see cref="StatusCodeResult"/> is read
/// as its singleton, a <see cref="Results{T1, T2}"/> as its members, and anything else as a value whose bodiless, plain-text
/// and body reads follow from the type.
/// </summary>
/// <remarks>
/// The choice is reflection, made once and cached per type. Reading a response with the chosen reader is the same code a
/// call whose result type is known when it is compiled runs.
/// </remarks>
internal static class RuntimeResponseReaders
{
    /// <summary>
    /// Gets the reader for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <returns>The reader.</returns>
    /// <exception cref="NotSupportedException">When <typeparamref name="T"/>, or a member of it as a union, is an abstract
    /// resource event, which no response can be read as. A caller gets the reader before it sends, so nothing is sent.</exception>
    internal static BlueprintResponseReader<T> For<T>()
    {
        return Cache<T>.Reader ?? throw new NotSupportedException(Cache<T>.Refusal);
    }

    /// <summary>
    /// Why no response can be read as <paramref name="type"/>, or <see langword="null"/> when one can: an abstract resource
    /// event cannot be created, and as a union member it would otherwise leave the other member to read the response.
    /// </summary>
    private static string? FindRefusal(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Results<,>))
        {
            var memberTypes = type.GetGenericArguments();

            return FindRefusal(memberTypes[0]) ?? FindRefusal(memberTypes[1]);
        }

        return type.IsAbstract && typeof(ResourceEvent).IsAssignableFrom(type)
            ? $"The resource event type '{type.FullName}' is abstract, so no response can be read as it."
            : null;
    }

    private static BlueprintResponseReader<T> Create<T>()
    {
        var type = typeof(T);

        if (typeof(StatusCodeResult).IsAssignableFrom(type) && type != typeof(StatusCodeResult))
        {
            return (BlueprintResponseReader<T>)InvokeGeneric(nameof(CreateStatusCodeReader), type);
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Results<,>))
        {
            var memberTypes = type.GetGenericArguments();

            return (BlueprintResponseReader<T>)InvokeGeneric(
                nameof(CreateUnionReader),
                [memberTypes[0], memberTypes[1]],
                [CreateMember(memberTypes[0]), CreateMember(memberTypes[1])]);
        }

        if (ResourceEventReader.CanRead(type))
        {
            return (BlueprintResponseReader<T>)InvokeGeneric(nameof(CreateResourceEventReader), [type, ResourceEventReader.FindResourceType(type)!]);
        }

        if (IsResourceEventResultType(type))
        {
            return (BlueprintResponseReader<T>)InvokeGeneric(nameof(CreateResourceEventResultReader), type.GetGenericArguments()[0]);
        }

        if (type == typeof(string))
        {
            return (BlueprintResponseReader<T>)(object)BlueprintResponseReaders.String();
        }

        if (CreateNoMediaTypeResult(type) is { } noMediaTypeResult)
        {
            return BlueprintResponseReaders.Bodiless<T>(status => (T?)noMediaTypeResult(status), typeof(PlainTextResult).IsAssignableTo(type));
        }

        return type == typeof(PlainTextResult)
            ? (BlueprintResponseReader<T>)(object)BlueprintResponseReaders.PlainText()
            : BlueprintResponseReaders.Json<T>();
    }

    /// <summary>
    /// Makes the union member of <paramref name="memberType"/>, a <see cref="UnionMember{T}"/> of it.
    /// </summary>
    private static object CreateMember(Type memberType)
    {
        if (ResourceEventReader.CanRead(memberType))
        {
            return InvokeGeneric(nameof(CreateResourceEventMember), [memberType, ResourceEventReader.FindResourceType(memberType)!]);
        }

        if (IsResourceEventResultType(memberType))
        {
            return InvokeGeneric(nameof(CreateResourceEventResultMember), memberType.GetGenericArguments()[0]);
        }

        if (memberType == typeof(PlainTextResult))
        {
            return BlueprintResponseReaders.PlainTextMember();
        }

        if (CreateBodilessResult(memberType) is { } bodiless)
        {
            return InvokeGeneric(nameof(CreateBodilessMember), [memberType], [bodiless]);
        }

        // The host writes a type that is not an operation result, or a resource event, as a 200 only, and an OkResult<T>,
        // CreatedResult<T> or AcceptedResult<T> as its content with its own status. No other operation result is read back
        // from a body.
        if (typeof(ResourceEvent).IsAssignableFrom(memberType) || !typeof(OperationResult).IsAssignableFrom(memberType))
        {
            var eventResourceType = ResourceEventReader.FindResourceType(memberType);

            return InvokeGeneric(nameof(CreateJsonMember), [memberType], [eventResourceType is null ? null : ApiResource.GetTypeName(eventResourceType)]);
        }

        if (memberType.IsGenericType)
        {
            var definition = memberType.GetGenericTypeDefinition();
            var content = memberType.GetGenericArguments()[0];

            if (definition == typeof(OkResult<>))
            {
                return InvokeGeneric(nameof(CreateOkMember), content);
            }

            if (definition == typeof(CreatedResult<>))
            {
                return InvokeGeneric(nameof(CreateCreatedMember), content);
            }

            if (definition == typeof(AcceptedResult<>))
            {
                return InvokeGeneric(nameof(CreateAcceptedMember), content);
            }
        }

        return InvokeGeneric(nameof(CreateBodilessMember), [memberType], [null]);
    }

    /// <summary>
    /// The result a response with no media type is, for any of the shapes legacy's fallback branch covered when nothing else
    /// claimed the response: whatever <see cref="CreateBodilessResult"/> already gives; a <see cref="StatusCodeResult"/>
    /// singleton of the status when the type can hold a <see cref="StatusCodeResult"/>, <see cref="OperationResult"/> or
    /// <see cref="object"/> (legacy's client made a <c>StatusCodeResult</c> of any status whenever nothing else claimed the
    /// response); otherwise, when it can only hold the base <see cref="HttpResult"/> (a sibling of
    /// <see cref="StatusCodeResult"/> here, not a base of it), a status-only <see cref="HttpResult"/>.
    /// </summary>
    /// <remarks>
    /// A type of the base <see cref="StatusCodeResult"/>, <see cref="OperationResult"/> or <see cref="object"/> at a status
    /// with no nested <see cref="StatusCodeResult"/> singleton (for example 205) still finds nothing here: legacy's
    /// <c>StatusCodeResult</c> could hold any status; the nested singletons here cannot. That is a pre-existing departure.
    /// </remarks>
    private static Func<int, object?>? CreateNoMediaTypeResult(Type resultType)
    {
        var bodiless = CreateBodilessResult(resultType);

        Func<int, object?>? fallback = typeof(StatusCodeResult).IsAssignableTo(resultType)
            ? BodilessResults.AnyStatusCode
            : typeof(HttpResult).IsAssignableTo(resultType)
                ? BodilessResults.StatusOnlyHttpResult
                : null;

        return (bodiless, fallback) switch
        {
            (null, null) => null,
            ({ } only, null) => only,
            (null, { } only) => only,
            ({ } first, { } second) => status => first(status) ?? second(status),
        };
    }

    /// <summary>
    /// The result a response with no body and a status is, when the host writes one that way: a <see cref="StatusCodeResult"/>
    /// by its status, an <see cref="HttpNoContentResult"/> as a 204, a <see cref="NoResultOperationResult"/> as a 200, an
    /// <see cref="HttpAcceptedResult"/> as a 202, and a <see cref="KeyOnlyResourceEventResult"/> as the 201, 200 or 204 of
    /// its change type.
    /// </summary>
    private static Func<int, object?>? CreateBodilessResult(Type resultType)
    {
        if (resultType == typeof(StatusCodeResult))
        {
            return BodilessResults.AnyStatusCode;
        }

        if (typeof(StatusCodeResult).IsAssignableFrom(resultType))
        {
            var instance = FindStatusCodeResultInstance(resultType);

            return status => instance is not null && instance.StatusCode == status ? instance : null;
        }

        if (resultType == typeof(HttpNoContentResult))
        {
            return BodilessResults.HttpNoContent;
        }

        if (resultType == typeof(NoResultOperationResult))
        {
            return BodilessResults.NoResult;
        }

        if (resultType == typeof(HttpAcceptedResult))
        {
            return BodilessResults.HttpAccepted;
        }

        return resultType == typeof(KeyOnlyResourceEventResult) ? BodilessResults.KeyOnlyResourceEvent : null;
    }

    private static StatusCodeResult? FindStatusCodeResultInstance(Type statusCodeResultType)
    {
        return statusCodeResultType.GetField(nameof(StatusCodeResult.Accepted.Instance), BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as StatusCodeResult;
    }

    private static bool IsResourceEventResultType(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ResourceEventResult<>);
    }

    private static object InvokeGeneric(string methodName, params Type[] typeArguments)
    {
        return InvokeGeneric(methodName, typeArguments, []);
    }

    private static object InvokeGeneric(string methodName, Type[] typeArguments, object?[] arguments)
    {
        return typeof(RuntimeResponseReaders)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeArguments)
            .Invoke(null, arguments)!;
    }

    private static BlueprintResponseReader<T> CreateStatusCodeReader<T>()
        where T : StatusCodeResult
    {
        return new StatusCodeResultResponseReader<T>((T?)FindStatusCodeResultInstance(typeof(T)));
    }

    private static BlueprintResponseReader<Results<T1, T2>> CreateUnionReader<T1, T2>(UnionMember<T1> first, UnionMember<T2> second)
        where T1 : notnull
        where T2 : notnull
    {
        return BlueprintResponseReaders.Union(first, second);
    }

    private static BlueprintResponseReader<TEvent> CreateResourceEventReader<TEvent, TResource>()
        where TEvent : ResourceEvent<TResource>
        where TResource : ApiResource
    {
        return BlueprintResponseReaders.ResourceEvent<TEvent, TResource>(ResourceEventReader.CreateUninitialised<TEvent>);
    }

    private static BlueprintResponseReader<ResourceEventResult<T>> CreateResourceEventResultReader<T>()
        where T : notnull
    {
        return BlueprintResponseReaders.ResourceEventResult<T>();
    }

    private static UnionMember<TEvent> CreateResourceEventMember<TEvent, TResource>()
        where TEvent : ResourceEvent<TResource>
        where TResource : ApiResource
    {
        return BlueprintResponseReaders.ResourceEventMember<TEvent, TResource>(ResourceEventReader.CreateUninitialised<TEvent>);
    }

    private static UnionMember<ResourceEventResult<T>> CreateResourceEventResultMember<T>()
        where T : notnull
    {
        return BlueprintResponseReaders.ResourceEventResultMember<T>();
    }

    private static UnionMember<T> CreateBodilessMember<T>(Func<int, object?>? bodiless)
    {
        return BlueprintResponseReaders.BodilessMember<T>(bodiless is null ? null : status => (T?)bodiless(status));
    }

    private static UnionMember<T> CreateJsonMember<T>(string? eventResourceTypeName)
    {
        return BlueprintResponseReaders.JsonMember<T>(eventResourceTypeName);
    }

    private static UnionMember<OkResult<T>> CreateOkMember<T>()
        where T : notnull
    {
        return BlueprintResponseReaders.ContentMember<OkResult<T>, T>(200, static content => new OkResult<T>(content));
    }

    private static UnionMember<CreatedResult<T>> CreateCreatedMember<T>()
        where T : notnull
    {
        return BlueprintResponseReaders.ContentMember<CreatedResult<T>, T>(201, static content => new CreatedResult<T>(content));
    }

    private static UnionMember<AcceptedResult<T>> CreateAcceptedMember<T>()
        where T : notnull
    {
        return BlueprintResponseReaders.ContentMember<AcceptedResult<T>, T>(202, static content => new AcceptedResult<T>(content));
    }

    private static class Cache<T>
    {
        internal static readonly string? Refusal = FindRefusal(typeof(T));

        internal static readonly BlueprintResponseReader<T>? Reader = Refusal is null ? Create<T>() : null;
    }
}
