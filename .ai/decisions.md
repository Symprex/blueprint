# Blueprint Operation Executor Pipeline Generator - Design Decisions

## Date: 2025-10-19

## Stakeholders
- Development Team
- Reviewed and approved by: [User]

## Resolved Questions

### 1. Generated Executor Naming ✅
**Decision**: `{App}Executor` as a partial class

**Implementation**:
```csharp
namespace Blueprint.SourceGenerator.Sample;

public partial class SampleBlueprintAppExecutor : BlueprintAppExecutor
{
    // Generated constructor and registrations
}
```

**Rationale**: Clean API, allows user extension via partial classes, matches existing pattern.

---

### 2. Generated Pipeline Namespace ✅
**Decision**: All pipelines in `Blueprint.Generated` namespace

**Implementation**:
```csharp
namespace Blueprint.Generated
{
    public class GetTemplatesQueryExecutorPipeline : IOperationExecutorPipeline { }
}
```

**Rationale**: Clear separation, pipelines are internal infrastructure not directly referenced by users.

---

### 3. Handler Class Instantiation ✅
**Decision**: Convention over Configuration - Use `ActivatorUtilities.CreateInstance()` with DI fallback

**Implementation**:
```csharp
// Try to get from DI first, then use ActivatorUtilities to create with DI parameters
var handler = context.ServiceProvider.GetService<DeleteFieldCommandHandler>()
    ?? ActivatorUtilities.CreateInstance<DeleteFieldCommandHandler>(context.ServiceProvider);
```

**Benefits**:
- ✅ Zero boilerplate - handlers work without manual registration
- ✅ Constructor injection still works automatically
- ✅ Can override with explicit DI registration for advanced scenarios
- ✅ Best developer experience

**Example**:
```csharp
// This handler will work without any registration!
public class CreateOrderHandler
{
    private readonly IOrderRepository _repository;
    
    // ActivatorUtilities will inject repository from DI
    public CreateOrderHandler(IOrderRepository repository)
    {
        _repository = repository;
    }
    
    public async Task<OrderApiResource> HandleAsync(CreateOrderCommand command)
    {
        // Implementation
    }
}
```

---

### 4. Base Class Property Setting ✅
**Decision**: Compile-time property analysis with caching per base type

**Implementation**:
- Analyze all public settable properties on handler class and base classes
- Match by type first, then by naming convention
- Cache the property mapping per base class type (using `ConcurrentDictionary<INamedTypeSymbol, ...>`)

**Property Matching Rules**:
1. **By Type**:
   - `ApiOperationContext` → `context`
   - `ILogger` → `_logger`
   - `CancellationToken` → `context.OperationCancelled`
   - `BlueprintApp` → `context.App`

2. **By Name** (case-insensitive):
   - Property named "Context" → `context`
   - Property named "Logger" → `_logger`
   - Property named "DataModel" or "App" → `context.App`
   - Property named "Identity" or "UserAuthorisationContext" → `context.UserAuthorisationContext`

**Rationale**: 
- Flexible - works for any base class pattern
- Performant - compile-time analysis with caching
- Handles both current and future base class patterns
- No runtime reflection overhead

---

### 5. Parameter Disambiguation ✅
**Decision**: Use clear precedence rules without attributes (initially)

**Precedence Order**:
1. Operation instance (if parameter type matches operation)
2. `ApiOperationContext` (exact type match)
3. Known context properties (`BlueprintApp`, `CancellationToken`)
4. Everything else → Dependency Injection

**Rationale**: 
- Simple, no ceremony
- Covers 95% of cases
- Can add `[FromContext]`, `[FromDI]` attributes later if needed
- Clear and predictable

---

### 6. Multiple Operations per Handler ✅
**Decision**: One operation per handler (defer multiple operations to Phase 2)

**Current**: Each handler handles exactly one operation
**Future**: Could support handler classes with multiple methods for different operations

**Rationale**: 
- Keeps initial implementation focused
- Clear separation of concerns
- Can add later without breaking existing code
- Most handlers will be single-operation anyway

---

### 7. Behaviour Opt-In Strategy ✅
**Decision**: Smart defaults with opt-out (Phase 2)

**Approach** (when behaviours are implemented):
- Auto-detect based on attributes/properties:
  - DataAnnotations present → enable validation
  - `[MustBeAuthenticated]` → enable auth checks
- Allow explicit opt-out: `[DisableBehaviour(typeof(ValidationBehaviour))]`

**Rationale**: 
- Convention over configuration
- Most convenient for developers
- Explicit opt-out for edge cases
- Follows framework best practices

---

## Implementation Summary

### Phase 1 Scope (Current Implementation)
- ✅ Handler discovery (inline and external)
- ✅ Parameter resolution
- ✅ Pipeline generation with logging and exception handling
- ✅ Executor generation with all registrations
- ✅ Convention-over-configuration handler instantiation
- ✅ Base class property analysis and setup
- ✅ Behaviour injection point markers (comments only)

### Phase 2 Scope (Future)
- Behaviour framework implementation
- `IBehaviourModule` interface
- Behaviour registration system
- Example behaviour modules

### Phase 3 Scope (Future)
- Concrete behaviour implementations:
  - ValidationBehaviour
  - HttpPopulationBehaviour
  - AuthenticationBehaviour

---

## Technical Specifications

### Generated Code Characteristics
- **Namespace**: `Blueprint.Generated` for pipelines
- **Naming**: `{OperationName}ExecutorPipeline` for pipelines
- **Executor**: `{AppName}Executor` as partial class in app namespace
- **Dependencies**: ILoggerFactory always injected, others as needed
- **Error Handling**: ValidationException, ApiException, general Exception
- **Telemetry**: Activity tracing with proper status codes
- **Logging**: Structured logging with LoggerMessage.Define

### Code Quality Requirements
- ✅ No compiler warnings
- ✅ Follows repository style guidelines
- ✅ Deterministic output
- ✅ XML documentation on all public APIs
- ✅ Comprehensive diagnostic messages
- ✅ >90% test coverage

### Performance Requirements
- ✅ Generator completes <5s for large projects
- ✅ Incremental generation with proper caching
- ✅ No runtime reflection overhead
- ✅ Cached property analysis per base type

---

## Approval

**Approved by**: [User]
**Date**: 2025-10-19
**Status**: ✅ Ready for Implementation

All design decisions have been reviewed and approved. Implementation can proceed according to the plan.md and tasks.md.
