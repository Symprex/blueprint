# BLUEPRINT LEGACY LIBRARY - COMPREHENSIVE EXPLORATION

## 1. DIRECTORY STRUCTURE

### Main Projects Under D:\Projects\Signature365\src\Blueprint:
- **Blueprint** - Core library containing operations, pipelines, middleware, authorization, validation
- **Blueprint.Compiler** - Code generation and compilation framework 
- **Blueprint.Http** - HTTP/REST API hosting and integration
- **Blueprint.OpenApi** - OpenAPI/Swagger documentation generation
- **Blueprint.Tasks** - Background task and job scheduling framework
- **Blueprint.Tasks.AspNetCore** - ASP.NET Core integration for tasks
- **Blueprint.Tasks.Hangfire** - Hangfire integration for background jobs
- **Blueprint.Testing** - Testing utilities
- **tests** - Test projects

---

## 2. KEY ARCHITECTURE PATTERNS & COMPONENTS

### A. CORE OPERATION MODEL

#### Operation Types (Base Interfaces):
- **ICommand** - Modifies data (POST/PATCH/DELETE), typically not safe to re-execute
- **ICommand<TResponse>** - Command with typed response
- **IQuery** - Reads data (GET), safe to re-execute multiple times  
- **IQuery<TResponse>** - Query with typed response
- **IReturn<TResponse>** - Marker interface for response type
- **IClaimable** - Has string ResourceKey property for authorization claims

#### Key Operation Markers:
- **UnexposedOperationAttribute** - Excludes operation from link generation (IsExposed = false)
- **AllowAnonymousAttribute** - Allows unauthenticated access (AnonymousAccessAllowed = true)
- **MustBeAuthenticatedAttribute** - Requires authenticated user
- **DoNotRecordPerformanceMetricsAttribute** - Skip metrics (e.g., health checks)
- **DoNotAuditAttribute** - Skip audit logging
- **HttpMethodAttribute** - Specify HTTP method (default: POST for ICommand, GET otherwise)

---

### B. NESTED EXECUTION & CHILD OPERATIONS

**Core Support:**
- **ApiOperationContext.CreateNested(Type type)** - Create child context from type
- **ApiOperationContext.CreateNested(object operation)** - Create child context from instance
- **ApiOperationContext.IsNested** - Boolean property indicating if context is child
- **ApiOperationContext.Parent** - Parent context reference (null if not nested)
- **ApiOperationContext.SkipAuthorisation** - Flag to skip auth for child operations (parent auth sufficient)

**Pipeline Execution:**
- **IOperationExecutorPipeline.ExecuteAsync()** - Main execution 
- **IOperationExecutorPipeline.ExecuteNestedAsync()** - Child execution (different pipeline generated)
- **IMiddlewareBuilder.SupportsNestedExecution** - Boolean: whether middleware runs in nested contexts

**Child Context Population (PopulateNested method):**
- Parent reference set
- Data dictionary shared
- ClaimsIdentity inherited
- UserAuthorisationContext inherited

---

### C. EXECUTOR & PIPELINE ARCHITECTURE

#### Core Executor Interfaces:
\\\csharp
public interface IApiOperationExecutor
{
    ApiDataModel DataModel { get; }
    Task<OperationResult> ExecuteAsync(ApiOperationContext context);
    Task<OperationResult> ExecuteWithNewScopeAsync(object operation, CancellationToken token = default);
}

public interface IOperationExecutorPipeline
{
    Task<OperationResult> ExecuteAsync(ApiOperationContext context);
    Task<OperationResult> ExecuteNestedAsync(ApiOperationContext context);
}
\\\

#### Executor Builders:
- **AutoApiOperationExecutorBuilder** - Development: generates code to disk, compiles missing pipelines in-memory
- **StaticApiOperationExecutorBuilder** - Production: loads pre-generated pipeline types from assembly
- **InMemoryApiOperationExecutorBuilder** - In-memory only compilation
- **IApiOperationExecutorBuilder** - Factory interface for creating executors

#### Handler Execution:
- **IApiOperationHandler<T>** - Generic handler for operation type T
- **ValueTask<object> Handle(T operation, ApiOperationContext context)** - Handler method signature
- **ApiOperationHandlerExecutorBuilder** - Code generator for IApiOperationHandler invocation

