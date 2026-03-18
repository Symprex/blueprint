# BLUEPRINT LEGACY LIBRARY - COMPREHENSIVE ARCHITECTURAL ANALYSIS

## 1. MODULE SYSTEM & OPERATIONS

### Core Marker Interfaces (CQRS-style operations):
- **ICommand** (Blueprint/ICommand.cs)
  - Marker interface for write operations (POST, PATCH, DELETE)
  - Generic variant: ICommand<TResponse>
  - Indicates operation modifies data

- **IQuery** (Blueprint/IQuery.cs)
  - Marker interface for read operations (GET)
  - Generic variant: IQuery<TResponse>
  - Indicates operation retrieves data without modification

- **IReturn<TResponse>** (Blueprint/IReturn.cs)
  - Generic marker interface specifying response type
  - Implemented by ICommand<TResponse> and IQuery<TResponse>

### Host System - Operation Registration:
- **IBlueprintHost** (Blueprint/Configuration/IBlueprintHost.cs)
  - IsSupported(Type operationType) - determines if operation is supported by this host
  - Implementations:
    - CommandOrQueryHost - supports ICommand and IQuery
    - HttpHost - supports operations with LinkAttribute
    - TasksHost - supports IBackgroundTask operations

- **IOperationScannerConvention** (Blueprint/Configuration/IOperationScannerConvention.cs)
  - Apply(ApiOperationDescriptor) - modifies descriptor metadata
  - Implementations include HttpHost (IOperationScannerConvention + IBlueprintHost)
  - XmlDocResponseConvention, ApiExceptionFactoryResponseConvention

- **OperationScanner** (Blueprint/Configuration/OperationScanner.cs)
  - Scans assemblies for operations and handlers
  - Methods: ScanCallingAssembly(), ScanOperationsInAssembly(), AddOperation<T>()
  - Manages hosts, conventions, assemblies
  - Lazy evaluation pattern for flexibility

- **ExecutorScanner** (Blueprint/Configuration/ExecutorScanner.cs)
  - Finds IOperationExecutorBuilder implementations for operations
  - Default scanners:
    - ApiOperationHandlerExecutorBuilderScanner (IApiOperationHandler<T>)
    - ApiOperationInClassConventionExecutorBuilderScanner (inline handlers)
  - Returns problems if handlers not found

---

## 2. DATA MODEL & VARIABLE REGISTRY

### Core Data Model:
- **ApiDataModel** (Blueprint/ApiDataModel.cs)
  - Central registry for all operations and links
  - Methods:
    - RegisterOperation(ApiOperationDescriptor)
    - CreateOperationContext(IServiceProvider, Type, CancellationToken)
    - FindOperation(Type), TryFindOperation(Type, out descriptor)
    - GetLinksForOperation(Type), GetLinksForResource(Type)
    - GetRootLinks()
  - Internal dictionaries:
    - _allOperations: Type -> ApiOperationDescriptor
    - _operationTypeToLinks: Type -> List<ApiOperationLink>
    - _resourceTypeToLinks: Type -> List<ApiOperationLink>

### Operation Descriptor:
- **ApiOperationDescriptor** (Blueprint/ApiOperationDescriptor.cs)
  - OperationType: Type of operation
  - Name: Derived from class name (Query/Command removed)
  - Source: How operation was discovered (scan vs explicit)
  - TypeAttributes, Properties, PropertyAttributes: Cached reflection data
  - Features:
    - AnonymousAccessAllowed, IsExposed, ShouldAudit, RecordPerformanceMetrics
    - IsCommand: bool (implements ICommand)
    - Links, Responses, Handlers: Collections
    - AllowMultipleHandlers, RequiresReturnValue: Configurable flags
    - PipelineNamespace, PipelineClassName: Code generation names
  - Methods:
    - RegisterHandler(IOperationExecutorBuilder)
    - AddLink(ApiOperationLink), AddResponse(ResponseDescriptor)
    - GetFeatureData<T>(), TryGetFeatureData<T>()
    - SetFeatureData(object)
    - TryFindPipelineHandler(Assembly)

### Operation Context (Variable Registry):
- **ApiOperationContext** (Blueprint/ApiOperationContext.cs)
  - ServiceProvider: Scoped DI container
  - DataModel: Reference to ApiDataModel
  - Descriptor: Operation descriptor
  - Operation: The operation instance being executed
  - Data: Dictionary<string, object> - middleware state sharing
  - OperationCancelled: CancellationToken
  - Parent: Parent context (for nested operations)
  - Activity: Activity.Current (APM integration)
  - IsNested: bool
  - UserAuthorisationContext, ClaimsIdentity: Auth data
  - SkipAuthorisation: bool
  - Methods:
    - CreateNested(Type), CreateNested(object operation)
    - PopulateNested(context) - inherits parent data

