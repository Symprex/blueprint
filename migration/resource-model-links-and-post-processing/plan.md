# Resource model, links, and post-processing

- Status: Planned
- Priority: P2
- Classification: Core/resource
- Depends on: `../foundation-module-system/plan.md`, `../operation-discovery-and-descriptors/plan.md`, `../pipeline-composition-and-execution/plan.md`, `../result-model-and-host-translation/plan.md`, `../http-host-and-message-population/plan.md`

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

This plan now contains the canonical resource-event direction directly rather than sending readers through a chain of round-by-round design notes.

### Legacy findings that still matter

- legacy nested execution is only the `Data == null && SelfQuery != null` path
- query-backed definitions do not always nest; the call-site shape decides whether `Data` is present
- deleted query-backed events usually use the query only for `Href`, not for `Data`
- `SelfQuery` is doing two jobs today and should be split into explicit identity/href binding plus explicit payload shape
- Signature365's production audit flow does not populate `ChangedValues`, so `ChangedValues` is removed entirely from the new design
- the real event granularity lives in **event reasons** (70+ distinct reason strings across Signature365), not in the coarse `Created/Updated/Deleted` change types
- the audit contract becomes save-only (`IResourceEventAuditRepository.AddAsync` only), eliminating all read-back

### Canonical resource-event direction

1. remove `SelfQuery` from the long-term resource-event contract
2. generate key/data-to-`Href` binding from compile-time link metadata
3. declare concrete contract-level resource-event result types in `*.Contract`, grouped by family, with reason metadata carried on the concrete nested result type
4. make operations declare allowed one-of-many event outcomes through a Blueprint-native `Results<T1, ...>` union in their response contract
5. separate event body from event identity; deleted/key-only results carry identity only and default to no body
6. keep authored event records shape-focused and let the generator own the framework boilerplate for body-bearing versus identity-only event shapes
7. do not make generated `*ResourceEventBindings` part of the baseline design; handlers construct concrete result types directly using whatever mapping/services are already appropriate
8. depend on the shared host-agnostic result model described in `../result-model-and-host-translation/plan.md` rather than creating a second resource-event-specific result system
9. return one concrete resource-event result per execution path; do not model simultaneous event batches
10. do not make every pipeline contributor understand a universal nested-execution mode just to support resource events
11. `ChangedValues` is removed entirely; no passthrough, no optional support
12. the audit contract is save-only; no read-back, no `GetCurrentDataAsync`
13. if a temporary key-only created/updated bridge is still required during migration, gate it with analyzers and treat it as transitional rather than as a new runtime facility

### Canonical authoring surface

Illustrative direction:

```csharp
[ResourceEventFamily(typeof(Microsoft365AddOnApiResource), KeyType = typeof(Microsoft365AddOnKey))]
public static partial class Microsoft365ResourceEvents
{
    [ResourceEventReason(ChangeType.Updated, "domains.checked")]
    public sealed partial record DomainsChecked(Microsoft365AddOnApiResource Resource);

    [ResourceEventReason(ChangeType.Updated, "sentItems.settings.updated")]
    public sealed partial record SentItemsSettingsUpdated(
        [ResourceEventBody] SentItemsSettingsApiResource Resource,
        [ResourceEventKey] Microsoft365AddOnKey Key);

    [ResourceEventReason(ChangeType.Deleted, "deleted")]
    public sealed partial record Deleted([ResourceEventKey] Microsoft365AddOnKey Key);
}
```

Authoring rules:

- if the event has a single constructor parameter whose type matches the family resource type, treat it as the body and derive identity from that resource
- if the event has no full resource body, require `[ResourceEventKey]`
- if the event uses a partial body plus explicit identity, require `[ResourceEventBody]` and `[ResourceEventKey]`
- the generator owns the framework boilerplate and `EventId` generation; authors do not hand-author framework inheritance for event shapes

### Canonical handler surface

Handlers should construct the concrete result directly:

```csharp
return new Microsoft365ResourceEvents.DomainsChecked(
    Microsoft365Mapper.MapFrom(addOn, tenantDomains));
```

or:

```csharp
return new Microsoft365ResourceEvents.Deleted(new Microsoft365AddOnKey(operation.AddOnId));
```

If richer enrichment is required, the handler should call the relevant service directly and still return the concrete result. Generated `*ResourceEventBindings` are intentionally not part of the default model.

## Compile-time checks to add

- existing route-placeholder diagnostics (`BPLG001`-`BPLG003`) stay and grow
- self/resource link target types must be compatible
- resource response type must be known where link/event processing depends on it
- resources used in events must expose analyzable key-to-self-link binding
- event-family anchors must resolve to a concrete resource type and key-binding model
- contract-level concrete event result types must carry unique exact reason strings within a family
- created/updated resource events should prefer direct data and diagnose migration-only escape hatches
- operations using `Results<T1, ...>` must only declare supported result types
- handlers must return types compatible with the declared union
- ambiguous event constructor shapes must declare body/key roles explicitly
- generated body-bearing and identity-only event-shape plumbing must match the declared family resource/key model
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
- The audit contract is save-only (`IResourceEventAuditRepository.AddAsync` only). No read-back. No `GetCurrentDataAsync`.
- Put the public concrete resource-event result types in `*.Contract`, grouped by event family, and carry reason metadata on the concrete nested result type itself.
- Make the operation response contract the canonical declaration of one-of-many event outcomes through a Blueprint-native `Results<T1, ...>` union.
- Separate event body from event identity. Deleted/key-only event results carry identity only and default to no body.
- Do not require authored event records to hand-author framework inheritance or boilerplate for event shapes; the generator should own that plumbing.
- Do not make generated `*ResourceEventBindings` a core authoring surface. Construct concrete results directly in handlers unless a real extracted helper is justified.
- Return one concrete resource-event result per execution path. Do not adopt a simultaneous event-batch model.
- Build resource-event results on top of the shared host-agnostic result model described in `../result-model-and-host-translation/plan.md`.
- `EventId` is generated from `resourceObject + "." + reason`. Handler authors never compose it.
- First-class partial and deleted-key shapes for resource events.
- Do not preserve a projector or re-query facility as the default resource-event path.

## Sequencing

Implement after the base HTTP + execution model is stable.
