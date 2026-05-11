# Signature365 → Source-Generated Blueprint: Migration Playbook

This document is the step-by-step guide for migrating all 22 Signature365 services from the legacy in-tree Blueprint framework to the new source-generated Blueprint.

## Table of Contents

1. [Migration Strategy](#migration-strategy)
2. [Proof-of-Compatibility Approach](#proof-of-compatibility-approach)
3. [Pre-Migration: Framework Gaps to Close](#pre-migration-framework-gaps-to-close)
4. [Service-by-Service Migration Steps](#service-by-service-migration-steps)
5. [OpenAPI & TypeScript Client Generation](#openapi--typescript-client-generation)
6. [BlueprintHttpClient Migration](#blueprinthttpclient-migration)
7. [Testing Infrastructure](#testing-infrastructure)
8. [Rebus Integration](#rebus-integration)
9. [Hangfire Integration](#hangfire-integration)
10. [Marten Integration](#marten-integration)
11. [Performance Validation](#performance-validation)
12. [Rollback Strategy](#rollback-strategy)

---

## Migration Strategy

### Approach: Worktree Playground + Incremental Service Migration

We use a **Git worktree** of Signature365 as a migration playground. This avoids touching the main branch while giving us a real compilation target.

```powershell
# Create a migration worktree
cd D:\Projects\Signature365
git worktree add D:\Projects\Signature365-BlueprintMigration main -b blueprint-sourcegen-migration
```

### Migration Order (by dependency risk, lowest first)

### S365 Scale (measured)

| Metric | Count |
|--------|-------|
| Total projects | ~158 |
| Microservices (API hosts) | 16+ |
| Operations (ICommand/IQuery) | ~240+ |
| `[Resource(...)]` usage | 349 |
| `[FromResource(...)]` (legacy inheritance) | 158 |
| S365Nouns constants | 96 |
| Gateway projects | 5 |
| Contract assemblies | ~14 |
| Test projects | 42 |
| SMTP subsystem projects | 59 |

### Migration Order (risk-prioritized, not just size)

Migrate by **risk profile** — validate the highest-risk patterns early, then expand.
Gateways migrate AFTER the services they call are stable.

| Tier | Services | Ops | Risk Focus |
|------|----------|-----|------------|
| **Tier 0** | Isolated contract compilation test | — | Prove compile-time compatibility |
| **Tier 1 — Proof** | Analytics (2 ops), EventStore (2 ops) | 4 | Simplest services, minimal auth, Marten only |
| **Tier 2 — Auth spike** | Templates (1 op), Generation (6 ops, query-only) | 7 | Validate auth + tenant flow end-to-end |
| **Tier 3 — Persistence + messaging** | Outlook (11 ops), Import (11 ops) | 22 | Commands, Rebus events, Marten persistence |
| **Tier 4 — Gateway** | VCard (8 ops + gateway), ETA (5 ops + gateway) | 13 | First gateway migrations, BlueprintHttpClient |
| **Tier 5 — Medium** | Microsoft365 (4 ops + gateway), Partners (17 ops + gateway), Subscriptions (26 ops) | 47 | Multiple gateways, partner auth context |
| **Tier 6 — High complexity** | Billing (38 ops), IdentityServer (42 ops), AdminPortal | 80+ | Financial ops, OAuth2 provider, admin tooling |
| **Tier 7 — Highest** | Portal (70 ops) | 70 | Largest, most middleware, most Rebus subscriptions |
| **Separate track** | SMTP subsystem (59 projects) | ? | Discovery needed — may not be Blueprint-based |

### Pre-Migration Audit (per service, before touching code)

Each service MUST complete these audits before migration begins:

#### Serialization Audit (HIGH RISK)
S365 uses Newtonsoft.Json extensively. The new Blueprint requires System.Text.Json with
an app-owned `JsonSerializerContext`. Missing/incomplete coverage fails build (`BP007`/`BP006`).

1. Inventory all Newtonsoft.Json usage: converters, settings, `JToken`, `dynamic`
2. Inventory all HTTP body types (request + response) — these need `[JsonSerializable]`
3. Check NodaTime serialization (`NodaTime.Serialization.SystemTextJson` replaces `NodaTime.Serialization.JsonNet`)
4. Identify custom `JsonConverter` implementations that need STJ equivalents
5. Create `JsonSerializerContext` with all body types declared
6. Add old-vs-new JSON snapshot tests (serialize with both, diff)

Key Newtonsoft → STJ differences:
- `[JsonProperty]` → `[JsonPropertyName]`
- `NullValueHandling.Ignore` → `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`
- Custom `JsonConverter<T>` base class is different
- `JObject`/`JToken` → `JsonElement`/`JsonNode`
- `StringEnumConverter` → `JsonStringEnumConverter`

#### Auth Audit
1. Count `[Resource]` vs `[FromResource]` operations
2. Map `{Auth:...}` template patterns used
3. Identify tenant-scoped vs partner-scoped vs internal operations
4. Document claim-to-context mappings from `Signature365UserAuthorisationContext`

#### OpenAPI Baseline (HARD GATE)
1. Capture current OpenAPI JSON: `curl http://localhost:{port}/openapi -o openapi-baseline.json`
2. Capture current NSwag TypeScript client output
3. These become the diff baseline — **service does not ship until OpenAPI diff is reviewed
   and TypeScript client generates equivalent output**

#### Dependency Audit
1. List all `Blueprint.*` package references
2. List all middleware in the pipeline
3. Identify custom middleware needing migration

---

## Proof-of-Compatibility Approach

### Step 1: Copy a Contract Project and Compile

```powershell
# Copy a small contract project to a temp location
Copy-Item -Recurse D:\Projects\Signature365\src\Apps\Analytics\Symprex.Signature365.Analytics.Contract `
  D:\Projects\blueprint-source-gen\.scratch\compat-test\Analytics.Contract

# Create a minimal .csproj that references new Blueprint
# Build and check for compile errors
```

**What we're testing:**
- `ICommand<T>` / `IQuery<T>` interfaces compile
- `[RootLink]`, `[SelfLink]`, `[Link]` attributes compile
- `[Resource]`, `[FromResource]` attributes compile
- `[HttpGet]`, `[HttpPost]` etc. compile
- Return types (`OkResult<T>`, `ResourceEvent<T>`, `StatusCodeResult`, etc.) compile
- `ApiResource` base class compiles

### Step 2: Compile a Service Project

Copy the simplest service (Analytics) alongside its contract, add new Blueprint references, and compile:

```powershell
# Check: does the generated executor compile?
# Check: do handlers resolve correctly?
# Check: does Startup.cs wire up with AddBlueprintApi<T, TExec>()?
```

### Step 3: Run Existing Tests Against New Blueprint

```powershell
# Copy the test project too
# Re-target to new Blueprint
# Run: do existing unit tests pass?
```

### Step 4: OpenAPI Shape Comparison

```powershell
# Start old service, capture OpenAPI JSON
# Start new service, capture OpenAPI JSON
# Diff the two — document breaking changes for TS client
```

---

## Pre-Migration: Framework Gaps to Close

Based on S365 analysis, these gaps need closing in `blueprint-source-gen` before migration:

### Type Compatibility Matrix

| S365 Type | Source | New Blueprint Equivalent | Action |
|---|---|---|---|
| `IQuery<T>` | Legacy Blueprint | `Blueprint.Model.IQuery<T>` | Namespace change only |
| `ICommand<T>` | Legacy Blueprint | `Blueprint.Model.ICommand<T>` | Namespace change only |
| `ApiResource` | Legacy Blueprint | `Blueprint.Http.ApiResource` (Runtime.Model) | Same base class, new namespace |
| `[SelfLink]` | Legacy Blueprint | Source-generated by `LinkGenerationModule` | Same syntax, auto-generated |
| `[RootLink]` | Legacy Blueprint | Source-generated by `LinkGenerationModule` | Same syntax, auto-generated |
| `[Resource(noun, verb, key)]` | S365 Custom | `Blueprint.Model.Attributes.ResourceAttribute` | **Constructor compatible** — same (noun, verb, resourceKeyTemplate) signature |
| `PagedApiResource<T>` | Legacy Blueprint | `Blueprint.Runtime.Model.PagedApiResource<T>` | Namespace change |
| `OrderedPagedQuery` | Legacy Blueprint | `Blueprint.Runtime.Model.OrderedPagedQuery` | Namespace change |
| `ResourceEvent<T>` | Legacy Blueprint | `Blueprint.Runtime.Exec.ResourceEvent<T>` | Namespace change |
| `StatusCodeResult` | Legacy Blueprint | `Blueprint.Runtime.Exec.StatusCodeResult` | Namespace change |
| `ApiId` | S365 Custom | **Keep in S365** | No change needed |
| `TenantOrderedPagedQuery<T>` | S365 Custom | **Keep in S365** (update base class import) | Carries forward |
| `TenantPagedApiResource<T>` | S365 Custom | **Keep in S365** (update base class import) | Carries forward |
| `S365Nouns` / `S365Verbs` | S365 Custom | **Keep in S365** | No change needed |

**Key insight:** `[SelfLink]` and `[RootLink]` attributes are **source-generated into the consuming project** by the `LinkGenerationModule`. S365 operations can use them with the same syntax as today — no attribute class definition needed in S365 code.

**Key insight:** New Blueprint's `[Resource(noun, verb, resourceKeyTemplate)]` constructor is compatible with S365's usage. S365 can either (a) use the new Blueprint `ResourceAttribute` directly or (b) keep its custom one since the source generator matches by attribute name, not type identity.

### Critical (Must Fix)

| Gap | Description | Status |
|-----|-------------|--------|
| **InvokeAsync handler pattern** | S365 operations use `InvokeAsync()` instance methods with DI parameters, not separate `IApiOperationHandler<T>` classes. The source generator must support this "inline handler" pattern. | ✅ Already supported |
| **`IReturn<T>` marker interface** | Used by `BlueprintHttpClient` to associate operations with response types. | ✅ Available in `Blueprint.Runtime.Model` |
| **`{Auth:PropertyName}` resource key placeholders** | S365 uses `{Auth:AccountId}` in resource templates. Source gen must expand these from `IUserAuthorisationContext`. | ✅ Implemented (BPAU007) |
| **`ResourceEvent<T>` return type** | S365 operations return `ResourceEvent<T>` extensively. Source gen must handle this as a valid result type. | ✅ Implemented |
| **`ApiResource` base class** | S365 resources extend `ApiResource`. Must be available in new Blueprint. | ✅ Available in Runtime.Model |
| **`StatusCodeResult`** | S365 uses `StatusCodeResult` for non-content responses. | ✅ Available in `Blueprint.Runtime.Exec` |
| **`PlainTextResult`** | Used for proxied responses. | ✅ Implemented |
| **`BlueprintHttpClient`** | Inter-service HTTP calls using operation metadata. | ✅ Implemented in `Blueprint.HttpClient` package |
| **`TestApiOperationExecutor` equivalent** | In-memory test executor for unit/integration testing. | ✅ `BlueprintTestHost` in `Blueprint.Testing` package |
| **`[Permission]`/`[PermissionFor]` removal** | S365 doesn't use these. Remove from source generator. | ✅ **Done** — removed in this session |
| **Route deduplication** | Deduplicate ASP.NET endpoints by (route, operation) pair. | ✅ **Done** — implemented in this session |

### Important (Should Fix)

| Gap | Description | Status |
|-----|-------------|--------|
| **Rebus integration package** | `Blueprint.Rebus` — message subscription, outbox pattern | ✅ Implemented in `Blueprint.Rebus` |
| **Hangfire integration package** | `Blueprint.Tasks.Hangfire` — background task scheduling | ✅ Implemented in `Blueprint.Tasks.Hangfire` + `Blueprint.Hangfire` |
| **`OperationsHandlerScanner`** | S365 convention: scan `ApiOperations` base classes for handler methods | ✅ Not needed — source gen discovers `InvokeAsync` inline handlers automatically |
| **NSwag → Built-in OpenAPI** | S365 uses NSwag for OpenAPI. New Blueprint has built-in OpenAPI with pluggable security schemes. | ✅ Feature-complete, TypeScript client gen compatible |
| **`FromResourceAttribute` with path** | `[FromResource(typeof(X), "/Tenants/{Auth:AccountId}")]` — path override variant | ✅ Supported via `PropertyOwnershipModule` |
| **`Link` attribute (non-self, non-root)** | `[Link(typeof(Resource), "/path", Rel = "name")]` — arbitrary HATEOAS links | ✅ Implemented |

### Nice to Have

| Gap | Description | Status |
|-----|-------------|--------|
| **`AppDefinition` pattern** | S365 uses `AppDefinition.Instance` for service-level configuration | Consumer-side pattern, no framework change needed |
| **Convention test infrastructure** | `MustHaveResourceAttributesConventionSpecification` etc. | Consumer-side, document migration |
| **`IRegistry` modular DI** | S365 uses `IRegistry` pattern for service discovery | Consumer-side, no framework change needed |
| **Pluggable OpenAPI security schemes** | Custom auth schemes beyond Bearer JWT | ✅ Implemented — `OpenApiInfoOptions.SecuritySchemes` |
| **OpenAPI HATEOAS metadata** | `x-links` vendor extension per operation | Low priority — links are runtime, not schema-level |

---

## Service-by-Service Migration Steps

### Per-Service Checklist

For each service, follow these steps:

#### 1. Create the BlueprintApp Root

```csharp
// Before (legacy Startup.cs)
services.AddBlueprintApi(b => b
    .ConfigureCommon(AppDefinition.Instance)
    .Http(h => { /* ... */ })
    .Operations(o => o.ScanCallingAssembly())
    .Executors(e => e.AddScanner<OperationsHandlerScanner>())
);

// After (new)
// In a new file: AnalyticsBlueprintApp.cs
[BlueprintAppScan(FromAssemblyOf = typeof(FindSentEmailsQuery))]
public partial class AnalyticsBlueprintApp : BlueprintApp { }
```

#### 2. Make Operations Partial

All operation classes need the `partial` keyword:

```csharp
// Before
public class FindSentEmailsQuery : IQuery<FindSentEmailsQuery.Result> { ... }

// After
public partial class FindSentEmailsQuery : IQuery<FindSentEmailsQuery.Result> { ... }
```

**Automation:** The migration analyzer `BPMIG300` flags non-partial operations.

#### 3. Update Attribute Namespaces

```csharp
// Before
using Blueprint.Configuration;
using Blueprint.Authorisation;

// After
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Model;
```

**Automation:** The codemod CLI handles namespace migration automatically.

#### 4. Update Service Registration

```csharp
// Before
services.AddBlueprintApi(b => { ... });
app.UseBlueprintApi();

// After
services.AddBlueprintApi<AnalyticsBlueprintApp, AnalyticsBlueprintAppExecutor>(blueprint =>
{
    blueprint.AddAuditSink<S365AuditSink>();
    blueprint.AddResourceAuthorizationProvider<S365ResourceAuthorizationProvider>();
    blueprint.AddUserAuthorisationContextFactory<S365UserContextFactory>();
    blueprint.AddBackgroundTaskScheduler<HangfireTaskScheduler>();
    blueprint.AddUnitOfWork<MartenUnitOfWork>(); // if applicable
});

app.UseEndpoints(endpoints =>
{
    endpoints.MapBlueprintApi<AnalyticsBlueprintApp, AnalyticsBlueprintAppExecutor>(
        "api/",
        configureOpenApi: info =>
        {
            info.Title = "Analytics API";
            info.Version = "1.0";
        });
});
```

#### 5. Update Project References

```xml
<!-- Before -->
<ProjectReference Include="..\..\Blueprint\Blueprint\Blueprint.csproj" />
<ProjectReference Include="..\..\Blueprint\Blueprint.Http\Blueprint.Http.csproj" />
<ProjectReference Include="..\..\Blueprint\Blueprint.OpenApi\Blueprint.OpenApi.csproj" />

<!-- After -->
<ProjectReference Include="..\..\..\..\blueprint-source-gen\src\Blueprint.Runtime\Blueprint.Runtime.csproj" />
<ProjectReference Include="..\..\..\..\blueprint-source-gen\src\Blueprint.Runtime.Model\Blueprint.Runtime.Model.csproj" />
<ProjectReference Include="..\..\..\..\blueprint-source-gen\src\Blueprint.Validation\Blueprint.Validation.csproj" />
<ProjectReference Include="..\..\..\..\blueprint-source-gen\src\Blueprint.SourceGenerator\Blueprint.SourceGenerator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

#### 6. Migrate Handler Pattern

S365 uses two handler patterns:

**Pattern A: Inline `InvokeAsync`** (most common — ~600+ operations)
```csharp
// Already supported by source generator — no change needed
public partial class LoadAnalyticsQuery : IQuery<ProxyResult>
{
    public async Task<ProxyResult> InvokeAsync(
        IUserAuthorisationContext identity,
        HttpRequest request,
        CancellationToken token)
    {
        // Implementation unchanged
    }
}
```

**Pattern B: Separate handler class** (used via `OperationsHandlerScanner`)
```csharp
// Before: ApiOperations base class with convention-matched methods
public class AnalyticsOperations : ApiOperations
{
    public async Task<Result> Handle(FindSentEmailsQuery query, ...) { ... }
}

// After: Implement IApiOperationHandler<T> explicitly
public class AnalyticsOperations : IApiOperationHandler<FindSentEmailsQuery>
{
    public async ValueTask<object> Handle(
        FindSentEmailsQuery operation,
        ApiOperationContext context)
    {
        // Implementation adapted
    }
}
```

#### 7. Verify and Run Tests

```powershell
# Build the service
dotnet build

# Run unit tests
dotnet test

# Compare OpenAPI output
# Run integration tests
```

---

## Concrete Example: Analytics Service Migration

This section walks through the actual Analytics service migration using real code from S365.

### Current State

Analytics has 2 HTTP operations (both queries) + ~25 Rebus event handlers:

| Operation | Type | Route |
|---|---|---|
| `GetSentEmailQuery` | `IQuery<SentEmailApiResource>` | `GET /tenants/{TenantId}/sent-emails/{Id}` |
| `FindSentEmailsQuery` | `TenantOrderedPagedQuery<SentEmailApiResource>` | `GET /tenants/{TenantId}/sent-emails` |

**Handler pattern:** Separate handler classes extending `ApiOperations` (e.g. `GetSentEmailQueryHandler`).
**Auth:** `[Resource(S365Nouns.SentEmail, S365VerbNames.Read, "/Tenants/{TenantId}/SentEmails/{Id}")]`
**Serialization:** Already using `SystemTextJson` with NodaTime — no Newtonsoft migration needed.
**DB:** Dapper + PostgreSQL (no Marten).

### Step-by-Step Transformation

#### A. Create BlueprintApp

```csharp
// New file: AnalyticsBlueprintApp.cs
using Blueprint;
using Symprex.Signature365.Analytics.Contract.Api;

namespace Symprex.Signature365.Analytics;

[BlueprintAppScan(FromAssemblyOf = typeof(GetSentEmailQuery))]
public partial class AnalyticsBlueprintApp : BlueprintApp { }
```

#### B. Make Operations Partial

```csharp
// Before
public class GetSentEmailQuery : IQuery<SentEmailApiResource>

// After
public partial class GetSentEmailQuery : IQuery<SentEmailApiResource>
```

#### C. Move Handler Logic Into Operations (or keep separate)

**Option 1: Inline handler** (preferred for simple operations)
```csharp
public partial class GetSentEmailQuery : IQuery<SentEmailApiResource>
{
    [Required]
    public ApiId TenantId { get; set; }

    [Required]
    public string Id { get; set; } = null!;

    public async Task<SentEmailApiResource> HandleAsync(
        AnalyticsDatabase analyticsDatabase)
    {
        // Handler logic moved here from GetSentEmailQueryHandler
    }
}
```

**Option 2: Keep separate handler** (register as `IApiOperationHandler<T>`)
```csharp
public class GetSentEmailQueryHandler : IApiOperationHandler<GetSentEmailQuery>
{
    private readonly AnalyticsDatabase _db;
    public GetSentEmailQueryHandler(AnalyticsDatabase db) => _db = db;

    public async ValueTask<object> Handle(GetSentEmailQuery op, ApiOperationContext ctx)
    {
        // Existing logic, adapted signature
    }
}
```

#### D. Update Startup.cs

```csharp
// Before
services.AddBlueprintApi(b => b
    .ConfigureCommon(AppDefinition.Instance)
    .Http(h => { h.Json(j => j.SerializerOptions.ConfigureForNodaTime(...)); })
    .RebusConsumer(r => { r.Subscribe<CampaignEditedEvent>(); /* ... */ })
    .Executors(e => e.AddScanner<OperationsHandlerScanner>())
    .Operations(o => o.ScanCallingAssembly().Scan(typeof(IAnalyticsApi).Assembly))
    .AddAuthentication(a => a.UseContextLoader<Signature365UserAuthorisationContextFactory>())
    .AddAuthorisation()
    .UseRebus());

// After
services.AddBlueprintApi<AnalyticsBlueprintApp, AnalyticsBlueprintAppExecutor>(blueprint =>
{
    blueprint.AddResourceAuthorizationProvider<S365ResourceAuthorizationProvider>();
    blueprint.AddUserAuthorisationContextFactory<Signature365UserAuthorisationContextFactory>();

    blueprint.ConfigureHttp(http =>
    {
        http.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
    });
});

services.AddBlueprintRebus(rebus =>
{
    rebus.Subscribe<CampaignEditedEvent>();
    rebus.Subscribe<CampaignCreatedEvent>();
    // ... all subscriptions
});

// In Configure:
app.UseEndpoints(endpoints =>
{
    endpoints.MapBlueprintApi<AnalyticsBlueprintApp, AnalyticsBlueprintAppExecutor>(
        "api/",
        configureOpenApi: info =>
        {
            info.Title = AppDefinition.Instance.OpenApiTitle;
            info.Version = "1.0";
        });
});
```

#### E. Update .csproj References

```xml
<!-- Remove -->
<ProjectReference Include="..\..\Blueprint\Blueprint\Blueprint.csproj" />
<ProjectReference Include="..\..\Blueprint\Blueprint.Http\Blueprint.Http.csproj" />

<!-- Add -->
<ProjectReference Include="path\to\Blueprint.Runtime\Blueprint.Runtime.csproj" />
<ProjectReference Include="path\to\Blueprint.Runtime.Model\Blueprint.Runtime.Model.csproj" />
<ProjectReference Include="path\to\Blueprint.SourceGenerator\Blueprint.SourceGenerator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
<ProjectReference Include="path\to\Blueprint.Rebus\Blueprint.Rebus.csproj" />
```

#### F. Verify

1. `dotnet build` — should compile with source-gen generating executor + descriptors
2. Compare OpenAPI output against baseline
3. Run existing tests (update test infrastructure if needed)

---

## OpenAPI & TypeScript Client Generation

### Current S365 Setup
- Uses NSwag (`NSwag.AspNetCore`, `NSwag.CodeGeneration.TypeScript`)
- 23+ projects reference NSwag
- TypeScript API clients are generated from OpenAPI specs
- Custom `BlueprintLinkSchemaProcessor` for HATEOAS link schemas

### Migration Path

1. **Keep NSwag for TS generation**: New Blueprint's built-in OpenAPI replaces NSwag's *server-side* doc generation, but NSwag CLI can still consume the OpenAPI JSON to generate TypeScript clients.

2. **Verify OpenAPI shape compatibility**:
   ```powershell
   # Start old service, save spec
   curl http://localhost:5000/openapi -o old-spec.json

   # Start new service, save spec
   curl http://localhost:5000/api/.blueprint/openapi.json -o new-spec.json

   # Compare with openapi-diff or manual review
   npx openapi-diff old-spec.json new-spec.json
   ```

3. **Key differences to document**:
   - Endpoint path: `/openapi` → `/api/.blueprint/openapi.json`
   - HATEOAS link schemas: New Blueprint renders `_links` in schema definitions differently
   - `operationId` format: May differ (new uses C# type name directly)
   - Response shapes: `200 OK` vs `201 Created` — new Blueprint infers from result types
   - Security scheme: New Blueprint writes `bearerAuth` with document-level hoisting

4. **TS client generation command** (post-migration):
   ```powershell
   # Use NSwag CLI to generate from new Blueprint's OpenAPI
   nswag openapi2tsclient /input:http://localhost:5000/api/.blueprint/openapi.json /output:api-client.ts
   ```

### Action Items
- [ ] Create an OpenAPI comparison test that captures old vs new specs
- [ ] Document any breaking changes for frontend team
- [ ] Test NSwag TS generation against new OpenAPI output

---

## BlueprintHttpClient Migration

### Current Architecture
- `BlueprintHttpClient` wraps `HttpClient` for typed inter-service calls
- Operations implement `IReturn<TResponse>` marker interface
- Client reads operation metadata (`ApiDataModel`) to build HTTP requests
- Handles JSON serialization, validation errors (422), and API errors (4xx+)
- 238 lines of implementation in `Symprex.Signature365.Common.Core`

### New Architecture

**Option A: Source-Generated HTTP Client** (preferred)
The source generator can emit a typed HTTP client per `BlueprintApp`:

```csharp
// Generated: AnalyticsBlueprintAppHttpClient
public sealed class AnalyticsBlueprintAppHttpClient
{
    // Generated per-operation methods with typed input/output
    public Task<FindSentEmailsQuery.Result> CallAsync(
        FindSentEmailsQuery query,
        CallOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Generated: knows the HTTP method, URL template, and response type at compile time
        // No reflection, no ApiDataModel lookup
    }
}
```

**Option B: Runtime Client with Generated Metadata** (simpler migration)
Keep `BlueprintHttpClient` but feed it source-generated metadata instead of runtime-discovered metadata. The client reads from `BlueprintApp.Operations` instead of `ApiDataModel`.

### Action Items
- [ ] Create `Blueprint.HttpClient` project in blueprint-source-gen
- [ ] Add second sample project (`Blueprint.SourceGenerator.Sample.Client`) for integration testing
- [ ] Implement typed HTTP client that uses generated descriptors
- [ ] Test with inter-service call scenarios
- [ ] Benchmark against legacy client

---

## Testing Infrastructure

### Current S365 Testing
- `TestApiOperationExecutor`: In-memory executor for unit tests
- `ApiOperationContextSetup`: Fluent context builder
- `ResultTestingExtensions`: Result type assertions
- 42 test projects, xUnit + FluentAssertions + NSubstitute

### New Testing Infrastructure

We need to provide:

1. **`BlueprintTestExecutor<TApp, TExec>`** — In-memory executor
   ```csharp
   var executor = BlueprintTestExecutor.Create<MyApp, MyAppExecutor>(
       services => { /* register test doubles */ });

   var result = await executor.ExecuteAsync(new GetTemplateQuery { Id = "123" });
   result.Should().BeOfType<OkResult<Template>>();
   ```

2. **`BlueprintTestContext`** — Test context builder
   ```csharp
   var context = executor.CreateContext(new GetTemplateQuery { Id = "123" })
       .WithUser(claims => claims.AddClaim("sub", "user-1"))
       .WithService<ITemplateStore>(mockStore);
   ```

3. **Assertion extensions** — For result types
   ```csharp
   result.ShouldBeOk<Template>(t => t.Id.Should().Be("123"));
   result.ShouldBeNotFound();
   result.ShouldBeValidationFailed("Name", "Name is required");
   ```

### Action Items
- [ ] Create `Blueprint.Testing` project in blueprint-source-gen
- [ ] Implement `BlueprintTestExecutor` using generated pipelines
- [ ] Implement context builder with user/service configuration
- [ ] Implement result assertion extensions
- [ ] Port key S365 test patterns to validate infrastructure

---

## Rebus Integration

### Current S365 Pattern
```csharp
// Startup
.RebusConsumer(c =>
{
    c.Subscribe<TenantSignedUpEvent>();
    c.Subscribe<ImportRunStartedEvent>();
    // ... 17+ subscriptions per service
})

// BlueprintRebusConfiguration registers Rebus message types as Blueprint operations
// Messages are dispatched through the Blueprint pipeline (validation, auth, etc.)
```

### New Blueprint Integration

Create `Blueprint.Rebus` package:

```csharp
// New registration
services.AddBlueprintApi<MyApp, MyAppExecutor>(blueprint =>
{
    blueprint.AddRebus(rebus =>
    {
        rebus.Subscribe<TenantSignedUpEvent>();
        rebus.Subscribe<ImportRunStartedEvent>();
    });
});
```

**Implementation:**
- Rebus message handler that dispatches to Blueprint pipeline
- Message types registered as Blueprint operations
- Outbox pattern integration with `IMessageOutbox`
- Transaction coordination with UnitOfWork

### Action Items
- [ ] Create `Blueprint.Rebus` project
- [ ] Implement `BlueprintRebusMessageHandler`
- [ ] Implement `AddRebus()` extension on `BlueprintServiceBuilder`
- [ ] Add Rebus integration tests to sample app
- [ ] Test with Azure Service Bus transport

---

## Hangfire Integration

### Current S365 Pattern
```csharp
// BlueprintTasksBuilder.UseHangfire() registers:
// - IBackgroundTaskScheduleProvider (HangfireBackgroundTaskScheduleProvider)
// - IRecurringTaskProvider (HangfireRecurringTaskProvider)
```

### New Blueprint Integration

Create `Blueprint.Tasks.Hangfire` package:

```csharp
services.AddBlueprintApi<MyApp, MyAppExecutor>(blueprint =>
{
    blueprint.AddBackgroundTaskScheduler<HangfireBackgroundTaskScheduler>();
    blueprint.AddRecurringTaskProvider<HangfireRecurringTaskProvider>();
});
```

### Action Items
- [ ] Create `Blueprint.Tasks.Hangfire` project
- [ ] Implement `HangfireBackgroundTaskScheduler : IBackgroundTaskScheduler`
- [ ] Implement recurring task support

---

## Authorization Migration

S365 has a layered authorization model that maps cleanly to new Blueprint's split interfaces:

### Current S365 Authorization Stack

| Class | Role | New Blueprint Equivalent |
|-------|------|-------------------------|
| `ScopeApiAuthoriser` | Checks OAuth scopes + tenant access | Register as `IApiAuthoriser` (backward compatible) |
| `S365ClaimsApiAuthoriser` | Claims-based resource permission checks | Register as `IApiAuthoriser` |
| `IS365ResourceKeyExpander` (async) | Expands resource keys via DB lookups | Custom `IApiAuthoriser` implementation (see below) |
| `IS365ClaimInspector` (async) | Inspects claims with async expansion | Consumer-side interface, called from authoriser |
| `ResourcePermissionsFeatureData` | Per-operation resource key templates + scope pairs | Set via runtime `SetFeatureData<T>()` during startup |

### Migration Approach

#### 1. Keep `ScopeApiAuthoriser` / `S365ClaimsApiAuthoriser` As-Is

New Blueprint's `IApiAuthoriser : IOperationAuthoriser, ILinkVisibilityAuthoriser` is backward-compatible:

```csharp
services.AddBlueprintApi<MyApp, MyAppExecutor>(blueprint =>
{
    // These work unchanged — they implement IApiAuthoriser
    blueprint.AddOperationAuthoriser<ScopeApiAuthoriser>();
    blueprint.AddOperationAuthoriser<S365ClaimsApiAuthoriser>();
});
```

#### 2. Populate `ResourcePermissionsFeatureData` at Startup

S365's legacy Blueprint populated this during `DataModel.Build()`. In the new model, populate it after registration:

```csharp
services.AddBlueprintApi<MyApp, MyAppExecutor>(blueprint =>
{
    blueprint.ConfigureApp(app =>
    {
        // Populate resource permissions from [Resource] attributes at startup
        foreach (var descriptor in app.Operations)
        {
            var resourceAttrs = descriptor.OperationType
                .GetCustomAttributes<ResourceAttribute>(inherit: true);

            if (resourceAttrs.Length > 0)
            {
                descriptor.SetFeatureData(new ResourcePermissionsFeatureData
                {
                    ResourceKeyTemplates = resourceAttrs
                        .Select(a => a.Template)
                        .ToArray(),
                    ExpectedOneOfScopePairs = resourceAttrs
                        .Select(a => new ScopePair(a.Noun, a.Verb))
                        .ToArray(),
                    SpecialPermission = descriptor.OperationType
                        .GetCustomAttribute<AllowAnonymousAttribute>(),
                });
            }
        }
    });
});
```

#### 3. `IS365ResourceKeyExpander` (Async) — No Framework Change Needed

S365's async expander is a consumer-level concern. The async expansion runs INSIDE `S365ClaimsApiAuthoriser.CanExecuteOperationAsync()`, which is already called from the generated pipeline's authorization stage (via `IApiAuthoriser`). The pipeline awaits it — no synchronous constraint.

#### 4. Removed `[Permission]`/`[PermissionFor]` from Blueprint

S365 exclusively uses `[Resource]`-based authorization, so Blueprint no longer carries built-in
`[Permission]` or `[PermissionFor]` attribute support. The framework now keeps the reusable
`ClaimRequiredAttribute` / `ClaimsRequiredApiAuthoriser` seam for consumers that need custom
claims attributes while the built-in generator and sample flows stay focused on `[Resource]`.

`[Resource]` remains the preferred hot path:

- Generated per-operation static scope-pair arrays (zero allocation on hot path)
- Inline scope comparison where possible
- `ScopeApiAuthoriser` uses `StringBuilderPool` for scope key construction — preserve this
- The `Manage` composite verb (implies Create+Read+Update+Delete) is handled in `ScopeApiAuthoriser`
  consumer code, not in the framework

**Measured S365 authorization patterns:**
- 96 distinct resource nouns (S365Nouns)
- 4 simple verbs (Create, Read, Update, Delete) + 1 composite (Manage)
- `{Auth:AccountId}` resolves to `TenantId ?? PartnerId ?? "Symprex"`
- `{Auth:Id}` resolves to subject (sub) claim
- `{Auth:IdentityId}` resolves to Sig365IdentityId claim

---

## Marten Integration

### Current S365 Pattern

S365 uses Marten for event sourcing and document storage, integrated with Blueprint's UnitOfWork:

```csharp
// CommitMartenTransactionScopeMiddleware commits IDocumentSession after handler
```

### New Blueprint Integration

The `AddUnitOfWork<T>()` extension handles this:

```csharp
services.AddBlueprintApi<MyApp, MyAppExecutor>(blueprint =>
{
    blueprint.AddUnitOfWork<MartenUnitOfWork>();
});
```

The generated pipeline calls `IUnitOfWork.CommitAsync()` after successful handler execution, then `IUnitOfWork.RollbackAsync()` on failure — matching S365's middleware behavior without the middleware abstraction.
- [ ] Add Hangfire integration to sample app
- [ ] Test job scheduling and execution

---

## Marten Integration

### Current S365 Pattern
- Marten used for event sourcing in Portal/EventStore
- Not directly integrated with Blueprint pipeline
- Uses standard DI + `IDocumentSession`

### New Blueprint Integration
- Implement `MartenUnitOfWork : IUnitOfWork` that calls `SaveChangesAsync`
- Register via `blueprint.AddUnitOfWork<MartenUnitOfWork>()`
- Pipeline automatically commits after successful handler execution

### Action Items
- [ ] Create `Blueprint.Marten` project (or document inline pattern)
- [ ] Implement `MartenUnitOfWork : IUnitOfWork`
- [ ] Test with sample event store scenario

---

## Performance Validation

### Comparison Testing Strategy

Use **NBomber** for load testing and **BenchmarkDotNet** for micro-benchmarks.

#### NBomber Load Tests

```
tests/Blueprint.Performance.LoadTests/
├── Scenarios/
│   ├── SimpleQueryScenario.cs      # GET /health — baseline
│   ├── AuthenticatedQueryScenario.cs # GET /templates/{id} — with JWT
│   ├── CommandScenario.cs          # POST /templates — with body + validation
│   ├── ValidationFailureScenario.cs # POST with invalid body
│   └── InterServiceCallScenario.cs  # BlueprintHttpClient call
├── LegacyBaseline/                  # Same scenarios against old Blueprint
│   └── ...
└── Reports/                         # Auto-generated comparison reports
```

#### BenchmarkDotNet Micro-Benchmarks

```
tests/Blueprint.Performance.Benchmarks/
├── PipelineExecutionBenchmark.cs   # Execute pipeline directly
├── ValidationBenchmark.cs          # Validation fast-path vs runtime
├── SerializationBenchmark.cs       # JSON response serialization
├── OpenApiRenderingBenchmark.cs    # OpenAPI document generation
├── DescriptorLookupBenchmark.cs    # Operation descriptor resolution
├── SourceGeneratorBenchmark.cs     # Generator compilation time
└── Results/                        # Saved benchmark results
```

#### Profiling

```powershell
# CPU profiling with dotnet-trace
dotnet-trace collect --process-id <pid> --providers Microsoft-DotNETCore-SampleProfiler

# Allocation profiling
dotnet-trace collect --process-id <pid> --providers Microsoft-Windows-DotNETRuntime:0x1:5

# Generator compilation profiling
dotnet build -bl  # binary log
dotnet-trace collect -- dotnet build src/Blueprint.SourceGenerator.Sample/...
```

### Key Metrics to Compare

| Metric | Legacy Blueprint | New Blueprint | Target |
|--------|-----------------|---------------|--------|
| Requests/sec (simple query) | TBD | TBD | ≥ legacy |
| P99 latency (query) | TBD | TBD | ≤ legacy |
| P99 latency (command) | TBD | TBD | ≤ legacy |
| Memory per request | TBD | TBD | < legacy |
| Startup time | TBD | TBD | ≤ legacy |
| Compilation time (generator) | N/A | TBD | < 5s for 100 ops |
| Validation failure (no exception) | TBD | TBD | 10x faster (no throw) |

### Action Items
- [ ] Create `Blueprint.Performance.LoadTests` project with NBomber
- [ ] Create `Blueprint.Performance.Benchmarks` project with BenchmarkDotNet
- [ ] Run baseline benchmarks against legacy Blueprint
- [ ] Run benchmarks against new Blueprint
- [ ] Document comparison in `docs/performance/`
- [ ] Profile generator compilation with dotnet-trace
- [ ] Profile runtime pipeline execution

---

## Rollback Strategy

Since migration uses a Git worktree:
- Main S365 branch is untouched until migration is proven
- Each service migration is a separate PR
- If a service has issues, revert its PR independently
- Legacy Blueprint source stays in-tree until all services are migrated
- Both old and new Blueprint can coexist temporarily (different namespace prefixes if needed)

---

## Appendix: File Inventory

### S365 Operation Counts by Service

| Service | Queries | Commands | Total |
|---------|---------|----------|-------|
| Portal | ~80 | ~100 | ~180 |
| Billing | ~15 | ~25 | ~40 |
| Templates | ~20 | ~15 | ~35 |
| IdentityServer | ~15 | ~20 | ~35 |
| Import | ~10 | ~15 | ~25 |
| Analytics | ~8 | ~5 | ~13 |
| Microsoft365 | ~10 | ~10 | ~20 |
| VCard | ~5 | ~8 | ~13 |
| Partners | ~5 | ~8 | ~13 |
| EventStore | ~5 | ~5 | ~10 |
| ExchangeTransportAgent | ~5 | ~5 | ~10 |
| Outlook | ~5 | ~5 | ~10 |
| Subscriptions | ~5 | ~5 | ~10 |
| AdminPortal | ~10 | ~15 | ~25 |
| **Total** | **~198** | **~241** | **~648** |

### Key S365 Files to Reference

| File | Purpose |
|------|---------|
| `src/Core/Symprex.Signature365.Common.Core/Api/BlueprintHttpClient.cs` | Inter-service HTTP client |
| `src/Core/Symprex.Signature365.Common/Configuration/BlueprintApiBuilderExtensions.cs` | Common Blueprint config |
| `src/Core/Symprex.Signature365.BlueprintRebus/BlueprintRebusConfiguration.cs` | Rebus integration |
| `src/Blueprint/Blueprint.Tasks.Hangfire/BlueprintTasksBuilderExtensions.cs` | Hangfire integration |
| `src/Blueprint/Blueprint.Testing/TestApiOperationExecutor.cs` | Test executor |
| `src/Core/Symprex.Signature365.Common.Core/Authorisation/ResourceAttribute.cs` | Auth attributes |

---

## Route Deduplication

### Problem

Multiple `[Link]`/`[SelfLink]` attributes on one operation are valid for HATEOAS — one
operation can be a "self" link for multiple resource types. But each unique
(RoutingUrl, OperationType) pair should only produce **one** ASP.NET Core endpoint.

Currently, if an operation has `[SelfLink(typeof(A), "/items/{Id}")]` and
`[Link(typeof(B), "/items/{Id}", Rel = "related")]`, Blueprint would register
two ASP.NET Core endpoints for the same route+method+operation combination.

### Solution

In `EndpointRouteBuilderExtensions.MapBlueprintApi()`:
1. Deduplicate by `(routeTemplate, operationType)` before calling `endpoints.Map*()`
2. Cross-operation route collisions (different operations, same route+method) should be
   a build diagnostic, not silent dedupe
3. HATEOAS link metadata remains unchanged — all links are still available in descriptors

### Impact on S365

S365 operations frequently declare both `[SelfLink]` and `[Link]` with the same URL
but different `Rel` values. After deduplication, each unique route maps to exactly one
ASP.NET Core endpoint, while the HATEOAS `$links` dictionary still contains all
declared link relations.

---

## Multi-Tenancy Migration

### Current S365 Pattern

`SetTenantMiddleware` runs in the Authentication pipeline stage:
1. Extracts `sig365_tenant_id` claim from JWT
2. Sets `ICurrentTenantAccessor.Current = tenantId`
3. Logs tenant ID to Serilog `IDiagnosticContext`
4. Auto-populates `TenantId` property on operations (if property is `ApiId` type and writable)

### Migration Options

**Option A (recommended): ASP.NET Core middleware** — runs before Blueprint pipeline:
```csharp
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var tenantId = context.User.FindFirst("sig365_tenant_id")?.Value;
        var accessor = context.RequestServices.GetRequiredService<ICurrentTenantAccessor>();
        accessor.Current = tenantId;
    }
    await next();
});
```

**Option B: `IContextMetadataProvider`** — if Blueprint supports pre-handler context enrichment.

**Option C: Property populator** — use `IPropertyPopulator` registered via
`AddPropertyPopulator<T>()` for auto-populating `TenantId` on operations.

The tenant accessor is a scoped DI service — it survives the full request including
Marten queries, Rebus message headers, and background task context propagation.

---

## Improvement Roadmap (Blueprint source-gen)

These items should be tracked and implemented in the `blueprint-source-gen` repo
to support a smooth S365 migration.

### Immediate (blocks migration proof)
1. **Route deduplication** — One ASP.NET endpoint per unique route+operation pair.
2. **Auth spike integration test** — Prove tenant/partner/internal auth flow end-to-end.

### Before Tier 3 migration
4. **Verify Blueprint.Marten outbox + Rebus transaction ordering** — Must match S365's
   pipeline: Handler → Commit Rebus Transaction → Save Marten Changes.
5. **Verify Blueprint.Testing ApiFixture** — Must support CommandAsync, QueryAsync,
   MessageAsync, BackgroundTaskAsync, tenant auth context switching.
6. **Serialization migration guide** — `docs/serialization-migration.md` covering
   Newtonsoft → STJ with NodaTime, converters, and JsonSerializerContext patterns.

### Before Tier 4 migration (gateways)
7. **Second sample project** — `Blueprint.SourceGenerator.Sample.Client` using
   `BlueprintHttpClient` to call main sample. Integration tests.
8. **OpenAPI shape comparison** — `docs/openapi-migration.md` with diff analysis,
   NSwag compatibility notes, and TypeScript generation verification.

### Ongoing
9. **NBomber load tests** — Add to Blueprint.LoadTests:
   - Simple query scenario (baseline throughput)
   - Command with validation (validation overhead)
   - Command with persistence + background task (full pipeline)
   - Auth-required operation (auth overhead)
   - Compare old Blueprint vs new Blueprint source-gen
10. **BenchmarkDotNet runtime benchmarks** — Complement compiler benchmarks:
    - Pipeline execution latency (per stage)
    - Request binding (route, query, body, header)
    - Response serialization (STJ source-gen vs reflection)
    - Link generation for resources with many links
    - OpenAPI document rendering
11. **Profiling guide** — `docs/performance-profiling.md`:
    - `dotnet-trace collect` against running pipelines
    - `dotnet-counters monitor` for live metrics
    - `dotnet-gcdump` for allocation analysis
    - Profile source generator compiler performance
    - Use AI-assisted profiling skills for best practices

### Record All Results
All benchmarks, load test results, profiling findings, and migration learnings
should be recorded in the `docs/` folder:

| Document | Content |
|----------|---------|
| `docs/s365-migration-playbook.md` | This document — step-by-step guide |
| `docs/openapi-migration.md` | ✅ OpenAPI shape comparison, NSwag compatibility |
| `docs/serialization-migration.md` | ✅ Newtonsoft → STJ guide, NodaTime, pitfalls |
| `docs/testing-migration.md` | ✅ S365 test pattern → Blueprint.Testing mapping |
| `docs/performance-profiling.md` | ✅ Profiling guide with dotnet-trace/counters/gcdump |
| `docs/integration-packages.md` | ✅ Marten/Rebus/Hangfire/HttpClient/Testing package reference |
| `docs/benchmark-results.md` | 🔲 BenchmarkDotNet pipeline/serialization results (after benchmarks run) |
| `docs/performance-results.md` | 🔲 NBomber load test results (after load tests run) |

---

## Key Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Newtonsoft → STJ breaks serialization | **HIGH** | Per-service serialization audit + JSON snapshot tests before migration |
| Auth/tenant semantics change silently | **HIGH** | Early auth spike (Tier 2), 401/403 behavior tests, claim expansion tests |
| OpenAPI shape change breaks TS clients | **HIGH** | Hard gate: OpenAPI diff + NSwag generation per service before shipping |
| Multi-tenancy context loss | **HIGH** | ASP.NET Core middleware approach, integration tests with tenant context |
| Marten outbox + Rebus ordering change | **MEDIUM** | Integration tests: handler → save → outbox commit → bus publish |
| Performance regression | **MEDIUM** | NBomber baselines before/after, BenchmarkDotNet per-stage |
| Gateway calls break | **MEDIUM** | Migrate gateways AFTER target services are stable (Tier 4+) |
| SMTP subsystem entangled | **LOW** | Separate discovery track — may not need migration |
| Config/resource collision in worktree | **LOW** | Isolated infra: ports, DBs, ASB topics, Hangfire schemas, appsettings |
