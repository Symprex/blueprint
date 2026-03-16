# Resource events without nested execution — round 3

## Status

This document supersedes the **recommendation** in [`resource-events-no-nested-design-round-2.md`](resource-events-no-nested-design-round-2.md) where the two documents differ.

Round 2 is still useful for:

- the legacy behavior analysis
- the Signature365 usage taxonomy
- the decision to remove `SelfQuery` and nested execution
- the rationale for dropping `ChangedValues` as a design driver

Round 3 exists because round 2's flat factory surface (`Created / Updated / Deleted`) is too coarse to model the real Signature365 event landscape.

## What changed since round 2

### Event reasons are the real granularity

The legacy API's event identity is not just `ChangeType`. Every event has a **reason** embedded in `EventId`:

```
EventId = resourceObject + "." + reason
```

`ChangeType` (`Created / Updated / Deleted`) is only the coarse category. The reason is the fine-grained semantic.

Real evidence from `ResourceEvent<TResource>.CreateId(string eventSubId)`:

```csharp
public static string CreateId(string eventSubId)
{
    return ApiResource.GetTypeName(typeof(TResource)) + "." + eventSubId;
}
```

### Scale of real event reasons

Verified counts from the Signature365 codebase:

- **70+ distinct Updated reasons** across all resource types
- **7 distinct Created reasons** with custom names (plus many using the default `"created"`)
- Most Deleted events use the default `"deleted"` reason

Resources with the highest reason granularity:

| Resource | Distinct reasons |
|----------|-----------------|
| Microsoft365AddOnApiResource | 15 |
| BillingAccountApiResource | 10 |
| SubscriptionApiResource | 7 |
| ImportRunApiResource | 6 |
| VCardConfigurationApiResource | 5 |
| CampaignApiResource | 5 |
| LicenseKeyApiResource | 4 |
| ExchangeTransportAgentAddOnApiResource | 4 |

Representative reason strings from `Microsoft365Events`:

```csharp
_factory.Updated("enabled")
_factory.Updated("connected")
_factory.Updated("settings.updated")
_factory.Updated("domains.checked")
_factory.Updated("sentItems.settings.updated")
_factory.Updated("injection.settings.updated")
_factory.Updated("import.settings.updated")
```

### Why round 2 is too coarse

Round 2 recommended a flat factory with only:

```csharp
Microsoft365Events.Created(data)
Microsoft365Events.Updated(data)
Microsoft365Events.Deleted(key)
```

This collapses all 15 Microsoft365 update reasons into a single `Updated()` method. The reason string is lost, which means:

- the wire-format `EventId` cannot be populated correctly
- downstream consumers that filter on specific event reasons break silently
- there is no compile-time enforcement that each reason maps to the correct payload shape

### ChangedValues is removed entirely

Round 2 treated `ChangedValues` as optional passthrough. Round 3 removes it from the design entirely:

- the new envelope does not carry `changedValues`
- no `WithChangedValues()` extension or passthrough API
- the runtime model does not reserve storage or serialization for it
- the audit repository no longer needs any read-back to compute diffs

This is justified because Signature365's production `BusResourceEventAuditRepository` always returns `null` from `GetCurrentDataAsync(...)`, and no downstream consumer depends on `ChangedValues`.

### Save-only audit contract

`IResourceEventAuditRepository` becomes a save-only sink. No `GetCurrentDataAsync`. No read-back contract. The interface reduces to:

```csharp
public interface IResourceEventSink
{
    Task SaveAsync(ResourceEventEnvelope envelope, CancellationToken cancellationToken);
}
```

This decouples event authoring from any audit storage strategy and eliminates the last runtime reason for reading back state.

## Restated design goals (round 3)

1. No nested execution
2. No `SelfQuery`
3. No `ChangedValues`
4. **Every event reason declared once, named, and compile-time enforced**
5. Broad envelope compatibility for: `$object`, `eventId`, `changeType`, `created`, `resourceObject`, `href`, `correlationId`, `data`, `metadata`, `secureData`
6. Very low authoring friction for humans and AI agents
7. Compile-time `Href` binding from key or resource data
8. First-class support for deleted events with `data: null`
9. First-class support for partial payloads
10. Save-only audit contract

