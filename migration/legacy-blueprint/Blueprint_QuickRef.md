# BLUEPRINT LIBRARY - QUICK REFERENCE GUIDE

## DIRECTORY STRUCTURE

\\\
D:\Projects\Signature365\src\Blueprint\
├── Blueprint/                              # Core library (operations, pipelines, validation, auth)
├── Blueprint.Compiler/                     # Code generation & compilation framework
├── Blueprint.Http/                         # HTTP/REST integration
├── Blueprint.OpenApi/                      # OpenAPI/Swagger generation
├── Blueprint.Tasks/                        # Background task scheduling
├── Blueprint.Tasks.AspNetCore/             # ASP.NET Core task integration
├── Blueprint.Tasks.Hangfire/               # Hangfire background job integration
├── Blueprint.Testing/                      # Testing utilities
└── tests/                                  # Test projects
\\\

---

## OPERATION EXECUTION FLOW

\\\
1. Operation registered in ApiDataModel
2. BlueprintApiBuilder configures pipeline with middleware
3. IApiOperationExecutor.ExecuteAsync(ApiOperationContext) called
4. Code-generated IOperationExecutorPipeline.ExecuteAsync() runs:
   
   a. POPULATION stage: Populate operation from HTTP request
   b. VALIDATION stage: BlueprintValidator checks all validation attributes
   c. PREEXECUTION stage: Authentication/Authorization middleware
   d. EXECUTION stage: IApiOperationHandler<T>.Handle() invoked
   e. POSTEXECUTION stage: Link generation, Resource events
   
5. Middleware can register exception handlers (per-type)
6. OperationResult returned to HTTP layer
7. For nested calls: ApiOperationContext.CreateNested() + ExecuteNestedAsync()
\\\

---

## KEY PATTERNS

### 1. OPERATION DEFINITION
\\\csharp
[SelfLink(typeof(UserResource), "users/{Id}")]
[ClaimRequired("account", "{AccountId}", "ViewUser")]
public class GetUserQuery : IQuery<UserResource>
{
    [Required]
    public Guid Id { get; set; }
    
    public Guid AccountId { get; set; }
}

public class GetUserQueryHandler : IApiOperationHandler<GetUserQuery>
{
    public ValueTask<object> Handle(GetUserQuery op, ApiOperationContext ctx)
    {
        // Query database, return UserResource
    }
}
\\\

### 2. NESTED OPERATION EXECUTION
\\\csharp
public class CreateOrderCommandHandler : IApiOperationHandler<CreateOrderCommand>
{
    public async ValueTask<object> Handle(CreateOrderCommand op, ApiOperationContext ctx)
    {
        // Create order
        var order = new Order { ... };
        
        // Execute nested query in same context
        var nested = ctx.CreateNested(new GetOrderQuery { Id = order.Id });
        nested.SkipAuthorisation = true; // Parent auth sufficient
        
        var result = await executor.ExecuteAsync(nested);
        var orderResource = ((OkResult<OrderResource>)result).Content;
        
        return new ResourceEvent<OrderResource>(
            ResourceEventChangeType.Created,
            "order.created",
            orderResource);
    }
}
\\\

### 3. CUSTOM VALIDATION ATTRIBUTE
\\\csharp
[AttributeUsage(AttributeTargets.Property)]
public class MinimumAgeAttribute : BlueprintValidationAttribute
{
    public int MinimumAge { get; set; }
    
    protected override async Task<bool> IsValidAsync(
        object value, 
        string propertyName, 
        ApiOperationContext apiOperationContext)
    {
        if (value is DateTime birthDate)
        {
            var age = DateTime.Now.Year - birthDate.Year;
            return age >= MinimumAge;
        }
        return true;
    }
}
\\\

### 4. AUTHORIZATION WITH CLAIMS
\\\csharp
[ClaimRequired("account", "{AccountId}", "Manage")]
[ClaimRequired("team", "{TeamId}", "View")]
public class UpdateTeamCommand : ICommand<TeamResource>
{
    public Guid AccountId { get; set; }
    public Guid TeamId { get; set; }
    public string Name { get; set; }
}

// Claim template expansion:
// If AccountId=6, TeamId=5 -> requires:
// Claim(type="account", value="6", right="Manage")
// Claim(type="team", value="5", right="View")
\\\

