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

For S365, this is **purely additive** — no breaking changes:

1. Add `ApiResourceDescriptor` to `Blueprint.Runtime.Model`
2. Add `RegisterResource()` to `BlueprintApp`
3. Update `LinkGenerationModule` to emit resource registrations
4. Optionally: emit diagnostic when resource has links but no self-link

S365 doesn't need a codemod — the generator automatically discovers resource relationships from existing `[SelfLink]` and `[Link]` attributes.

## Scope for This Session

**Phase 1** (implement now):
- Add `ApiResourceDescriptor` class to Blueprint.Runtime.Model
- Add `RegisterResource()` and lookup methods to `BlueprintApp`
- Update `LinkGenerationModule` to emit resource descriptor registrations
- Add test coverage

**Phase 2** (future):
- State-based checker discovery in generator
- Compile-time diagnostics for resource consistency
- Resource graph API endpoint
- Per-request link population optimization using resource descriptors
