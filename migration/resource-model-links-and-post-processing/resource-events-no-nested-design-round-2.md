# Resource events without nested execution - round 2

## Status

> Note:
> Round 2's recommendation of a generic flat factory (`Created / Updated / Deleted`) is now
> superseded by [`resource-events-no-nested-design-round-3.md`](resource-events-no-nested-design-round-3.md).
> Round 3 addresses the event-reason granularity gap (70+ distinct reasons in Signature365),
> removes `ChangedValues` entirely, and adopts a save-only audit contract.

This document supersedes the **recommendation** in [`resource-events-no-nested-design.md`](resource-events-no-nested-design.md) where the two documents differ.

The round-1 document is still useful for:

- the legacy behavior analysis
- the Signature365 usage taxonomy
- the envelope compatibility evidence

Round 2 exists because one of round 1's biggest complexity drivers turned out not to matter in the real Signature365 production path.

## What changed since round 1

The legacy envelope still exposes `ChangedValues`, but Signature365 does not materially rely on it.

Verified evidence:

- `ResourceEventHandler` only computes `ChangedValues` when `resourceEvent.ChangeType == Updated`, `resourceEvent.Data != null`, and `resourceEventAuditRepository.GetCurrentDataAsync(...)` returns a previous snapshot.
- Signature365's production audit repository, `BusResourceEventAuditRepository`, always returns `null` from `GetCurrentDataAsync(...)`.
- The EventStore message mapper persists `Data`, `Metadata`, and `CorrelationId`, but does not build its contract around `ChangedValues`.

That means:

- `ChangedValues` should be treated as **compatibility ballast / optional passthrough**
- it should **not** drive the public authoring model
- the no-nested design can be materially simpler than the round-1 recommendation

## Evidence specific to the round-2 change

- `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\Middleware\ResourceEventHandler.cs`
- `D:\Projects\Signature365\src\Apps\EventStore\Symprex.Signature365.EventStore.Contract\Domain\BusResourceEventAuditRepository.cs`
- `D:\Projects\Signature365\src\Apps\EventStore\Symprex.Signature365.EventStore.Contract\Messages\EventMapper.cs`

## Restated design goals

The no-nested resource-event design should now optimize for:

1. no nested execution
2. no `SelfQuery`
3. broad envelope compatibility
4. very low authoring friction for humans and AI agents
5. compile-time `Href` binding from key or resource data
6. first-class support for deleted events with `data: null`
7. first-class support for partial payloads
8. `ChangedValues` remaining optional and explicit, never required

## The three round-2 designs

Round 2 used three distinct designs:

1. **TEA** - Typed Event Announcement
2. **Flat Factory**
3. **ResourceEnvelopeBuilder**

The goal was not just to pick a winner, but to test where the complexity should live:

- at the declaration site
- at the call site
- or in a generated builder/state machine

## Design 1 - TEA (Typed Event Announcement)

### Core idea

Each event variant is declared explicitly as a named record in the contract project, and the source generator reads that declaration to generate the event envelope plumbing.

Example direction:

```csharp
public static class UserEvents
{
    [ResourceEvent(ChangeType.Created)]
    public partial record Created(UserApiResource Data, UserKey Key);

    [ResourceEvent(ChangeType.Updated)]
    public partial record Updated(UserApiResource Data, UserKey Key);

    [ResourceEvent(ChangeType.Deleted)]
    public partial record Deleted(UserKey Key);
}
```

### What a human needs to do to define it

- create the event family type
- declare each event variant explicitly
- choose which variants carry `Data` and which are key-only
- annotate partial or tombstone variants where needed

### What an AI agent needs to do to define it

- discover the event family file
- add or update the relevant record declaration
- ensure constructor shape matches the intended payload and key requirements
- keep attributes aligned with `ChangeType`

### What a human needs to do to use it in handlers

- map domain data to the resource payload when required
- create the named event type directly

Example:

```csharp
return new UserEvents.Created(
    Data: UserMapper.MapFrom(user),
    Key: new UserKey(user.Id));
```

### What an AI agent needs to do to use it in handlers

- find the record declaration
- read its constructor parameters
- construct the correct record instance
- optionally set passthrough properties such as `ChangedValues` only if explicitly asked

### Strengths

- strongest declaration-site explicitness
- very easy to inspect once the declarations exist
- partial and deleted shapes are obvious in the type system

### Weaknesses

- the heaviest declaration surface
- awkward generator/input relationship because the generator is driven by partial record declarations that also need generated companions
- more work up front before any handler migration can start
- better for library authors than for large-scale call-site migration

## Design 2 - Flat Factory

### Core idea

Keep event families, but simplify the authoring surface hard:

