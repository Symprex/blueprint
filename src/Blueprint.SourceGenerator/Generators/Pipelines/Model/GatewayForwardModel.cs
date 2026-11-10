using Blueprint.SourceGenerator.Helpers;

namespace Blueprint.SourceGenerator.Generators.Pipelines.Model;

/// <summary>
/// The forwarding handler the pipeline generator synthesises for an operation marked <c>[Expose&lt;TInner&gt;]</c> (D12):
/// it creates a fresh <see cref="InnerTypeName"/> from the operation's properties and forwards it upstream.
/// </summary>
/// <param name="InnerTypeName">The inner operation, <c>global::</c>-qualified.</param>
/// <param name="ClientTypeName">The <c>[BlueprintClient]</c> class whose named client and JSON options reach the upstream.</param>
/// <param name="ResultTypeName">The operation's declared result, which the forward reads the upstream response as.</param>
/// <param name="CopiedPropertyNames">The properties copied from the operation onto the inner operation, by name.</param>
public sealed record GatewayForwardModel(
    string InnerTypeName,
    string ClientTypeName,
    string ResultTypeName,
    EquatableList<string> CopiedPropertyNames);
