# Consumer migration guide

This guide walks through migrating a legacy Blueprint application (such as a Signature365 service) to the source-generated Blueprint stack. The new stack replaces runtime middleware discovery with compile-time code generation, giving you deterministic pipelines, build-time validation, and zero-reflection execution.

## Prerequisites

- .NET 8+ SDK
- The `Blueprint.Runtime`, `Blueprint.Runtime.Model`, and `Blueprint.Validation` packages (NuGet or project references)
- The `Blueprint.SourceGenerator` analyzer reference (see [Generator wiring](#generator-wiring))
- Optional: `Blueprint.Migration.Analyzers` for automated migration diagnostics

## Migration overview

| Legacy concept | New equivalent | Migration type |
|---|---|---|
| `BlueprintApiBuilder` feature toggles | Source-generated pipeline modules | Auto-fixable / configuration-required |
| `BlueprintApiBuilder` references | `AddBlueprintApi<TApp, TExec>()` + `BlueprintServiceBuilder` | Consumer rewrite (BPMIG302) |
| `IMiddlewareBuilder` implementations | `IBlueprintSourceGeneratorModule` companion generators | Consumer rewrite (BPMIG301) |
| `AddOpenApi()` | Automatic with HTTP host | Auto-fixable (BPMIG001) |
| `AddHateoasLinks()` | Automatic when `[SelfLink]`/`[RootLink]` exist | Auto-fixable (BPMIG002) |
| `AddAuthorisation()` | Automatic when `[Resource]`/`[MustBeAuthenticated]` exist | Auto-fixable (BPMIG003) |
| `AddOpenApi(Action<OpenApiOptions>)` | `configureOpenApi:` parameter on `MapBlueprintApi` | Configuration-required (BPMIG100) |
| `AddAuthentication(...)` | ASP.NET Core `AddAuthentication()` + `UseAuthentication()` | Configuration-required (BPMIG101) |
| `[FromResource(...)]` attribute | `[Resource("noun", "verb")]` + `[FromResource(typeof(X))]` | Consumer rewrite (BPMIG200) |
| `{Auth:...}` placeholders | Source-generated auth metadata | Consumer rewrite (BPMIG201) |
| Legacy namespace usings | Updated namespaces | Auto-fixable (BPMIG300) |
| Runtime middleware pipeline | Generated `IOperationExecutorPipeline` per operation | Automatic |
| `IApiOperationHandler<T>` | Same interface, same registration | No change |
| `ICommand` / `IQuery` | Same interfaces | No change |
| `ValidationException` | `ValidationFailedResult` (no-exception fast path) | Transparent |
| `IAuditSink` | Same interface, registered via `BlueprintServiceBuilder` | No change |
| `SetTenantMiddleware` | `IPropertyPopulator` via `AddPropertyPopulator<T>()` | Consumer rewrite |
| `StartRebusTransactionScope` | `IMessageOutbox` via `AddMessageOutbox<T>()` | Consumer rewrite |
| `SaveMartenChanges` middleware | `IUnitOfWork` via `AddUnitOfWork<T>()` | Registration change |
| `IUnitOfWork` middleware | Built-in `UnitOfWorkModule` via `AddUnitOfWork<T>()` | No change |
| `IBackgroundTaskScheduler` | Same interface via `AddBackgroundTaskScheduler<T>()` | No change |
| `IUserAuthorisationContext` | Same interface via `AddUserAuthorisationContextFactory<T>()` | No change |
| `IApiAuthoriser` | Same interface via `AddApiAuthoriser<T>()`, or focused `IOperationAuthoriser` / `ILinkVisibilityAuthoriser` | Recommended split |
| `StateBasedLinkChecker<T,R>` | Now implements `ILinkVisibilityAuthoriser` via `AddLinkVisibilityAuthoriser<T>()` | Registration change |
| `Blueprint.Tasks.Hangfire` | Consumer-provided `IBackgroundTaskScheduler` implementation | Consumer rewrite |
| `IAuditor` / `AuditItem` | `IResourceEventAuditSink` | Consumer rewrite |

## Step-by-step migration

### 1. Add the migration analyzers

Reference `Blueprint.Migration.Analyzers` in your solution to get build-time diagnostics for legacy patterns:

```xml
<PackageReference Include="Blueprint.Migration.Analyzers" Version="*" />
```

Or run the codemod CLI for a full report:

```bash
dotnet run --project src/Blueprint.Codemod -- --solution YourApp.sln --mode review
```

This produces a JSON report classifying every finding as auto-fixable, configuration-required, consumer-extension rewrite, or manual-design-review.

### 2. Create the app root

Replace your legacy `BlueprintApiBuilder` setup with a `partial` class extending `BlueprintApp`:

```csharp
// Before (legacy)
services.AddBlueprintApi(api =>
{
    api.ScanForOperations(typeof(MyOperation).Assembly);
    api.AddOpenApi();
    api.AddHateoasLinks();
    api.AddAuthentication();
    api.AddAuthorisation();
});

// After (source-generated)
[BlueprintAppScan(FromAssemblyOf = typeof(MyOperation))]
public partial class MyBlueprintApp : BlueprintApp { }
```

The `partial` keyword is required — the source generator emits the `Register()` method, operation descriptors, and executor code into the other part of this class.

### 3. Generator wiring

Reference the source generator as an analyzer in your app project:

```xml
<ProjectReference Include="..\Blueprint.SourceGenerator\Blueprint.SourceGenerator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

Or as a NuGet package:

```xml
<PackageReference Include="Blueprint.SourceGenerator" Version="*"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

### 4. Register services

```csharp
// In Startup.cs or Program.cs
services.AddBlueprintApi<MyBlueprintApp, MyBlueprintAppExecutor>(blueprint =>
{
    // Register infrastructure services
    blueprint.AddAuditSink<YourAuditSink>();
    blueprint.AddResourceEventAuditSink<YourResourceEventAuditSink>();
    blueprint.AddResourceAuthorizationProvider<YourAuthProvider>();
    blueprint.AddBackgroundTaskScheduler<YourTaskScheduler>();

    // HTTP options
    blueprint.ConfigureHttp(http =>
    {
        http.ExposeExceptionDetailsInErrorResponses = env.IsDevelopment();
    });
});
```

`MyBlueprintAppExecutor` is generated automatically from the app root.

### 5. Map HTTP endpoints

```csharp
app.UseRouting();

// Authentication middleware MUST run before Blueprint endpoints
app.UseAuthentication();
app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapBlueprintApi<MyBlueprintApp, MyBlueprintAppExecutor>(
        "api/",
        configureOpenApi: info =>
        {
            info.Title = "My API";
            info.Version = "1.0.0";
        });
});
```

### 6. Migrate authentication

**Legacy:** Authentication was configured through `BlueprintApiBuilder.AddAuthentication(...)` with Blueprint-specific scheme wiring.

**New:** Authentication uses standard ASP.NET Core middleware. Blueprint operations declare their auth requirements declaratively:

```csharp
// Identity-only check (must be logged in, no scope check)
[MustBeAuthenticated]
[HttpMethod(HttpMethod.Get, "/api/profile")]
public partial class GetProfileQuery : IQuery<OkResult<Profile>> { }

// Scope-based authorization
[Resource("template", "read")]
[HttpMethod(HttpMethod.Get, "/api/templates/{Id}")]
public partial class GetTemplateQuery : IQuery<Results<OkResult<Template>, NotFoundResult>>
{
    [FromRoute]
    public string Id { get; set; }
}

// Inherit scopes from another operation
[FromResource(typeof(GetTemplateQuery))]
[HttpMethod(HttpMethod.Put, "/api/templates/{Id}")]
public partial class UpdateTemplateCommand : ICommand<OkResult<Template>>
{
    [FromRoute]
    public string Id { get; set; }
}

// Public access (no authentication required)
[AllowAnonymous]
[HttpMethod(HttpMethod.Get, "/api/health")]
public partial class HealthCheckQuery : IQuery<OkResult<HealthStatus>> { }
```

Register your `IResourceAuthorizationProvider` to implement scope checking:

```csharp
blueprint.AddResourceAuthorizationProvider<YourResourceAuthorizationProvider>();
```

#### User context factory

In legacy Blueprint, the user context was built during the authentication middleware. In source-generated Blueprint, register an `IUserAuthorisationContextFactory` that converts ASP.NET Core's `ClaimsIdentity` into your application-specific `IUserAuthorisationContext`:

```csharp
public sealed class AppUserContextFactory : IUserAuthorisationContextFactory
{
    public Task<IUserAuthorisationContext?> CreateContextAsync(
        ClaimsIdentity identity,
        CancellationToken cancellationToken)
    {
        var userId = identity.FindFirst("sub")?.Value;
        if (userId is null) return Task.FromResult<IUserAuthorisationContext?>(null);

        var accountId = identity.FindFirst("account_id")?.Value ?? "default";
        return Task.FromResult<IUserAuthorisationContext?>(
            new AppUserContext(userId, accountId, identity));
    }
}

// Register:
blueprint.AddUserAuthorisationContextFactory<AppUserContextFactory>();
```

The user context is then available via `operationContext.UserAuthorisationContext` in authorisers and handlers.

#### Business-level authorisers

For authorisers that only check **execution** (not link visibility), implement `IOperationAuthoriser`:

```csharp
public sealed class TenantIsolationAuthoriser : IOperationAuthoriser
{
    // Only check operations with a TenantId property
    public bool AppliesTo(ApiOperationDescriptor descriptor)
        => descriptor.OperationType.GetProperty("TenantId") is not null;

    public Task<ExecutionAllowed> CanExecuteOperationAsync(
        ApiOperationContext ctx, ApiOperationDescriptor descriptor, object operation)
    {
        var tenantId = operation.GetType().GetProperty("TenantId")?.GetValue(operation)?.ToString();
        var userAccountId = ctx.UserAuthorisationContext?.AccountId;

        if (tenantId is not null && !string.Equals(userAccountId, tenantId, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(ExecutionAllowed.No("Access denied", "tenant-isolation", ExecutionAllowedFailureType.Authorisation));

        return ExecutionAllowed.YesTask;
    }
}

// Register:
blueprint.AddOperationAuthoriser<TenantIsolationAuthoriser>();
```

For authorisers that need **both** execution and link visibility, implement `IApiAuthoriser` (which extends both `IOperationAuthoriser` and `ILinkVisibilityAuthoriser`):

```csharp
blueprint.AddApiAuthoriser<FullAuthoriser>();
```

#### State-based link visibility

Use `StateBasedLinkChecker<TOperation, TResource>` (which implements `ILinkVisibilityAuthoriser`) for state-based link hiding:

```csharp
public sealed class ArchiveLinkChecker : StateBasedLinkChecker<ArchiveTemplateCommand, Template>
{
    protected override bool IsLinkAvailableForOperation(ApiOperationContext context, Template resource)
        => !resource.IsArchived;
}

// Register as ILinkVisibilityAuthoriser:
blueprint.AddLinkVisibilityAuthoriser<ArchiveLinkChecker>();
```

> **Migration note:** In legacy Blueprint, `StateBasedLinkChecker` implemented `IApiAuthoriser` and was registered via `AddApiAuthoriser<T>()`. In the new stack it implements `ILinkVisibilityAuthoriser` and registers via `AddLinkVisibilityAuthoriser<T>()`. The `CanExecuteOperationAsync` stub is no longer needed. If you used Scrutor scanning for `IApiAuthoriser`, split the scan into `IApiAuthoriser`, `IOperationAuthoriser`, and `ILinkVisibilityAuthoriser` as appropriate.

### 7. Migrate validation

Validation works the same way — use `System.ComponentModel.DataAnnotations` attributes on operation properties. The generator creates compile-time fast paths for supported attributes:

```csharp
public partial class CreateTemplateCommand : ICommand<ResourceEventResult<Template>>
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; }

    [Required]
    [EmailAddress]
    public string OwnerEmail { get; set; }

    [Range(1, 100)]
    public int Priority { get; set; }
}
```

Blueprint-specific validators are also available:

| Attribute | Purpose |
|---|---|
| `GreaterThanAttribute` | Value must be greater than a constant |
| `LessThanAttribute` | Value must be less than a constant |
| `GreaterThanOrEqualToAttribute` | Value must be greater than or equal to a constant |
| `LessThanOrEqualToAttribute` | Value must be less than or equal to a constant |
| `GreaterThanPropertyAttribute` | Value must be greater than another property |
| `LessThanPropertyAttribute` | Value must be less than another property |
| `MustBeGreaterThanOrEqualAttribute` | Cross-property >= comparison |
| `RequiredIfAttribute` | Conditionally required when another property equals a value |
| `RequiredIfNotAttribute` | Conditionally required when another property does not equal a value |
| `NotAllowedIfAttribute` | Forbidden when another property equals a value |
| `MustBeTrueAttribute` | Boolean must be true (consent/terms validation) |
| `NotEmptyListAttribute` | Collection must not be empty |
| `MaxItemsListAttribute` | Collection must not exceed max items |
| `InFutureAttribute` | DateTime must be in the future |
| `InPastAttribute` | DateTime must be in the past |
| `NotInFutureAttribute` | DateTime must not be in the future (inclusive of now) |
| `NotInPastAttribute` | DateTime must not be in the past (inclusive of now) |
| `PhoneNumberAttribute` | Phone number format validation |
| `UkPostcodeAttribute` | UK postcode format validation |
| `PasswordAttribute` | Strong password complexity requirements |
| `ResourceKeyAttribute` | type/GUID resource key format |

Invalid property references in cross-property validators are caught at build time (BPV001–BPV007).

### 8. Migrate result types

Handler return types now use semantic result types instead of raw objects:

```csharp
// Before (legacy)
public class GetTemplateHandler : IApiOperationHandler<GetTemplateQuery>
{
    public async Task<object> Handle(GetTemplateQuery operation, ApiOperationContext context)
    {
        var template = await _store.FindAsync(operation.Id);
        if (template == null) throw new NotFoundException();
        return template;
    }
}

// After (source-generated)
public class GetTemplateHandler : IApiOperationHandler<GetTemplateQuery>
{
    public async Task<Results<OkResult<Template>, NotFoundResult>> Handle(
        GetTemplateQuery operation,
        ApiOperationContext context)
    {
        var template = await _store.FindAsync(operation.Id);
        if (template == null) return new NotFoundResult();
        return new OkResult<Template>(template);
    }
}
```

Available result types:

| Result type | HTTP status | Usage |
|---|---|---|
| `OkResult<T>` | 200 | Successful response with body |
| `CreatedResult<T>` | 201 | Resource created (includes Location header from self-link) |
| `AcceptedResult<T>` | 202 | Async operation accepted |
| `NoResultOperationResult` | 204 | Void command success |
| `NotFoundResult` / `NotFoundResult<T>` | 404 | Resource not found |
| `ConflictResult` / `ConflictResult<T>` | 409 | Conflict |
| `ValidationFailedResult` | 422 | Validation failure (auto-generated) |
| `PlainTextResult` | 200 | Plain text response |
| `FileStreamResult` | 200 | Binary file download |
| `RedirectResult` | 301/302 | HTTP redirect |
| `ResourceEventResult<T>` | 200/201/204 | Domain event with audit trail |
| `KeyOnlyResourceEventResult` | 200/204 | Domain event without body |
| `StatusCodeResult.*` | Various | Bodyless status code responses |
| `NotModifiedResult<T>` | 304 | Conditional request matched (via `ETagger`) |

#### Response header extensions

`OkResult` supports fluent response header configuration via extension methods:

```csharp
using System.Net.Http.Headers;

// Cache-Control header (replaces legacy WithCacheControl)
return new OkResult<Template>(template)
    .WithCacheControl(new CacheControlHeaderValue { Private = true, MaxAge = TimeSpan.FromHours(1) });

// ETag header
return new OkResult<Template>(template)
    .WithETag("\"abc123\"");

// Arbitrary custom headers
return new OkResult<Template>(template)
    .WithHeader("X-Custom", "value");

// Fluent chaining
return new OkResult<Template>(template)
    .WithCacheControl(cacheControl)
    .WithETag(eTag)
    .WithHeader("X-Request-Id", requestId);
```

#### ETag / conditional requests

For full conditional request support (If-None-Match → 304), inject `ETagger` into your handler:

```csharp
public class GetTemplateHandler(TemplateStore store, ETagger etagger)
{
    public OkResult<Template> Handle(GetTemplateQuery query)
    {
        var template = store.GetById(query.Id);
        var eTag = $"\"{template.Version}\"";

        // Returns OkResult<T> with ETag + Cache-Control on cache miss,
        // or NotModifiedResult<T> (304) on cache hit — skipping factory call
        return etagger.WithETag(eTag, () => template);
    }
}
```

`ETagger` is auto-registered when you call `AddBlueprintApi`. No additional setup is needed.

### 9. Migrate links

Replace legacy HATEOAS configuration with declarative link attributes:

```csharp
// Self-link — the canonical URL for this resource
[SelfLink(typeof(GetTemplateQuery), "/api/templates/{Id}")]
public partial class GetTemplateQuery : IQuery<OkResult<Template>>
{
    [FromRoute]
    public string Id { get; set; }
}

// Root-link — collection endpoint
[RootLink(typeof(ListTemplatesQuery), "/api/templates")]
public partial class ListTemplatesQuery : IQuery<OkResult<TemplateList>> { }
```

Link generation is automatic when link metadata exists — no `AddHateoasLinks()` call needed.

### 10. Migrate auditing

Audit sinks use the same `IAuditSink` interface. Register them through the builder:

```csharp
blueprint.AddAuditSink<YourAuditSink>();
blueprint.AddResourceEventAuditSink<YourResourceEventAuditSink>();
```

Opt-out attributes work the same:

```csharp
[DoNotAudit]                         // Skip audit recording
[DoNotRecordPerformanceMetrics]      // Skip Activity/trace creation
public partial class HealthCheckQuery : IQuery<OkResult<string>> { }
```

Mark sensitive properties for audit redaction:

```csharp
public partial class LoginCommand : ICommand<OkResult<Token>>
{
    public string Username { get; set; }

    [Sensitive]
    public string Password { get; set; }
}
```

### 11. Serializer context (AOT readiness)

For AOT-compatible serialization, declare a `JsonSerializerContext` and link it to your app:

```csharp
[BlueprintAppSerializerContext(JsonSerializerContextType = typeof(AppJsonContext))]
[BlueprintAppScan(FromAssemblyOf = typeof(MyOperation))]
public partial class MyBlueprintApp : BlueprintApp { }

[JsonSerializable(typeof(Template))]
[JsonSerializable(typeof(TemplateList))]
// ... all response body types
public partial class AppJsonContext : JsonSerializerContext { }
```

The generator validates coverage at build time — missing types produce BP006/BP007 diagnostics.

### 12. Background tasks

Background task operations implement `ICommand` and are discovered via `[BlueprintAppScan]` like any other operation. The generated pipeline automatically flushes scheduled tasks after successful handler execution.

#### Task scheduling

Register a task schedule provider and optionally a context provider:

```csharp
// Provider that actually enqueues tasks (e.g. Hangfire, custom queue)
blueprint.AddBackgroundTaskScheduleProvider<HangfireTaskProvider>();

// Context provider that captures ambient state (e.g. tenant) for later restoration
blueprint.AddBackgroundTaskContextProvider<TenantContextProvider>();

// Recurring tasks with cron schedules
blueprint.AddRecurringTaskScheduler<HangfireRecurringScheduler>();
```

#### Scheduling from handlers

Inject `IBackgroundTaskScheduler` to enqueue tasks from command handlers:

```csharp
public class CreateTemplateHandler(IBackgroundTaskScheduler scheduler)
{
    public async Task<ResourceEventResult<Template>> Handle(CreateTemplateCommand op, ApiOperationContext ctx)
    {
        var template = new Template(op.Name);

        // Schedule a background task — flushed after successful command completion
        scheduler.Enqueue(new SendWelcomeEmailTask { TemplateId = template.Id });

        return new ResourceEventResult<Template>(ResourceCreated.Instance, template);
    }
}
```

#### Background task operations

Background tasks are regular operations with a `[Queue]` attribute:

```csharp
[Queue("email")]
[DoNotAudit]
public partial class SendWelcomeEmailTask : ICommand<NoResultOperationResult>
{
    public string TemplateId { get; set; }
}
```

The generated pipeline for background tasks includes validation, unit-of-work, message outbox, and audit stages just like HTTP operations.

### 13. Persistence

Register a unit-of-work implementation to auto-save changes after successful command execution:

```csharp
blueprint.AddUnitOfWork<MartenUnitOfWork>();
```

The generated pipeline calls `SaveChangesAsync()` on `IUnitOfWork` after successful command execution. Opt out per-operation:

```csharp
[DoNotSaveChanges]
public partial class PreviewCommand : ICommand<OkResult<Preview>> { }
```

### 14. Messaging

Register a message outbox to publish messages after successful command execution:

```csharp
blueprint.AddMessageOutbox<RebusMessageOutbox>();
```

The generated pipeline calls `FlushAsync()` on `IMessageOutbox` after successful command execution (and after unit-of-work save). Opt out per-operation:

```csharp
[DoNotPublishMessages]
public partial class InternalSyncCommand : ICommand<NoResultOperationResult> { }
```

### 15. Property population

Replace manual property-setting middleware (like legacy `SetTenantMiddleware`) with `IPropertyPopulator`:

```csharp
public sealed class TenantPropertyPopulator : IPropertyPopulator
{
    public void Populate(object operation, ApiOperationContext context)
    {
        if (operation.GetType().GetProperty("TenantId") is { } prop
            && prop.GetValue(operation) is null)
        {
            var tenantId = context.UserAuthorisationContext?.AccountId;
            if (tenantId is not null)
                prop.SetValue(operation, tenantId);
        }
    }
}

// Register:
blueprint.AddPropertyPopulator<TenantPropertyPopulator>();
```

Property populators run at the Authentication stage, after user context is loaded but before validation and authorization.

## Development-time inspection

Blueprint exposes development-only endpoints for inspecting the generated API surface:

| Endpoint | Description |
|---|---|
| `/.blueprint/openapi.json` | Generated OpenAPI 3.0 document |
| `/.blueprint/manifests/operations.json` | Full operations manifest with all metadata (JSON) |
| `/.blueprint/manifests/operations.html` | Interactive diagnostics page |
| `/.blueprint/manifests/operations.md` | Markdown-formatted manifest |
| `/.blueprint/manifests/operations.schema.json` | JSON Schema for the manifest envelope |

These endpoints are only mapped when `IWebHostEnvironment.IsDevelopment()` is true.

### OpenAPI operation customisation

Use `configureOpenApiOperations` in `MapBlueprintApi` to modify rendered OpenAPI operations before the document is cached. This replaces legacy Blueprint's `OpenApiOptions.ConfigureOperation` hook.

```csharp
endpoints.MapBlueprintApi<MyApp, MyAppExecutor>(
    "api/",
    configureOpenApiOperations: ops =>
    {
        // Remove auto-populated TenantId parameters from the public spec
        ops.Processors.Add(static (operation, descriptor) =>
        {
            var filtered = operation.Parameters
                .Where(static p => !string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase))
                .ToImmutableArray();

            return filtered.Length != operation.Parameters.Length
                ? operation with { Parameters = filtered }
                : operation;
        });
    });
```

Processors run once at startup and receive both the rendered `OpenApiOperationModel` and the source `ApiOperationDescriptor`, so you can filter by descriptor metadata (e.g. only strip parameters from authenticated operations).

## Compile-time diagnostics

The source generator catches contract errors at build time. Key diagnostics:

| Range | Category | Examples |
|---|---|---|
| BP001–BP008 | Core contracts | Missing partial, missing handler, unsupported return type |
| BP101–BP104 | Validation | Invalid CompareAttribute, GreaterThanProperty, LessThanProperty references |
| BPA001 | Auditing | [Sensitive] on non-public property |
| BPM001 | Messaging | [DoNotPublishMessages] on non-command |
| BPP001 | Persistence | [DoNotSaveChanges] on non-command |
| BPT001 | Background tasks | BackgroundTask operation not a command |
| BPMOD001–004 | Module system | Ordering conflicts, duplicate contributors, missing variables |
| BPMIG001–201 | Migration | Legacy patterns requiring attention |

See `docs/compile-time-validation.md` for the full diagnostic reference.

## Running the migration tooling

### Review mode (dry run)

```bash
dotnet run --project src/Blueprint.Codemod -- --solution YourApp.sln --mode review
```

Produces a JSON report and markdown summary classifying findings by migration bucket.

### Apply mode (auto-fix safe removals)

```bash
dotnet run --project src/Blueprint.Codemod -- --solution YourApp.sln --mode apply
```

Applies auto-fixable code changes (BPMIG001–003) and reports remaining manual work.

### Output artifacts

```bash
dotnet run --project src/Blueprint.Codemod -- --solution YourApp.sln --mode review --output ./migration-report/
```

Writes `codemod-report.json` and `codemod-report.md` to the output directory.

## Namespace mapping

The source-generated Blueprint framework reorganizes namespaces for clarity and consistency. Use this reference when updating `using` directives:

| Legacy namespace | New namespace | Notes |
|---|---|---|
| `Blueprint` (root) | `Blueprint.Model` | Core types: `BlueprintApp`, `ApiOperationDescriptor`, `ICommand`, `IQuery`, `ApiResource` |
| `Blueprint` (root) | `Blueprint.Runtime.Exec` | Result types: `OkResult`, `OperationResult`, `ApiOperationContext` |
| `Blueprint.Authorisation` | `Blueprint.Authorization` | US spelling, same interfaces: `IApiAuthoriser`, `IUserAuthorisationContext` |
| `Blueprint.Http` | `Blueprint.Model.Attributes` | HTTP method attributes: `HttpMethodAttribute`, `FromRouteAttribute` etc. |
| `Blueprint.Http` | `Blueprint.Model` | Resource types: `ApiResource`, `PagedApiResource`, `ApiOperationLink` |
| `Blueprint.Validation` | `Blueprint.Validation` | Same namespace: `ValidationException` |
| `Blueprint.Auditing` | `Blueprint.Auditing` | Same namespace: `IAuditSink`, `DoNotAuditAttribute`, `SensitiveAttribute` |
| `Blueprint.Tasks` | `Blueprint.Tasks` | Same namespace: `IBackgroundTaskScheduler`, `IScheduledBackgroundTask` |
| `Blueprint.Configuration` | N/A | Replaced by `[BlueprintAppScan]` + `BlueprintServiceBuilder` |
| `Blueprint.Middleware` | N/A | Replaced by source-generated pipeline modules |
| `Blueprint.Compiler` | N/A | Replaced by source-generated code |
| `Blueprint.CodeGen` | N/A | Replaced by source-generated code |
| `Blueprint.Utilities` | `Blueprint.Utilities` | Same namespace: `TypeExtensions`, `FileSize`, `DictionaryExtensions`, `ReflectionUtilities`, `TaskExtensions`, `DateTimeExtensions`, `EnumUtilities`, `StreamExtensions`, `CiDetector`, `Inflector`, `StringExtensions`, `CommonRegularExpressions` |

### Compatibility aliases

For gradual migration, the framework provides obsolete compatibility types that map to new names:

| Legacy type | New type | Provided by |
|---|---|---|
| `ApiDataModel` | `BlueprintApp` | `Blueprint.Runtime.Model` (obsolete alias) |

These aliases emit compiler warnings pointing to the correct type. Use them to unblock compilation during incremental migration, then update to the new types.

## Verification checklist

After migration, verify:

- [ ] `dotnet build` succeeds with no Blueprint diagnostics
- [ ] All operations appear in `/.blueprint/manifests/operations`
- [ ] OpenAPI document at `/.blueprint/openapi.json` matches expectations
- [ ] Validation returns 422 with RFC 7807 ProblemDetails bodies
- [ ] Authentication returns 401/403 with ProblemDetails bodies
- [ ] Audit events fire with correct trace correlation
- [ ] Background tasks flush after successful commands
- [ ] Unit of work saves after successful commands
- [ ] Resource events emit with correct change types (201 Created, 200 Updated, 204 Deleted)
- [ ] Self-link URLs appear in response `_links` and 201 Location headers

## Consumer-space companion packages

The source-generated Blueprint framework provides proven extensibility seams for consumer-specific infrastructure. Signature365 services should implement these as companion generator packages in their own repositories:

### Marten / EF Core persistence

Blueprint generates `IUnitOfWork.SaveChangesAsync()` calls automatically. Connect your persistence provider via the builder:

```csharp
blueprint.AddUnitOfWork<MartenUnitOfWork>();
```

For advanced patterns (event sourcing, read-model projections), create a companion pipeline module:

```csharp
// In your companion generator package
public class MartenOutboxModule : IBlueprintSourceGeneratorModule
{
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        // Add transactional outbox coordination after SaveChanges
    }
}
```

The `ConsumerModuleCompositionTests` in this repository prove that companion generators can compose custom modules into the pipeline via `BlueprintSourceGenerationConfig`.

### Rebus messaging

Blueprint includes a built-in `IMessageOutbox` pipeline module that commits enqueued messages after successful command execution. This replaces legacy Blueprint's `StartRebusTransactionScopeMiddleware` / `CommitRebusTransactionScopeMiddleware` pair:

```csharp
public class RebusMessageOutbox : IMessageOutbox
{
    private readonly RebusTransactionScope _scope = new();

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        return _scope.CompleteAsync();
    }
}
```

Register it via the builder:

```csharp
blueprint.AddMessageOutbox<RebusMessageOutbox>();
```

The generated pipeline calls `CommitAsync` in the PostExecution stage (after UnitOfWork save, before auditing) only when `handlerResult.WasSuccessful` is true. Operations decorated with `[DoNotPublishMessages]` are excluded.

For Rebus handler discovery, create a companion generator that:

1. Derives from `OperationExecutorPipelineSourceGenerator`
2. Registers a `RebusHostContributor` that exposes messaging operations on the Rebus host

The dual-companion-generator integration proof (commit `1d47a1e`) demonstrates descriptor + pipeline generators composing for a consumer host with runtime partitioning.

### Hangfire background tasks

Blueprint's `IBackgroundTaskScheduler` abstraction is deliberately minimal. For Hangfire integration:

```csharp
public class HangfireBackgroundTaskScheduler : IBackgroundTaskScheduler
{
    public void Enqueue<TTask>(TTask task) where TTask : class, ICommand
    {
        BackgroundJob.Enqueue<IBlueprintTaskDispatcher>(
            dispatcher => dispatcher.DispatchAsync(task, CancellationToken.None));
    }
}
```

Register it via the builder:

```csharp
blueprint.AddBackgroundTaskScheduler<HangfireBackgroundTaskScheduler>();
```

### Tenant context

Multi-tenant resolution is a consumer concern. Typical patterns:

```csharp
// ASP.NET Core middleware approach (runs before Blueprint)
app.UseMiddleware<TenantResolutionMiddleware>();

// Or register a scoped tenant context that handlers can inject
services.AddScoped<ITenantContext>(sp =>
{
    var httpContext = sp.GetRequiredService<IHttpContextAccessor>().HttpContext!;
    return new ClaimsTenantContext(httpContext.User);
});
```

For compile-time tenant-aware route expansion, create a companion descriptor module that enriches operation URLs with `{tenantId}` placeholders derived from a `[TenantScoped]` attribute.

### Building a companion generator package

The proven recipe for companion generators:

1. Create a new class library referencing `Blueprint.SourceGenerator` internals
2. Derive from `OperationExecutorPipelineSourceGenerator` or `OperationDescriptorSourceGenerator`
3. Pass your custom module(s) via `CreateDefaultConfig(new YourModule())`
4. Reference it as an additional analyzer in consumer projects

```xml
<!-- Consumer .csproj -->
<ProjectReference Include="..\Blueprint.Rebus\Blueprint.Rebus.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

See `src/Blueprint.SourceGenerator.Tests/Modules/ConsumerModuleCompositionTests.cs` for complete integration proofs covering single-module, dual-module, and host-partitioning scenarios.

## Source-generation optimizations vs. legacy

The new Blueprint stack moves substantial runtime work to compile-time code generation.
This section documents what has been optimized and what remains runtime by design.

### What is source-generated (zero reflection at runtime)

| Area | Legacy (runtime) | New (compile-time) |
|------|------------------|--------------------|
| Pipeline construction | Middleware chain built via reflection at startup | Per-operation `IOperationExecutorPipeline` class generated at build |
| Handler discovery | Assembly scanning + `MethodInfo` resolution | Static `HandleAsync` call inlined in generated pipeline |
| Validation | `DataAnnotations` via reflection walker | Generated inline property checks |
| Resource key expansion | `Regex.Replace` + `GetProperty().GetValue()` per request | String interpolation with direct property access (`operation.TenantId`) |
| Claims template tokens | `Regex` parsing at every authorization check | `$"Tenant/{operation.TenantId}"` in generated code |
| Self-link URLs | Runtime URL construction via `UriTemplate` | Inline `$"/resources/{resource.Id}"` in generated pipeline |
| Compiled URL builders | Not available | Lambda `static (obj) => { ... }` stored on `ApiOperationLink` |
| OpenAPI document | NSwag runtime reflection | Compile-time schema generation from operation types |
| Operation descriptors | Runtime `ApiOperationDescriptor` assembly scan | Generated `static Configure()` methods per operation |
| Resource event IDs | `typeof(T).Name + ".created"` string at runtime | `const string EventId` in generated event records |
| Property ownership | Runtime `SetProperty` via reflection | Generated inline `operation.Property = value` in pipeline |
| Resource expansion discovery | Runtime property-walker via reflection | Compile-time `Expandable<T>` property discovery |

### What remains runtime (by design)

| Area | Reason |
|------|--------|
| Authorization checks (`CanShowLinkAsync`) | Depend on current user's claims and context |
| `IApiAuthoriser` / `IOperationAuthoriser` | Business-rule authorizers are DI-resolved by design |
| `ResourceKeyTemplateExpander` | Resource key expansion depends on runtime data and the current principal/context |
| HATEOAS link population (non-self) | Auth-gated link visibility requires runtime user context |
| `IUnitOfWork.SaveChangesAsync` | Database persistence is inherently runtime |
| `IMessageOutbox.CommitAsync` | Message bus interaction is runtime |
| `IAuditSink.RecordAsync` | Audit recording targets runtime sinks |
| `IResourceExpander.ExpandAsync` | Lazy-loaded data depends on runtime queries |

### Diagnostic coverage (compile-time validation)

The source-generator emits diagnostics that catch configuration errors at build time
rather than runtime:

| ID | What it catches |
|----|-----------------|
| BP001–BP004 | Invalid operation class structure (not partial, missing handler, etc.) |
| BPLG001–004 | Link format errors, missing URL placeholders, duplicate URLs |
| BPAU003–009 | Authorization conflicts (`[Resource]` + `[AllowAnonymous]`, missing template properties) |
| BPRE001–006 | Resource event family shape errors |
| BPOW001–002 | Property ownership route placeholder mismatches |
| BPMIG001–200 | Migration diagnostics for legacy patterns |

## Full migration example: Signature365-style service

This shows the complete before/after for a typical Signature365 service startup.

### Before (legacy Blueprint)

```csharp
// Startup.cs — Legacy
using Blueprint.Configuration;
using NSwag;

public class Startup
{
    public static void AddBlueprintApi(IServiceCollection services)
    {
        services.AddBlueprintApi(b => b
            .ConfigureCommon(AppDefinition.Instance)
            .Http(h => h.Json(j => { /* NodaTime etc */ }))
            .Executors(e => e.AddScanner<OperationsHandlerScanner>())
            .Operations(o => o
                .ScanCallingAssembly()
                .Scan(typeof(ITemplatesApi).Assembly))
            .AddOpenApi(o => { o.PostConfigure = d => { d.Info = new OpenApiInfo { Title = "Templates API" }; }; })
            .AddAuthentication(a => a.UseContextLoader<Sig365UserContextFactory>())
            .AddAuthorisation()
            .AddResourceEvents<BusResourceEventAuditRepository>());
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseRouting();
        app.UseAuthentication();
        app.UseEndpoints(endpoints => endpoints.MapBlueprintApi("api/"));
    }
}
```

### After (source-generated Blueprint)

```csharp
// App root (in contract project)
[BlueprintAppScan(FromAssemblyOf = typeof(GetTemplatesQuery))]
[BlueprintAppSerializerContext(JsonSerializerContextType = typeof(TemplatesJsonContext))]
public partial class TemplatesBlueprintApp : BlueprintApp { }

// Startup.cs — Source-generated
public class Startup
{
    public void ConfigureServices(IServiceCollection services, IWebHostEnvironment env)
    {
        services.AddAuthentication("Bearer").AddJwtBearer(/* ... */);

        services.AddBlueprintApi<TemplatesBlueprintApp, TemplatesBlueprintAppExecutor>(blueprint =>
        {
            blueprint.AddUserAuthorisationContextFactory<Sig365UserContextFactory>();
            blueprint.AddResourceEventAuditSink<BusResourceEventAuditSink>();
            blueprint.AddUnitOfWork<MartenUnitOfWork>();
            blueprint.AddMessageOutbox<RebusMessageOutbox>();
            blueprint.AddPropertyPopulator<TenantPropertyPopulator>();
            blueprint.AddApiAuthoriser<TenantIsolationAuthoriser>();

            blueprint.ConfigureHttp(http =>
            {
                http.ExposeExceptionDetailsInErrorResponses = env.IsDevelopment();
            });
        });
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapBlueprintApi<TemplatesBlueprintApp, TemplatesBlueprintAppExecutor>(
                "api/",
                configureOpenApi: info => { info.Title = "Templates API"; info.Version = "1.0.0"; });
        });
    }
}
```

### Key differences

| Concern | Legacy | Source-generated |
|---------|--------|-----------------|
| Operation scanning | `.Operations(o => o.Scan(...))` | `[BlueprintAppScan(FromAssemblyOf = ...)]` on app class |
| Handler discovery | `.Executors(e => e.AddScanner<...>())` | Automatic (built-in) |
| OpenAPI | `.AddOpenApi(o => {...})` | `configureOpenApi:` parameter on `MapBlueprintApi` |
| Authentication | `.AddAuthentication(a => ...)` | ASP.NET Core `AddAuthentication()` + `AddUserAuthorisationContextFactory<T>()` |
| Authorization | `.AddAuthorisation()` | Automatic from `[Resource]`/`[MustBeAuthenticated]` attributes |
| Middleware pipeline | Runtime middleware chain | Generated `IOperationExecutorPipeline` per operation |
| Serialization | Newtonsoft.Json | System.Text.Json with source-generated context |
| Persistence | Blueprint middleware | `AddUnitOfWork<T>()` seam |
| Messaging | Blueprint Rebus middleware | `AddMessageOutbox<T>()` seam |
| Endpoint mapping | `endpoints.MapBlueprintApi("api/")` | `endpoints.MapBlueprintApi<TApp, TExecutor>("api/")` |

## Convenience result types

### ResourceCreated / ResourceUpdated / ResourceDeleted

The `ResourceCreated<T>`, `ResourceUpdated<T>`, and `ResourceDeleted<T>` convenience types are
now **deprecated** in favor of the source-generated `[ResourceEventFamily]` pattern (see
[Migrating from ResourceCreated/Updated/Deleted](#migrating-from-resourcecreatedupdateddeleted-convenience-types)
below). They still work but will produce `[Obsolete]` warnings.

## Consumer integration seams

Blueprint provides clean interfaces for third-party infrastructure integration. These replace
the legacy middleware-builder pattern with DI-resolved contracts that the generated pipeline
calls at the correct stage.

### Hangfire (background task scheduling)

| Legacy | New |
|--------|-----|
| `HangfireBackgroundTaskScheduleProvider` | Implement `IBackgroundTaskScheduleProvider` |
| `HangfireRecurringTaskProvider` | Implement `IRecurringTaskScheduler` |
| `BlueprintTasksBuilderExtensions.UseHangfire()` | `blueprint.AddBackgroundTaskScheduleProvider<T>()` |

```csharp
// Register in startup:
blueprint.AddBackgroundTaskScheduleProvider<HangfireTaskProvider>();
blueprint.AddRecurringTaskScheduler<HangfireRecurringTasks>();

