# Blueprint Sample App

A reference Blueprint application demonstrating the source-generated API stack end to end.

## Project structure

```
Blueprint.SourceGenerator.Sample.Contract/   ← Public operations, resources, attributes
Blueprint.SourceGenerator.Sample/            ← Handlers, services, host wiring, generated execution
```

The **contract** project holds the API surface that consumers depend on. The **host** project wires
handlers, registers services, and hosts the generated executor pipeline.

## Operations

Operations span the `Templates` group (via `[ApiOperations("Templates")]`) and supporting groups for authorization, account, and infrastructure:

| Operation | HTTP | Route | Result |
|-----------|------|-------|--------|
| `GetTemplatesQuery` | GET | `/templates` | `TemplateApiResource[]` |
| `GetTemplateQuery` | GET | `/templates/{Id}` | `Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>` |
| `SearchTemplatesQuery` | GET | `/templates/search` | `TemplateApiResource[]` |
| `GetPagedTemplatesQuery` | GET | `/templates/paged` | `PagedApiResource<TemplateApiResource>` |
| `GetTemplateVersionsQuery` | GET | `/templates/{Id}/versions` | `TemplateVersionApiResource[]` |
| `GetTemplateHistoryQuery` | GET | `/templates/{Id}/history` | `TemplateVersionApiResource[]` |
| `CreateTemplateCommand` | POST | `/templates` | `ResourceEventResult<TemplateApiResource>` |
| `UpdateTemplateCommand` | PUT | `/templates/{Id}` | `ResourceEventResult<TemplateApiResource>` |
| `PatchTemplateSettingsCommand` | PATCH | `/templates/{Id}/settings` | `ResourceEventResult<TemplateApiResource>` |
| `PublishTemplateCommand` | POST | `/templates/{Id}/publish` | `Results<ResourceEventResult<TemplateApiResource>, ConflictResult<TemplateApiResource>>` |
| `CloneTemplateCommand` | POST | `/templates/{Id}/clone` | `ResourceEventResult<TemplateApiResource>` |
| `PreviewTemplateCommand` | POST | `/templates/{Id}/preview` | `TemplatePreviewApiResource` |
| `ValidateTemplateCommand` | POST | `/templates/{Id}/validate` | `OkResult` |
| `ArchiveTemplateCommand` | DELETE | `/templates/{Id}` | `KeyOnlyResourceEventResult` |
| `BulkArchiveTemplatesCommand` | POST | `/templates/bulk-archive` | `StatusCodeResult.Accepted` |
| `ExportTemplatesCommand` | POST | `/templates/export` | `ExportJobApiResource` |
| `UpdateTemplateSettingsCommand` | PUT | `/tenants/{TenantId}/templates/{Id}/settings` | `OkResult` |
| `GetMyAuthManifestQuery` | GET | `/me/authManifest` | `AuthManifestApiResource` |
| `ChangePasswordCommand` | PUT | `/account/password` | `NoResultOperationResult` |
| `HealthCheckQuery` | GET | `/system/health` | `PlainTextResult` |
| `DownloadTemplateQuery` | GET | `/templates/{Id}/download` | `FileStreamResult` |
| `RedirectToTemplateDocsQuery` | GET | `/templates/{Id}/docs` | `RedirectResult` |

A background task (`SendTemplateSharedNotificationTask`) demonstrates non-HTTP execution.

## Key patterns demonstrated

