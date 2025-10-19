# Blueprint Operation Executor Pipeline Generator - Implementation Plan

## Executive Summary

This plan outlines the implementation of a new Roslyn incremental source generator that automatically discovers operation handlers and generates concrete `IOperationExecutorPipeline` implementations for each operation, plus a complete `BlueprintAppExecutor` that registers all discovered handlers.

### Key Objectives

1. **Handler Discovery**: Automatically find handlers for operations (both inline and external)
2. **Pipeline Generation**: Create tailored executor pipelines per operation
3. **Executor Registration**: Generate a complete BlueprintAppExecutor with all registrations
4. **Behaviour Framework**: Establish extensibility points for future behaviours (validation, auth, etc.)
5. **Replace DelegatePipeline**: Eliminate the need for manual DelegatePipeline registrations

## Current State Analysis

### Existing Infrastructure

1. **AppDataModelSourceGenerator**: Scans assemblies for IOperation types and generates BlueprintApp registrations
2. **OperationDescriptorSourceGenerator**: Generates Configure() methods for operations with metadata
3. **Module System**: IBlueprintSourceGeneratorModule allows extensibility
4. **Runtime Components**:
   - `IOperationExecutorPipeline`: Interface for execution pipelines
   - `BlueprintAppExecutor`: Base class for operation registration and execution
   - `ApiOperationContext`: Execution context with service provider, operation, activity, etc.
   - `OperationResult`: Standard response types (OkResult, etc.)

### Current Handler Patterns (Examples)

**Inline Handler** (method directly on operation class):
```csharp
public partial class GetTemplateInlineQuery : IQuery<TemplateApiResource>
{
    public TemplateApiResource Handle() { ... }
}
```

**External Handler** (separate class with first parameter as operation):
```csharp
public class GetTemplatesQueryHandler
{
    public TemplateApiResource[] HandleAsync(GetTemplatesQuery query) { ... }
}
```

### Current Manual Registration (To Be Eliminated)

```csharp
public class SampleBlueprintAppExecutor : BlueprintAppExecutor
{
    public SampleBlueprintAppExecutor() : base(new SampleBlueprintApp())
    {
        // Manual DelegatePipeline registrations
        this.Register(typeof(GetTemplateInlineQuery), 
            new DelegatePipeline(c => ...));
    }
}
```

## Design Overview

### Architecture Components

```
┌─────────────────────────────────────────────────────────────┐
│  New: OperationExecutorPipelineSourceGenerator              │
│  ─────────────────────────────────────────────────────────  │
│  1. Scan for operations (from BlueprintApp registrations)   │
│  2. Find handlers (inline or external)                      │
│  3. Analyze handler signatures (params, dependencies)       │
│  4. Generate concrete IOperationExecutorPipeline per op     │
│  5. Generate BlueprintAppExecutor with all registrations    │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  Generated Output (per operation)                           │
│  ─────────────────────────────────────────────────────────  │
│  • {OperationName}ExecutorPipeline : IOperationExecutor...  │
│  • Logging, activity tracing, exception handling            │
│  • Dependency resolution from ServiceProvider               │
│  • Behaviour injection points (future)                      │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  Generated: BlueprintAppExecutor implementation             │
│  ─────────────────────────────────────────────────────────  │
│  • Constructor with all pipeline registrations              │
│  • No more manual DelegatePipeline usage                    │
└─────────────────────────────────────────────────────────────┘
```

### Convention Over Configuration Benefits

The decision to use `ActivatorUtilities.CreateInstance()` with DI fallback provides several key advantages:

1. **Zero Boilerplate**: Handlers work without manual DI registration
2. **Constructor Injection**: Still get automatic dependency injection for constructor parameters
3. **DI Override**: Can still register handlers explicitly in DI for advanced scenarios (testing, decoration, etc.)
4. **Flexibility**: Supports both simple handlers and complex ones with dependencies
5. **Developer Experience**: Just write the handler, everything else is automatic

**Example**:
```csharp
// Handler with dependencies - no registration needed!
public class CreateOrderHandler
{
    private readonly IOrderRepository _repository;
    private readonly IEmailService _emailService;
    
    // ActivatorUtilities will inject these from DI automatically
    public CreateOrderHandler(IOrderRepository repository, IEmailService emailService)
    {
        _repository = repository;
        _emailService = emailService;
    }
    
    public async Task<OrderApiResource> HandleAsync(CreateOrderCommand command)
    {
        // Implementation
    }
}
```

## Handler Discovery Rules

### Handler Method Names

Methods matching these names will be considered handlers:
- `Invoke`
- `InvokeAsync`
- `Handle`
- `HandleAsync`

### Handler Types

#### 1. Inline Handlers

**Definition**: Method directly on an IOperation-derived class

**Characteristics**:
- Can be static or instance
- Can be async or non-async
- Can have parameters beyond the operation itself
- Parameters resolved from environment (ApiOperationContext) or DI

**Example**:
```csharp
public partial class GetMyAuthManifestQuery : IQuery<AuthManifestApiResource>
{
    public static async Task<AuthManifestApiResource> InvokeAsync(
        ApiOperationContext context,
        BlueprintApp dataModel,
        IApiAuthoriserAggregator authoriserAggregator)
    {
        // Implementation
    }
}
```