// The provider receives BackgroundTaskEnvelope with W3C traceparent headers
// for distributed trace correlation across async boundaries.
```

### Rebus (transactional outbox)

| Legacy | New |
|--------|-----|
| `StartRebusTransactionScopeMiddleware` | Implement `IMessageOutbox` |
| `CommitRebusTransactionScopeMiddleware` | (automatic — generated pipeline calls `CommitAsync`) |

```csharp
// Register in startup:
blueprint.AddMessageOutbox<RebusMessageOutbox>();

// The generated pipeline calls CommitAsync() in PostExecution stage
// only on handler success, after unit-of-work save. If handler fails,
// the outbox is never committed and messages are discarded.
```

### Marten (document store persistence)

| Legacy | New |
|--------|-----|
| `SaveMartenChangesMiddleware` | Implement `IUnitOfWork` |
| `UseMartenAsOutboxMiddleware` | Combine with `IMessageOutbox` above |

```csharp
// Register in startup:
blueprint.AddUnitOfWork<MartenUnitOfWork>();

// The generated pipeline calls SaveChangesAsync() after successful
// command execution, before background task flush. Skipped for queries,
// validation failures, and [DoNotSaveChanges] operations.
```

### Pipeline execution order

The generated pipeline executes consumer seams in this order:

1. **Authentication** → identity check
2. **Property population** → `IPropertyPopulator` (e.g. set TenantId from JWT)
3. **Authorization** → `[Resource]`, `IApiAuthoriser`
4. **Validation** → data annotation + `IValidator` checks
5. **Handler execution** → `HandleAsync()`
6. **Unit of work** → `IUnitOfWork.SaveChangesAsync()` (commands only, on success)
7. **Message outbox** → `IMessageOutbox.CommitAsync()` (on success)
8. **Background task flush** → `IBackgroundTaskScheduleProvider.EnqueueAsync()`
9. **Resource expansion** → `IResourceExpander` (for `?expand=` parameters)
10. **Link generation** → HATEOAS link population

### Resource expansion (`?expand=`)

Legacy Blueprint uses `ResourceExpandMiddleware` with runtime property-walker reflection.
New Blueprint discovers `Expandable<T>` properties at compile time and generates typed
expansion calls per property. `IResourceExpander` remains a runtime interface.

```csharp
// Response type with expandable properties
public class EmployeeApiResource : ApiResource
{
    public override string Object => "employee";
    public Expandable<DepartmentResource> Department { get; set; } = new("dept-1");

