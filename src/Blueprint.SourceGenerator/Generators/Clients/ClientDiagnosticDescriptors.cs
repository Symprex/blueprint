using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Generators.Clients;

/// <summary>
/// The <c>BPCL</c> diagnostics <see cref="BlueprintClientSourceGenerator"/> reports for a <c>[BlueprintClient]</c> typed
/// client. Each has a row in <c>docs/diagnostic-reference.md</c>.
/// </summary>
internal static class ClientDiagnosticDescriptors
{
    private const string Category = "Blueprint.Client";

    /// <summary>
    /// The client class is not a partial, top-level, non-generic, non-abstract class, so its implementation cannot be generated into it.
    /// </summary>
    public static readonly DiagnosticDescriptor ClientNotPartial = new(
        id: "BPCL001",
        title: "Typed client class must be a partial, top-level, concrete, non-generic class",
        messageFormat: "The [BlueprintClient] class '{0}' {1}",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generator emits the client's interface and its implementation, constructors and clones into the class, so it must be a partial class declared directly in a namespace, with no type parameters, and not abstract.");

    /// <summary>
    /// An include does not name a concrete, non-generic operation.
    /// </summary>
    public static readonly DiagnosticDescriptor IncludeNotConcreteOperation = new(
        id: "BPCL002",
        title: "Included type is not a concrete operation",
        messageFormat: "'{0}' is not a concrete, non-generic operation, so a typed client cannot include it",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[BlueprintClientInclude] takes a concrete, non-generic class that implements IOperation.");

    /// <summary>
    /// An included operation declares no <c>IReturn&lt;T&gt;</c> result.
    /// </summary>
    public static readonly DiagnosticDescriptor OperationHasNoResult = new(
        id: "BPCL003",
        title: "Included operation has no typed result",
        messageFormat: "The operation '{0}' implements no IReturn<T>, so its client method has no result type",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Each client method returns the operation's declared IReturn<T> result. Declare the operation as ICommand<T> or IQuery<T>.");

    /// <summary>
    /// An included operation has no generated typed request builder, or is unexposed.
    /// </summary>
    public static readonly DiagnosticDescriptor OperationHasNoRequestBuilder = new(
        id: "BPCL004",
        title: "Included operation has no typed request builder",
        messageFormat: "The operation '{0}' {1}, so a typed client cannot send it",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A typed client builds each request with the operation's generated Op.Http builders. An operation gets them when it is partial and has a route, unless it is named Http, declares a member named Http or derives from a concrete operation. An [UnexposedOperation] is not sent over HTTP.");

    /// <summary>
    /// An operation is included twice, or two includes produce the same method name.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateInclude = new(
        id: "BPCL005",
        title: "Duplicate typed client method",
        messageFormat: "{0}",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Each included operation gets one method, named after the operation type less one trailing Command or Query, with Async appended. Include each operation once, and rename one of two operations that would share a method name.");

    /// <summary>
    /// A public method of the client class is generic, so it cannot be copied into the interface.
    /// </summary>
    public static readonly DiagnosticDescriptor GenericPublicMethod = new(
        id: "BPCL006",
        title: "Typed client public method is generic",
        messageFormat: "The public method '{0}' is generic, so it cannot be copied into the client's interface",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Public non-generic instance methods of a [BlueprintClient] class are copied into its interface. Make the method non-generic, or not public.");

    /// <summary>
    /// The client's serializer context has no metadata for a type an included operation needs.
    /// </summary>
    public static readonly DiagnosticDescriptor SerializerContextMissingType = new(
        id: "BPCL007",
        title: "Serializer context is missing a type",
        messageFormat: "The serializer context '{0}' neither declares nor reaches '{1}', which '{2}' needs; its calls fall back to the options' own metadata for it",
        category: Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A context has metadata for each type it declares [JsonSerializable] for and each type those reach through their properties, fields, elements and type arguments. Add [JsonSerializable(typeof(...))] for the type to the context so that the client's calls use its source-generated metadata.");

    /// <summary>
    /// An included operation's result shape has no response reader.
    /// </summary>
    public static readonly DiagnosticDescriptor ResultHasNoReader = new(
        id: "BPCL008",
        title: "Included operation's result has no reader",
        messageFormat: "The result '{0}' of '{1}' has no response reader, so a typed client cannot read it",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A typed client reads a JSON type, a resource event (ResourceEvent<T>, ResourceCreated<T>, ResourceUpdated<T>, ResourceDeleted<T>), a ResourceEventResult<T>, a nested StatusCodeResult, a PlainTextResult, a string, a two-member Results<,> union, or a result the host writes with no body. Other operation results, such as a FileStreamResult or a non-generic OkResult, have none, and neither has an abstract resource event.");
}