## Envelope contract (round 3)

```json
{
  "$object": "event",
  "eventId": "microsoft365AddOn.domains.checked",
  "changeType": "Updated",
  "created": "2025-01-15T10:30:00+00:00",
  "resourceObject": "microsoft365AddOn",
  "href": "https://api.example.com/microsoft365/{tenantId}",
  "correlationId": "abc-123",
  "data": { ... },
  "metadata": { ... },
  "secureData": { ... }
}
```

`changedValues` is absent. All other fields remain.

## The three round-3 designs

### Design A — Reasoned Flat Event Definitions

#### Core idea

Keep the explicit event-family anchor from round 2, but replace the generic `Created / Updated / Deleted` methods with **named methods per event reason**. Each reason is declared once in the family and carries its `ChangeType` and reason string internally.

#### Declaration

```csharp
[GenerateResourceEventFamily(typeof(Microsoft365AddOnApiResource), KeyType = typeof(Microsoft365Key))]
public static partial class Microsoft365Events
{
    [EventReason(ChangeType.Updated, "enabled")]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> Enabled(
        Microsoft365AddOnApiResource data);

    [EventReason(ChangeType.Updated, "domains.checked")]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> DomainsChecked(
        Microsoft365AddOnApiResource data);

    [EventReason(ChangeType.Updated, "sentItems.settings.updated")]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> SentItemsSettingsUpdated(
        Microsoft365AddOnApiResource data);

    [EventReason(ChangeType.Deleted)]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> Deleted(
        Microsoft365Key key);

    [EventReason(ChangeType.Updated, "disabled", Partial = true)]
    public static partial ResourceEventEnvelope<Microsoft365DisabledPartialApiResource> Disabled(
        Microsoft365DisabledPartialApiResource data, Microsoft365Key key);
}
```

#### Generated output

The source generator reads the partial method declarations and generates:

- the method body that constructs the envelope with the correct `EventId`, `ChangeType`, `resourceObject`, and `Href`
- compile-time validation that the reason string is unique within the family
- compile-time validation that the key-to-`Href` binding is resolvable

#### Call-site usage

```csharp
// In a handler
return Microsoft365Events.DomainsChecked(
    Microsoft365AddOnMapper.MapFrom(addOn));

return Microsoft365Events.SentItemsSettingsUpdated(
    Microsoft365AddOnMapper.MapFrom(addOn));

return Microsoft365Events.Deleted(new Microsoft365Key(tenantId));

return Microsoft365Events.Disabled(
    Microsoft365DisabledPartialApiResource.From(addOn),
    new Microsoft365Key(addOn.TenantId));
```

#### Strengths

- **Every reason is a named, discoverable method.** No string-based dispatch at the call site.
- **`EventId` composition is invisible to the handler author.** The generator handles `resourceObject + "." + reason`.
- **Partial payloads are first-class.** The `Partial = true` annotation and the separate resource type parameter make them explicit.
- **`ChangeType` is carried by the definition, not the call site.** The handler author never writes `ChangeType.Updated`.
- **AI-friendly.** An LLM can discover the exact set of methods on `Microsoft365Events` via IntelliSense or syntax tree.
- **Migration path is clear.** Each legacy `_factory.Updated("domains.checked")` maps 1:1 to a method declaration; each call site maps to a method invocation.
- **Compile-time reason uniqueness.** Duplicate reasons are caught at build time.

#### Weaknesses

- **Declaration surface scales with reason count.** Microsoft365 has 15 reasons, so the family file has 15 partial method declarations.
- **Partial method declarations require the generator to supply the body.** This is a well-supported Roslyn pattern but slightly less familiar than attribute-on-class.
- **Naming convention for method names needs a mechanical rule.** The conversion from `"sentItems.settings.updated"` to `SentItemsSettingsUpdated` must be consistent.

### Design B — Reason Records

#### Core idea

Each reason is a named record/type inside the event family. The generator reads the declarations and emits envelope plumbing for each record. The record itself is the call-site type.