    [AlwaysExpand]
    public Expandable<ManagerResource> Manager { get; set; } = new("mgr-1");
}

// Expander implementation (same interface as legacy)
public class DepartmentExpander : IResourceExpander
{
    public bool CanExpand(Type type) => type == typeof(DepartmentResource);

    public async ValueTask ExpandAsync(
        ApiOperationContext context, object response,
        IEnumerable entities, CancellationToken ct)
    {
        foreach (var entity in entities.Cast<Expandable<DepartmentResource>>())
            entity.Value = await LoadDepartment(entity.Key);
    }
}

// Registration
blueprint.AddResourceExpander<DepartmentExpander>();
```

Supported query formats: `?expand=Department`, `?expand=Department,Manager` (comma-separated),
`?expand=Department&expand=Manager` (repeated keys), and `?expand[]=Department&expand[]=Manager`
(array syntax).

## Source-generation optimization notes

### What is already compile-time optimised

| Area | Legacy approach | Source-gen approach |
|------|----------------|---------------------|
| **Pipeline steps** | Runtime middleware chain discovery | Generated per-operation pipeline code |
| **Self-link URLs** | Runtime reflection-based URL construction | Compiled `$links.self` URL builder inlined in pipeline |
| **Resource key expansion** | `PropertyResourceKeyTemplateExpander` via regex + reflection | Generated property-access expressions in `ResourceKeyTemplateParser` |
| **Validation** | Data Annotations via reflection | Generated validation expressions per-property |
| **Handler dispatch** | Runtime method discovery | Direct `HandleAsync` call in generated code |
| **HATEOAS link URLs** | `CompiledUrlBuilder` set at app startup | Source-generated lambda delegates per link |
| **Auth metadata** | `[Resource]` attributes read at runtime | Compiled into `ApiOperationDescriptor.Configure()` |

### What remains at runtime (by design)

Some operations are inherently runtime because they depend on per-request state:

- **`CanShowLinkAsync`** checks — authorizer decisions depend on the authenticated user and resource state
- **`IResourceLinkGenerator`** loop — the set of applicable links is determined by DI registrations which can vary between tests and production
- **`IApiAuthoriser`** execution — business-logic authorization depends on runtime context
- **`IPropertyPopulator`** execution — populating operation properties from request context

The `ApiAuthoriserAggregator` already caches `AppliesTo()` results per operation type using `ConcurrentDictionary`, so the "does this authorizer apply" check only runs once per operation type lifetime.

## Authorization architecture (split interfaces)

Blueprint provides three authorization interfaces with clear separation of concerns:

| Interface | Purpose | When to use |
|-----------|---------|-------------|
| `IOperationAuthoriser` | Guards operation **execution** only | Tenant isolation, feature flags, subscription checks |
| `ILinkVisibilityAuthoriser` | Guards **link visibility** only | State-based link hiding (e.g. can't delete if already deleted) |
| `IApiAuthoriser` | Combined: both execution and link visibility | When the same authorizer needs to control both |

### StateBasedLinkChecker

Use `StateBasedLinkChecker<TOperation, TResource>` for link-visibility decisions based on
resource state. It implements `ILinkVisibilityAuthoriser` and provides a typed `IsLinkAvailableForOperation` method:

```csharp
public class DeactivateLinkChecker : StateBasedLinkChecker<DeactivateUser, UserResource>
{
    protected override bool IsLinkAvailableForOperation(
        ApiOperationContext context, UserResource resource)
    {
        return resource.IsActive; // Only show "deactivate" link on active users
    }
}