**Handler Registration:**
- Handlers discovered via scanning or explicit registration
- Can implement multiple IApiOperationHandler<T> interfaces
- Retrieved from DI container during execution

---

### D. MIDDLEWARE & PIPELINE BUILDER PATTERN

#### Core Interfaces:
\\\csharp
public interface IMiddlewareBuilder
{
    bool SupportsNestedExecution { get; }
    bool Matches(ApiOperationDescriptor operation);
    void Build(MiddlewareBuilderContext context);
}

public interface IOperationExecutorBuilder
{
    Type HandlerType { get; }
    Variable Build(MiddlewareBuilderContext context, ExecutorReturnType executorReturnType);
}
\\\

#### Middleware Stages (MiddlewareStage enum):
1. **Population** - Message/operation population from HTTP request
2. **Validation** - Input validation
3. **PreExecution** - Pre-execution logic (authentication, authorisation)
4. **Execution** - Handler execution
5. **PostExecution** - Post-execution logic (link generation, resource events)

#### PipelineBuilder Configuration:
\\\csharp
public class PipelineBuilder
{
    public PipelineBuilder AddMiddleware<T>(MiddlewareStage middlewareStage) 
    public PipelineBuilder AddMiddlewareBefore<T>(MiddlewareStage middlewareStage)
    public PipelineBuilder AddMiddleware(IMiddlewareBuilder builder, MiddlewareStage middlewareStage)
}
\\\

#### Code Generation Context:
\\\csharp
public class MiddlewareBuilderContext
{
    public GeneratedMethod ExecuteMethod { get; }
    public GeneratedType GeneratedType { get; }
    public GeneratedAssembly GeneratedAssembly { get; }
    public ApiOperationDescriptor Descriptor { get; }
    public ApiDataModel Model { get; }
    public IServiceProvider ServiceProvider { get; }
    public bool IsNested { get; } // True when building nested pipeline
    public IReadOnlyDictionary<Type, List<Func<Variable, IEnumerable<Frame>>>> ExceptionHandlers { get; }
}
\\\

#### Built-in Middleware (BuiltinBlueprintMiddlewares):
- MessagePopulationMiddlewareBuilder
- ValidationMiddlewareBuilder
- AuthenticationMiddlewareBuilder
- UserContextLoaderMiddlewareBuilder
- AuthorisationMiddlewareBuilder
- OperationExecutorMiddlewareBuilder
- ReturnFrameMiddlewareBuilder

---

### E. VALIDATION FRAMEWORK

#### Validation Interfaces & Classes:
\\\csharp
public interface IValidator
{
    Task<ValidationFailures> GetValidationResultsAsync(
        object value, 
        ApiOperationContext apiOperationContext);
}

public abstract class BlueprintValidationAttribute : Attribute
{
    public virtual string ErrorMessage { get; set; }
    
    public virtual async Task<ValidationResult> GetValidationResultAsync(
        object value, 
        string propertyName, 
        ApiOperationContext apiOperationContext)
    
    protected virtual Task<bool> IsValidAsync(
        object value, 
        string propertyName, 
        ApiOperationContext apiOperationContext)
}
\\\

#### Validation Sources:
- **DataAnnotationsValidationSource** - System.ComponentModel.DataAnnotations attributes
- **BlueprintValidationSource** - Blueprint custom validation attributes
- **IValidationSourceBuilder** - Service interface for validation providers

#### Built-in Custom Validation Attributes:
- **GreaterThanAttribute**, **GreaterThanOrEqualToAttribute**
- **LessThanAttribute**, **LessThanOrEqualToAttribute**  
- **GreaterThanPropertyAttribute** - Compare to another property
- **InFutureAttribute**, **InPastAttribute**
- **NotInFutureAttribute**, **NotInPastAttribute**
- **MustBeTrueAttribute**
- **NotEmptyListAttribute**, **MaxItemsListAttribute**
- **RequiredIfAttribute**, **RequiredIfNotAttribute**
- **NotAllowedIfAttribute**
- **ResourceKeyAttribute** - Validates resource key format
- **PasswordAttribute**, **EmailAddressAttribute**, **PhoneNumberAttribute**
- **UrlAttribute**, **UkPostcodeAttribute**, **RegexAttribute**