#### Declaration

```csharp
public static class Microsoft365Events
{
    [ResourceEvent(ChangeType.Updated, "enabled")]
    public partial record Enabled(Microsoft365AddOnApiResource Data, Microsoft365Key Key);

    [ResourceEvent(ChangeType.Updated, "domains.checked")]
    public partial record DomainsChecked(Microsoft365AddOnApiResource Data, Microsoft365Key Key);

    [ResourceEvent(ChangeType.Updated, "sentItems.settings.updated")]
    public partial record SentItemsSettingsUpdated(Microsoft365AddOnApiResource Data, Microsoft365Key Key);

    [ResourceEvent(ChangeType.Deleted)]
    public partial record Deleted(Microsoft365Key Key);

    [ResourceEvent(ChangeType.Updated, "disabled", Partial = true)]
    public partial record Disabled(Microsoft365DisabledPartialApiResource Data, Microsoft365Key Key);
}
```

#### Generated output

The generator reads each partial record and generates:

- an implicit conversion or interface implementation that produces `ResourceEventEnvelope`
- the `EventId`, `ChangeType`, `resourceObject` wiring
- `Href` binding from the key

#### Call-site usage

```csharp
return new Microsoft365Events.DomainsChecked(
    Data: Microsoft365AddOnMapper.MapFrom(addOn),
    Key: new Microsoft365Key(tenantId));

return new Microsoft365Events.Deleted(Key: new Microsoft365Key(tenantId));
```

#### Strengths

- **Strongest type-system integration.** Each reason is its own type, which means pattern matching, exhaustiveness checking, and separate handler dispatch are all possible.
- **Declaration is extremely explicit.** The shape of every event is visible in the record signature.
- **Partial payloads are naturally typed.** Different data types per reason are obvious in the record constructor.

#### Weaknesses

- **Heaviest declaration surface.** Every reason requires a full record declaration with all parameters. For Microsoft365's 15 reasons, this is 15 record declarations.
- **Redundant key specification.** Nearly every record repeats `Microsoft365Key Key` — high boilerplate when the key type is always the same within a family.
- **Awkward generator relationship.** The generator must read partial records and add companions, which is less ergonomic than reading partial methods.
- **Noisier call site.** `new Microsoft365Events.DomainsChecked(Data: ..., Key: ...)` is longer than `Microsoft365Events.DomainsChecked(data)` because the key must always be supplied explicitly.
- **AI agent burden.** An LLM must understand record constructors and named parameters, which is more error-prone than a flat method signature.
- **Migration cost is higher.** Legacy `_factory.Updated("domains.checked").New(data)` → `new Microsoft365Events.DomainsChecked(Data: ..., Key: ...)` requires more structural change.

### Design C — Reason Specs with Tiny Terminal API

#### Core idea

Each reason is declared once as a spec object inside the family. The spec carries the `ChangeType` and reason string. Call sites use a fixed tiny terminal API (`.WithData(data)` or `.WithKey(key)` or `.WithPartial(data, key)`) to produce the envelope.

#### Declaration

```csharp
[GenerateResourceEventFamily(typeof(Microsoft365AddOnApiResource), KeyType = typeof(Microsoft365Key))]
public static partial class Microsoft365Events
{
    public static readonly EventSpec<Microsoft365AddOnApiResource, Microsoft365Key> Enabled =
        EventSpec.Updated("enabled");

    public static readonly EventSpec<Microsoft365AddOnApiResource, Microsoft365Key> DomainsChecked =
        EventSpec.Updated("domains.checked");

    public static readonly EventSpec<Microsoft365AddOnApiResource, Microsoft365Key> SentItemsSettingsUpdated =
        EventSpec.Updated("sentItems.settings.updated");

    public static readonly EventSpec<Microsoft365AddOnApiResource, Microsoft365Key> Deleted =
        EventSpec.Deleted();

    public static readonly EventSpec<Microsoft365DisabledPartialApiResource, Microsoft365Key> Disabled =
        EventSpec.Updated<Microsoft365DisabledPartialApiResource>("disabled");
}
```