### Operation Links:
- **ApiOperationLink** (Blueprint/ApiOperationLink.cs)
  - OperationDescriptor: Reference to operation
  - UrlFormat: URL template (not starting with /)
  - RoutingUrl: Simplified URL for routing (templates cleaned)
  - Rel: Link relationship type (self, create, etc.)
  - ResourceType: Associated resource type (can be null for root links)
  - IsRootLink: bool
  - Placeholders: List<ApiOperationLinkPlaceholder> - parsed URL parameters
  - Methods:
    - CreateRelativeUrl(object), CreateRelativeUrl(object, bool includeExtra)
    - HasPlaceholders()
  - Regex parsing for: {propName}, {propName:alternateName}, {propName:alternateName(format)}

---

## 3. HOST MODELING

### HTTP Host:
- **HttpHost** (Blueprint.Http/HttpHost.cs)
  - Implements: IBlueprintHost, IOperationScannerConvention
  - IsSupported: Returns true if operation has LinkAttribute
  - Apply: Sets HttpOperationFeatureData, allows single handler only
  - Default HTTP method: POST for commands, GET for queries
  - Registers responses: Status code results, OK results with content
  - Adds ValidationFailedOperationResult (422) and UnhandledExceptionOperationResult (500)

### Background Tasks Host:
- **TasksHost** (Blueprint.Tasks/TasksHost.cs)
  - Supports IBackgroundTask operations

### Command/Query Host:
- **CommandOrQueryHost** (Blueprint/Configuration/CommandOrQueryHost.cs)
  - Default host in OperationScanner
  - Supports ICommand and IQuery markers

---

## 4. PIPELINE & MIDDLEWARE SYSTEM

### Middleware Framework:
- **IMiddlewareBuilder** (Blueprint/IMiddlewareBuilder.cs)
  - SupportsNestedExecution: bool - can execute in child contexts
  - Matches(ApiOperationDescriptor): bool - applies to operation?
  - Build(MiddlewareBuilderContext): void - code generation

- **MiddlewareBuilderContext** (Blueprint/MiddlewareBuilderContext.cs)
  - ExecuteMethod: GeneratedMethod being built
  - GeneratedType, GeneratedAssembly: Code gen targets
  - Descriptor, Model, ServiceProvider: Operation context
  - IsNested: bool
  - ExceptionHandlers: Dictionary<Type, List<Func<Variable, IEnumerable<Frame>>>>
  - Methods:
    - AppendFrames(), Insert/Prepend operations
    - FindVariable<T>(), TryGetVariableFromContainer<T>()
    - RegisterExceptionHandler<T>()
    - etc.

### Pipeline Building:
- **PipelineBuilder** (Blueprint/Configuration/PipelineBuilder.cs)
  - Manages middleware registration by stage
  - Stages (enum MiddlewareStage):
    1. Setup - initial setup, logging, APM
    2. Population - message population (HTTP body, route, etc.)
    3. Authentication - load claims identity
    4. Authorisation - check permissions
    5. Validation - validate operation
    6. Execution - call handler
    7. PostExecution - auditing
    8. Cleanup - resource cleanup
  - Methods:
    - AddMiddleware<T>(MiddlewareStage), AddMiddlewareBefore<T>()
    - Internal sorting by stage then priority

### Standard Middleware Builders:
- **MessagePopulationMiddlewareBuilder** - population stage
- **OperationExecutorMiddlewareBuilder** - execution stage
- **ReturnFrameMiddlewareBuilder** - post-execution (high priority int.MaxValue)
- **ValidationMiddlewareBuilder** - validation stage
- **AuthenticationMiddlewareBuilder** - authentication stage
- **AuthorisationMiddlewareBuilder** - authorisation stage
- **LoggingMiddlewareBuilder** - setup stage
- **AuditMiddleware** - post-execution stage
- **LinkGeneratorMiddlewareBuilder** - HTTP-specific
- **ResourceEventHandlerMiddlewareBuilder** - HTTP-specific

