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

All operations belong to the `Templates` group (via `[ApiOperations("Templates")]`):

| Operation | HTTP | Route | Result |
|-----------|------|-------|--------|
| `GetTemplatesQuery` | GET | `/templates` | `TemplateApiResource[]` |
| `GetTemplateQuery` | GET | `/templates/{Id}` | `Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>` |
| `SearchTemplatesQuery` | GET | `/templates/search` | `TemplateApiResource[]` |
| `GetTemplateVersionsQuery` | GET | `/templates/{Id}/versions` | `TemplateVersionApiResource[]` |
| `CreateTemplateCommand` | POST | `/templates` | `ResourceEventResult<TemplateApiResource>` |
| `UpdateTemplateCommand` | PUT | `/templates/{Id}` | `ResourceEventResult<TemplateApiResource>` |
| `PublishTemplateCommand` | POST | `/templates/{Id}/publish` | `Results<ResourceEventResult<TemplateApiResource>, ConflictResult<TemplateApiResource>>` |
| `CloneTemplateCommand` | POST | `/templates/{Id}/clone` | `ResourceEventResult<TemplateApiResource>` |
| `PreviewTemplateCommand` | POST | `/templates/{Id}/preview` | `TemplatePreviewApiResource` |
| `ArchiveTemplateCommand` | DELETE | `/templates/{Id}` | `KeyOnlyResourceEventResult` |

A background task (`SendTemplateSharedNotificationTask`) demonstrates non-HTTP execution.

## Key patterns demonstrated

- **Cross-assembly scanning** — `[BlueprintAppScan(FromAssemblyOf = typeof(GetTemplatesQuery))]`
- **Self and root links** — `[SelfLink]` / `[RootLink]` for HATEOAS-style URL generation
- **Validation** — `[Required]`, `[MaxLength]`, `[Range]` with generated fast-path validation
- **Resource authorization** — `[Resource("template", "read/write")]` scope-pair model (implies authentication)
- **Authorization provider** — `IResourceAuthorizationProvider` with claim-based scope evaluation
- **Scope inheritance** — `[FromResource(typeof(X))]` inherits scope pairs from another operation
- **Anonymous access** — `[AllowAnonymous]` on `SearchTemplatesQuery` for public endpoints
- **Sensitive properties** — `[Sensitive]` on `CreateTemplateCommand.HtmlSource/PlainTextSource` for audit redaction hints
- **General auditing** — `IAuditSink` for cross-operation audit recording with structured activity events
- **Audit opt-out** — `[DoNotAudit]` on `SearchTemplatesQuery` to suppress noisy read auditing
- **Background task scheduling** — `IBackgroundTaskScheduler` with `Enqueue()` in handler and pipeline-managed flush
- **Persistence opt-out** — `[DoNotSaveChanges]` on `PreviewTemplateCommand` to suppress UoW commit for read-only commands
- **Performance metrics opt-out** — `[DoNotRecordPerformanceMetrics]` on `GetTemplateVersionsQuery` for trace suppression
- **Discriminated union results** — `Results<OkResult<T>, NotFoundResult<T>>` on `GetTemplateQuery` maps to 200/404 via generated pipeline
- **Typed exception escape hatch** — `NotFoundException` in `UpdateTemplateCommandHandler` maps to 404 when a template ID is unknown
- **Resource events** — `ResourceEventResult<T>` and `KeyOnlyResourceEventResult` for event-sourced commands
- **Resource event auditing** — `IResourceEventAuditSink` for post-execution audit recording
- **Operation grouping** — `[ApiOperations("Templates")]` for explicit OpenAPI tag provenance
- **Serializer context** — `[BlueprintAppSerializerContext]` with `System.Text.Json` source-generated serialization
- **Generated executor** — `SampleBlueprintAppExecutor` is entirely source-generated

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
    blueprint.AddBackgroundTaskScheduler<InProcessBackgroundTaskScheduler>();
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