#### Call-site usage

```csharp
return Microsoft365Events.DomainsChecked.WithData(
    Microsoft365AddOnMapper.MapFrom(addOn));

return Microsoft365Events.Deleted.WithKey(new Microsoft365Key(tenantId));

return Microsoft365Events.Disabled.WithPartial(
    Microsoft365DisabledPartialApiResource.From(addOn),
    new Microsoft365Key(addOn.TenantId));
```

#### Strengths

- **Declaration is compact.** Specs are single-line field declarations with no method body.
- **Reason strings stay visible.** The developer sees the exact reason string in the declaration.
- **Terminal API is consistent.** Only three terminal methods: `WithData`, `WithKey`, `WithPartial`.
- **No generator-driven partial methods or records.** The spec is a plain runtime object.

#### Weaknesses

- **Two-step call site.** `Microsoft365Events.DomainsChecked.WithData(data)` is two concepts: select the reason, then supply the payload. This is slightly more cognitive load than `Microsoft365Events.DomainsChecked(data)`.
- **Terminal API is string-untyped.** Nothing prevents calling `DomainsChecked.WithKey(key)` when data is expected, unless the spec has runtime validation.
- **Harder to add compile-time validation.** The generator cannot read spec field values at compile time as easily as it can read attributes or partial method signatures.
- **AI agents must learn two conventions.** The spec selection and the terminal call are separate knowledge.
- **`Href` binding is more complex.** The spec object must carry enough type information for the terminal method to resolve the key-to-`Href` binding, which pushes complexity into the spec's generic signature.
- **Partial payload typing is awkward.** `Disabled` uses a different generic parameter for the spec, which means the family has mixed spec types and cannot be iterated uniformly.
- **Runtime string in declarations.** The reason string `"domains.checked"` is a runtime constant, not an attribute value. This is fine for runtime behavior but harder for analyzers to validate at compile time.

## Comparative critique

### 1. Event reason modeling

| Criterion | Design A | Design B | Design C |
|-----------|----------|----------|----------|
| Reason is a named symbol | ✅ method name | ✅ type name | ✅ field name |
| Reason string is compile-time analyzable | ✅ attribute | ✅ attribute | ⚠️ runtime constant |
| EventId composition is hidden from call site | ✅ | ✅ | ✅ |
| ChangeType is hidden from call site | ✅ | ✅ | ✅ |
| Duplicate reason detection | ✅ compile-time | ✅ compile-time | ⚠️ runtime or analyzer |

All three designs correctly model each reason as a distinct named symbol. The critical difference is that **A and B carry the reason string in an attribute** where the generator can validate it at compile time, while **C carries it as a runtime string** that requires a separate analyzer pass or convention enforcement.

### 2. Authoring ergonomics

| Criterion | Design A | Design B | Design C |
|-----------|----------|----------|----------|
| Declaration lines per reason | 3 (partial method + attribute) | 2 (record + attribute) | 2 (field + initializer) |
| Key type repetition in declaration | ❌ none (family-level) | ⚠️ every record | ❌ none (field-level generic) |
| Call-site tokens for data event | ~1 (`Method(data)`) | ~3 (`new Type(Data:, Key:)`) | ~2 (`Spec.WithData(data)`) |
| Call-site tokens for key-only delete | ~1 (`Method(key)`) | ~2 (`new Type(Key:)`) | ~2 (`Spec.WithKey(key)`) |
| Partial payload call-site tokens | ~2 (`Method(data, key)`) | ~3 (`new Type(Data:, Key:)`) | ~3 (`Spec.WithPartial(data, key)`) |

**Design A has the smallest call-site surface.** The handler author writes one method call per event. The method signature already encodes which parameters are needed, so there is no risk of calling `WithData` on a delete spec or omitting a key on a partial.

**Design B is the most declaration-heavy.** Every record repeats the key type in its constructor, and the call site requires named parameter syntax (`Data:`, `Key:`). For 15 Microsoft365 reasons, this adds substantial visual noise.

**Design C is in between.** The declaration is compact, but the call site has a two-concept structure (select spec, then call terminal) that is slightly harder to read and more error-prone.

