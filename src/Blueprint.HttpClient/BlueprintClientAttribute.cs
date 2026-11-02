using System.Diagnostics;

namespace Blueprint.HttpClient;

/// <summary>
/// Marks a hand-written <c>partial class XApi</c> as a typed service client. Blueprint's source generator emits an
/// <c>IXApi</c> interface and the class's implementation of it: one
/// <c>Task&lt;R&gt; NameAsync(Op operation, CallOptions? options = null, CancellationToken cancellationToken = default)</c>
/// per operation named by a <see cref="BlueprintClientIncludeAttribute"/>, and <c>WithCredential</c>.
/// </summary>
/// <remarks>
/// <para>
/// The client lives in the contract assembly beside its operations; operations from a referenced assembly can be included
/// too. Each call builds its request with the operation's own generated <c>Op.Http</c> builders and reads its response with
/// a reader chosen when it is compiled, so no <c>BlueprintApp</c>, descriptor or runtime scan is involved.
/// </para>
/// <para>
/// The partial class can extend the client: its public non-generic instance methods are copied into the interface, its
/// instance <see langword="readonly"/> fields become constructor parameters after the <see cref="BlueprintHttpClient"/>, it
/// can implement the generated <c>partial void OnConfigure(Op operation, ref CallOptions? options)</c> of any operation, and
/// it can call the generated <c>WithRequestModifier</c> to return a clone that changes every request.
/// </para>
/// <para>
/// The attribute is only read at compile time, so it is not kept in the compiled assembly.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
[Conditional("CODE_ANALYSIS")]
public sealed class BlueprintClientAttribute : Attribute
{
    /// <summary>
    /// Gets or sets an optional, hand-declared <c>JsonSerializerContext</c> whose metadata the client's calls use first. It
    /// is inserted at the front of a copy of the client's <c>JsonSerializerOptions.TypeInfoResolverChain</c>, so the
    /// options' converters still apply and a type the context lacks falls back to the options' own metadata. A type an
    /// included operation needs that the context lacks is warning <c>BPCL007</c>.
    /// </summary>
    /// <remarks>
    /// It must be hand-declared because the System.Text.Json source generator cannot see the <c>[JsonSerializable]</c>
    /// attributes another generator emits.
    /// </remarks>
    public Type? SerializerContext { get; set; }
}

/// <summary>
/// Includes an operation in the <see cref="BlueprintClientAttribute"/> typed client it is applied to. Repeat it once per
/// operation.
/// </summary>
/// <remarks>
/// The operation must be a concrete, non-generic operation that returns an <c>IReturn&lt;T&gt;</c> result, has a route and
/// so a generated <c>Op.Http</c> request builder, and is not unexposed. The client's method for it is named after the
/// operation type, less one trailing <c>Command</c> or <c>Query</c>, with <c>Async</c> appended.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
[Conditional("CODE_ANALYSIS")]
public sealed class BlueprintClientIncludeAttribute : Attribute
{
    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintClientIncludeAttribute"/> class.
    /// </summary>
    /// <param name="operationType">The operation type to include.</param>
    public BlueprintClientIncludeAttribute(Type operationType)
    {
        this.OperationType = operationType;
    }

    /// <summary>
    /// Gets the operation type to include.
    /// </summary>
    public Type OperationType { get; }
}
