# Resource events without nested execution

> Note:
> The legacy analysis in this document still stands, but the **current recommendation** is now in
> [`resource-events-no-nested-design-round-3.md`](resource-events-no-nested-design-round-3.md).
> Round 3 incorporates per-reason event granularity, removes `ChangedValues` entirely, and adopts a
> save-only audit contract. See also
> [`resource-events-no-nested-design-round-2.md`](resource-events-no-nested-design-round-2.md) for
> the intermediate round that confirmed `ChangedValues` is not a real production driver.

## Purpose

This document records the deep-dive analysis of legacy `ResourceEvent` behavior in Blueprint and Signature365, then evaluates three replacement API contracts that remove nested execution entirely while keeping the important consumer-facing event shape.

It is a design document, not an implementation plan. The module-level implementation tracker remains [`plan.md`](plan.md).

## Evidence reviewed

### Legacy Blueprint runtime

- `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\ResourceEvent.cs`
- `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\ResourceEventDefinition.cs`
- `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\ResourceEventDefinitionFactory.cs`
- `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\Middleware\ResourceEventHandler.cs`
- `D:\Projects\Signature365\src\Blueprint\Blueprint\ApiOperationContext.cs`
- `D:\Projects\Signature365\src\Blueprint\Blueprint\CodeGennedExecutor.cs`
- `D:\Projects\Signature365\src\Blueprint\Blueprint\IOperationExecutorPipeline.cs`

### Legacy tests that define expected behavior

- `D:\Projects\Signature365\src\Blueprint\tests\Blueprint.Tests\ResourceEvents\Given_ResourceCreated_With_SelfQuery_Result.cs`
- `D:\Projects\Signature365\src\Blueprint\tests\Blueprint.Tests\ResourceEvents\Given_ResourceEvent_Data_Return.cs`
- `D:\Projects\Signature365\src\Blueprint\tests\Blueprint.Tests\ResourceEvents\Given_Permissioned_GetById.cs`
- `D:\Projects\Signature365\src\Blueprint\tests\Blueprint.Tests\ResourceEvents\ResourceEventJsonTests.cs`
- `D:\Projects\Signature365\src\Blueprint\tests\Blueprint.Tests\ResourceEvents\ResourceEventJsonTests.Can_serialise_to_JSON.verified.txt`

### Representative Signature365 usage

- Query-backed and nested:
  - `D:\Projects\Signature365\src\Apps\Billing\Symprex.Signature365.Billing\Api\Accounts\AccountEvents.cs`
  - `D:\Projects\Signature365\src\Apps\Billing\Symprex.Signature365.Billing\Api\Accounts\ApplyAccountCouponCommandHandler.cs`
  - `D:\Projects\Signature365\src\Apps\Billing\Symprex.Signature365.Billing\Api\Invoices\InvoiceEvents.cs`
  - `D:\Projects\Signature365\src\Apps\Billing\Symprex.Signature365.Billing\Api\Invoices\RefundInvoiceCommandHandler.cs`
  - `D:\Projects\Signature365\src\Apps\Partners\Symprex.Signature365.Partners\Api\PartnerTenants\PartnerTenantEvents.cs`
  - `D:\Projects\Signature365\src\Apps\Partners\Symprex.Signature365.Partners\Api\PartnerTenants\EditPartnerTenantCommandHandler.cs`
  - `D:\Projects\Signature365\src\Apps\Portal\Symprex.Signature365.Portal\Api\Modules\Campaigns\CreateCampaignCommand.cs`
  - `D:\Projects\Signature365\src\Apps\Subscriptions\Symprex.Signature365.Subscriptions\Api\Injection\OverrideInjectionStatusCommandHandler.cs`