### 3. Migration cost

| Migration step | Design A | Design B | Design C |
|----------------|----------|----------|----------|
| Declare family anchor | 1 attribute | none (implicit in class) | 1 attribute |
| Declare each reason | 1 partial method per reason | 1 record per reason | 1 field per reason |
| Migrate `_factory.Updated("reason").New(data)` | → `Events.Reason(data)` | → `new Events.Reason(Data: data, Key: key)` | → `Events.Reason.WithData(data)` |
| Migrate `_factory.Deleted().New(query)` | → `Events.Deleted(key)` | → `new Events.Deleted(Key: key)` | → `Events.Deleted.WithKey(key)` |
| Migrate partial helper | → `Events.Disabled(partialData, key)` | → `new Events.Disabled(Data: partialData, Key: key)` | → `Events.Disabled.WithPartial(partialData, key)` |
| Naming convention needed | ✅ reason string → PascalCase method | ✅ reason string → PascalCase type | ❌ field name is free-form |

**Design A has the most mechanical migration.** Each legacy factory call maps 1:1 to a method call. The reason string moves from the call site into the declaration attribute. The call site shrinks.

**Design B has the highest migration cost.** Each legacy call must be rewritten to a record constructor with named parameters, and the key must be explicitly supplied even when the legacy code relied on implicit identity.

**Design C migration is moderate** but has a risk: the two-step call site (`Spec.WithData`) is a new pattern that does not exist anywhere in the legacy code, so every migration site requires learning the terminal API.

### 4. AI friendliness

| Criterion | Design A | Design B | Design C |
|-----------|----------|----------|----------|
| Method discovery via IntelliSense | ✅ direct methods | ⚠️ nested types | ✅ fields then methods |
| Single-step call site | ✅ | ✅ | ❌ two-step |
| Parameter correctness from signature | ✅ | ✅ | ⚠️ terminal API is generic |
| Risk of wrong terminal method | N/A | N/A | ⚠️ `WithData` vs `WithKey` confusion |
| Discoverable by syntax tree search | ✅ method invocation | ✅ object creation | ⚠️ member access + invocation |

**Design A is the most AI-friendly.** An LLM can:

1. Find `Microsoft365Events` in the project
2. List its methods
3. Pick the right method name
4. Fill in the parameters from the method signature

There is no two-step reasoning. The method signature tells the LLM exactly what to provide. Wrong payload types are caught by the compiler.

**Design B is moderately AI-friendly** but the record constructor pattern with named parameters is more error-prone for LLMs, especially when the key must always be supplied.

**Design C is the least AI-friendly** because the LLM must:

1. Find the spec field
2. Know which terminal method to call
3. Not confuse `WithData` / `WithKey` / `WithPartial`
4. Handle the fact that the spec is generic and the terminal method may accept different types

## Recommendation

**Adopt Design A — Reasoned Flat Event Definitions.**

### Why Design A wins

1. **It solves the round-2 granularity gap.** Every event reason becomes a named method. No string-based dispatch remains at the call site.

2. **It has the smallest call-site surface.** Handler code is one method call per event. The method signature encodes all payload requirements.

3. **It has the most mechanical migration path.** Each legacy `_factory.Updated("reason").New(data)` maps to `Events.Reason(data)`. The reason string moves to the declaration attribute, not the call site.

4. **It is the most AI-friendly design.** A single method invocation is the easiest pattern for an LLM to discover and use correctly.

5. **It has strong compile-time guarantees.** The generator validates reason uniqueness, key-to-`Href` binding, and payload type correctness at build time.

6. **It preserves round-2's family anchor concept.** The `[GenerateResourceEventFamily]` attribute provides the resource type and key type once, at the family level.

7. **Declaration cost is proportional to actual domain complexity.** Microsoft365 has 15 reasons because it genuinely has 15 distinct event semantics. That complexity should be visible and named, not hidden behind a generic `Updated()` overload.

### Why not Design B