### Executor Pipeline:
- **IOperationExecutorPipeline** (Blueprint/IOperationExecutorPipeline.cs)
  - ExecuteAsync(ApiOperationContext): Task<OperationResult>
  - ExecuteNestedAsync(ApiOperationContext): Task<OperationResult>
  - Generated per operation

---

## 5. OPERATION DISCOVERY & HANDLERS

### Operation Handlers:
- **IApiOperationHandler<T>** (Blueprint/IApiOperationHandler.cs)
  - Generic: T is the operation type
  - Handle(T operation, ApiOperationContext context): ValueTask<object>
  - Returns potentially null result
  - Supports async (async/await) and sync (return default or new ValueTask)

- **IOperationExecutorBuilder** (Blueprint/Middleware/IOperationExecutorBuilder.cs)
  - HandlerType: Type of actual handler
  - Build(MiddlewareBuilderContext, ExecutorReturnType): Variable
  - Code generation interface for handlers
  - Enum ExecutorReturnType: NoReturn, Return

### Operation Executor:
- **IApiOperationExecutor** (Blueprint/IApiOperationExecutor.cs)
  - DataModel: Reference to ApiDataModel
  - ExecuteAsync(ApiOperationContext): Task<OperationResult>
  - ExecuteWithNewScopeAsync(object operation, CancellationToken): Task<OperationResult>

- **IApiOperationExecutorBuilder** (Blueprint/IApiOperationExecutorBuilder.cs)
  - Build(): IApiOperationExecutor
  - Factory pattern for creating executors

---

## 6. EXCEPTIONS & ERROR HANDLING

### Exception Hierarchy:
- **ApiException** (Blueprint/ApiException.cs)
  - Base for all API exceptions
  - Title, Type (URI), Detail, HttpStatus
  - Extensions: Dictionary<string, object> for extra data
  - RFC 7807 Problem Details based

### Standard Exception Types:
- **BadRequestException** (Blueprint/Errors/BadRequestException.cs) - 400
- **NotFoundException** (Blueprint/Errors/NotFoundException.cs) - 404
- **UnauthorizedException** (Blueprint/Errors/UnauthorizedException.cs) - 401
- **ForbiddenException** (Blueprint/Errors/ForbiddenException.cs) - 403
- **ConflictException** (Blueprint/Errors/ConflictException.cs) - 409

### Validation Exceptions:
- **ValidationException** (Blueprint/Validation/ValidationException.cs)
  - ValidationResults: IReadOnlyDictionary<string, IEnumerable<string>>
  - FormLevelPropertyName: "form" for non-property errors
  - Remap(keyMappings): Remaps property names

- **ValidationFailures** (Blueprint/Validation/ValidationFailures.cs)
  - Count: int
  - AddFailure(ValidationResult), AddFailure(property, message)
  - AsDictionary()
  - Static: ValidationFailures.None

### Operation Results:
- **OperationResult** (Blueprint/OperationResult.cs)
  - Abstract base class
  - ExecuteAsync(ApiOperationContext): Task
  - Unwrap<T>(): T - unwrap to specific result type

- **IOperationResultExecutor<TResult>** (Blueprint/IOperationResultExecutor.cs)
  - Generic: TResult extends OperationResult
  - ExecuteAsync(ApiOperationContext, TResult): Task

- Result Types:
  - UnhandledExceptionOperationResult
  - ValidationFailedOperationResult
  - NoResultOperationResult
  - OkResult
  - HttpResult types (Blueprint.Http)

---

## 7. BACKGROUND TASKS

### Background Task Marker:
- **IBackgroundTask** (Blueprint.Tasks/IBackgroundTask.cs)
  - Marker interface for out-of-process work

### Task Scheduling:
- **IBackgroundTaskScheduler** (Blueprint.Tasks/IBackgroundTaskScheduler.cs)
  - Enqueue(IBackgroundTask): IScheduledBackgroundTask
  - Schedule(IBackgroundTask, TimeSpan): IScheduledBackgroundTask
  - RunNowAsync(): ValueTask
  - Deferred execution pattern (queued until RunNowAsync)

- **IScheduledBackgroundTask** (Blueprint.Tasks/IScheduledBackgroundTask.cs)
  - ContinueWith(IBackgroundTask, options?): IScheduledBackgroundTask
  - BackgroundTaskContinuationOptions: OnlyOnSucceededState, etc.

### Recurring Tasks:
- **IRecurringTaskScheduler** (Blueprint.Tasks/IRecurringTaskScheduler.cs)
  - GetTaskSchedulesAsync(): Task<IEnumerable<RecurringTaskSchedule>>
  - Discovered at regular intervals