// Register in DI:
services.AddScoped<ILinkVisibilityAuthoriser, DeactivateLinkChecker>();
```

### Generated authorization pipeline

The source generator emits per-operation authorization checks that avoid reflection:

```csharp
// For [Resource("user", "update", "/Tenants/{Auth:TenantId}/Users/{UserId}")]
// the generator produces inline property access:
var expandedKey = $"/Tenants/{principal.FindFirst("TenantId")?.Value}/Users/{operation.UserId}";
var isAuthorized = await resolvedAuthProvider.IsAuthorizedAsync(
    context, principal,
    new ResolvedScopePair[] { new("user", "update", template, expandedKey) },
    cancellationToken);
```

This replaces the legacy `ResourceKeyTemplateExpander` which used `GetProperty()` + `GetValue()` reflection per token per request.

## Migrating from ResourceCreated/Updated/Deleted convenience types

The `ResourceCreated<T>`, `ResourceUpdated<T>`, and `ResourceDeleted<T>` convenience types
are now marked `[Obsolete]`. They still work but should be migrated to the source-generated
`[ResourceEventFamily]` pattern for compile-time safe event IDs.

### Before (convenience types)

```csharp
// Convention-based event ID derived from type name at runtime
return new ResourceDeleted<UserResource>(user.Id.ToString(), user);
// Event ID: "UserResource.deleted" (string, unchecked)
```

### After (source-generated events)

```csharp
// 1. Define the event family in your contract project:
[ResourceEventFamily(typeof(UserResource), ResourceObject = "user")]
public static partial class UserResourceEvents
{
    [ResourceEventReason(ChangeType.Created, "created")]
    public sealed partial record Created(UserResource Resource);