#### 2. External Handlers

**Definition**: Class with a handler method where the first argument is an IOperation-derived type

**Characteristics**:
- Handler class can be any accessible class
- First parameter MUST be the operation type
- Subsequent parameters follow same resolution rules as inline handlers
- Can be static or instance methods

**Example**:
```csharp
public class DeleteFieldCommandHandler : ApiOperations
{
    public async Task<ResourceEvent<UserFieldApiResource>> HandleAsync(
        DeleteFieldCommand operation,
        IDocumentSession db)
    {
        // Implementation
    }
}
```

### Parameter Resolution Strategy

For handler method parameters:

1. **Operation Instance**: Always available as `context.Operation`
2. **ApiOperationContext**: Pass `context` directly
3. **BlueprintApp**: Pass `context.App` (or `context.DataModel` for compatibility)
4. **Known Environment Types**: Extract from context if available
5. **DI Services**: Resolve via `context.ServiceProvider.GetRequiredService<T>()`

**Special Considerations**:
- If a handler is on a class deriving from a base (like `ApiOperations`), may need to set base class properties
- Parameters like `CancellationToken` should map to `context.OperationCancelled`

## Generated Pipeline Structure

### Per-Operation Pipeline Class

Each operation gets a generated pipeline class:

**File**: `{OperationName}ExecutorPipeline.g.cs`

**Structure**:
```csharp
namespace Blueprint.Generated
{
    public class {OperationName}ExecutorPipeline : IOperationExecutorPipeline
    {
        // Logging delegates (LoggerMessage.Define)
        private static readonly Action<ILogger, string, Exception> _ApiOperationExecuting = ...;
        private static readonly Action<ILogger, string, Exception> _NonException = ...;
        private static readonly Action<ILogger, string, Exception> _UnhandledException = ...;
        
        // Dependencies injected into pipeline constructor
        private readonly ILogger _logger;
        private readonly {OtherDependencies} ...;
        
        // Constructor with DI
        public {OperationName}ExecutorPipeline(
            ILoggerFactory loggerFactory,
            {OtherRequiredDependencies})
        {
            _logger = loggerFactory.CreateLogger("{OperationName}ExecutorPipeline");
            // Store other dependencies
        }
        
        public async Task<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            // Activity tracing
            using var activity = BlueprintActivitySource.ActivitySource.StartActivity(...);
            
            var operation = ({OperationType})context.Operation;
            
            try
            {
                // [BEHAVIOUR INJECTION POINT: Pre-execution]
                
                // Log operation execution
                _ApiOperationExecuting(_logger, "{OperationName}", null);
                
                // Resolve dependencies for handler
                var dependency1 = context.ServiceProvider.GetRequiredService<Type1>();
                
                // Invoke handler (inline or external)
                var handlerResult = await {HandlerInvocation}(...);
                
                // Wrap result
                OperationResult operationResult = new OkResult(handlerResult);
                
                // [BEHAVIOUR INJECTION POINT: Post-execution]
                
                return operationResult;
            }
            catch (ValidationException e)
            {
                context.Activity?.SetTag("otel.status_code", "OK");
                return new ValidationFailedOperationResult(e.ValidationResults);
            }
            catch (System.ComponentModel.DataAnnotations.ValidationException e)
            {
                context.Activity?.SetTag("otel.status_code", "OK");
                return ValidationMiddlewareBuilder.ToValidationFailedOperationResult(e);
            }
            catch (Exception e)
            {
                // Handle ApiException vs unhandled exceptions
                if (e is ApiException apiException && apiException.HttpStatus < 500)
                {
                    _NonException(_logger, e.Message, e);
                    context.Activity?.SetTag("otel.status_code", "OK");
                }
                else
                {
                    BlueprintActivitySource.RecordException(context.Activity, e, false);
                    _UnhandledException(_logger, e.Message, e);
                    context.Activity?.SetTag("otel.status_code", "ERROR");
                    context.Activity?.SetTag("otel.status_description", e.Message);
                }
                
                return new UnhandledExceptionOperationResult(e);
            }
        }
    }
}
```

### Generated BlueprintAppExecutor

**File**: `{AppClassName}Executor.g.cs` (e.g., `SampleBlueprintAppExecutor.g.cs`)

**Structure**:
```csharp
namespace {AppNamespace}
{
    public partial class {AppClassName}Executor : BlueprintAppExecutor
    {
        public {AppClassName}Executor(
            ILoggerFactory loggerFactory,
            {AllPipelineDependencies},
            {AppClassName} app)
            : base(app)
        {
            // Register all pipelines
            this.Register(typeof(GetTemplatesQuery), 
                new GetTemplatesQueryExecutorPipeline(loggerFactory, ...));
            this.Register(typeof(CreateTemplateCommand), 
                new CreateTemplateCommandExecutorPipeline(loggerFactory, ...));
            // ... all other operations
        }
    }
}
```

## Behaviour Framework Design

### Concept

Behaviours are pluggable components that can be injected into the execution pipeline at specific points. This allows for modular, reusable cross-cutting concerns.

### Behaviour Interface (Future)