- **IRecurringTaskProvider** (Blueprint.Tasks/Provider/IRecurringTaskProvider.cs)
  - UpdateAsync(IEnumerable<RecurringTaskScheduleDto>): Task
  - SetupRecurringManagerAsync(): Task

- **RecurringTaskSchedule** (Blueprint.Tasks/RecurringTaskSchedule.cs)
  - Task configuration and schedule definition

### Related:
- **IBackgroundTaskScheduleProvider** (Blueprint.Tasks/Provider/IBackgroundTaskScheduleProvider.cs)
- **IBackgroundTaskContextProvider** (Blueprint.Tasks/Provider/IBackgroundTaskContextProvider.cs)
- **IBackgroundTaskPreprocessor** (Blueprint.Tasks/IBackgroundTaskPreprocessor.cs)

---

## 8. AUTHENTICATION & AUTHORIZATION

### Authorization:
- **IApiAuthoriser** (Blueprint/Authorisation/IApiAuthoriser.cs)
  - AppliesTo(ApiOperationDescriptor): bool
  - CanExecuteOperationAsync(context, descriptor, operation): Task<ExecutionAllowed>
  - CanShowLinkAsync(context, descriptor, resource): Task<ExecutionAllowed>

- **IApiAuthoriserAggregator** (Blueprint/IApiAuthoriserAggregator.cs)
  - CanShowLinkAsync(...): ValueTask<ExecutionAllowed>

- **ExecutionAllowed** (Blueprint/Authorisation/ExecutionAllowed.cs)
  - Struct with static Yes/No members
  - IsAllowed: bool
  - Reason, Message: strings
  - FailureType: ExecutionAllowedFailureType (enum)

### Authorization Implementations:
- **ClaimsRequiredApiAuthoriser** - checks for claims
- **MustBeAuthenticatedApiAuthoriser** - requires non-anonymous user
- **ApiAuthoriserAggregator** - aggregates multiple authorizers

### Authentication:
- **IUserAuthorisationContext** (Blueprint/Authorisation/IUserAuthorisationContext.cs)
  - IsActive, IsAnonymous: bool
  - Id, IdentityId, AccountId, Email, Name: strings
  - PopulateMetadata(Action<string, object>): void

- **IUserAuthorisationContextFactory** (Blueprint/Authorisation/IUserAuthorisationContextFactory.cs)
  - CreateContextAsync(ClaimsIdentity): Task<IUserAuthorisationContext>

- **IClaimsIdentityProvider** - loads ClaimsIdentity from request

### Attributes:
- **AllowAnonymousAttribute** (Blueprint/Authorisation/AllowAnonymousAttribute.cs)
  - Marks operations accessible without authentication

### Middleware Builders:
- **AuthenticationMiddlewareBuilder** - loads claims identity
- **AuthorisationMiddlewareBuilder** - checks permissions
- **UserContextLoaderMiddlewareBuilder** - creates user context

---

## 9. VALIDATION

### Validator Interface:
- **IValidator** (Blueprint/Validation/IValidator.cs)
  - GetValidationResultsAsync(object, ApiOperationContext): Task<ValidationFailures>

### Validation Sources:
- **IAttributeValidationSource** - attribute-based (DataAnnotations)
- **IClassValidationSource** - class-level validation
- **IRequireCustomValidation** - interface for custom validation
- **DataAnnotationsValidationSource** - standard .NET validation
- **BlueprintValidationSource** - custom implementation

### Validation Middleware:
- **ValidationMiddlewareBuilder** (Blueprint/Middleware/ValidationMiddlewareBuilder.cs)
  - Applied at Validation stage

---

## 10. RESOURCES & LINKS

### Resource Base:
- **ILinkableResource** (Blueprint/ILinkableResource.cs)
  - Links: IDictionary<string, Link>
  - AddLink(rel, Link): void

- **ApiResource** (Blueprint.Http/ApiResource.cs)
  - Implements ILinkableResource, IHaveResourceKey
  - Object: string () - resource type name
  - Links: IDictionary<string, Link> ()
  - IsPartial: bool ()
  - ResourceKey: string (virtual)
  - GetTypeName(Type): Removes ApiResource/Resource/DTO suffixes

- **Link** (Blueprint/Link.cs)
  - Record type
  - Href: string
  - Type: string (optional, resource type)

