# Source-Generated Resource Events — Feasibility & Findings

## Executive Summary

The current Blueprint resource event architecture is **already well-optimized** with lazy dictionary allocation, compiled property accessors, and modular pipeline stages. Full source-generation of resource events is **technically feasible** but offers diminishing returns for the effort. The recommended path is targeted improvements to eliminate the remaining runtime reflection.

## Current Architecture

### Resource Event Flow (per operation)

```
Handler returns ResourceEvent<T>
  → ResourceEventSelfQueryModule (stage -5): resolve SelfQuery if Data is null
  → LinkGenerationModule (stage 0): populate $links.self on resource Data
  → ResourceExpansionModule (stage 1010): expand the event's Expandable<T> data
  → ResourceEventAuditModule (stage 1020): stamp, make Href, extract EventId + ResourceId, invoke audit sink
  → PipelineReturnModule: serialize envelope to HTTP response
```

### Key Types

| Type | Role | Location |
|------|------|----------|
| `ResourceEvent` | Legacy base class (S365 uses) | `Blueprint.Runtime.Http` |
| `ResourceCreated<T>` / Updated / Deleted | Convenience constructors | `Blueprint.Runtime.Http` |
| `ResourceEventResult` | New source-gen-oriented type | `Blueprint.Runtime.Exec` |
| `ResourceEventDefinition<T>` | Factory pattern (S365 `ClientEvents.Created`) | `Blueprint.Runtime.Http` |
| `IResourceEventAuditSink` | DI-registered audit hook | `Blueprint.Runtime.Auditing` |

### S365 Usage Patterns

1. **Inline Data** (most common): `return new ResourceCreated<TResource>(resource)`
2. **Self-Query** (deferred loading): `return new ResourceCreated<TResource>(new GetQuery { Id = x })`
3. **Factory + SecureData**: `return Events.Created.New(resource).WithSecureData("key", secret)`
4. **Custom subclass** (rare): `return new TenantCreatedEvent(resource)` extending ResourceCreated

## Source-Generation Opportunities

### ✅ Phase 1: Immediate Wins (Implemented)

> **Superseded.** A resource event's `Href` is no longer made from the operation's URL, so `FillUrlTemplate` is gone.
> It is made as legacy made it: the registered `self` link of the event's resource type, filled from `Data` or, failing
> that, `SelfQuery`, under the request's base URI (`PipelineHelpers.TryCreateResourceEventHref`). The operation's URL was
> another resource's for a create (the collection's), and relative. Items 1 and 2 below describe the removed fallback.

**1. Inline Href from operation properties (eliminates reflection fallback)**

Current: `PipelineHelpers.FillUrlTemplate()` uses `ConcurrentDictionary + CompiledPropertyAccessor` at runtime.

Improved: The generator already knows the URL template and operation properties at compile time. When `ResourceEvent.Data` is null (DELETE operations), generate:
```csharp
resourceEvent.Href = $"{baseUri}tenants/{operation.TenantId}/users/{operation.Id}";
```

This eliminates the per-type property accessor cache lookup and reflection setup.

**2. StringBuilder-based FillUrlTemplate (shipped: `845f788d`)**

Replaced string.Concat-in-loop with single StringBuilder pass, eliminating O(n) intermediate string allocations per URL placeholder.

### ⚠️ Phase 2: Medium Effort (Deferred)

**3. Constant EventId strings**

Generator knows resource type + change type at compile time. Could emit:
```csharp
const string _eventId = "tenant.created"; // instead of runtime CreateId()
```

Negligible perf benefit (compiler already optimizes string literals), but cleaner diagnostics.

**4. Direct audit sink result extraction**

Instead of runtime pattern matching across 3 result types, generate the exact type check for the handler's declared return type.

### ❌ Phase 3: Not Recommended

**5. Full ResourceEvent construction in generated code** — S365's factory patterns (`ResourceEventDefinition.For(...)`) are user-defined and outside generator control.

**6. SelfQuery execution inlining** — Requires runtime Executor lookup; must stay runtime.

**7. Metadata population source-gen** — Often data-dependent (can't predict at compile time).

## Design Constraints

1. **SelfQuery requires runtime**: The nested pipeline execution needs the app's Executor instance, which isn't known until DI composition.
2. **Audit sink is DI-registered**: Can't be resolved at source-gen time.
3. **ResourceEventDefinition factories**: User-defined patterns — S365 uses `ClientEvents.Created.New(x)` which is outside generator visibility.
4. **Metadata/SecureData can be data-dependent**: `.WithSecureData("key", computedValue)` depends on runtime state.

## Codemod Strategy

For migrating S365 to source-generated `ResourceEventResult`:

1. **No immediate codemod needed** — the current `ResourceEvent<T>` pattern is fully supported by the generated pipeline (`ResourceEventAuditModule` handles both types).
2. **Future consideration**: When S365 stabilizes on new Blueprint, a codemod could convert `ResourceEvent<T>` → `ResourceEventResult<T>` for operations that don't need metadata/secureData/selfQuery. This gives a cleaner contract but is purely cosmetic.
3. **The `ResourceEventDefinition<T>` factory pattern** used in IdentityServer (`ClientEvents.Created`) is ergonomic and should be preserved — it's a pattern on top of `ResourceEvent`, not an alternative to it.

## Conclusion

**Recommended approach**: Keep the current dual-path (`ResourceEvent` + `ResourceEventResult`) architecture. The generated pipeline already handles both efficiently. The most impactful improvement was the `FillUrlTemplate` StringBuilder optimization (shipped), which eliminates the worst allocation pattern in the event flow. No S365 codemod is needed for resource events — they work correctly as-is.

## Related Files

- `src/Blueprint.SourceGenerator/Modules/ResultModel/ResourceEventAuditModule.cs`
- `src/Blueprint.SourceGenerator/Modules/ResultModel/ResourceEventSelfQueryModule.cs`
- `src/Blueprint.Runtime/Exec/PipelineHelpers.cs` (lines 761-884)
- `src/Blueprint.Runtime/Http/ResourceEvent.cs`
- `src/Blueprint.Runtime/Exec/ResourceEventResult.cs`
