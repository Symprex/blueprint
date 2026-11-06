namespace Blueprint.Model.Attributes;

/// <summary>
/// Exposes another operation, <typeparamref name="TInner"/>, as this one, and forwards each call to it. The decorated
/// class is a <c>partial</c> class that declares its operation interface (for example
/// <c>[Expose&lt;GetTemplateQuery&gt;] partial class GetGatewayTemplateQuery : IQuery&lt;TemplateApiResource&gt;;</c>),
/// its route, HTTP method and authorisation, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The source generator gives the class each public settable property of <typeparamref name="TInner"/> that is not
/// obsolete, with its type, attributes, documentation and default value, and a handler that sends a fresh
/// <typeparamref name="TInner"/> carrying those values through the <c>[BlueprintClient]</c> typed client that includes
/// it. The class declares no handler of its own.
/// </para>
/// <para>
/// Minimal mode is for first-party apps: a property added to <typeparamref name="TInner"/> becomes settable by this
/// operation's callers with no change to its source. A public API sets the MSBuild property
/// <c>BlueprintGatewayAllowExpose</c> to <c>false</c>, which makes every use of this attribute an error, and declares
/// each property of its operations itself.
/// </para>
/// </remarks>
/// <typeparam name="TInner">The operation this one exposes, usually from an upstream service's contract.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ExposeAttribute<TInner> : Attribute
    where TInner : class, IOperation;