- `Created(data)`
- `Updated(data)`
- `Deleted(key)`
- `Partial(data, key)`
- optional future `DeletedWithData(key, lastKnown)` if a tombstone shape becomes valuable later

No before/after default overloads. No computed `ChangedValues`. No nested fluent shapes.

Example direction:

```csharp
[GenerateResourceEventFamily(typeof(UserApiResource), KeyType = typeof(UserKey))]
public static partial class UserEvents;
```

Generated usage:

```csharp
return UserEvents.Created(UserMapper.MapFrom(user));
return UserEvents.Updated(UserMapper.MapFrom(user));
return UserEvents.Partial(UserEmailPartialApiResource.From(user), new UserKey(user.Id));
return UserEvents.Deleted(new UserKey(operation.Id));
```

### What a human needs to do to define it

- declare a single event family anchor
- identify the full resource type
- specify or allow inference of the key type
- define partial payload resource types only where they really exist

### What an AI agent needs to do to define it

- add the family anchor in the right namespace/project
- wire the correct resource type and key type
- add any partial payload resource types or annotations only where the family truly needs them

### What a human needs to do to use it in handlers

- for created/updated events, return the mapped resource
- for deleted events, return the key
- for partial events, return a partial payload and key

### What an AI agent needs to do to use it in handlers

- find the generated event family
- choose one of a very small number of obvious methods
- map the domain object when a nested legacy site previously relied on `SelfQuery`
- leave `ChangedValues` alone unless a task explicitly asks for passthrough data

### Strengths

- smallest usage-site cognitive load
- simplest migration from legacy call sites
- most LLM-friendly call-site surface
- lowest generator complexity of the three designs
- best fit now that `ChangedValues` is not a driver

### Weaknesses

- less declaration-site explicit than TEA
- needs discipline so factory overloads do not grow back into a complex API
- partial payload typing still needs a clean compile-time story

## Design 3 - ResourceEnvelopeBuilder

### Core idea

Use a generated builder/spec object so key and data are supplied in a constrained order.

Example direction:

```csharp
return UserEvents.Updated
    .For(new UserKey(user.Id))
    .WithData(UserMapper.MapFrom(user));

return UserEvents.Deleted
    .For(new UserKey(operation.Id));
```

### What a human needs to do to define it

- declare the family anchor
- let the generator emit the constrained builder API
- define partial payload types when needed

### What an AI agent needs to do to define it

- very similar to Flat Factory at declaration time
- more generator assumptions need to be understood if the builder shape changes

### What a human needs to do to use it in handlers

- supply the key first
- then decide whether to attach data
- learn the builder's terminal shape and any optional chaining

### What an AI agent needs to do to use it in handlers

- understand the builder ordering rules
- construct the right chain without accidentally choosing optional enrichment methods
- know whether the builder returns an envelope directly or requires a final `Build()` call

### Strengths

- strongest usage-site constraints
- naturally models deleted key-only events
- good at preventing accidental omission of key data

### Weaknesses

- the noisiest handler code
- more generated API surface than Flat Factory
- less AI-friendly because the author has to understand the chain rather than one direct call
- unresolved terminal-shape question (`Build()`, implicit conversion, or direct envelope return)

## Cross-model review

### Inputs

- Claude Opus 4.6 proposed **TEA**
- Claude Sonnet 4.6 proposed **Flat Factory**
- GPT-5.4 proposed **ResourceEnvelopeBuilder** as a stress-test of whether stronger sequencing justified a richer API

### Convergence after review

Although the proposals started in different places, the review convergence was strong:

- **all three reviews agreed that removing `ChangedValues` as a core driver should flatten the public API**
- **TEA was liked for discoverability, but judged too declaration-heavy for the migration**
- **Builder was liked for constraints, but judged too verbose and too complex for routine handler authoring**
- **Flat Factory or a Flat Factory hybrid was the clearest winner for usage-site ergonomics, migration cost, and AI friendliness**

### What the reviews agreed on

1. The public API should not force humans or AI agents to think about before/after snapshots by default.
2. `ChangedValues` should remain an optional passthrough field only.
3. Deleted events should be explicitly key-based and should happily serialize `data: null`.
4. Partial payloads should become first-class rather than relying on the current reflection helper.
5. The new design should use compile-time key-to-`Href` binding rather than preserving any query-shaped identity object.

### What the reviews disagreed on

- whether the declaration anchor should be:
  - explicit event-family types
  - or an attribute directly on the resource type

The practical constraint from Signature365 usage is that **multiple namespaces and hosts may want distinct event families around related resources**, so an explicit family anchor is the safer recommendation.

## Round-2 recommendation