```csharp
public interface IOperationBehaviour
{
    /// <summary>
    /// Indicates at which stage this behaviour should execute
    /// </summary>
    BehaviourStage Stage { get; }
    
    /// <summary>
    /// Determines if this behaviour applies to the given operation
    /// </summary>
    bool AppliesTo(INamedTypeSymbol operationType);
}

public enum BehaviourStage
{
    PreExecution,   // Before handler invocation
    PostExecution,  // After handler, before result wrap
    PostResult      // After result creation
}
```

### Injection Points in Generated Code

```csharp
// [BEHAVIOUR INJECTION POINT: Pre-execution]
// Future: DataAnnotations validation, HTTP body population, etc.

var handlerResult = await handler.InvokeAsync(...);

// [BEHAVIOUR INJECTION POINT: Post-execution]
// Future: Response transformation, additional logging

OperationResult operationResult = new OkResult(handlerResult);

// [BEHAVIOUR INJECTION POINT: Post-result]
// Future: Result enrichment, link generation
```

### Module for Behaviours

Create `IBehaviourModule` interface that extends `IBlueprintSourceGeneratorModule`:

```csharp
public interface IBehaviourModule : IBlueprintSourceGeneratorModule
{
    /// <summary>
    /// Determines if this behaviour should be applied to the operation
    /// </summary>
    bool ShouldApply(INamedTypeSymbol operationType, 
                     OperationConfigurationModel config);
    
    /// <summary>
    /// Generates code for this behaviour at the specified stage
    /// </summary>
    void GenerateBehaviourCode(ICodeWriter writer, 
                               BehaviourStage stage,
                               OperationToRegisterModel operation);
}
```

### Example Future Behaviours

1. **ValidationBehaviour**: Apply DataAnnotations validation pre-execution
2. **HttpPopulationBehaviour**: Read operation properties from HTTP request
3. **AuthorizationBehaviour**: Check [MustBeAuthenticated], [RequireRole], etc.
4. **AuditingBehaviour**: Log operation execution for audit trails
5. **CachingBehaviour**: Cache operation results based on attributes

## Implementation Phases

### Phase 1: Core Handler Discovery (Initial Implementation)

**Scope**: Handler finding and basic pipeline generation WITHOUT behaviours

**Components**:
1. New source generator: `OperationExecutorPipelineSourceGenerator`
2. Handler discovery logic (inline and external)
3. Parameter analysis and resolution
4. Basic pipeline generation with logging and exception handling
5. BlueprintAppExecutor generation

**Deliverables**:
- Working generator that finds handlers
- Generated pipelines that can execute operations
- Generated executor with all registrations
- Comprehensive unit tests
- Updated sample project demonstrating usage

### Phase 2: Behaviour Framework (Future)

**Scope**: Add behaviour extensibility WITHOUT implementing specific behaviours

**Components**:
1. `IBehaviourModule` interface
2. Behaviour injection point markers in generated code
3. Module registration system
4. Documentation for creating custom behaviours

**Deliverables**:
- Behaviour framework interfaces
- Injection point placeholders in generated code
- Example skeleton behaviour module
- Documentation on creating behaviours

### Phase 3: Concrete Behaviours (Future)

**Scope**: Implement actual behaviours

**Examples**:
1. DataAnnotations validation behaviour
2. HTTP request population behaviour
3. Authentication/authorization behaviour

**Note**: Phase 2 and 3 are OUT OF SCOPE for initial implementation

## Technical Implementation Details

### Source Generator Structure

**New Generator**: `OperationExecutorPipelineSourceGenerator`

**Incremental Pipeline**:
```
Stage 1: Find BlueprintApp classes with operations
    ↓
Stage 2: For each operation, find handler (inline or external)
    ↓
Stage 3: Analyze handler signature and dependencies
    ↓
Stage 4: Generate pipeline class per operation
    ↓
Stage 5: Generate BlueprintAppExecutor with all registrations
```

### Data Models

```csharp
// Models the discovered handler
public record OperationHandlerModel(
    string OperationTypeName,
    INamedTypeSymbol OperationType,
    HandlerType HandlerType,        // Inline or External
    IMethodSymbol HandlerMethod,
    INamedTypeSymbol? HandlerClass, // null for inline
    EquatableList<HandlerParameter> Parameters);

public enum HandlerType
{
    Inline,    // Method on operation class
    External   // Method on separate handler class
}

public record HandlerParameter(
    string ParameterName,
    ITypeSymbol ParameterType,
    ParameterSource Source);        // How to resolve this parameter

public enum ParameterSource
{
    Operation,              // The operation instance itself
    FromContext,            // ApiOperationContext or properties
    FromContextProperty,    // context.App, context.DataModel, etc.
    FromDependencyInjection // Resolve from ServiceProvider
}
```

### Handler Discovery Algorithm