- Query-backed definitions that bypass nested execution by supplying `Data` directly:
  - `D:\Projects\Signature365\src\Apps\Import\Symprex.Signature365.Import\Api\ImportRunEvents.cs`
  - `D:\Projects\Signature365\src\Apps\Import\Symprex.Signature365.Import\Api\CreateImportRunCommandHandler.cs`
  - `D:\Projects\Signature365\src\Apps\Outlook\Symprex.Signature365.Outlook\Api\Agent\AgentSettingsEvents.cs`
  - `D:\Projects\Signature365\src\Apps\Outlook\Symprex.Signature365.Outlook\Api\Agent\UpdateAgentSettingsCommandHandler.cs`
  - `D:\Projects\Signature365\src\Apps\ExchangeTransportAgent\Symprex.Signature365.ExchangeTransportAgent\Api\AddOnEvents.cs`
  - `D:\Projects\Signature365\src\Apps\ExchangeTransportAgent\Symprex.Signature365.ExchangeTransportAgent\Api\AddOnApiOperations.cs`
  - `D:\Projects\Signature365\src\Apps\Partners\Symprex.Signature365.Partners\Api\PartnerTenants\CreatePartnerTenantCommandHandler.cs`
- Direct-data and partial-data patterns:
  - `D:\Projects\Signature365\src\Apps\Portal\Symprex.Signature365.Portal\Api\Modules\Users\UserEvents.cs`
  - `D:\Projects\Signature365\src\Apps\Portal\Symprex.Signature365.Portal\Api\Modules\Users\DeleteUserCommand.cs`
  - `D:\Projects\Signature365\src\Apps\Portal\Symprex.Signature365.Portal\Api\Modules\Images\CompleteImageUploadCommand.cs`
  - `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common\Api\ResourceEventHelpers.cs`
  - `D:\Projects\Signature365\src\Apps\Portal\Symprex.Signature365.Portal\Api\Modules\AddOns\AddOnIdentityServerExtensions.cs`
- Explicit delete façade wrappers:
  - `D:\Projects\Signature365\src\Apps\Portal\Symprex.Signature365.Portal\Api\Modules\TenantUsers\PortalDeleteTenantUserCommand.cs`
  - `D:\Projects\Signature365\src\Apps\AdminPortal\Symprex.Signature365.AdminPortal\Api\Partners\PartnerUsers\AdminPortalDeletePartnerUserCommand.cs`
  - `D:\Projects\Signature365\src\Apps\Partners\Symprex.Signature365.Partners.Gateway\Api\PartnerUsers\PartnersGatewayDeletePartnerUserCommand.cs`

## What actually forces nested execution today

The legacy runtime does **not** perform nested execution for every resource event. The trigger is much narrower:

- the handler returned a `ResourceEvent`
- `resourceEvent.Data == null`
- `resourceEvent.SelfQuery != null`
- `resourceEvent.ChangeType != Deleted`

When those conditions hold, `ResourceEventHandler` creates a nested `ApiOperationContext`, sets `SkipAuthorisation = true`, and re-runs the executor to populate `Data`.

Two consequences matter for the rewrite:

1. `SelfQuery` is doing **two jobs** in the legacy design:
   - materialising `Data`
   - providing enough identity information to generate `Href`
2. A query-backed definition does **not** always mean nested execution. `ResourceEventDefinitionWithMapper<...>` inherits `ResourceEventDefinition<TResource>.New(TResource resource)`, so the call site can bypass `SelfQuery` entirely by supplying an API resource directly.

Deleted events are a third important nuance. Query-backed deletes often use `SelfQuery` only to generate `Href`; they intentionally skip `Data` population.

## Client-visible wire shape that must remain broadly compatible

`ResourceEventJsonTests` and the verified snapshot show the current envelope shape:

```json
{
  "$object": "event",
  "eventId": "createdInTest",
  "changeType": "Created",
  "created": "2021-07-07T00:00:00+00:00",
  "resourceObject": "my",
  "href": "https://www.my-api.com/my-resource/test-1234",
  "correlationId": "my-correlation-id",
  "data": {
    "id": "test-1234",
    "name": "My named resource",
    "$object": "my",
    "$links": {},
    "$partial": false
  },
  "changedValues": {
    "key1": "changed-value1"
  },
  "metadata": {
    "stringKey": "value1",
    "numberKey": 1,
    "booleanKey": true
  },
  "secureData": {
    "key1": "value1"
  }
}
```