### Recommended public shape

Adopt a **generated Flat Factory surface with an explicit event-family anchor**.

Recommended direction:

```csharp
[GenerateResourceEventFamily(typeof(UserApiResource), KeyType = typeof(UserKey))]
public static partial class UserEvents;
```

Generated public usage:

```csharp
UserEvents.Created(UserApiResource data)
UserEvents.Updated(UserApiResource data)
UserEvents.Partial(UserEmailPartialApiResource data, UserKey key)
UserEvents.Deleted(UserKey key)
```

Optional compatibility-only passthrough:

```csharp
UserEvents.Updated(data).WithChangedValues(diff)
```

That passthrough should be deliberate and rare, not part of the default event-family shape. It can be an extension/helper on the returned envelope type rather than a reason to complicate the factory surface.

### Why this wins

- it keeps the usage-site surface tiny
- it makes migration mostly mechanical where data is already supplied
- it makes the genuinely nested legacy sites obvious: they are the places where a mapper now has to be inserted
- it keeps declaration cost low
- it is the easiest surface for future AI agents to discover and use correctly

## Work required from humans and AI agents

### 1. Defining event families

### Human work

- declare the event family anchor in the correct host/contract namespace
- confirm the resource type and key type
- decide whether the family needs any partial payload resource types
- review generated diagnostics for missing self-link or key binding information

### AI-agent work

- add the event family anchor with the correct resource and key type
- create or wire partial payload resource types only when existing behavior proves they are needed
- avoid inventing extra variants because the generated family should stay minimal

### 2. Using event families in handlers

### Human work

- for currently nested sites, choose the correct mapper and return the resource explicitly
- for deleted sites, pass the key only
- for partial sites, map to the dedicated partial resource and pass the key

### AI-agent work

- replace legacy `.New(...)` or `new ResourceDeleted<T>(query)` usage with the generated flat methods
- where legacy nested execution existed, insert the resource mapper rather than preserving any query-shaped indirection
- avoid introducing `ChangedValues` unless the task explicitly requires passthrough compatibility

## Migration mapping from legacy patterns

### Already direct-data or already mapped

Mechanical migration:

```csharp
// before
return ImportRunEvents.Created.New(ImportApiMapper.MapFrom(importRun));

// after
return ImportRunEvents.Created(ImportApiMapper.MapFrom(importRun));
```

### Query-backed and genuinely nested

These require explicit mapping work:

```csharp
// before
return AccountEvents.Updated.New(stripeAccount);

// after
return AccountEvents.Updated(AccountMapper.MapFrom(stripeAccount));
```

### Delete façades

These become key-only:

```csharp
// before
return new ResourceDeleted<UserApiResource>(new PortalGetUserQuery { Id = operation.Id });

// after
return UserEvents.Deleted(new UserKey(operation.Id));
```

### Partial helper sites

These stop using reflection and become first-class partial events:

```csharp
// before
return ResourceEventHelpers.CreatePartialApiResourceEvent<AddOnDisabledPartialApiResource, AddOnApiResource>(...);

// after
return AddOnEvents.Partial(AddOnDisabledPartialApiResource.From(addOn), new AddOnKey(addOn.TenantId));
```

## Consequences for the generator and analyzers

### Generator

- generate event families from an explicit family anchor
- bind `Href` from resource data or key using compile-time link metadata
- emit envelope serialization with `data: null` preserved for deleted events
- support optional `ChangedValues` passthrough without generating diff logic
- support partial payload types as first-class event inputs

### Analyzers

- flag legacy `SelfQuery`-based resource-event construction
- offer code fixes for straightforward method renames
- flag families with no analyzable self-link/key binding
- flag partial event usage where the payload type is not declared as a partial resource
- discourage or ban additional ad-hoc overloads beyond the generated surface

## Explicit round-2 decisions

- `ChangedValues` is **not** a core design driver for the source-generated rewrite.
- No default public API should require before/after snapshots.
- Flat Factory is preferred over declaration-heavy records and over builder-style chains.
- Use an explicit event-family anchor rather than hanging generation directly from the resource type.
- Deleted events should be key-only by default.
- Partial payloads should be first-class.
- Any richer deleted-with-data or tombstone shape should be a later additive decision, not required for the initial migration.

## Planning decision

For the Blueprint.SourceGenerator migration, the working round-2 assumption should now be:

- no `SelfQuery`
- no nested execution for resource events
- no `ChangedValues`-driven API complexity
- generated flat event families with explicit family anchors
- compile-time key/data-to-`Href` binding
- first-class partial payload support

This is the design direction that should now drive the module plan unless later evidence shows a concrete incompatibility that the simpler model cannot cover.