### Resource Collections:
- **IApiResourceCollection** (Blueprint/IApiResourceCollection.cs)
  - GetEnumerable(): IEnumerable<object>
  - For nested resources

- **PagedApiResource**, **ListApiResource**, **ContinuationPagedApiResource**

### Link Generation:
- **IApiLinkGenerator** (Blueprint.Http/IApiLinkGenerator.cs)
  - CreateSelfLink<T>(id variants)
  - CreateUrl(ApiOperationLink, object result?)
  - CreateUrl(object operation)

- **IResourceLinkGenerator** (Blueprint.Http/IResourceLinkGenerator.cs)
  - AddLinksAsync(IApiLinkGenerator, ApiOperationContext, ILinkableResource)

- Implementations:
  - ApiLinkGenerator
  - ResourceLinkGenerator
  - EntityOperationResourceLinkGenerator
  - LinkGeneratorHandler (middleware)

### Link Attributes:
- **LinkAttribute** (Blueprint/LinkAttribute.cs)
  - ResourceType: Type
  - RoutePattern: string
  - Rel: string (optional)

- **SelfLinkAttribute** - extends LinkAttribute for 'self' relations
- **RootLinkAttribute** - for root-level links

---

## CONFIGURATION & BUILDERS

### Main Builder:
- **BlueprintApiBuilder** (Blueprint/Configuration/BlueprintApiBuilder.cs)
  - Services: IServiceCollection
  - Operations(action): Configure OperationScanner
  - WithOperation<T>(), WithHandler<T>()
  - Pipeline(action): Configure PipelineBuilder
  - Compilation(action): Code generation
  - Methods for adding specific features

### API Data Model:
- **BlueprintApiOptions** - Configuration holder

### HTTP Specific:
- **HttpOperationFeatureData** - HTTP method and other HTTP-specific data
- **ResponseDescriptor** - Response type and HTTP status mapping

### Message Population:
- **IMessagePopulationSource** (Blueprint/Middleware/IMessagePopulationSource.cs)
  - Priority: int - sources with higher priority can't be overridden
  - GetOwnedProperties(...): Declares which properties it owns
  - Build(...): Generates code to populate message
  - OwnedPropertyDescriptor - tracks property ownership

- HTTP Sources:
  - HttpBodyMessagePopulationSource - JSON/XML body
  - HttpRouteMessagePopulationSource - URL route values
  - HttpPartMessagePopulationSource - Headers, query string

---

## KEY PATTERNS & RELATIONSHIPS

### Execution Flow:
Operation (Type with ICommand/IQuery)
  ↓
Scanned by OperationScanner
  ↓
ApiOperationDescriptor created
  ↓
Host validates support (IBlueprintHost)
  ↓
Convention applies metadata (IOperationScannerConvention)
  ↓
Handler discovered by ExecutorScanner (IOperationExecutorBuilder)
  ↓
Registered in ApiDataModel
  ↓
At runtime: ApiOperationContext created
  ↓
Middleware pipeline executes (IMiddlewareBuilder stages)
  ↓
Handler executed (IApiOperationHandler<T>)
  ↓
OperationResult returned and formatted

### Nested Contexts:
Parent ApiOperationContext (outer operation)
  ↓ CreateNested()
Child ApiOperationContext (inner operation)
- Inherits: Parent ref, Data dict, ClaimsIdentity, UserContext
- Can skip auth if parent authorized
- Uses same service scope

---

## COMPILER/CODE GENERATION

- **Blueprint.Compiler** - IL generation engine
- **IVariableSource** (Blueprint.Compiler/Model/IVariableSource.cs)
  - Provides variables for generated code
- Generated pipelines implement **IOperationExecutorPipeline**
- Located in namespace: Blueprint.Generated
- Class name pattern: {OperationType}ExecutorPipeline

---

SUMMARY:
The Blueprint architecture is a CQRS-based, middleware-oriented operation execution framework that:
1. Discovers operations (ICommand/IQuery) through scanning and registration
2. Validates support via hosts (IBlueprintHost)
3. Enhances metadata via conventions (IOperationScannerConvention)
4. Finds handlers via executors (IOperationExecutorBuilder)
5. Executes through generated pipelines with middleware stages
6. Manages state via contexts (ApiOperationContext with Data dict)
7. Supports auth/validation/links/results through pluggable components
8. Enables nested operation execution with context inheritance
9. Code-generates IL for performance (Blueprint.Compiler)
10. Supports multiple hosts (HTTP, Tasks, etc.)