The migration target is to preserve this broad envelope contract even if the authoring API changes substantially.

## Signature365 usage taxonomy

Definition counts are useful only as a taxonomy, not as a direct measure of nested execution:

- 19 query-backed definition families
- 12 direct-resource definition families
- 2 mapper-to-resource definition families
- 4 partial-resource helper call sites
- 3 explicit query-backed delete façade wrappers

The true nested cases are fewer because some query-backed definitions are already used with direct `Data`.

### 1. Query-backed and genuinely nested

These are the cases that really depend on the legacy `Data == null && SelfQuery != null` path.

Representative examples:

- `AccountEvents.*.New(stripeAccount)` in Billing
- `LicenseKeyEvents.*.New(licenseKey)` in Billing
- `InvoiceEvents.Refunded.New((tenantId, number))`
- `PartnerTenantEvents.Updated.New(new PartnerTenantId(...))`
- many Portal CRUD events such as `CampaignEvents.Created.New(campaign)` and `SignatureEvents.Updated.New(signature)`
- `SubscriptionEvents.InjectionStatusOverriden.New(chargebeeSubscription)`

These handlers do not supply `TResource` directly. They rely on the event definition or the `SelfQuery` constructor to re-project later.

### 2. Query-backed definition, but the call site already supplies `Data`

These do **not** need nested execution despite the definition shape.

Representative examples:

- `ImportRunEvents.Created.New(ImportApiMapper.MapFrom(...))`
- `ClientEvents.Created.New(ClientMapper.MapFrom(...))`
- `AgentSettingsEvents.Updated.New(AgentMapper.MapFrom(...))`
- `AddOnEvents.Enabled.New(apiResource)` in Exchange Transport Agent
- `PartnerTenantEvents.Created.New(PartnerTenantMapper.MapFrom(...))`
- explicit minimal delete stubs such as `UserEvents.Deleted.New(new UserApiResource { Id = this.Id })`

These are already close to the target direction.

### 3. Direct-data definitions

These never depended on nested execution.

Representative examples:

- `TenantEvents` with `TenantsMapper.MapFrom`
- `ImageEvents` with `ImageContainerApiResource.MapFrom`
- `VCardConfigurationEvents`
- `CustomerEvents`
- `PaymentSourceEvents`
- the main Subscriptions event families
- `AddInUserEvents.FieldsUpdated`

### 4. Partial-resource helper path

`ResourceEventHelpers.CreatePartialApiResourceEvent<TPartialApiResource, TApiResource>` already proves that the ecosystem sometimes wants:

- the **resource identity and `resourceObject` of the full resource**
- a **partial payload**
- no nested execution

Today this is achieved via direct `Data` plus a reflection-based override of `ResourceObject` and internal `ResourceType`. The rewrite should replace that hack with a first-class partial/tombstone concept.

### 5. Explicit query-backed delete façades

Portal, AdminPortal, and Partners Gateway have delete wrappers that delegate the actual delete elsewhere, then return `new ResourceDeleted<T>(Get...Query)`.

Those uses exist mainly so the event can carry the host-specific `Href` and resource identity without loading a final payload. This is an argument for **typed key-to-href generation**, not for keeping `SelfQuery`.

## Design constraints for the replacement contract

The replacement contract should:

- eliminate nested execution entirely
- eliminate the silent `SkipAuthorisation = true` post-execution read path
- preserve the broad event JSON shape
- keep delete events first-class when `Data` is legitimately absent
- make `Href` generation compile-time analyzable
- give `ChangedValues` an explicit story instead of an incidental runtime trick
- be easy to migrate with analyzers and code fixes
- avoid recreating a hidden re-query mechanism under a new name

## Candidate A - Materialised Resource Events

### Model

- remove `SelfQuery` entirely
- created and updated events require a materialised `TResource`
- deleted events use key-only authoring
- generated helpers build `Href` from resource or key
- optional generated diff helpers compute `ChangedValues`

### Example shape