Design B is the strongest type-system design, but the declaration cost is too high for the migration. Repeating the key type in every record constructor, requiring named parameter syntax at every call site, and the awkward generator/partial-record relationship make it unsuitable for a codebase with 70+ event reasons.

### Why not Design C

Design C's two-step call site (`Spec.WithData()`) adds cognitive load, is harder for AI agents, and is harder to validate at compile time because the reason string is a runtime constant rather than an attribute value. The terminal API also has a confusion risk where `WithData`, `WithKey`, and `WithPartial` are not constrained by the type system to the correct usage.

## How the save-only audit contract changes the runtime model

### Before (legacy)

```
Handler returns ResourceEvent
  → ResourceEventHandler checks if Data == null && SelfQuery != null
    → If yes: nested execution to populate Data
    → Then: IResourceEventAuditRepository.GetCurrentDataAsync() for previous state
    → Then: compute ChangedValues diff
    → Then: IResourceEventAuditRepository.SaveAsync()
    → Then: publish to bus
```

The audit repository was both a **read** and **write** contract. The read (`GetCurrentDataAsync`) existed solely to support `ChangedValues` computation.

### After (round 3)

```
Handler returns ResourceEventEnvelope (fully populated)
  → Post-processing stamps Href, correlationId, created timestamp
  → IResourceEventSink.SaveAsync(envelope) — write only
  → Publish to bus
```

The runtime model simplifies to:

1. **No read-back.** The sink never reads previous state. It only writes.
2. **No nested execution.** The handler provides all data. The runtime adds only envelope metadata.
3. **No diff computation.** `ChangedValues` does not exist in the envelope.
4. **Single responsibility.** The sink is a dumb write endpoint. Event consumers (bus subscribers, projections) handle their own state tracking.

### Consequences

- The `IResourceEventSink` interface is trivially mockable for testing.
- There is no hidden performance cost from read-before-write in the event pipeline.
- The sink can be an in-memory stub, a queue publisher, an event store writer, or any combination without affecting the event authoring model.
- The correlation between "save the event" and "publish the event" becomes an explicit composition choice in the pipeline, not an implicit side-effect of the audit repository.

## What humans and AI agents need to do

### To define event reasons

#### Human work

1. **Choose the family anchor.** Decide which resource type and key type the family serves. Create the `[GenerateResourceEventFamily]` partial class.

2. **Declare each reason.** For each distinct event in the legacy code, add a partial method with an `[EventReason]` attribute. The attribute carries the `ChangeType` and the reason string. The method name is a PascalCase conversion of the reason (or a human-chosen name if the reason string is awkward).

3. **Choose the payload shape per reason.** Most reasons take the full `TApiResource`. Partial reasons take a dedicated partial resource type and a key. Delete reasons take only a key.

4. **Review generator diagnostics.** Fix any missing self-link bindings, duplicate reason strings, or unresolvable key types.

#### AI-agent work

1. **Find the event family file** in the correct namespace/project (or create one from the `[GenerateResourceEventFamily]` template).

2. **Add partial method declarations** with `[EventReason]` attributes. Use the reason string from the legacy `_factory.Updated("reason")` call.

3. **Convert the reason string to a PascalCase method name** using a consistent mechanical rule (split on `.` and `-`, PascalCase each segment, concatenate).

4. **Match the payload type** from the legacy definition's resource type. For partial events, use the partial resource type.

5. **Do not invent extra reasons.** Only declare reasons that exist in the legacy code or are explicitly requested.

### To use event reasons in handlers

#### Human work

1. **Replace legacy `.New(data)` calls** with the named method: `Microsoft365Events.DomainsChecked(data)`.

2. **For previously nested sites**, insert a mapper call that produces the `TApiResource` from the domain entity. This is the one place where non-mechanical work is needed.

3. **For delete sites**, replace `new ResourceDeleted<T>(query)` with `Events.Deleted(new Key(...))`.

4. **For partial sites**, replace `ResourceEventHelpers.CreatePartialApiResourceEvent<TPartial, TFull>(...)` with `Events.Reason(partialData, key)`.

5. **Do not supply `ChangedValues`.** It does not exist in the design.

#### AI-agent work