```csharp
private static OperationHandlerModel? FindHandler(
    INamedTypeSymbol operationType,
    Compilation compilation)
{
    // 1. Check for inline handler
    var inlineHandler = FindInlineHandler(operationType);
    if (inlineHandler != null)
        return inlineHandler;
    
    // 2. Search for external handler
    var externalHandler = FindExternalHandler(operationType, compilation);
    if (externalHandler != null)
        return externalHandler;
    
    // 3. No handler found - emit diagnostic
    return null;
}

private static OperationHandlerModel? FindInlineHandler(
    INamedTypeSymbol operationType)
{
    var handlerMethodNames = new[] { 
        "Invoke", "InvokeAsync", "Handle", "HandleAsync" 
    };
    
    var handlerMethod = operationType.GetMembers()
        .OfType<IMethodSymbol>()
        .FirstOrDefault(m => handlerMethodNames.Contains(m.Name));
    
    if (handlerMethod == null)
        return null;
    
    // Analyze parameters
    var parameters = AnalyzeParameters(handlerMethod, operationType);
    
    return new OperationHandlerModel(
        operationType.Name,
        operationType,
        HandlerType.Inline,
        handlerMethod,
        null,
        parameters);
}

private static OperationHandlerModel? FindExternalHandler(
    INamedTypeSymbol operationType,
    Compilation compilation)
{
    // Search all types in compilation
    var allTypes = GetAllTypes(compilation);
    
    foreach (var type in allTypes)
    {
        var methods = type.GetMembers().OfType<IMethodSymbol>();
        
        foreach (var method in methods)
        {
            // Check if first parameter matches operation type
            if (method.Parameters.Length > 0 &&
                SymbolEqualityComparer.Default.Equals(
                    method.Parameters[0].Type, operationType))
            {
                var handlerMethodNames = new[] { 
                    "Invoke", "InvokeAsync", "Handle", "HandleAsync" 
                };
                
                if (handlerMethodNames.Contains(method.Name))
                {
                    var parameters = AnalyzeParameters(method, operationType);
                    
                    return new OperationHandlerModel(
                        operationType.Name,
                        operationType,
                        HandlerType.External,
                        method,
                        type,
                        parameters);
                }
            }
        }
    }
    
    return null;
}
```

### Parameter Analysis Algorithm

```csharp
private static EquatableList<HandlerParameter> AnalyzeParameters(
    IMethodSymbol handlerMethod,
    INamedTypeSymbol operationType)
{
    var parameters = new List<HandlerParameter>();
    
    foreach (var param in handlerMethod.Parameters)
    {
        var source = DetermineParameterSource(param, operationType);
        parameters.Add(new HandlerParameter(
            param.Name,
            param.Type,
            source));
    }
    
    return new EquatableList<HandlerParameter>(parameters);
}

private static ParameterSource DetermineParameterSource(
    IParameterSymbol param,
    INamedTypeSymbol operationType)
{
    var paramType = param.Type;
    
    // Check if it's the operation itself
    if (SymbolEqualityComparer.Default.Equals(paramType, operationType))
        return ParameterSource.Operation;
    
    // Check for ApiOperationContext
    if (paramType.ToString() == "Blueprint.Runtime.Exec.ApiOperationContext")
        return ParameterSource.FromContext;
    
    // Check for BlueprintApp or known aliases
    if (paramType.ToString() == "Blueprint.Model.BlueprintApp" ||
        param.Name == "dataModel" || param.Name == "app")
        return ParameterSource.FromContextProperty;
    
    // Check for CancellationToken
    if (paramType.ToString() == "System.Threading.CancellationToken")
        return ParameterSource.FromContextProperty;
    
    // Everything else is DI
    return ParameterSource.FromDependencyInjection;
}
```

## Code Generation Patterns

### Pipeline Constructor Generation

```csharp
private void GeneratePipelineConstructor(
    ICodeWriter writer,
    OperationHandlerModel handler)
{
    // Always need ILoggerFactory
    var constructorParams = new List<string> 
    { 
        "Microsoft.Extensions.Logging.ILoggerFactory loggerFactory" 
    };
    
    // Add dependencies that need to be injected into pipeline
    var pipelineDependencies = handler.Parameters
        .Where(p => p.Source == ParameterSource.FromDependencyInjection)
        .Where(p => IsExpensiveOrStateful(p.ParameterType))
        .Select(p => $"{p.ParameterType.ToDisplayString()} {ToCamelCase(p.ParameterName)}")
        .Distinct();
    
    constructorParams.AddRange(pipelineDependencies);
    
    writer.AppendLine($"public {handler.OperationTypeName}ExecutorPipeline(");
    writer.Indent();
    writer.AppendLine(string.Join($",{Environment.NewLine}", constructorParams));
    writer.Unindent();
    writer.AppendLine(")");
    writer.AppendLine("{");
    writer.Indent();
    writer.AppendLine("_logger = loggerFactory.CreateLogger(\"{0}\");", 
        handler.OperationTypeName + "ExecutorPipeline");
    
    // Store injected dependencies
    foreach (var dep in pipelineDependencies)
    {
        var fieldName = "_" + GetParameterNameFromDeclaration(dep);
        writer.AppendLine($"{fieldName} = {GetParameterNameFromDeclaration(dep)};");
    }
    
    writer.Unindent();
    writer.AppendLine("}");
}
```

### Handler Invocation Generation