    [ResourceEventReason(ChangeType.Updated, "updated")]
    public sealed partial record Updated(UserResource Resource);

    [ResourceEventReason(ChangeType.Deleted, "archived")]
    public sealed partial record Archived([ResourceEventKey] string Key);
}

// 2. Use in handlers — implicit conversion to OperationResult:
return new UserResourceEvents.Archived(user.Id.ToString());
// Event ID: "user.archived" (const, compile-time validated)
```

### Key benefits of the source-generated pattern

- **Compile-time validated event IDs** — `const string EventId` prevents typos
- **Implicit conversion operators** — records convert directly to `OperationResult`
- **Custom event reasons** — "archived" instead of generic "deleted"
- **Key-only events** — `[ResourceEventKey]` for events without full resource body
- **Diagnostics** — BPRE001–BPRE006 catch invalid shapes at build time

### 16. Migrate custom middleware (IMiddlewareBuilder)

**Legacy:** Custom pipeline steps used IMiddlewareBuilder with the Lamar-based code-generation system:

```csharp
// Legacy: Compile-time middleware using the Lamar codegen model
public class SetTenantMiddleware : IMiddlewareBuilder
{
    public bool Matches(ApiOperationDescriptor descriptor) => true;

    public void Build(MiddlewareBuilderContext context)
    {
        context.AppendFrame(new SetTenantFrame());
    }
}

