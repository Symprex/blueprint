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
- **Scope inheritance** — `[FromResource(typeof(X))]` inherits scope pairs from another operation
- **Anonymous access** — `[AllowAnonymous]` on `SearchTemplatesQuery` for public endpoints
- **Sensitive properties** — `[Sensitive]` on `CreateTemplateCommand.HtmlSource/PlainTextSource` for audit redaction hints
- **Audit opt-out** — `[DoNotAudit]` on `SearchTemplatesQuery` to suppress noisy read auditing
- **Performance metrics opt-out** — `[DoNotRecordPerformanceMetrics]` on `GetTemplateVersionsQuery` for trace suppression
- **Discriminated union results** — `Results<OkResult<T>, NotFoundResult<T>>` on `GetTemplateQuery` maps to 200/404 via generated pipeline
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
- `ReferenceAppProgrammaticInspectionTests` — verifies registered operations, URLs, methods, links, and serializer coverage