- **Cross-assembly scanning** — `[BlueprintAppScan(FromAssemblyOf = typeof(GetTemplatesQuery))]`
- **Self and root links** — `[SelfLink]` / `[RootLink]` for HATEOAS-style URL generation
- **Validation** — `[Required]`, `[MaxLength]`, `[Range]`, `[RegularExpression]`, `[EmailAddress]`, `[Url]`, `[GreaterThanProperty]` with generated fast-path validation
- **Nullable validation** — `[Range]`, `[GreaterThanProperty]` on `int?`/`double?` with null-guard fast-paths
- **PATCH semantics** — `[HttpPatch]` on `PatchTemplateSettingsCommand` for partial updates
- **Resource authorization** — `[Resource("template", "read/write")]` scope-pair model (implies authentication)
- **Authorization provider** — `IResourceAuthorizationProvider` with claim-based scope evaluation
- **Scope inheritance** — `[FromResource(typeof(X))]` inherits scope pairs from another operation
- **Anonymous access** — `[AllowAnonymous]` on `SearchTemplatesQuery` for public endpoints
- **Header binding** — `[FromHeader("X-Correlation-Id")]` on `SearchTemplatesQuery` for cross-system trace correlation
- **Cookie binding** — `[FromCookie("session-id")]` on `SearchTemplatesQuery` for cookie-sourced values
- **Plain text responses** — `PlainTextResult` on `HealthCheckQuery` for text/plain health-check endpoints
- **File downloads** — `FileStreamResult` on `DownloadTemplateQuery` for binary file streaming with Content-Disposition
- **HTTP redirects** — `RedirectResult` on `RedirectToTemplateDocsQuery` for 302 redirect responses
- **Sensitive properties** — `[Sensitive]` on `CreateTemplateCommand.HtmlSource/PlainTextSource` for audit redaction hints
- **General auditing** — `IAuditSink` for cross-operation audit recording with structured activity events
- **Audit opt-out** — `[DoNotAudit]` on `SearchTemplatesQuery` to suppress noisy read auditing
- **Background task scheduling** — `IBackgroundTaskScheduler` with `Enqueue()` in handler and pipeline-managed flush
- **Background task anonymous convention** — background tasks auto-project `AuthenticationRequirement.Anonymous` in descriptors (matching legacy `BackgroundTaskAllowAnonymousConvention`)
- **Persistence opt-out** — `[DoNotSaveChanges]` on `PreviewTemplateCommand` to suppress UoW commit for read-only commands
- **Performance metrics opt-out** — `[DoNotRecordPerformanceMetrics]` on `GetTemplateVersionsQuery` for trace suppression
- **Discriminated union results** — `Results<OkResult<T>, NotFoundResult<T>>` on `GetTemplateQuery` maps to 200/404 via generated pipeline
- **Typed exception escape hatch** — `NotFoundException` in `UpdateTemplateCommandHandler` maps to 404 when a template ID is unknown
- **Resource events** — `ResourceEventResult<T>` and `KeyOnlyResourceEventResult` for event-sourced commands
- **Resource event auditing** — `IResourceEventAuditSink` for post-execution audit recording
- **Operation grouping** — `[ApiOperations("Templates")]` for explicit OpenAPI tag provenance
- **Serializer context** — `[BlueprintAppSerializerContext]` with `System.Text.Json` source-generated serialization
- **Generated executor** — `SampleBlueprintAppExecutor` is entirely source-generated
- **Paged queries** — `OrderedPagedQuery<T>` on `GetPagedTemplatesQuery` with offset/limit/sort
- **Compare validation** — `[Compare(nameof(NewPassword))]` on `ChangePasswordCommand` for cross-property equality checks
- **Length validation** — `[Length(8, 128)]` on `ChangePasswordCommand.NewPassword` for min/max string length
- **Bulk operations** — `BulkArchiveTemplatesCommand` with `[MinLength(1)]` array validation
- **Status code results** — `StatusCodeResult.Accepted` for fire-and-forget HTTP 202 patterns
- **Auth manifest** — `GetMyAuthManifestQuery` with `IApiAuthoriserAggregator` for SPA permission manifests
- **Tenant-scoped routes** — `/tenants/{TenantId}/templates/{Id}/settings` with multi-segment route binding
- **Claims authorization** — `[Permission]` and `[PermissionFor]` with claims-based access control
- **Context metadata** — `IContextMetadataProvider` for enriching resource events with ambient context

## Consumer seam demonstrations

The sample app demonstrates how consumer apps wire companion middleware patterns without requiring
those libraries as core Blueprint dependencies:

