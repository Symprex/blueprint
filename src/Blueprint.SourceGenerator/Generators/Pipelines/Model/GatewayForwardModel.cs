using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules.Gateway;

namespace Blueprint.SourceGenerator.Generators.Pipelines.Model;

/// <summary>
/// The forwarding handler the pipeline generator synthesises for an operation marked <c>[Expose&lt;TInner&gt;]</c> or
/// <c>[ForwardsTo&lt;TInner&gt;]</c> (D12): it creates a fresh <see cref="InnerTypeName"/> from the operation's properties
/// and bindings and forwards it upstream.
/// </summary>
/// <param name="InnerTypeName">The inner operation, <c>global::</c>-qualified.</param>
/// <param name="ClientTypeName">The <c>[BlueprintClient]</c> class whose named client and JSON options reach the upstream.</param>
/// <param name="ResultTypeName">The operation's declared result, which the forward reads the upstream response as.</param>
/// <param name="CopiedProperties">The properties copied from the operation onto the inner operation.</param>
/// <param name="Bindings">The inner properties D11 binds or hides, set after the copied ones.</param>
/// <param name="MapHookName">
/// The operation's <c>OnMap</c> or <c>OnMapAsync</c>, called with the inner operation and the handler parameters after the
/// bindings and before sending, or <see langword="null"/> when it has none.
/// </param>
/// <param name="IsMapHookAsync">Whether <paramref name="MapHookName"/> returns a task to await.</param>
/// <param name="IsMapHookStatic">
/// Whether <paramref name="MapHookName"/> is static, so the forward calls it on the operation type rather than the
/// operation (#176).
/// </param>
public sealed record GatewayForwardModel(
    string InnerTypeName,
    string ClientTypeName,
    string ResultTypeName,
    EquatableList<GatewayCopiedProperty> CopiedProperties,
    EquatableList<GatewayPropertyBinding> Bindings,
    string? MapHookName,
    bool IsMapHookAsync,
    bool IsMapHookStatic = false);

/// <summary>
/// One property a forward copies onto the inner operation: the same name in minimal mode, and in declared mode the name a
/// <c>[ForwardAs]</c> gives (D10).
/// </summary>
/// <param name="InnerName">The inner operation's property that is set.</param>
/// <param name="SourceName">The gateway operation's property it is read from.</param>
public sealed record GatewayCopiedProperty(string InnerName, string SourceName);