```csharp
private void GenerateHandlerInvocation(
    ICodeWriter writer,
    OperationHandlerModel handler,
    string contextVarName,
    string operationVarName)
{
    // Resolve all parameters
    var resolvedParams = new List<string>();
    
    foreach (var param in handler.Parameters)
    {
        switch (param.Source)
        {
            case ParameterSource.Operation:
                resolvedParams.Add(operationVarName);
                break;
                
            case ParameterSource.FromContext:
                resolvedParams.Add(contextVarName);
                break;
                
            case ParameterSource.FromContextProperty:
                resolvedParams.Add(ResolveContextProperty(param, contextVarName));
                break;
                
            case ParameterSource.FromDependencyInjection:
                var varName = ToCamelCase(param.ParameterName);
                
                if (IsStoredInPipeline(param))
                {
                    // Use field from constructor
                    resolvedParams.Add("_" + varName);
                }
                else
                {
                    // Resolve inline
                    writer.AppendLine($"var {varName} = {contextVarName}.ServiceProvider.GetRequiredService<{param.ParameterType.ToDisplayString()}>();");
                    resolvedParams.Add(varName);
                }
                break;
        }
    }
    
    // Generate invocation
    if (handler.HandlerType == HandlerType.Inline)
    {
        if (handler.HandlerMethod.IsStatic)
        {
            writer.AppendLine($"var handlerResult = await {handler.OperationType.ToDisplayString()}.{handler.HandlerMethod.Name}({string.Join(", ", resolvedParams)});");
        }
        else
        {
            writer.AppendLine($"var handlerResult = await {operationVarName}.{handler.HandlerMethod.Name}({string.Join(", ", resolvedParams)});");
        }
    }
    else
    {
        // External handler - use convention over configuration
        var handlerInstance = ToCamelCase(handler.HandlerClass!.Name);
        var handlerTypeName = handler.HandlerClass.ToDisplayString();
        
        // Try GetService first, then ActivatorUtilities
        writer.AppendLine($"var {handlerInstance} = {contextVarName}.ServiceProvider.GetService<{handlerTypeName}>()");
        writer.AppendLine($"    ?? Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<{handlerTypeName}>({contextVarName}.ServiceProvider);");
        
        // Set base class properties if needed (analyzed at compile time)
        if (NeedsBasePropertySetup(handler.HandlerClass))
        {
            GenerateBasePropertySetup(writer, handler.HandlerClass, handlerInstance, contextVarName);
        }
        
        writer.AppendLine($"var handlerResult = await {handlerInstance}.{handler.HandlerMethod.Name}({string.Join(", ", resolvedParams)});");
    }
}

private void GenerateBasePropertySetup(
    ICodeWriter writer,
    INamedTypeSymbol handlerClass,
    string handlerVarName,
    string contextVarName)
{
    // Analyze all public settable properties (cached per base type)
    var propertiesToSet = GetSettablePropertiesForHandler(handlerClass);
    
    foreach (var (property, source) in propertiesToSet)
    {
        var propertyName = property.Name;
        var propertyType = property.Type.ToDisplayString();
        
        switch (source)
        {
            case PropertySource.ApiOperationContext:
                writer.AppendLine($"{handlerVarName}.{propertyName} = {contextVarName};");
                break;
                
            case PropertySource.Logger:
                writer.AppendLine($"{handlerVarName}.{propertyName} = _logger;");
                break;
                
            case PropertySource.CancellationToken:
                writer.AppendLine($"{handlerVarName}.{propertyName} = {contextVarName}.OperationCancelled;");
                break;
                
            case PropertySource.BlueprintApp:
                writer.AppendLine($"{handlerVarName}.{propertyName} = {contextVarName}.App;");
                break;
                
            case PropertySource.UserAuthorisationContext:
                // Common pattern in examples
                writer.AppendLine($"{handlerVarName}.{propertyName} = {contextVarName}.UserAuthorisationContext;");
                break;
        }
    }
}

// Cached property analysis
private static readonly ConcurrentDictionary<INamedTypeSymbol, List<(IPropertySymbol, PropertySource)>> 
    _propertyCache = new(SymbolEqualityComparer.Default);

private List<(IPropertySymbol Property, PropertySource Source)> GetSettablePropertiesForHandler(
    INamedTypeSymbol handlerClass)
{
    return _propertyCache.GetOrAdd(handlerClass, type =>
    {
        var properties = new List<(IPropertySymbol, PropertySource)>();
        
        // Walk up the inheritance chain
        var currentType = type;
        while (currentType != null)
        {
            foreach (var property in currentType.GetMembers().OfType<IPropertySymbol>())
            {
                if (!property.IsReadOnly && property.DeclaredAccessibility == Accessibility.Public)
                {
                    var source = DeterminePropertySource(property);
                    if (source != PropertySource.Unknown)
                    {
                        properties.Add((property, source));
                    }
                }
            }
            
            currentType = currentType.BaseType;
        }
        
        return properties;
    });
}

private PropertySource DeterminePropertySource(IPropertySymbol property)
{
    var propertyTypeName = property.Type.ToDisplayString();
    
    // Match by type first
    if (propertyTypeName == "Blueprint.Runtime.Exec.ApiOperationContext")
        return PropertySource.ApiOperationContext;
    
    if (propertyTypeName == "Microsoft.Extensions.Logging.ILogger")
        return PropertySource.Logger;
        
    if (propertyTypeName == "System.Threading.CancellationToken")
        return PropertySource.CancellationToken;
        
    if (propertyTypeName == "Blueprint.Model.BlueprintApp")
        return PropertySource.BlueprintApp;
    
    // Match by name convention (for interfaces or when type name varies)
    var propertyName = property.Name.ToLowerInvariant();
    
    if (propertyName == "context")
        return PropertySource.ApiOperationContext;
        
    if (propertyName == "logger")
        return PropertySource.Logger;
        
    if (propertyName == "datamodel" || propertyName == "app")
        return PropertySource.BlueprintApp;
        
    if (propertyName == "identity" || propertyName == "userauthorisationcontext")
        return PropertySource.UserAuthorisationContext;
    
    return PropertySource.Unknown;
}

public enum PropertySource
{
    Unknown,
    ApiOperationContext,
    Logger,
    CancellationToken,
    BlueprintApp,
    UserAuthorisationContext
}

private string ResolveContextProperty(HandlerParameter param, string contextVarName)
{
    var paramTypeName = param.ParameterType.ToString();
    
    return paramTypeName switch
    {
        "Blueprint.Model.BlueprintApp" => $"{contextVarName}.App",
        "System.Threading.CancellationToken" => $"{contextVarName}.OperationCancelled",
        _ when param.ParameterName == "dataModel" => $"{contextVarName}.App",
        _ => throw new InvalidOperationException($"Unknown context property for {param.ParameterName}")
    };
}
```