### 5. RESOURCE EVENTS
\\\csharp
public class CreateUserCommandHandler : IApiOperationHandler<CreateUserCommand>
{
    public async ValueTask<object> Handle(CreateUserCommand op, ApiOperationContext ctx)
    {
        var user = await CreateUserInDb(op);
        
        return new ResourceEvent<UserResource>(
            ResourceEventChangeType.Created,
            "user.created",
            userResource);
    }
}

// Client receives:
// {
//   "\": "event",
//   "changeType": "created",
//   "eventId": "user.created",
//   "resourceObject": "user",
//   "data": { UserResource ... },
//   "created": "2024-01-15T10:30:00Z"
// }
\\\

### 6. FLUENT BUILDER CONFIGURATION
\\\csharp
services.AddBlueprintApi(blueprint =>
{
    blueprint
        .Operations(ops => ops.Scan(typeof(Program).Assembly))
        .Http()
        .AddOpenApi()
        .AddHateoasLinks()
        .AddResourceEvents<ResourceEventRepository>()
        .Authentication(auth => auth.UseContextLoader<CustomAuthContextFactory>())
        .Pipeline(p => 
        {
            p.AddMiddleware<CustomLoggingMiddlewareBuilder>(MiddlewareStage.PreExecution);
        });
});
\\\

---

## CORE INTERFACE SIGNATURES

### IApiOperationExecutor
\\\csharp
public interface IApiOperationExecutor
{
    ApiDataModel DataModel { get; }
    Task<OperationResult> ExecuteAsync(ApiOperationContext context);
    Task<OperationResult> ExecuteWithNewScopeAsync(object operation, CancellationToken token = default);
}
\\\

### IOperationExecutorPipeline (Generated)
\\\csharp
public interface IOperationExecutorPipeline
{
    Task<OperationResult> ExecuteAsync(ApiOperationContext context);
    Task<OperationResult> ExecuteNestedAsync(ApiOperationContext context);
}
\\\

### IApiOperationHandler<T>
\\\csharp
public interface IApiOperationHandler<in T>
{
    ValueTask<object> Handle(T operation, ApiOperationContext apiOperationContext);
}
\\\

### IMiddlewareBuilder
\\\csharp
public interface IMiddlewareBuilder
{
    bool SupportsNestedExecution { get; }
    bool Matches(ApiOperationDescriptor operation);
    void Build(MiddlewareBuilderContext context);
}
\\\

### IValidator
\\\csharp
public interface IValidator
{
    Task<ValidationFailures> GetValidationResultsAsync(
        object value, 
        ApiOperationContext apiOperationContext);
}
\\\

### IApiAuthoriser
\\\csharp
public interface IApiAuthoriser
{
    bool AppliesTo(ApiOperationDescriptor descriptor);
    Task<ExecutionAllowed> CanExecuteOperationAsync(
        ApiOperationContext operationContext, 
        ApiOperationDescriptor descriptor, 
        object operation);
    Task<ExecutionAllowed> CanShowLinkAsync(
        ApiOperationContext operationContext, 
        ApiOperationDescriptor descriptor, 
        object resource);
}
\\\

### IApiOperationHandler
\\\csharp
public interface IApiOperationHandler
{
    // Marker interface for handler discovery
}
\\\

---

## CONTEXT OBJECT - ApiOperationContext

| Property | Type | Purpose |
|----------|------|---------|
| DataModel | ApiDataModel | API model containing all operations |
| Descriptor | ApiOperationDescriptor | Metadata about current operation |
| Operation | object | The actual operation instance |
| ServiceProvider | IServiceProvider | DI container for this scope |
| OperationCancelled | CancellationToken | Signals operation cancellation |
| Parent | ApiOperationContext | Parent if nested, null if root |
| IsNested | bool | True if has parent |
| SkipAuthorisation | bool | Skip auth checks (for nested calls) |
| UserAuthorisationContext | IUserAuthorisationContext | Auth claims holder |
| ClaimsIdentity | ClaimsIdentity | Claims identity from auth |
| Activity | Activity | APM tracing activity |
| Data | Dictionary | Generic middleware storage |