// Registered via BlueprintApiBuilder:
builder.Pipeline(p => p.AddMiddleware<SetTenantMiddleware>(MiddlewareStage.Setup));
```

**New:** For cross-cutting concerns like tenant isolation, use IPropertyPopulator:

```csharp
// Property populator runs for every operation that has a matching property
public sealed class TenantPropertyPopulator : IPropertyPopulator
{
    private readonly ITenantAccessor _tenantAccessor;

    public TenantPropertyPopulator(ITenantAccessor tenantAccessor)
    {
        _tenantAccessor = tenantAccessor;
    }

    public Task PopulateAsync(object operation, ApiOperationContext context)
    {
        if (operation is ITenantScoped scoped)
            scoped.TenantId = _tenantAccessor.CurrentTenantId;

        return Task.CompletedTask;
    }
}

// Register:
blueprint.AddPropertyPopulator<TenantPropertyPopulator>();
```

For persistence concerns (transaction scopes, outbox patterns), the built-in modules handle them:

| Legacy middleware | New equivalent | Registration |
|---|---|---|
| `SaveMartenChangesMiddleware` | `IUnitOfWork` implementation | `blueprint.AddUnitOfWork<MartenUnitOfWork>()` |
| `StartRebusTransactionScopeMiddleware` | `IMessageOutbox` implementation | `blueprint.AddMessageOutbox<RebusMessageOutbox>()` |
| `ResourceExpandMiddleware` | Built-in `ResourceExpansionModule` | `blueprint.AddResourceExpander<T>()` |

For truly custom pipeline stages (e.g., domain-specific enrichment that must run at a specific pipeline position), create a companion source-generator package. See the [companion generator composition docs](architecture.md) for the `IBlueprintSourceGeneratorModule` pattern.

### 17. Migrate Hangfire task scheduling

**Legacy:** Blueprint provided `Blueprint.Tasks.Hangfire` with Hangfire-specific adapters.

**New:** Blueprint defines the `IBackgroundTaskScheduler` interface; you provide a Hangfire implementation:

```csharp
public sealed class HangfireBackgroundTaskScheduler : IBackgroundTaskScheduler
{
    private readonly IBackgroundJobClient _jobClient;

