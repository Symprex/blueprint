# Compile-Time Resource Descriptors — Design Document

## Problem Statement

Blueprint has `ApiOperationDescriptor` for operations but no equivalent for **resources**. The user asks: "Can we, like we have descriptors for operations, have descriptors for resources that can be contributed to? A way of centralising information about resources that are produced, their relationships to operations, the links."

Currently:
- `BlueprintApp.GetLinksForResource(Type)` returns links but nothing else
- No way to ask "which operations produce this resource?"
- No way to ask "which state-based link checkers apply to this resource?"
- No compile-time validation that a resource has a self-link
- HATEOAS link population loops through all link checkers at runtime per-resource

## Proposed Design

### `ApiResourceDescriptor` — Runtime Model

```csharp
public sealed class ApiResourceDescriptor
{
    public Type ResourceType { get; }
    public string TypeName { get; }  // camelCase (e.g., "tenantUser")
    
    // Which operations produce this resource as a result
    public IReadOnlyList<ApiOperationDescriptor> ProducedBy { get; }
    
    // Self-link (may be null for non-linkable resources)
    public ApiOperationLink? SelfLink { get; }
    
    // All links targeting this resource (named rels)
    public IReadOnlyList<ApiOperationLink> Links { get; }
    
    // Resource key template (from [Resource] attribute)
    public string? ResourceKeyTemplate { get; }
    
    // State-based checkers (discovered by assembly scan)
    public IReadOnlyList<Type> StateBasedCheckers { get; }
}
```

### Source Generator Contribution

The `LinkGenerationModule` (or a new `ResourceDescriptorModule`) would:

1. **Scan all operations** in the app's scanned assemblies
2. **Group by resource type** — any type referenced in `[SelfLink(typeof(T))]` or `[Link(typeof(T))]`
3. **Emit `RegisterResource()`** calls in the generated `Configure()` method:

```csharp
// Generated in BlueprintApp.Configure()
var tenantUserResource = new ApiResourceDescriptor(
    typeof(TenantUserApiResource),
    "tenantUser");
tenantUserResource.SetSelfLink(/* link ref */);
tenantUserResource.AddProducer(GetTenantUserQuery.Descriptor);
tenantUserResource.AddProducer(CreateTenantUserCommand.Descriptor);
this.RegisterResource(tenantUserResource);
```

### How This Helps

1. **Per-request link population** — Instead of iterating all link checkers, look up resource descriptor → get applicable checkers directly
2. **Compile-time diagnostics** — Warn if a resource has no self-link, or if operations produce it without a link
3. **Developer tooling** — `/api/_resources` endpoint showing full resource graph
4. **AOT-friendly** — All type references are compile-time, no runtime scanning

### State-Based Checker Discovery

The generator can find `StateBasedLinkChecker<TOperation, TResource>` subclasses in scanned assemblies:

```csharp
// Generator scans for types that:
// 1. Extend StateBasedLinkChecker<,>
// 2. Have TResource matching a known resource type
// Then emits:
tenantUserResource.AddStateBasedChecker(typeof(DeactivateTenantUserLinkChecker));
```

This replaces the runtime DI scan for `ILinkVisibilityAuthoriser` implementations.

## Migration Strategy

For S365, this was **purely additive** — no breaking changes were required:

1. ✅ `ApiResourceDescriptor` added to `Blueprint.Runtime.Model`
2. ✅ `RegisterResource()` added to `BlueprintApp`
3. ✅ `LinkGenerationModule` emits resource descriptor registrations
4. ✅ `PopulateResourceCheckers()` wires state-based checkers at compile time
5. ✅ `RegisterLinkCheckers()` always generated (even if empty) so consumer call sites compile

S365 didn't need a codemod — the generator automatically discovers resource relationships from existing `[SelfLink]` and `[Link]` attributes. State-based checkers are discovered by scanning scanned assemblies for `ILinkVisibilityAuthoriser` implementations.

## Implementation Status

**Phase 1** — ✅ COMPLETE:
- `ApiResourceDescriptor` class in `Blueprint.Runtime.Model` (`src/Blueprint.Runtime.Model/ApiResourceDescriptor.cs`)
- `RegisterResource()` / `TryGetResourceDescriptor()` / `GetLinksForResource()` on `BlueprintApp`
- `LinkGenerationModule` emits resource descriptor registrations at compile time
- `PopulateResourceCheckers()` generated method wires state-based checkers into resource descriptors
- Test coverage in `Blueprint.SourceGenerator.Tests/Model/ApiResourceDescriptorTests.cs`

**Phase 2** — ✅ LARGELY COMPLETE:
- State-based checker discovery in generator → ✅ Implemented via `PopulateResourceCheckers()` scan of `ILinkVisibilityAuthoriser` implementations
- Compile-time diagnostics for resource consistency → BPLG003 (non-linkable resource), BPLG004 (ambiguous self-link)
- Resource graph API endpoint → Not yet implemented (optional runtime enrichment)
- Per-request link population optimization → ✅ Per-request link auth cache (commit `3ab7edb7`)