#### Validation Result Types:
\\\csharp
public class ValidationFailures
{
    const string FormLevelPropertyName = "form"; // For non-property validation errors
    
    public int Count { get; }
    public void AddFailure(ValidationResult validationResult)
    public void AddFailure(string propertyName, string errorMessage)
    public IReadOnlyDictionary<string, IEnumerable<string>> AsDictionary()
}

public class ValidationFailedOperationResult : OperationResult
{
    // Serialized as validation error response
}
\\\

---

### F. AUTHORIZATION & CLAIMS FRAMEWORK

#### Core Authorization Interfaces:
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

public interface IApiAuthoriserAggregator
{
    ValueTask<ExecutionAllowed> CanShowLinkAsync(
        ApiOperationContext operationContext, 
        ApiOperationDescriptor descriptor, 
        object resource);
}
\\\

#### Authorization Attributes:
\\\csharp
[AttributeUsage(AttributeTargets.Class)]
public class ClaimRequiredAttribute : Attribute
{
    // Regex-based property expansion: {PropertyName:AlternatePropertyName}
    // Wildcard '*' means any value with this claim type is allowed
    public string ClaimType { get; }
    public string ValueTemplate { get; } // Resource key template or '*'
    public string ValueType { get; }     // Right/permission name
    
    public Claim GetClaim(object resource) // Expands template using resource properties
}

[AttributeUsage(AttributeTargets.Class)]
public class MustBeAuthenticatedAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
public class AllowAnonymousAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
public class PermissionAttribute : Attribute 
{
    public string Permission { get; }
}

[AttributeUsage(AttributeTargets.Class)]
public class PermissionForAttribute : Attribute 
{
    // Links specific operation to resource claims requirement
}
\\\

#### Resource Key Expansion:
\\\csharp
public interface IResourceKeyExpander
{
    // Expands "User/1" to "Account/6/Team/5/User/1" hierarchy
    // Allows parent claims to apply to children (e.g., Account/6 claim grants User/1)
    string Expand(string resourceKey);
}

public interface IClaimable
{
    string ResourceKey { get; }
}

public class ResourceKey<T>
{
    public T Id { get; }
    public string ResourceType { get; }
    
    public static ResourceKey<T> Parse(string resourceKey)
    public static T ParseId(string resourceKey)
    public static bool TryParse(string resourceKey, out ResourceKey<T> parsedResourceKey)
}
\\\

#### Authorization Result:
\\\csharp
public struct ExecutionAllowed
{
    public static readonly ExecutionAllowed Yes;
    public static readonly Task<ExecutionAllowed> YesTask;
    
    public bool IsAllowed { get; }
    public string Reason { get; }
    public string Message { get; }
    public ExecutionAllowedFailureType? FailureType { get; }
    
    public static ExecutionAllowed No(
        string reason, 
        string message, 
        ExecutionAllowedFailureType failureType)
}

public enum ExecutionAllowedFailureType
{
    Authorisation,
    Authentication,
    Unknown
}
\\\

#### Built-in Authorizers:
- **ClaimsRequiredApiAuthoriser** - Checks claims against ClaimRequired attributes
- **MustBeAuthenticatedApiAuthoriser** - Ensures user is authenticated
- **ApiAuthoriserAggregator** - Aggregates multiple authorizers

---

### G. OPERATION HANDLING & SCANNING

#### Operation Descriptor:
\\\csharp
public class ApiOperationDescriptor
{
    public string Name { get; set; }
    public Type OperationType { get; }
    public string Source { get; }
    public object[] TypeAttributes { get; }
    public PropertyInfo[] Properties { get; }
    public object[][] PropertyAttributes { get; }
    
    public bool AnonymousAccessAllowed { get; set; }
    public bool IsExposed { get; set; }
    public bool ShouldAudit { get; set; }
    public bool RecordPerformanceMetrics { get; set; }
    public bool IsCommand { get; } // Implements ICommand
    public bool AllowMultipleHandlers { get; set; }
    public bool RequiresReturnValue { get; set; }
    
    public IReadOnlyList<ApiOperationLink> Links { get; }
    public IReadOnlyList<ResponseDescriptor> Responses { get; }
    public IReadOnlyList<IOperationExecutorBuilder> Handlers { get; }
    
    public string PipelineNamespace { get; } // "Blueprint.Generated"
    public string PipelineClassName { get; }
    