## Diagnostic Rules

### Error Diagnostics

```csharp
// BP100: No handler found for operation
private static readonly DiagnosticDescriptor NoHandlerFoundRule = new(
    id: "BP100",
    title: "No handler found for operation",
    messageFormat: "The operation '{0}' does not have a handler method. " +
                   "Add a method named 'Handle', 'HandleAsync', 'Invoke', or 'InvokeAsync' " +
                   "either on the operation class or in a separate handler class.",
    category: "OperationExecutorPipelineSourceGenerator",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);

// BP101: Multiple handlers found
private static readonly DiagnosticDescriptor MultipleHandlersFoundRule = new(
    id: "BP101",
    title: "Multiple handlers found for operation",
    messageFormat: "The operation '{0}' has multiple handlers. " +
                   "Only one handler method is allowed per operation.",
    category: "OperationExecutorPipelineSourceGenerator",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);

// BP102: Invalid handler signature
private static readonly DiagnosticDescriptor InvalidHandlerSignatureRule = new(
    id: "BP102",
    title: "Invalid handler signature",
    messageFormat: "The handler method '{0}' has an invalid signature: {1}",
    category: "OperationExecutorPipelineSourceGenerator",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);

// BP103: Handler parameter cannot be resolved
private static readonly DiagnosticDescriptor UnresolvableParameterRule = new(
    id: "BP103",
    title: "Handler parameter cannot be resolved",
    messageFormat: "The parameter '{0}' of type '{1}' in handler '{2}' cannot be resolved. " +
                   "Ensure it's available in ApiOperationContext or registered in DI.",
    category: "OperationExecutorPipelineSourceGenerator",
    DiagnosticSeverity.Warning,
    isEnabledByDefault: true);
```

### Warning Diagnostics

```csharp
// BP200: Handler method should be async
private static readonly DiagnosticDescriptor HandlerShouldBeAsyncRule = new(
    id: "BP200",
    title: "Handler method should be async",
    messageFormat: "The handler method '{0}' should be async for better performance.",
    category: "OperationExecutorPipelineSourceGenerator",
    DiagnosticSeverity.Warning,
    isEnabledByDefault: true);

// BP201: Consider making handler static
private static readonly DiagnosticDescriptor ConsiderStaticHandlerRule = new(
    id: "BP201",
    title: "Consider making inline handler static",
    messageFormat: "The inline handler '{0}' doesn't use instance state. " +
                   "Consider making it static for better performance.",
    category: "OperationExecutorPipelineSourceGenerator",
    DiagnosticSeverity.Info,
    isEnabledByDefault: true);
```

## Testing Strategy

### Unit Tests

**Test Categories**:

1. **Handler Discovery Tests**
   - Find inline handlers (static and instance)
   - Find external handlers
   - Handle missing handlers (emit diagnostic)
   - Handle multiple handlers (emit diagnostic)

2. **Parameter Analysis Tests**
   - Resolve operation parameters
   - Resolve context parameters
   - Resolve context property parameters
   - Resolve DI parameters
   - Handle unresolvable parameters

3. **Code Generation Tests**
   - Generate pipeline with inline handler
   - Generate pipeline with external handler
   - Generate pipeline with various parameter types
   - Generate executor with all registrations
   - Verify logging delegates
   - Verify exception handling

4. **Snapshot Tests**
   - Verify generated pipeline code matches expected output
   - Verify generated executor matches expected output
   - Test various operation/handler combinations

### Integration Tests

1. **End-to-End Execution**
   - Compile sample project with generator
   - Execute operations through generated pipelines
   - Verify correct handler invocation
   - Verify dependency injection works

2. **Sample Scenarios**
   - Inline query handler
   - External command handler
   - Handler with multiple DI dependencies
   - Static handler
   - Async handler