1. **Find the generated event family** for the resource type being handled.

2. **Choose the correct method name** by matching the legacy reason string to the PascalCase method.

3. **Supply the correct parameters.** The method signature tells the agent exactly what to provide:
   - `data` for full-payload events
   - `key` for delete events
   - `(data, key)` for partial events

4. **Where legacy code relied on SelfQuery, insert a mapper.** The agent should look for the resource's mapper type and call it.

5. **Never introduce `ChangedValues`.** There is no mechanism for it.

## Recommended naming convention

The conversion from legacy reason string to method name should follow this mechanical rule:

1. Split the reason string on `.` and `-`
2. PascalCase each segment
3. Concatenate

Examples:

| Legacy reason string | Method name |
|---------------------|-------------|
| `"enabled"` | `Enabled` |
| `"domains.checked"` | `DomainsChecked` |
| `"sentItems.settings.updated"` | `SentItemsSettingsUpdated` |
| `"injection.settings.updated"` | `InjectionSettingsUpdated` |
| `"subscribed-with-license-key"` | `SubscribedWithLicenseKey` |
| `"quantity.increased"` | `QuantityIncreased` |
| `"subscription.autoRenewal.changed"` | `SubscriptionAutoRenewalChanged` |
| `"created"` | `Created` |
| `"deleted"` | `Deleted` |

Default reasons (`"created"`, `"updated"`, `"deleted"`) become simply `Created`, `Updated`, `Deleted` methods.

## Key migration risks

### 1. Naming collisions

Two different reason strings could map to the same PascalCase method name. This is unlikely given the current corpus but must be validated by the generator with a diagnostic.

### 2. Previously nested sites require mapper work

The 19 query-backed definitions in Signature365 include sites that today rely on nested execution to populate `Data`. Each of these must be migrated to explicit mapper calls. This is the hardest migration step because:

- the mapper may not exist yet
- the handler may not have access to the full domain entity needed for mapping
- some handlers rely on the nested execution's `SkipAuthorisation` to read back the resource

**Mitigation:** Enumerate all query-backed definitions that genuinely nest (the round-1 taxonomy is a starting point). Create or verify mappers for each. The remaining sites that supply data directly are mechanical renames.

### 3. Reason string drift

If a new reason is added in the legacy code but not declared in the new family, the event will fail at compile time (no method exists). This is **intentional** — compile-time enforcement is better than silent runtime string dispatch — but it means that teams adding new events must update the family declaration.

### 4. Partial payload type proliferation

Resources with multiple partial event shapes will need multiple partial resource types. Today this is handled with a single reflection helper. The new design makes each partial type explicit, which increases the type count but also makes the contract reviewable.

### 5. Cross-host event families

Some resources emit events from multiple hosts (Portal, AdminPortal, PartnersGateway). The family anchor lives in a specific namespace/project. Cross-host consumption needs either:

- a shared contract project that declares the family
- or host-specific families that share the same `resourceObject` and reason strings

This is an existing architectural question that the round-3 design does not change, but the explicit family anchor makes it more visible.

### 6. AI agent confusion with method overloads

If a resource has both a full-data `Enabled(data)` and a partial `Enabled(partialData, key)` for the same reason, method overload resolution could confuse AI agents. **Recommendation:** use distinct method names for partial variants (e.g., `DisabledPartial` or declare partial as a separate reason with a distinct name).

## Example: complete Microsoft365 family declaration