    public void RegisterHandler(IOperationExecutorBuilder builder)
    public void AddLink(ApiOperationLink apiOperationLink)
    public void AddResponse(ResponseDescriptor responseDescriptor)
    
    public T GetFeatureData<T>()
    public bool TryGetFeatureData<T>(out T feature)
    public void SetFeatureData(object newFeatureData)
    
    public Type TryFindPipelineHandler(Assembly pipelineAssembly)
    public object CreateInstance()
}
\\\

#### Operation Context:
\\\csharp
public class ApiOperationContext
{
    public ApiDataModel DataModel { get; }
    public ApiOperationDescriptor Descriptor { get; }
    public CancellationToken OperationCancelled { get; }
    public object Operation { get; set; }
    public IServiceProvider ServiceProvider { get; set; }
    public ApiOperationContext Parent { get; set; }
    public Activity Activity { get; set; } // APM integration
    public bool IsNested { get; } // Parent != null
    public bool SkipAuthorisation { get; set; }
    
    public IUserAuthorisationContext UserAuthorisationContext { get; set; }
    public ClaimsIdentity ClaimsIdentity { get; set; }
    public Dictionary<string, object> Data { get; } // Generic middleware storage
    
    public ApiOperationContext CreateNested(Type type)
    public ApiOperationContext CreateNested(object operation)
}
\\\