```csharp
return CampaignEvents.Created(CampaignMapper.MapFrom(campaign));

return CampaignEvents.Updated(
    before: CampaignMapper.MapFrom(beforeCampaign),
    after: CampaignMapper.MapFrom(campaign));

return CampaignEvents.Deleted(new CampaignKey(operation.Id));
```

### Strengths

- simplest runtime
- strongest compile-time guarantees
- no DI-based projection or re-query
- cleanest AOT story

### Weaknesses

- highest migration burden for currently nested call sites
- handler authors must always provide the projected resource
- needs an explicit answer for where `before` snapshots come from

## Candidate B - ResourceKeyedProjection

### Model

- replace query constructors with typed keys
- keep `.New(domainEntity)` ergonomics at the definition/call-site level
- post-execution resolves `IResourceProjector<TResource, TKey>` from DI
- projector materialises `Data` directly without re-entering the pipeline
- deleted events use key only and skip projection

### Example shape

```csharp
private static readonly ResourceKeyedEventFactory<LicenseKeyApiResource, LicenseKey, LicenseKeyId> _factory =
    ResourceKeyedEvent
        .For<LicenseKeyApiResource, LicenseKey>()
        .WithKey(l => new LicenseKeyId(l.Code));

return LicenseKeyEvents.Created.New(licenseKey);
```

### Strengths

- smallest migration delta for existing query-backed `.New(domain)` call sites
- centralises projection logic per resource type
- separates `Href` identity from data materialisation better than `SelfQuery`

### Weaknesses

- still reintroduces a runtime read/projection facility after execution
- adds DI registration and failure modes for every projected resource
- risks hiding re-query behind a lighter abstraction
- does not materially improve the `ChangedValues` story on its own

## Candidate C - Explicit Snapshot / Partial / Tombstone Contract

### Model

- make the event shape explicit at authoring time
- use named variants rather than overloaded `New(...)` behavior
- no projection and no re-query anywhere in the runtime
- generate `Href` from resource or key
- carry explicit internal key and tenant metadata for audit/event-store use

### Example shape

```csharp
return UserEvents.Created.Snapshot(UserMapper.MapFrom(user));

return UserEvents.FieldsUpdated.Snapshot(
    before: UserMapper.MapFrom(beforeUser),
    after: UserMapper.MapFrom(user));

return AddOnEvents.Disabled.Partial(
    data: AddOnDisabledPartialApiResource.From(addOn),
    key: new AddOnKey(addOn.TenantId));

return UserEvents.Deleted.WithKey(new UserKey(operation.Id));
```

Rare migration bridge cases could allow an explicit key-only created/updated shape, but only behind an analyzer-controlled escape hatch:

```csharp
return InvoiceEvents.Refunded.WithKey(
    new InvoiceKey(operation.TenantId, operation.Number));
```

That shape should be treated as a temporary migration bridge, not the default public contract for created/updated events.

### Strengths

- most explicit and analyzable authoring model
- replaces the partial-resource reflection hack with a first-class shape
- makes delete and tombstone semantics obvious
- cleanest audit/event-store story because identity and tenant metadata are explicit
- easiest to test because the chosen shape is visible in code

### Weaknesses

- biggest upfront authoring API surface
- requires discipline so teams do not overuse key-only created/updated bridges
- still requires real migration decisions at each currently nested call site

## Cross-model review

The three models were reviewed independently by Claude Opus 4.6, Claude Sonnet 4.6, and GPT-5.4.

### Claude Opus 4.6

- preferred **Candidate A**
- valued its runtime simplicity and compile-time guarantees
- saw **Candidate B** as DI-heavy and architecturally close to a re-query facility
- liked the clarity of **Candidate C** but considered it a broader public API with more room for misuse

### Claude Sonnet 4.6

- preferred **Candidate C** as the base contract
- strongly warned that **Candidate B** solves the wrong problem because it keeps a post-execution materialisation facility alive
- recommended borrowing only the ergonomic insight from **Candidate B**, not the projector
- argued that a free-form `Reference` shape for created/updated events should not become normal practice

### GPT-5.4 review