    public HangfireBackgroundTaskScheduler(IBackgroundJobClient jobClient)
    {
        _jobClient = jobClient;
    }

    public Task<string> EnqueueAsync(BackgroundTaskEnvelope envelope)
    {
        var jobId = _jobClient.Enqueue<ITaskExecutor>(
            executor => executor.ExecuteAsync(envelope, CancellationToken.None));
        return Task.FromResult(jobId);
    }

    public Task ScheduleAsync(BackgroundTaskEnvelope envelope, DateTimeOffset scheduledAt)
    {
        _jobClient.Schedule<ITaskExecutor>(
            executor => executor.ExecuteAsync(envelope, CancellationToken.None),
            scheduledAt);
        return Task.CompletedTask;
    }

    public Task RunNowAsync()
    {
        // Flush pending tasks — Hangfire handles this via its internal queue
        return Task.CompletedTask;
    }
}

// Register:
blueprint.AddBackgroundTaskScheduler<HangfireBackgroundTaskScheduler>();
```

### 18. Namespace migration

The BPMIG300 analyzer automatically detects legacy namespace using directives and the BPMIG300 code fix replaces them:

| Legacy namespace | New namespace(s) |
|---|---|
| `Blueprint.Http` | `Blueprint.Model` + `Blueprint.Runtime.Http` |
| `Blueprint.Http.Formatters` | `Blueprint.Runtime.Http` |
| `Blueprint.Http.Infrastructure` | `Blueprint.Runtime.Http` |
| `Blueprint.Http.MessagePopulation` | `Blueprint.Runtime.Http` |
| `Blueprint.Http.Middleware` | `Blueprint.Runtime.Middleware` |
| `Blueprint.Authorisation` | `Blueprint.Authorization` |
| `Blueprint.Errors` | `Blueprint` |
| `Blueprint.ThirdParty` | `Blueprint.Utilities` |
| `Blueprint.Configuration` | `Microsoft.Extensions.DependencyInjection` |
| `Blueprint.Properties` | `Blueprint.Model` |

Namespaces that remain unchanged: `Blueprint.Tasks`, `Blueprint.Middleware`, `Blueprint.Diagnostics`, `Blueprint.Validation`, `Blueprint.Utilities`.