#### Operation Handlers:
\\\csharp
[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public interface IApiOperationHandler<in T>
{
    // Can be async or sync (returns default or new ValueTask(...))
    ValueTask<object> Handle(T operation, ApiOperationContext apiOperationContext);
}
\\\

#### Handler Scanning:
- **ApiOperationHandlerExecutorBuilder** - Handles IApiOperationHandler<T> instances
- **ApiOperationInClassConventionExecutorBuilder** - Handles methods in operation class
- **IOperationExecutorBuilderScanner** - Service interface for handler discovery
- Support for multiple handlers per operation (configurable)

---

### H. HATEOAS LINKS & LINK GENERATION

#### Link Definitions:
\\\csharp
public class ApiOperationLink
{
    // System-level link (no specific resource)
    public ApiOperationLink(
        ApiOperationDescriptor operationDescriptor, 
        string urlFormat,              // e.g., "users/{Id:UserId}"
        string rel)                    // e.g., "self", "create"
    
    // Resource-level link (bound to specific resource type)
    public ApiOperationLink(
        ApiOperationDescriptor operationDescriptor, 
        string urlFormat,
        string rel, 
        Type resourceType)             // Must implement ILinkableResource
    
    public IReadOnlyList<ApiOperationLinkPlaceholder> Placeholders { get; }
    public string RoutingUrl { get; }  // Simplified for routing
    public string UrlFormat { get; }   // Original format
    public ApiOperationDescriptor OperationDescriptor { get; }
    public string Rel { get; }
    public Type ResourceType { get; }  // null for root links
    public bool IsRootLink { get; }
    
    public bool HasPlaceholders()
    public string CreateRelativeUrl(object value)
    public string CreateRelativeUrl(object value, bool includeExtraPropertiesAsQueryString)
}

public class ApiOperationLinkPlaceholder
{
    public string OriginalText { get; }        // e.g., "{Id:UserId(D)}"
    public int Index { get; }
    public PropertyInfo Property { get; }
    public string AlternatePropertyName { get; } // From alternate:name syntax
    public string Format { get; }               // Custom format specifier
}
\\\

#### Link Attributes:
\\\csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class LinkAttribute : Attribute
{
    public Type ResourceType { get; }
    public string RoutePattern { get; }
    public string Rel { get; set; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class SelfLinkAttribute : LinkAttribute
{
    // Rel = "self"
    public SelfLinkAttribute(Type resourceType, string resourceRoutePattern)
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RootLinkAttribute : LinkAttribute
{
    // ResourceType = null (system-level)
    public RootLinkAttribute(string routePattern)
}
\\\

#### Link Generation & Resources:
\\\csharp
public interface ILinkableResource
{
    IDictionary<string, Link> Links { get; }
    void AddLink(string rel, Link link);
}

public record Link
{
    public string Href { get; set; }
    public string Type { get; set; } // Media type
}

public abstract class ApiResource : ILinkableResource, IHaveResourceKey
{
    [JsonProperty(PropertyName = "\")]
    public string Object { get; protected set; } // Resource type name
    
    [JsonProperty(PropertyName = "\")]
    public IDictionary<string, Link> Links { get; }
    
    [JsonIgnore]
    public virtual string ResourceKey { get; } // For authorization
    
    [JsonProperty(PropertyName = "\")]
    public bool IsPartial { get; set; } // Indicates incomplete data
    
    public static string GetTypeName(Type resourceType)
}

public interface IApiLinkGenerator
{
    Link CreateSelfLink<T>(int id, object queryString = null) where T : ApiResource
    Link CreateSelfLink<T>(long id, object queryString = null) where T : ApiResource
    Link CreateSelfLink<T>(string id, object queryString = null) where T : ApiResource
    Link CreateSelfLink<T>(Guid id, object queryString = null) where T : ApiResource
    Link CreateSelfLink<T>(object idDefinition, object queryString = null) where T : ApiResource
}
\\\

#### Resource Events (For Change Notifications):
\\\csharp
public class ResourceEvent : IApiResourceCollection
{
    public ResourceEvent(
        ResourceEventChangeType changeType,
        string eventId,
        Type resourceType,
        IQuery selfQuery)
    
    public ResourceEvent(
        ResourceEventChangeType changeType,
        string eventId,
        object data)
    
    [JsonProperty(PropertyName = "\")]
    public string Object => "event";
    
    public string EventId { get; }
    public Type ResourceType { get; }
    public string ResourceObject { get; }
    public IQuery SelfQuery { get; set; }
    public object Data { get; }
    public ResourceEventChangeType ChangeType { get; }
    public DateTime Created { get; }
}

public enum ResourceEventChangeType
{
    Created,
    Updated,
    Deleted
}
\\\

---

### I. HTTP INTEGRATION

#### HTTP Operation Feature Data:
\\\csharp
public class HttpOperationFeatureData
{
    public string HttpMethod { get; } // "GET", "POST", "PUT", "DELETE", "PATCH"
}
\\\

#### HTTP Host Convention:
\\\csharp
public class HttpHost : IOperationScannerConvention, IBlueprintHost
{
    // Applies HTTP-specific metadata to operations with LinkAttribute
    // ICommand -> POST by default, others GET
    // Sets HttpOperationFeatureData feature
    // AllowMultipleHandlers = false
    // RequiresReturnValue = true
}
\\\

#### HTTP Response Types:
\\\csharp
public abstract class HttpResult : OperationResult
{
    public HttpStatusCode StatusCode { get; }
    public Dictionary<string, StringValues> Headers { get; }
}

public partial class StatusCodeResult : HttpResult
{
    // Pre-generated subclasses for common codes:
    // StatusCodeResult.Created (201)
    // StatusCodeResult.Accepted (202)
    // StatusCodeResult.NoContent (204)
    // StatusCodeResult.BadRequest (400)
    // StatusCodeResult.Unauthorized (401)
    // StatusCodeResult.Forbidden (403)
    // StatusCodeResult.NotFound (404)
    // StatusCodeResult.Conflict (409)
    // StatusCodeResult.InternalServerError (500)
}

public class OkResult : HttpResult { }
public class OkResult<T> : HttpResult where T : class
{
    public T Content { get; set; }
}

public class PlainTextResult : HttpResult
{
    public string Content { get; set; }
}

public class ProblemDetails : HttpResult
{
    public string Type { get; set; }
    public string Title { get; set; }
    public int Status { get; set; }
    public string Detail { get; set; }
    public string Instance { get; set; }
    public IDictionary<string, object> Extensions { get; set; }
}
\\\

#### HTTP Feature Integration:
\\\csharp
public static class BlueprintApiBuilderExtensions
{
    public static BlueprintApiBuilder Http(
        this BlueprintApiBuilder apiBuilder,
        Action<BlueprintHttpBuilder> configure = null)
    
    public static BlueprintApiBuilder AddHateoasLinks(
        this BlueprintApiBuilder apiBuilder)
    
    public static BlueprintApiBuilder AddResourceEvents<T>(
        this BlueprintApiBuilder pipelineBuilder) 
        where T : class, IResourceEventAuditRepository
}

public class BlueprintHttpBuilder
{
    public BlueprintHttpBuilder Json(Action<BlueprintJsonOptions> configure)
    public BlueprintHttpBuilder Options(Action<BlueprintHttpOptions> configure)
}

public class BlueprintJsonOptions
{
    public JsonSerializerOptions SerializerOptions { get; set; }
}

public class BlueprintHttpOptions
{
    public bool WriteIndentedJson { get; set; }
    public bool SerializeNullValues { get; set; }
}
\\\

---

### J. FLUENT BUILDER PATTERN (BlueprintApiBuilder)

#### Main Builder Class:
\\\csharp
public class BlueprintApiBuilder
{
    public IServiceCollection Services { get; }
    
    // Operation registration
    public BlueprintApiBuilder Operations(Action<OperationScanner> scannerAction)
    public BlueprintApiBuilder WithOperation<T>(Action<ApiOperationDescriptor> configure = null)
    public BlueprintApiBuilder WithHandler<T>(
        IApiOperationHandler<T> handler, 
        Action<ApiOperationDescriptor> configure = null)
    
    // Pipeline/middleware configuration
    public BlueprintApiBuilder Pipeline(Action<PipelineBuilder> pipelineAction)
    
    // Compilation options
    public BlueprintApiBuilder Compilation(
        Action<BlueprintCompilationBuilder> compilationAction)
    
    // Authentication setup
    public BlueprintApiBuilder Authentication(
        Action<BlueprintAuthenticationBuilder> authenticationAction)
    
    // Validation configuration  
    public BlueprintApiBuilder AddValidation()
    
    // Message source registration (for HTTP request population)
    public BlueprintApiBuilder AddMessageSource<T>() where T : IMessagePopulationSource
    public BlueprintApiBuilder AddMessageSource(IMessagePopulationSource source)
    
    // Code generation customization
    public BlueprintApiBuilder Compilation(Action<VariableSourceRegistry> variableAction)
}
\\\

#### Configuration Extension Methods:
\\\csharp
// In Microsoft.Extensions.DependencyInjection namespace

public static IServiceCollection AddBlueprintApi(
    this IServiceCollection services,
    Action<BlueprintApiBuilder> configure)

public static BlueprintApiBuilder Http(
    this BlueprintApiBuilder apiBuilder,
    Action<BlueprintHttpBuilder> configure = null)

public static BlueprintApiBuilder AddOpenApi(
    this BlueprintApiBuilder pipelineBuilder)

public static BlueprintApiBuilder AddOpenApi(
    this BlueprintApiBuilder pipelineBuilder,
    Action<OpenApiOptions> configure)

public static BlueprintApiBuilder AddHateoasLinks(
    this BlueprintApiBuilder apiBuilder)

public static BlueprintApiBuilder AddResourceEvents<T>(
    this BlueprintApiBuilder pipelineBuilder) 
    where T : class, IResourceEventAuditRepository
\\\

---

### K. OPENAPI CONFIGURATION

#### OpenAPI Generation:
\\\csharp
public class OpenApiDocumentBuilder
{
    // Generates OpenAPI document from registered operations and links
    public OpenApiDocument Build()
}

public class BlueprintJsonSchemaGenerator
{
    // Generates JSON schema for operation properties
}

public class BlueprintLinkSchemaProcessor
{
    // Adds Blueprint-specific \ schema to resources
}

public class OpenApiOptions
{
    public Action<OpenApiDocument> PostConfigure { get; set; }
    // Additional NSwag options
}

public class OpenApiQuery : IQuery<OpenApiDocument>
{
    // Returns OpenAPI spec at /openapi endpoint
}
\\\

---

### L. OPERATION RESULT EXECUTION

#### Result Model:
\\\csharp
public abstract class OperationResult
{
    public abstract Task ExecuteAsync(ApiOperationContext context);
    
    public virtual T Unwrap<T>()
    {
        if (this is T asType)
            return asType;
        throw new InvalidOperationException(...);
    }
}

public interface IOperationResultExecutor
{
    // No direct implementation shown in exploration
}

public interface IOperationResultExecutor<in TResult> where TResult : OperationResult
{
    Task ExecuteAsync(TResult result, ApiOperationContext context);
}
\\\

#### Built-in Result Types:
- **OkResult** - 200 OK (possibly with generic content)
- **NoResultOperationResult** - No response needed
- **OperationHandlerNotFoundResult** - No handler matched
- **UnhandledExceptionOperationResult** - Exception during execution
- **ValidationFailedOperationResult** - Input validation failed
- **StatusCodeResults** - HTTP status codes (201, 202, 204, 400, 401, 403, 404, 409, 500)
- **HttpResult** - Base for all HTTP responses
- **PlainTextResult** - Plain text response
- **ProblemDetails** - RFC 7807 problem details

---

### M. API DATA MODEL & REGISTRATION

#### ApiDataModel:
\\\csharp
public class ApiDataModel
{
    public IEnumerable<ApiOperationLink> Links { get; }
    public IEnumerable<ApiOperationDescriptor> Operations { get; }
    
    public ApiOperationContext CreateOperationContext(
        IServiceProvider serviceProvider,
        Type type,
        CancellationToken token)
    
    public ApiOperationContext CreateOperationContext(
        IServiceProvider serviceProvider,
        object operation,
        CancellationToken token)
    
    public void RegisterOperation(ApiOperationDescriptor descriptor)
    
    private void RegisterLink(ApiOperationLink link)
    
    public ApiOperationDescriptor FindOperation(Type operationType)
    
    public IEnumerable<ApiOperationLink> GetLinkFor(Type resourceType, string rel)
}
\\\

---

## 3. CODE GENERATION & COMPILATION

### Compilation Framework:
- **GeneratedAssembly** - Represents compiled assembly
- **GeneratedType** - Represents generated pipeline type
- **GeneratedMethod** - Represents ExecuteAsync/ExecuteNestedAsync methods
- **FramesCollection** - Collection of code frames to generate
- **Frame** - Base class for code generation (LogFrame, IfBlock, ThrowExceptionFrame, etc.)
- **Variable** - Represents a variable in generated code
- **MethodCall** - Represents method invocation
- **ConstructorFrame** - Instantiation code
- **CastVariable** - Type casting

### Code Generation Customization:
- **IVariableSource** - Sources for finding/creating variables
- **HttpVariableSource** - HTTP-specific variables
- **DependencyInjectionVariableSource** - DI container access
- **ApiOperationContextVariableSource** - Context-specific variables

### Error Handling:
- **ErrorHandlerFrame** - Try-catch frame generation
- **ThrowExceptionFrame** - Exception throwing
- **RegisterExceptionHandler** - Register per-type exception handlers

---

## 4. OPERATION SCANNING & CONVENTIONS

### Scanner Interfaces:
- **IOperationScanner** - Discovers operations
- **IOperationScannerConvention** - Applies conventions to discovered operations
- **IBlueprintHost** - Host-specific operation configuration
- **IOperationExecutorBuilderScanner** - Discovers handlers

### Convention System:
- Applied during operation registration
- Can modify ApiOperationDescriptor (links, feature data, etc.)
- HttpHost convention adds HTTP-specific metadata

---

## 5. KEY ATTRIBUTES SUMMARY

### Operation-Level Attributes:
\\\
@UnexposedOperationAttribute - Hide from link generation (IsExposed=false)
@AllowAnonymousAttribute - Allow unauthenticated access
@MustBeAuthenticatedAttribute - Require authentication
@DoNotRecordPerformanceMetricsAttribute - Skip metrics
@DoNotAuditAttribute - Skip audit logging  
@HttpMethodAttribute(method) - Specify HTTP verb
@LinkAttribute(resourceType, pattern) - System/resource-level endpoint
@SelfLinkAttribute(resourceType, pattern) - Self link (rel="self")
@RootLinkAttribute(pattern) - Root/system link
@ClaimRequiredAttribute(type, template, right) - Require specific claims
@PermissionAttribute(permission) - Require permission
@PermissionForAttribute(...) - Link operation to resource claims
\\\

### Property-Level Attributes:
\\\
@BlueprintValidationAttribute - Custom async validation
@GreaterThanAttribute, @GreaterThanOrEqualToAttribute
@LessThanAttribute, @LessThanOrEqualToAttribute
@GreaterThanPropertyAttribute - Compare to another property
@InFutureAttribute, @InPastAttribute
@NotInFutureAttribute, @NotInPastAttribute
@MustBeTrueAttribute
@NotEmptyListAttribute, @MaxItemsListAttribute
@RequiredIfAttribute, @RequiredIfNotAttribute
@NotAllowedIfAttribute
@ResourceKeyAttribute - Validate resource key
@PasswordAttribute, @EmailAddressAttribute, @PhoneNumberAttribute
@UrlAttribute, @UkPostcodeAttribute, @RegexAttribute
@DoNotCompareAttribute - Exclude from comparison
@SensitiveAttribute - Mark as sensitive for audit
@DoNotAuditAttribute - Don't audit this property
@DictionaryKeyAttribute - Dictionary key indicator
@FromBodyAttribute, @FromQueryAttribute, @FromHeaderAttribute, @FromCookieAttribute
\\\

---

## 6. TASK/BACKGROUND JOB FRAMEWORK

### Key Types (Blueprint.Tasks):
- **IBackgroundTask** - Marker interface for background jobs
- **IScheduledBackgroundTask** - Scheduled job
- **IBackgroundTaskScheduler** - Scheduler interface
- **BackgroundTaskScheduler** - Scheduler implementation
- **BackgroundTaskEnvelope** - Job envelope with metadata
- **ChildScheduledBackgroundTask** - Child job in execution context
- **BackgroundTaskRunnerMiddleware** - Middleware for task execution
- **IRecurringTaskScheduler** - Recurring job scheduling
- **RecurringTaskManager** - Manages recurring tasks
- **TaskExecutor** - Executes background tasks

### Features:
- Support for anonymous execution
- Child task execution within parent context
- Hangfire integration (Blueprint.Tasks.Hangfire)
- ASP.NET Core integration (Blueprint.Tasks.AspNetCore)

---

## 7. KEY INTERFACES SUMMARY

| Interface | Purpose |
|-----------|---------|
| IApiOperationExecutor | Creates/manages pipeline types |
| IOperationExecutorPipeline | Main/nested operation execution |
| IApiOperationHandler<T> | Handles specific operation type |
| IMiddlewareBuilder | Generates pipeline code |
| IOperationExecutorBuilder | Generates handler execution code |
| IValidator | Validates operation input |
| IApiAuthoriser | Authorizes operation execution |
| IApiAuthoriserAggregator | Aggregates authorizers |
| ILinkableResource | Resource with HATEOAS links |
| IResourceKeyExpander | Expands hierarchical keys |
| IClaimable | Has resource key for claims |
| IUserAuthorisationContext | User authorization context |
| IUserAuthorisationContextFactory | Creates auth context from claims |
| IClaimsIdentityProvider | Provides claims identity |
| IApiLinkGenerator | Generates HATEOAS links |
| IResourceLinkGenerator | Entity operation link generator |
| IResourceEventAuditRepository | Stores resource events |
| ICommandAsync | Async command execution |
| IQueryAsync | Async query execution |

---

## 8. KEY FILES REFERENCE

**Core:**
- Blueprint/IApiOperationExecutor.cs
- Blueprint/IOperationExecutorPipeline.cs
- Blueprint/IApiOperationHandler.cs
- Blueprint/ApiOperationDescriptor.cs
- Blueprint/ApiOperationContext.cs

**Middleware:**
- Blueprint/IMiddlewareBuilder.cs
- Blueprint/MiddlewareBuilderContext.cs
- Blueprint/Configuration/PipelineBuilder.cs

**Validation:**
- Blueprint/Validation/IValidator.cs
- Blueprint/Validation/BlueprintValidationAttribute.cs
- Blueprint/Validation/ValidationFailures.cs

**Authorization:**
- Blueprint/Authorisation/IApiAuthoriser.cs
- Blueprint/Authorisation/ClaimRequiredAttribute.cs
- Blueprint/Authorisation/ExecutionAllowed.cs

**Links:**
- Blueprint/ApiOperationLink.cs
- Blueprint/ILinkableResource.cs
- Blueprint/Link.cs

**HTTP:**
- Blueprint.Http/HttpOperationFeatureData.cs
- Blueprint.Http/ApiLinkGenerator.cs
- Blueprint.Http/ResourceEvent.cs
- Blueprint.Http/ApiResource.cs

**Builders:**
- Blueprint/Configuration/BlueprintApiBuilder.cs
- Blueprint.Http/BlueprintHttpBuilder.cs
- Blueprint.OpenApi/BlueprintApiBuilderExtensions.cs

