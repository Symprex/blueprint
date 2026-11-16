namespace Blueprint.Model.Attributes;

/// <summary>
/// Forwards each call of this operation to another operation, <typeparamref name="TInner"/>, while the class writes out
/// its own public shape (declared mode). The decorated class declares its operation interface, its route, HTTP method and
/// authorisation, and each property its callers may set.
/// </summary>
/// <remarks>
/// <para>
/// Each declared property is matched to the public settable property of <typeparamref name="TInner"/> of the same name, or
/// of the name its <see cref="ForwardAsAttribute"/> gives. A matched property that declares no validation attribute of its
/// own is validated by the inner property's rules, and documented by the inner property's XML docs where it has none of
/// its own. It must have the inner property's type. A declared property with no inner counterpart is not forwarded; it is
/// there for the operation's <c>OnMap</c> or <c>OnMapAsync</c> hook.
/// </para>
/// <para>
/// The source generator gives the class a handler that sends a fresh <typeparamref name="TInner"/> carrying the matched
/// properties, and the inner properties <see cref="BindAttribute"/>, <see cref="HideAttribute"/> and
/// <see cref="GatewayBindAttribute"/> fill, through the <c>[BlueprintClient]</c> typed client that includes it. The class
/// declares no handler of its own. A property added to <typeparamref name="TInner"/> stays out of this operation's public
/// shape until the class declares it, so this is the mode for public APIs.
/// </para>
/// </remarks>
/// <typeparam name="TInner">The operation this one forwards to, usually from an upstream service's contract.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ForwardsToAttribute<TInner> : Attribute
    where TInner : class, IOperation;