- agrees with Sonnet that **Candidate B** should not be the primary design
- agrees with Opus that **Candidate A** contributes the strongest compile-time `Href` and diff story
- prefers **Candidate C** as the public contract because it makes missing snapshot data a deliberate, reviewable choice instead of hidden runtime behavior

## Recommended direction for the migration plan

Adopt **Candidate C as the public contract**, simplified and strengthened with parts of Candidate A.

### Adopt

1. **Remove `SelfQuery` from the long-term public/runtime contract.**
2. **Use explicit event shapes instead of hidden runtime behavior.**
3. **Keep created and updated events snapshot-first.**
4. **Keep deleted events key-first, with optional tombstone support.**
5. **Generate key-to-href helpers from link metadata at compile time.**
6. **Generate diff helpers for `ChangedValues` from before/after snapshots.**
7. **Carry explicit internal identity metadata for audit/event-store use instead of inferring tenant from `Data` via reflection.**

### Do not adopt

1. do not keep a general `SelfQuery` concept
2. do not keep a narrow runtime projector/re-query facility as the default resource-event path
3. do not let created/updated events become reference-only by accident

### Recommended public authoring shapes

```csharp
ResourceEvent<TResource>.Created.Snapshot(TResource data)
ResourceEvent<TResource>.Updated.Snapshot(TResource after)
ResourceEvent<TResource>.Updated.Snapshot(TResource before, TResource after)
ResourceEvent<TResource>.Updated.Partial(TPartialResource data, TKey key)
ResourceEvent<TResource>.Deleted.WithKey(TKey key)
ResourceEvent<TResource>.Deleted.Tombstone(TKey key, TResource lastKnown)
```

The exact surface naming can change during implementation, but the semantics should remain:

- `Created` and `Updated` default to snapshot-based shapes
- `Partial` is first-class and replaces today's reflection helper
- `Deleted` supports key-only and tombstone variants
- any key-only created/updated bridge must be explicit, analyzer-gated, and treated as migration-only

## Consequences for the implementation plan

### Generator work

- generate typed resource keys or key-binding metadata from resource/link descriptors
- generate key/data-to-`Href` binders from `[SelfLink]`
- generate event envelope helpers for snapshot, partial, and tombstone shapes
- generate or support serializer metadata for the preserved event envelope
- ensure resource-event results still participate in resource/link post-processing through the `IApiResourceCollection` replacement path

### Analyzer and migration-tooling work

- detect legacy `SelfQuery`-based event construction
- classify call sites into:
  - already snapshot-capable
  - needs explicit partial shape
  - needs deleted key/tombstone migration
  - rare bridge case needing explicit temporary key-only created/updated
- forbid or strongly gate key-only created/updated authoring
- offer code fixes for:
  - `new ResourceDeleted<T>(query)` -> `Deleted.WithKey(key)`
  - `definition.New(mappedResource)` -> `Snapshot(mappedResource)`

### Runtime work

- remove the nested `ResourceEventHandler` data-population path
- keep event-envelope stamping, link generation, and audit dispatch
- make audit/event-store code consume explicit internal key and tenant metadata
- allow `Data = null` as a normal, tested case for deleted/tombstone events

## Risks to watch during implementation

- `ChangedValues` becomes a silent regression if before-state is not supplied explicitly or otherwise planned for.
- `Href` generation must still work for deleted façades and host-specific route shapes.
- the migration must distinguish "query used to fetch data" from "query used only to build `Href`".
- `Reference`/key-only created or updated events must not quietly become normal usage.
- OpenAPI schemas and clients that inspect event payload shape must continue to see the same envelope fields, with `data: null` rather than omitted fields when no payload exists.

## Planning decision

For Blueprint.SourceGenerator, the working assumption should now be:

- **resource-event self-query is removed**
- **nested execution is not retained for resource events**
- **the rewrite uses explicit snapshot/partial/tombstone authoring plus generated key-to-href binding**

If an exceptional migration case still cannot fit that model, treat it as an analyzer-guided, explicitly temporary bridge rather than the basis for a new runtime re-query feature.