**Key Methods:**
- CreateNested(Type) - Create child context for different operation type
- CreateNested(object) - Create child context for instance

---

## VALIDATION ATTRIBUTES

| Attribute | Usage | Async |
|-----------|-------|-------|
| GreaterThanAttribute | Must be > value | No |
| GreaterThanOrEqualToAttribute | Must be >= value | No |
| LessThanAttribute | Must be < value | No |
| LessThanOrEqualToAttribute | Must be <= value | No |
| GreaterThanPropertyAttribute | Must be > another property | No |
| InFutureAttribute | Must be future date | No |
| InPastAttribute | Must be past date | No |
| NotInFutureAttribute | Must not be future | No |
| NotInPastAttribute | Must not be past | No |
| MustBeTrueAttribute | Must be true | No |
| NotEmptyListAttribute | Must have items | No |
| MaxItemsListAttribute | Limit list size | No |
| RequiredIfAttribute | Conditional required | Yes |
| RequiredIfNotAttribute | Conditional required (not) | Yes |
| NotAllowedIfAttribute | Conditional forbid | Yes |
| ResourceKeyAttribute | Validate resource key format | No |
| PasswordAttribute | Password validation rules | No |
| EmailAddressAttribute | Email format | No |
| PhoneNumberAttribute | Phone format | No |
| UrlAttribute | URL format | No |
| UkPostcodeAttribute | UK postcode | No |
| RegexAttribute | Pattern match | No |
| BlueprintValidationAttribute | Custom async base | Yes |

---

## AUTHORIZATION ATTRIBUTES

| Attribute | Purpose |
|-----------|---------|
| AllowAnonymousAttribute | Allow unauthenticated access |
| MustBeAuthenticatedAttribute | Require authentication |
| ClaimRequiredAttribute | Require specific claims with property expansion |
| PermissionAttribute | Require specific permission |
| PermissionForAttribute | Link operation to resource permission |

---

## LINK ATTRIBUTES

| Attribute | Scope | Rel | Resource |
|-----------|-------|-----|----------|
| SelfLinkAttribute | Resource-specific | "self" | Specific type |
| RootLinkAttribute | System-level | Custom | null |
| LinkAttribute | Both | Custom | Optional |

---

## HTTP RESPONSE TYPES

| Type | Status | Purpose |
|------|--------|---------|
| OkResult | 200 | Success, no content |
| OkResult<T> | 200 | Success with content |
| StatusCodeResult.Created | 201 | Created |
| StatusCodeResult.Accepted | 202 | Accepted |
| StatusCodeResult.NoContent | 204 | No content |
| StatusCodeResult.BadRequest | 400 | Client error |
| StatusCodeResult.Unauthorized | 401 | Not authenticated |
| StatusCodeResult.Forbidden | 403 | Not authorized |
| StatusCodeResult.NotFound | 404 | Not found |
| StatusCodeResult.Conflict | 409 | Conflict |
| StatusCodeResult.InternalServerError | 500 | Server error |
| PlainTextResult | 200 | Plain text |
| ProblemDetails | Custom | RFC 7807 format |
| ResourceEvent | 200 | Change notification |

---

## MIDDLEWARE STAGES (Execution Order)

1. **Population** - Parse HTTP request into operation
2. **PreExecution** - Authentication, authorize execution
3. **Validation** - Validate operation properties
4. **Execution** - Run handler
5. **PostExecution** - HATEOAS links, resource events

---

## KEY SERVICE REGISTRATIONS

\\\csharp
// Core
IApiOperationExecutor (singleton)
IApiOperationHandler<T> (custom)
IMiddlewareBuilder (built-in + custom)

// Validation
IValidator (singleton)
IValidationSourceBuilder (scoped)

// Authorization
IApiAuthoriser (scoped)
IApiAuthoriserAggregator (scoped)
IUserAuthorisationContextFactory (scoped)
IClaimsIdentityProvider (scoped)
IResourceKeyExpander (scoped)

// HTTP
IApiLinkGenerator (scoped)
IResourceLinkGenerator (scoped)
IResourceEventAuditRepository (scoped, if enabled)

// Compilation (Dev)
AutoApiOperationExecutorBuilderOptions (options)
AutoApiOperationExecutorBuilder (singleton, in dev)