### Test Coverage Requirements

- Handler discovery: 100%
- Parameter resolution: 100%
- Code generation: >90%
- Integration scenarios: All common patterns

## Documentation Requirements

### Code Documentation

1. **XML Comments**: All public APIs, generator classes, and data models
2. **Inline Comments**: Complex algorithms (especially parameter resolution)
3. **Example Comments**: Show usage patterns in generated code

### User Documentation

1. **README Update**: 
   - Overview of automatic handler discovery
   - Examples of inline vs external handlers
   - Parameter resolution rules
   - How to debug when handler not found

2. **Usage Guide**:
   - How to define handlers
   - Best practices for handler design
   - Troubleshooting guide

3. **Migration Guide**:
   - How to migrate from DelegatePipeline
   - Breaking changes (if any)
   - Before/after examples

## Success Criteria

### Functional Requirements

- ✅ Generator finds inline handlers (static and instance)
- ✅ Generator finds external handlers
- ✅ Generator correctly analyzes handler parameters
- ✅ Generator produces compilable pipeline code
- ✅ Generated pipelines execute operations successfully
- ✅ Generated executor registers all operations
- ✅ Dependency injection works for all parameter types
- ✅ Exception handling matches example patterns
- ✅ Logging matches example patterns
- ✅ Activity tracing matches example patterns

### Quality Requirements

- ✅ No compiler warnings in generated code
- ✅ Generated code follows repository style guidelines
- ✅ All diagnostics have clear, actionable messages
- ✅ Test coverage >90%
- ✅ Snapshot tests verify deterministic output
- ✅ Performance: Generator completes <5s for large projects

### Documentation Requirements

- ✅ All public APIs documented with XML comments
- ✅ README updated with usage examples
- ✅ Migration guide provided
- ✅ Troubleshooting guide available

## Risks and Mitigations

### Risk 1: Handler Discovery Performance

**Risk**: Searching all types in compilation for external handlers may be slow

**Mitigation**: 
- Use incremental generator caching
- Implement early filtering (e.g., only classes with handler method names)
- Consider attribute marker for handler classes

### Risk 2: Parameter Resolution Ambiguity

**Risk**: Determining parameter source may be ambiguous in edge cases

**Mitigation**:
- Clear precedence rules in documentation
- Diagnostic warnings for ambiguous cases
- Consider attribute-based hints (future)

### Risk 3: Generated Code Complexity

**Risk**: Generated pipelines may be hard to debug

**Mitigation**:
- Emit clear comments in generated code
- Include source locations in activities
- Provide debugging guide

### Risk 4: Breaking Changes

**Risk**: Generated code may not match all existing patterns

**Mitigation**:
- Extensive integration testing with real scenarios
- Gradual rollout (optional at first)
- Maintain DelegatePipeline as fallback

## Future Enhancements

### Phase 2: Behaviour Framework

- Define `IBehaviourModule` interface
- Add injection points in generated code
- Implement behaviour registration system

### Phase 3: Concrete Behaviours

- **ValidationBehaviour**: DataAnnotations validation
- **HttpPopulationBehaviour**: HTTP request mapping
- **AuthBehaviour**: Authentication/authorization checks

### Phase 4: Advanced Features

- **Caching**: Attribute-based operation result caching
- **Retry**: Automatic retry logic for transient failures
- **Circuit Breaker**: Fault tolerance patterns
- **Metrics**: Automatic operation metrics collection

## Resolved Design Decisions

### 1. Generated Executor Naming - DECISION: Partial class with clean name

**Choice**: `{App}Executor` as a partial class

**Implementation**: 
```csharp
namespace Blueprint.SourceGenerator.Sample;

public partial class SampleBlueprintAppExecutor : BlueprintAppExecutor
{
    // Generated constructor and registrations
}
```

**Rationale**: Clean API, allows user extension via partial classes, matches existing pattern.

### 2. Generated Pipeline Namespace - DECISION: Blueprint.Generated

**Choice**: All pipelines in `Blueprint.Generated` namespace

**Implementation**:
```csharp
namespace Blueprint.Generated
{
    public class GetTemplatesQueryExecutorPipeline : IOperationExecutorPipeline { }
}
```

**Rationale**: Clear separation, pipelines are internal infrastructure not directly referenced by users.

### 3. Handler Class Instantiation - DECISION: Convention over Configuration

**Choice**: Use `ActivatorUtilities.CreateInstance()` or `GetService()` - no DI registration required

**Implementation**:
```csharp
// Try to get from DI first, then use ActivatorUtilities to create with DI parameters
var handler = context.ServiceProvider.GetService<DeleteFieldCommandHandler>()
    ?? ActivatorUtilities.CreateInstance<DeleteFieldCommandHandler>(context.ServiceProvider);
```

**Rationale**: 
- Convention over configuration
- Handlers work without manual registration
- Still supports constructor injection
- Falls back to DI if registered (for advanced scenarios)

### 4. Base Class Property Setting - DECISION: Compile-time property analysis with caching

**Choice**: Analyze all settable properties at compile-time, match by type and name, cache per base type