```csharp
[GenerateResourceEventFamily(
    typeof(Microsoft365AddOnApiResource),
    KeyType = typeof(Microsoft365Key))]
public static partial class Microsoft365Events
{
    [EventReason(ChangeType.Updated, "enabled")]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> Enabled(
        Microsoft365AddOnApiResource data);

    [EventReason(ChangeType.Updated, "connected")]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> Connected(
        Microsoft365AddOnApiResource data);

    [EventReason(ChangeType.Updated, "settings.updated")]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> SettingsUpdated(
        Microsoft365AddOnApiResource data);

    [EventReason(ChangeType.Updated, "additionalScopesGranted")]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> AdditionalScopesGranted(
        Microsoft365AddOnApiResource data);

    [EventReason(ChangeType.Updated, "verified")]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> Verified(
        Microsoft365AddOnApiResource data);

    [EventReason(ChangeType.Updated, "disabled")]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> Disabled(
        Microsoft365AddOnApiResource data);

    [EventReason(ChangeType.Updated, "importScheduled")]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> ImportScheduled(
        Microsoft365AddOnApiResource data);

    [EventReason(ChangeType.Updated, "domains.checked")]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> DomainsChecked(
        Microsoft365AddOnApiResource data);

    [EventReason(ChangeType.Updated, "sentItems.enabled")]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> SentItemsEnabled(
        Microsoft365AddOnApiResource data);

    [EventReason(ChangeType.Updated, "sentItems.disabled")]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> SentItemsDisabled(
        Microsoft365AddOnApiResource data);

    [EventReason(ChangeType.Updated, "sentItems.settings.updated")]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> SentItemsSettingsUpdated(
        Microsoft365AddOnApiResource data);

    [EventReason(ChangeType.Updated, "injection.enabled")]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> InjectionEnabled(
        Microsoft365AddOnApiResource data);

    [EventReason(ChangeType.Updated, "injection.disabled")]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> InjectionDisabled(
        Microsoft365AddOnApiResource data);

    [EventReason(ChangeType.Updated, "injection.settings.updated")]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> InjectionSettingsUpdated(
        Microsoft365AddOnApiResource data);

    [EventReason(ChangeType.Updated, "import.settings.updated")]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> ImportSettingsUpdated(
        Microsoft365AddOnApiResource data);

    [EventReason(ChangeType.Deleted)]
    public static partial ResourceEventEnvelope<Microsoft365AddOnApiResource> Deleted(
        Microsoft365Key key);
}
```

## Example: handler migration

### Before (legacy)

```csharp
return Microsoft365Events.DomainsChecked.New(microsoft365AddOn);
```

### After (round 3)

```csharp
return Microsoft365Events.DomainsChecked(
    Microsoft365AddOnMapper.MapFrom(microsoft365AddOn));
```

### Before (legacy delete façade)

```csharp
return new ResourceDeleted<UserApiResource>(new PortalGetUserQuery { Id = operation.Id });
```

### After (round 3)

```csharp
return UserEvents.Deleted(new UserKey(operation.Id));
```

### Before (legacy partial helper)

```csharp
return ResourceEventHelpers.CreatePartialApiResourceEvent<AddOnDisabledPartialApiResource, AddOnApiResource>(
    data: AddOnDisabledPartialApiResource.From(addOn),
    tenantId: addOn.TenantId);
```

### After (round 3)

```csharp
return AddOnEvents.Disabled(
    AddOnDisabledPartialApiResource.From(addOn),
    new AddOnKey(addOn.TenantId));
```

## Explicit round-3 decisions

1. `ChangedValues` is **removed from the design entirely**. Not optional. Not passthrough. Gone.
2. The audit contract is **save-only**. `IResourceEventSink` has one method: `SaveAsync`.
3. **Every event reason is a named partial method** in an event family, carrying `ChangeType` and reason string in an attribute.
4. **EventId is generated** from `resourceObject + "." + reason`. The handler author never composes it.
5. **Flat method invocation** is the call-site shape. No builder, no record constructor, no two-step spec.
6. **Partial payloads** are modeled as methods with a dedicated partial resource type and a key parameter.
7. **Deleted events** are key-only methods.
8. **Design A (Reasoned Flat Event Definitions)** is the recommended direction.
9. No nested execution. No `SelfQuery`. No read-back.

## Planning decision

For the Blueprint.SourceGenerator migration, the working round-3 assumption is:

- no `SelfQuery`
- no nested execution for resource events
- no `ChangedValues` at all
- save-only audit sink
- generated named methods per event reason within explicit family anchors
- compile-time key/data-to-`Href` binding
- first-class partial payload support
- mechanical migration rule: reason string → PascalCase method name

This is the design direction that should drive the module implementation unless later evidence shows a concrete incompatibility.
