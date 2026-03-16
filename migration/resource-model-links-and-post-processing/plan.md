# Resource model, links, and post-processing

- Status: Planned
- Priority: P2
- Classification: Core/resource
- Depends on: `../foundation-module-system/plan.md`, `../operation-discovery-and-descriptors/plan.md`, `../pipeline-composition-and-execution/plan.md`, `../http-host-and-message-population/plan.md`

## Why this module exists

Blueprint's resource story spans several concerns:

- `ApiResource` metadata such as the generated `Object` property
- operation-level link metadata (`[RootLink]`, `[SelfLink]`)
- post-execution link generation for `ILinkableResource`
- resource-event processing

The rewrite already has a good starting point with `ApiResourceSourceGenerator` and `LinkGenerationModule`, but the larger post-processing story is still missing.

## Evidence

- Current: `src\Blueprint.SourceGenerator\Generators\ApiResources\ApiResourceSourceGenerator.cs`
- Current: `src\Blueprint.SourceGenerator\Modules\LinkGenerationModule.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\Middleware\LinkGeneratorMiddlewareBuilder.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\Middleware\ResourceEventHandlerMiddlewareBuilder.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\Middleware\ResourceEventHandler.cs`
- Signature365 extension evidence: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common\Api\ResourceExpandMiddleware.cs`

## Migration goal

Unify resource metadata, links, and resource-related post-processing into a source-generated model that keeps link/resource behavior deterministic and analyzable.

## Runtime support to build

- generated `ApiResource` helper partials
- descriptor-level URL/link metadata
- post-result link-generation hooks
- resource-event processing hooks
- generated key-to-self-link binders for resource events
- explicit resource-event envelope/shape helpers for snapshot, partial, tombstone, and deleted-key flows

## Consumer configuration and activation

Preserve or replace with thin equivalents:

- `[RootLink]`
- `[SelfLink]`
- `ILinkableResource`
- `.AddResourceEvents<T>()`

`AddHateoasLinks()` should not remain the long-term activation switch. Link generation should be available whenever resource/link metadata exists and should compile away when it does not.

Resource expansion should not be treated as a core activation concept for the rewrite.

## Resource events and re-query strategy

Detailed analysis lives in:

- [`resource-events-no-nested-design.md`](resource-events-no-nested-design.md) for the legacy taxonomy and round-1 comparison
- [`resource-events-no-nested-design-round-2.md`](resource-events-no-nested-design-round-2.md) for the intermediate recommendation after confirming `ChangedValues` is not a real production driver
- [`resource-events-no-nested-design-round-3.md`](resource-events-no-nested-design-round-3.md) for the current recommendation incorporating per-reason granularity, save-only audit contract, and full `ChangedValues` removal

Key findings from that deep-dive:

- legacy nested execution is only the `Data == null && SelfQuery != null` path
- query-backed definitions do not always nest; the call-site shape decides whether `Data` is present
- deleted query-backed events usually use the query only for `Href`, not for `Data`
- `SelfQuery` is doing two jobs today and should be split into explicit identity/href binding plus explicit payload shape
- Signature365's production audit flow does not populate `ChangedValues`, so `ChangedValues` is removed entirely from the new design (not optional — gone)
- the real event granularity lives in **event reasons** (70+ distinct reason strings across Signature365), not in the coarse `Created/Updated/Deleted` change types
- the audit contract becomes save-only (`IResourceEventSink.SaveAsync`), eliminating all read-back

Recommended direction:

1. remove `SelfQuery` from the long-term resource-event contract
2. generate key/data-to-`Href` binding from compile-time link metadata
3. use generated **named methods per event reason** within explicit event-family anchors — each reason is a partial method with an `[EventReason]` attribute carrying `ChangeType` and reason string
4. do not make every pipeline contributor understand a universal nested-execution mode just to support resource events
5. `ChangedValues` is removed entirely; no passthrough, no optional support
6. the audit contract is save-only; no read-back, no `GetCurrentDataAsync`
7. if a temporary key-only created/updated bridge is still required during migration, gate it with analyzers and treat it as transitional rather than as a new runtime facility

## Compile-time checks to add

- existing route-placeholder diagnostics (`BPLG001`-`BPLG003`) stay and grow
- self/resource link target types must be compatible
- resource response type must be known where link/event processing depends on it
- resources used in events must expose analyzable key-to-self-link binding
- event-family anchors must resolve to a concrete resource type and key-binding model
- created/updated resource events should prefer direct data and diagnose migration-only escape hatches
- partial payload shapes must be explicit and diagnosable
- legacy `SelfQuery`-based event construction should be analyzer- and code-fix-driven to the new shapes

## Observability and debuggability

- generated link/resource manifest
- clear indication of which post-processing contributors run after execution
- diagnostics that explain why a resource was not linkable or why resource-event processing could not be generated
- trace/log tags for resource event handling and link generation

## Explicit decisions

- Keep `ApiResourceSourceGenerator` as part of the core parity path.
- Keep compile-time link validation at the heart of the design.
- Treat `ResourceExpand` as a consumer-space concern, not a core Blueprint module.
- Keep post-processing stage contributions distinct from execution logic.
- Do not carry `SelfQuery` forward as the long-term resource-event contract.
- Use generated key/data-to-`Href` binding instead of using a query object to build resource-event links.
- `ChangedValues` is removed entirely from the resource-event design. Not optional. Not passthrough. Gone.
- The audit contract is save-only (`IResourceEventSink.SaveAsync`). No read-back. No `GetCurrentDataAsync`.
- Use generated **named methods per event reason** within explicit event-family anchors. Each reason is a partial method with `[EventReason(ChangeType, reason)]`.
- `EventId` is generated from `resourceObject + "." + reason`. Handler authors never compose it.
- First-class partial and deleted-key shapes for resource events.
- Do not preserve a projector or re-query facility as the default resource-event path.

## Sequencing

Implement after the base HTTP + execution model is stable.