| Seam | Sample Implementation | Production Equivalent |
|------|----------------------|----------------------|
| **Persistence** | `SampleMartenUnitOfWork` | Marten `IDocumentSession.SaveChangesAsync()` |
| **Messaging** | `SampleRebusMessageOutbox` (`IMessageOutbox`) | Rebus `RebusTransactionScope` with outbox |
| **Multi-tenancy** | `AsyncLocalTenantAccessor` | Signature365's `ICurrentTenantAccessor` |
| **Property population** | `TenantPropertyPopulator` | Signature365's `SetTenantMiddleware` |
| **Task scheduling** | `SampleHangfireTaskProvider` (`IBackgroundTaskScheduleProvider`) | Hangfire `IBackgroundJobClient.Create()` |
| **Task execution** | `TaskExecutor` (built-in) | Hangfire `HangfireTaskExecutor` entry point |
| **Task context** | `SampleTenantContextProvider` (`IBackgroundTaskContextProvider`) | Restore tenant/user context in background jobs |
| **Trace propagation** | `TraceContextPreprocessor` (built-in) | W3C traceparent/tracestate injection |
| **Queue routing** | `[Queue("name")]` attribute | Hangfire named queues |
| **Auditing** | `LoggingAuditSink` | Database or event-store audit sink |
| **Resource events** | `LoggingResourceEventAuditSink` | Event-bus or audit-store sink |
| **Authorization** | `SampleResourceAuthorizationProvider` | Claims/scope-based ownership checks |
| **User context** | `SampleUserAuthorisationContextFactory` | JWT→`IUserAuthorisationContext` mapping |
| **Business auth** | `TenantIsolationApiAuthoriser` | Tenant isolation, role-based access |
| **Link visibility** | `ArchiveTemplateLinkChecker` | State-based link hiding (e.g. hide archive on archived) |
| **Claims auth** | `AddClaimsAuthorisation()` | Permission/PermissionFor claims evaluation |
| **Recurring tasks** | `SampleRecurringTaskScheduler` | Hangfire `RecurringJob.AddOrUpdate` |
| **Context metadata** | `SampleContextMetadataProvider` | Enrich resource events with tenant/correlation context |

These seams prove that the generated pipeline correctly calls consumer-registered services at the
right pipeline stages, enabling companion packages to integrate without core framework changes.

## Running

```bash
dotnet run --project src/Blueprint.SourceGenerator.Sample
```

The app starts on `http://localhost:5000` by default. All endpoints live under `/api/`.

## Inspecting generated code

Uncomment the `EmitCompilerGeneratedFiles` settings in the `.csproj` to write generated sources to disk:

```xml
<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
<CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)\GeneratedFiles</CompilerGeneratedFilesOutputPath>
```

## Integration tests

The sample app is exercised by:

- `ReferenceAppHttpEndToEndIntegrationTests` — proves HTTP execution through the generated pipeline
- `ReferenceAppOpenApiTests` — locks the full OpenAPI document shape as a reviewed snapshot
- `ReferenceAppManifestTests` — locks the debug manifest surface
- `ReferenceAppInspectionTests` — verifies registered operations, URLs, methods, links, authorization, sensitive properties, and serializer coverage

## Service registration

The recommended startup pattern uses `AddBlueprintApi<TApp, TExec>()`:

```csharp
services.AddBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>(blueprint =>
{
    blueprint.AddAuditSink<SampleAuditSink>();
    blueprint.AddResourceEventAuditSink<SampleResourceEventAuditSink>();
    blueprint.AddResourceAuthorizationProvider<SampleResourceAuthorizationProvider>();
    blueprint.AddBackgroundTaskScheduleProvider<SampleHangfireTaskProvider>();
    blueprint.AddUnitOfWork<SampleMartenUnitOfWork>();
    blueprint.AddMessageOutbox<SampleRebusMessageOutbox>();
    blueprint.AddPropertyPopulator<TenantPropertyPopulator>();
    blueprint.ConfigureHttp(options => options.PublicHost = "https://api.example.com");
});
```

## OpenTelemetry integration

Blueprint emits all activity spans on `Blueprint.Diagnostics.BlueprintActivitySource.ActivitySourceName`.
Register it with your OpenTelemetry tracing pipeline:

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(Blueprint.Diagnostics.BlueprintActivitySource.ActivitySourceName)
        .AddAspNetCoreInstrumentation());
```

## ASP.NET Core infrastructure

Blueprint delegates standard ASP.NET Core concerns to the consumer:

- **CORS** — `services.AddCors()` + `app.UseCors()` before routing
- **Health checks** — `app.MapHealthChecks("/health")` alongside `MapBlueprintApi`
- **Rate limiting** — `services.AddRateLimiter()` + `app.UseRateLimiter()`
- **Authentication** — `services.AddAuthentication()` + `app.UseAuthentication()` before `UseAuthorization()`
- **JSON naming** — Control via `[JsonSourceGenerationOptions]` on your `JsonSerializerContext`

## OpenAPI customisation

Blueprint's OpenAPI document is pre-rendered at startup. Use hooks on `MapBlueprintApi` to customise
the document:

```csharp
endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>(
    "api/",
    configureOpenApi: info => { info.Title = "My API"; info.Version = "1.0.0"; },
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