// Compilation (Prod)
StaticApiOperationExecutorBuilder (singleton, in prod)
\\\

---

## FEATURE DATA STORAGE

Operations can store host-specific metadata via descriptor.SetFeatureData<T>():

\\\csharp
// HTTP stores:
descriptor.GetFeatureData<HttpOperationFeatureData>()
  .HttpMethod // "GET", "POST", etc.
\\\

---

## RESOURCE KEY FORMAT

\\\
Format: Type/Id
Example: User/550e8400-e29b-41d4-a716-446655440000

Expansion (Hierarchical):
Original: User/123
Expanded: Account/6/Team/5/User/123

Usage: Authorization claims use expanded keys to grant parent permissions to children
\\\

---

## RESOURCE EVENT SCHEMA

\\\json
{
  "\": "event",
  "changeType": "created|updated|deleted",
  "eventId": "resource.action",
  "resourceObject": "user",
  "data": { /* ResourceEvent object */ },
  "created": "2024-01-15T10:30:00Z",
  "metadata": { /* optional */ }
}
\\\

---

## CODE GENERATION

### Compilation Strategies:
- **InMemoryOnlyCompileStrategy** - In-memory only (testing)
- **ToFileCompileStrategy** - Write to disk + memory (dev)
- **IncrementalAssemblyGenerator** - Only recompile changed pipelines (dev)
- **StaticApiOperationExecutorBuilder** - Load pre-compiled (prod)

### Generated Names:
- Namespace: Blueprint.Generated
- Class: [OperationType]ExecutorPipeline
- Methods: ExecuteAsync(), ExecuteNestedAsync()

---

## EXECUTION ALLOWED RESULT

\\\csharp
public struct ExecutionAllowed
{
    public bool IsAllowed { get; }
    public string Reason { get; }        // Why it failed
    public string Message { get; }       // User-facing message
    public ExecutionAllowedFailureType? FailureType { get; }
    // FailureType: Authorisation | Authentication | Unknown
}
\\\

---

## VALIDATION FAILURES

\\\csharp
public class ValidationFailures
{
    public const string FormLevelPropertyName = "form"; // Non-property errors
    
    public int Count { get; }
    public void AddFailure(ValidationResult validationResult)
    public void AddFailure(string propertyName, string errorMessage)
    public IReadOnlyDictionary<string, IEnumerable<string>> AsDictionary()
}

// Example response:
{
  "errors": {
    "email": [ "Email address is invalid" ],
    "password": [ "Must be at least 8 characters" ],
    "form": [ "Account already exists" ]
  }
}
\\\

---

## USEFUL FILE LOCATIONS

| Component | Files |
|-----------|-------|
| Core Execution | IApiOperationExecutor.cs, IOperationExecutorPipeline.cs, ApiOperationContext.cs |
| Handlers | IApiOperationHandler.cs, ApiOperationHandlerExecutorBuilder.cs |
| Operations | ApiOperationDescriptor.cs, ApiDataModel.cs |
| Middleware | IMiddlewareBuilder.cs, PipelineBuilder.cs, MiddlewareBuilderContext.cs |
| Validation | IValidator.cs, BlueprintValidationAttribute.cs, ValidationFailures.cs |
| Authorization | IApiAuthoriser.cs, ExecutionAllowed.cs, ClaimRequiredAttribute.cs |
| Links | ApiOperationLink.cs, ILinkableResource.cs, Link.cs, ApiResource.cs |
| HTTP | HttpOperationFeatureData.cs, ApiLinkGenerator.cs, StatusCodeResult.cs |
| Events | ResourceEvent.cs, ResourceEventDefinition.cs |
| Builders | BlueprintApiBuilder.cs, BlueprintHttpBuilder.cs |
| OpenAPI | BlueprintApiBuilderExtensions.cs (Blueprint.OpenApi) |

---

## FULL EXPLORATION DOCUMENT

Comprehensive documentation saved to:
**D:\Projects\Signature365\Blueprint_Exploration.md** (32.22 KB)

Contains:
- Full directory structure with all projects
- Complete interface and class signatures
- Detailed explanations of each pattern
- Code examples for key scenarios
- Attribute reference tables
- Service registration guide
- Code generation details