**Implementation**:
```csharp
// For each public settable property on handler class and base classes:
// - If type matches ApiOperationContext → set from context
// - If type matches ILogger → set from _logger  
// - If type matches CancellationToken → set from context.OperationCancelled
// - If name is "Context" or "DataModel" → set from context/context.App
// Cache the property mapping per base class type
```

**Rationale**: 
- Flexible, works for any base class pattern
- Compile-time analysis means no runtime penalty
- Caching ensures good generator performance
- Handles both current and future base class patterns

### 5. Parameter Disambiguation - DECISION: Precedence rules without attributes

**Choice**: Use clear precedence rules, no attributes needed initially

**Precedence Order**:
1. Operation instance (if parameter type matches operation)
2. ApiOperationContext (exact type match)
3. Known context properties (BlueprintApp, CancellationToken)
4. Everything else → Dependency Injection

**Rationale**: Simple, no ceremony, covers 95% of cases. Can add attributes later if needed.

### 6. Multiple Operations per Handler - DECISION: Defer to future

**Choice**: One operation per handler for initial implementation

**Future Enhancement**: Support multiple operations in one handler class in Phase 2

**Rationale**: Keeps initial implementation focused, can add later without breaking changes.

### 7. Behaviour Opt-In Strategy - DECISION: Smart defaults with opt-out

**Choice**: Detect and auto-apply behaviours with opt-out capability

**Implementation (Phase 2)**:
```csharp
// Auto-detect based on attributes/properties:
// - DataAnnotations present → enable validation
// - [MustBeAuthenticated] → enable auth checks
// Allow opt-out: [DisableBehaviour(typeof(ValidationBehaviour))]
```

**Rationale**: Convention over configuration, most convenient for developers, explicit opt-out for edge cases.

## Appendix: Example Generated Code

### Example 1: Inline Handler Pipeline

**Source**:
```csharp
public partial class GetMyAuthManifestQuery : IQuery<AuthManifestApiResource>
{
    public static async Task<AuthManifestApiResource> InvokeAsync(
        ApiOperationContext context,
        BlueprintApp dataModel,
        IApiAuthoriserAggregator authoriserAggregator)
    {
        // Implementation
    }
}
```

**Generated** (simplified):
```csharp
public class GetMyAuthManifestQueryExecutorPipeline : IOperationExecutorPipeline
{
    private readonly ILogger _logger;
    
    public GetMyAuthManifestQueryExecutorPipeline(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger("GetMyAuthManifestQueryExecutorPipeline");
    }
    
    public async Task<OperationResult> ExecuteAsync(ApiOperationContext context)
    {
        using var activity = BlueprintActivitySource.ActivitySource.StartActivity("GetMyAuthManifestQuery");
        var operation = (GetMyAuthManifestQuery)context.Operation;
        
        try
        {
            var authoriserAggregator = context.ServiceProvider.GetRequiredService<IApiAuthoriserAggregator>();
            var handlerResult = await GetMyAuthManifestQuery.InvokeAsync(context, context.App, authoriserAggregator);
            return new OkResult(handlerResult);
        }
        catch (Exception e)
        {
            // Exception handling
        }
    }
}
```

### Example 2: External Handler Pipeline

**Source**:
```csharp
public class DeleteFieldCommand : ICommand<ResourceEvent<UserFieldApiResource>>
{
    public ApiId TenantId { get; set; }
    public string? Token { get; set; }
}

public class DeleteFieldCommandHandler : ApiOperations
{
    public async Task<ResourceEvent<UserFieldApiResource>> HandleAsync(
        DeleteFieldCommand operation,
        IDocumentSession db)
    {
        // Implementation
    }
}
```

**Generated** (simplified):
```csharp
public class DeleteFieldCommandExecutorPipeline : IOperationExecutorPipeline
{
    private readonly ILogger _logger;
    
    public DeleteFieldCommandExecutorPipeline(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger("DeleteFieldCommandExecutorPipeline");
    }
    
    public async Task<OperationResult> ExecuteAsync(ApiOperationContext context)
    {
        using var activity = BlueprintActivitySource.ActivitySource.StartActivity("DeleteFieldCommand");
        var operation = (DeleteFieldCommand)context.Operation;
        
        try
        {
            // Convention over configuration - try DI first, then ActivatorUtilities
            var handler = context.ServiceProvider.GetService<DeleteFieldCommandHandler>()
                ?? ActivatorUtilities.CreateInstance<DeleteFieldCommandHandler>(context.ServiceProvider);
            
            // Set base class properties (analyzed at compile time)
            handler.Context = context;
            handler.Identity = context.UserAuthorisationContext;
            handler.Logger = _logger;
            handler.CancellationToken = context.OperationCancelled;
            
            var db = context.ServiceProvider.GetRequiredService<IDocumentSession>();
            var handlerResult = await handler.HandleAsync(operation, db);
            return new OkResult(handlerResult);
        }
        catch (Exception e)
        {
            // Exception handling
        }
    }
}
```

---

## Plan Approval

This plan requires review and approval before implementation begins. Key areas for feedback:

1. Overall design approach
2. Handler discovery rules
3. Parameter resolution strategy
4. Generated code structure
5. Behaviour framework design
6. Open questions resolution

Once approved, proceed to create detailed task breakdown in `tasks.md`.
