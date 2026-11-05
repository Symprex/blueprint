using Blueprint.SourceGenerator.Helpers;

namespace Blueprint.SourceGenerator.Generators.Clients;

/// <summary>
/// What <see cref="TypedClientGenerator"/> generates for one <c>[BlueprintClient]</c> class: its interface and the calls it
/// makes, one per operation it includes that it can call.
/// </summary>
/// <remarks>
/// Holds only strings, flags and equatable lists, never a symbol or a location, so that an edit which leaves the client's
/// shape as it was regenerates nothing.
/// </remarks>
/// <param name="Namespace">The class's namespace, or <see langword="null"/> for the global namespace.</param>
/// <param name="ClassName">The class's name, which its interface is named after.</param>
/// <param name="IsPublic">Whether the class is public, as its interface then is; otherwise both are internal.</param>
/// <param name="Operations">The calls, in the order the class includes the operations.</param>
/// <param name="Fields">The class's readonly fields the constructor is given, in declaration order.</param>
/// <param name="Methods">The class's public methods copied onto the interface, in declaration order.</param>
/// <param name="IsSealed">Whether the class is sealed, which a protected member it is given cannot be.</param>
/// <param name="SerializerContextFqn">
/// The fully qualified name of the serializer context the class names, or <see langword="null"/> for none, whose metadata the
/// calls then read and write with; either way the metadata is resolved once per options instance into the client's plan.
/// </param>
internal sealed record TypedClientModel(
    string? Namespace,
    string ClassName,
    bool IsPublic,
    EquatableList<TypedClientOperationModel> Operations,
    EquatableList<TypedClientFieldModel> Fields,
    EquatableList<TypedClientMethodModel> Methods,
    bool IsSealed,
    string? SerializerContextFqn)
{
    /// <summary>
    /// Gets the name of the interface generated for the class, which mock-based callers bind to.
    /// </summary>
    public string InterfaceName => "I" + this.ClassName;

    /// <summary>
    /// Gets the hint name of the generated file, unique per class.
    /// </summary>
    public string HintName => (this.Namespace is null ? string.Empty : this.Namespace + ".") + this.ClassName + ".BlueprintClient.g.cs";
}

/// <summary>
/// One call a typed client makes: everything its generated method needs, settled at compile time, so the call itself looks
/// nothing up.
/// </summary>
/// <param name="OperationFqn">The operation's fully qualified type name, which its request builders are nested in.</param>
/// <param name="OperationName">The operation's type name, which a failure to read its response names.</param>
/// <param name="MethodName">The generated method's name.</param>
/// <param name="ResultTypeFqn">The result type the operation declares, which the method returns as declared.</param>
/// <param name="HttpMethod">The HTTP method the operation is sent with, upper-cased, as its descriptor publishes it.</param>
/// <param name="ReaderExpression">
/// The expression constructing the reader of the declared result in the client's plan, which resolves the metadata each
/// reader is given from the plan's <c>client</c>.
/// </param>
internal sealed record TypedClientOperationModel(
    string OperationFqn,
    string OperationName,
    string MethodName,
    string ResultTypeFqn,
    string HttpMethod,
    string ReaderExpression)
{
    /// <summary>
    /// Gets a value indicating whether the URL is completed with the operation's query string, which only a GET carries:
    /// every other method sends the operation as its body.
    /// </summary>
    public bool AppendsQueryString => this.HttpMethod == "GET";
}

/// <summary>
/// A readonly field of the hand-written class that the generated constructor takes as a parameter, and every clone carries.
/// </summary>
/// <param name="FieldName">The field's name.</param>
/// <param name="ParameterName">The constructor parameter the field is given by, escaped where it is a keyword.</param>
/// <param name="TypeFqn">The field's fully qualified type, nullable annotations included.</param>
/// <param name="RefusesNull">Whether a missing value is refused: the field is a reference type not declared nullable.</param>
internal sealed record TypedClientFieldModel(
    string FieldName,
    string ParameterName,
    string TypeFqn,
    bool RefusesNull);

/// <summary>
/// A public method of the hand-written class that the generated interface repeats, so a caller holding the interface can call
/// it.
/// </summary>
/// <param name="Name">The method's name.</param>
/// <param name="Signature">
/// The method as declared on an interface: its return type as declared, name and parameters, default values and
/// <c>params</c>, <c>ref</c>, <c>out</c> and <c>in</c> modifiers included.
/// </param>
internal sealed record TypedClientMethodModel(string Name, string Signature);

/// <summary>
/// What <see cref="TypedClientGenerator"/> found in one <c>[BlueprintClient]</c> class: the client it generates, where the
/// class can be completed at all, and what it declares that the generated half cannot honour.
/// </summary>
/// <param name="Client">The client to generate, or <see langword="null"/> where the class cannot be completed.</param>
/// <param name="Diagnostics">The diagnostics to report, in cacheable form.</param>
internal sealed record TypedClientResult(TypedClientModel? Client, EquatableList<DiagnosticInfo> Diagnostics);
