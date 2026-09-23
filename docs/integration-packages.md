# Integration Packages Reference

This document covers Blueprint's integration packages for Marten, Rebus, Hangfire, HTTP client, and testing infrastructure. It serves as the reference for migrating Signature 365 services from their bespoke middleware to Blueprint's standardised abstractions.

---

## Blueprint.Marten

**NuGet:** `Blueprint.Marten` | **Source:** `src/Blueprint.Marten`

### What It Provides

| Abstraction | Class | Purpose |
|---|---|---|
| `IUnitOfWork` | `MartenUnitOfWork` | Wraps `IDocumentSession.SaveChangesAsync` for generated pipeline integration |
| `IDomainEvent` | marker interface | Tags domain events raised by aggregates |
| `IHaveDomainEvents` | interface | Aggregate root exposes `IReadOnlyList<IDomainEvent> DomainEvents` |
| `IDomainEventHandler<T>` | interface | In-process handler run before commit |
| `IDomainEventPublisher` | interface | Publishes events to message bus (Rebus) after commit |
| `DomainEventDispatchingSessionListener` | Marten listener | During `SaveChangesAsync`, dispatches each domain event to its in-process handlers, publishes it via `IDomainEventPublisher` when one is registered, then clears it |
| `OutboxPublishingSessionListener` | Marten listener | Publish-only alternative, for events that should reach the bus but no in-process handler; not registered by `AddBlueprintMarten()` |
| `MartenScopeAccessor` | `AsyncLocal<IDocumentSession>` | Ambient scoping for singleton listeners |

### Registration

```csharp
services.AddBlueprintApi<MyApp, MyAppExecutor>(blueprint =>
{
    blueprint.AddUnitOfWork<MartenUnitOfWork>();
});

// Marten configuration (separate from Blueprint)
services.AddMarten(options =>
{
    options.Connection(connectionString);
    options.AddBlueprintSessionListeners();
});
```

`AddBlueprintMarten()` adds the same listener through `ConfigureMarten`, which only a store configured by
`AddMarten()` applies. A consumer that builds `StoreOptions` itself (registering them as a singleton, or
subclassing them) must call `options.AddBlueprintSessionListeners()` on them. It is idempotent, so doing both
does not dispatch or publish an event twice.

### S365 Migration Mapping

| S365 Pattern | Blueprint Equivalent |
|---|---|
| `SaveMartenChangesMiddleware` | `UnitOfWorkModule` → `IUnitOfWork.SaveChangesAsync()` |
| `StartRebusTransactionMiddleware` | `MessageOutboxModule` → `IMessageOutbox.CommitAsync()` |
| `UseMartenAsOutboxMiddleware` | `OutboxPublishingSessionListener` on Marten session |
| Marten `IDocumentSession` injection | Same — injected via DI, scoped per request |

### Multi-Tenancy Gap

Blueprint.Marten does **not** provide built-in multi-tenant document session isolation. S365 services that need per-tenant Marten stores should configure Marten's built-in tenancy (`options.Policies.ForAllDocumentTypes(x => x.TenancyStyle = TenancyStyle.Conjoined)`) and set the tenant ID on the session before the pipeline runs.

**Recommended approach:** Use ASP.NET Core middleware to extract the tenant claim and set it on a scoped `ICurrentTenantAccessor`, then configure Marten to read from that accessor.

---

## Blueprint.Rebus

**NuGet:** `Blueprint.Rebus` | **Source:** `src/Blueprint.Rebus`

### What It Provides

| Abstraction | Class | Purpose |
|---|---|---|
| `IContainerAdapter` | `BlueprintApiActivator` | Routes Rebus messages through Blueprint pipeline |
| Subscription registry | `BlueprintRebusConfiguration` | Declares `Subscribe<T>()` for startup |
| Feature tag | `RebusMessageFeature` | Marks operations as Rebus-sourced in descriptors |
| Opt-out | `[NoRebusTransaction]` | Skips transaction scope for specific handlers |

### Registration

```csharp
services.AddBlueprintRebus(rebus =>
{
    rebus.Subscribe<TemplateCreatedEvent>();
    rebus.Subscribe<UserDeletedEvent>();
});

// Rebus configuration (separate from Blueprint)
services.AddRebus((configure, sp) =>
{
    return configure
        .Transport(t => t.UseAzureServiceBus(connectionString, "queue-name"))
        .Options(o => o.UseBlueprintApiActivator(sp));  // Routes to Blueprint pipeline
});
```

### S365 Migration Mapping

| S365 Pattern | Blueprint Equivalent |
|---|---|
| `BlueprintApiActivator` | Same class — `BlueprintApiActivator` |
| `config.RebusConsumer(c => c.Subscribe<T>())` | `services.AddBlueprintRebus(r => r.Subscribe<T>())` |
| `StartRebusTransaction` middleware | `RebusTransactionScope` (automatic unless `[NoRebusTransaction]`) |
| `IHandleMessages<T>` operation discovery | `BlueprintApiActivator.GetHandlers<T>()` caches and routes |
| Per-message DI scope | Fresh scope created per message automatically |

### Transaction Scope

Rebus messages are automatically wrapped in a `RebusTransactionScope`. When combined with Marten outbox, the flow is:

1. Message received → new DI scope
2. `RebusTransactionScope` started (unless `[NoRebusTransaction]`)
3. Pipeline executes: validate → auth → handler → save (UoW) → commit outbox
4. Transaction committed atomically

### Multi-Tenancy Gap

Blueprint.Rebus does **not** extract tenant context from message headers. S365 services that propagate tenant ID via Rebus headers should implement a custom `IBackgroundTaskContextProvider` or extract headers in a pipeline contribution.

---

## Blueprint.Tasks.Hangfire

**NuGet:** `Blueprint.Tasks.Hangfire` | **Source:** `src/Blueprint.Tasks.Hangfire`

### What It Provides

| Abstraction | Class | Purpose |
|---|---|---|
| `IBackgroundTaskScheduleProvider` | `HangfireBackgroundTaskScheduleProvider` | Enqueue/schedule/continuation |
| `IRecurringTaskProvider` | `HangfireRecurringTaskProvider` | Cron-based recurring tasks |
| Job entry point | `HangfireTaskExecutor` | Executes tasks through `TaskExecutor` pipeline |
| DI activation | `ServiceProviderJobActivator` | Per-job DI scopes |
| Retry config | `TaskAutomaticRetryJobFilter` | Per-task retry attributes |
| Dashboard | `HangfireBackgroundTaskWrapper` | Friendly display names |

### Registration

```csharp
services.AddBlueprintApi<MyApp, MyAppExecutor>(blueprint =>
{
    blueprint.AddBackgroundTaskScheduler<HangfireBackgroundTaskScheduleProvider>();
});

// Hangfire configuration (separate from Blueprint)
services.AddHangfire(config => config
    .UseSqlServerStorage(connectionString)
    .UseServiceProviderActivator());

services.AddHangfireServer();
```

### S365 Migration Mapping

| S365 Pattern | Blueprint Equivalent |
|---|---|
| `BackgroundJob.Enqueue(...)` | Pipeline auto-flushes via `IBackgroundTaskScheduleProvider` |
| Recurring jobs | `IRecurringTaskProvider.AddOrUpdateAsync(taskType, cron)` |
| Per-task retry | `[AutomaticRetry(N)]` attribute on task type |
| Queue routing | `[Queue("name")]` attribute on task type |

### Trace Context

Blueprint.Tasks.Hangfire propagates `traceparent`/`tracestate` headers into background execution, enabling distributed tracing across HTTP → background task boundaries.

### Multi-Tenancy Gap

Background tasks do **not** automatically restore tenant context. Services should implement `IBackgroundTaskContextProvider` to extract tenant ID from task headers and restore the ambient tenant.

---

## Blueprint.HttpClient

**NuGet:** `Blueprint.HttpClient` | **Source:** `src/Blueprint.HttpClient`

### What It Provides

| Abstraction | Class | Purpose |
|---|---|---|
| Typed HTTP client | `BlueprintHttpClient<TApp>` | Calls Blueprint APIs using operation types |
| Legacy compatibility base | `BlueprintHttpClient` | Lets old S365-generated partial clients compile against the non-generic client shape |
| DI registration | `AddBlueprintHttpClient<TApp>()` | Registers typed `HttpClient` |
| Fault handling | `BlueprintApiException` | Non-success response exception |

### Registration

```csharp
// In the gateway service
services.AddBlueprintHttpClient<TargetServiceApp>(client =>
{
    client.BaseAddress = new Uri("https://target-service.internal/api/");
});
```

### Usage

```csharp
public class MyHandler
{
    private readonly BlueprintHttpClient<TargetServiceApp> _client;

    public async Task<TemplateResource> HandleAsync(GetTemplateQuery query, CancellationToken ct)
    {
        return await _client.SendAsync<TemplateResource>(query, ct);
    }
}
```

### How It Works

1. Resolves `ApiOperationDescriptor` for the operation type
2. Extracts HTTP method (Query→GET, Command→POST) and URL pattern
3. Substitutes `{PropertyName}` tokens in URL from operation properties
4. For GET/DELETE: remaining properties → query string
5. For POST/PUT/PATCH: operation serialized as JSON body
6. Response: 2xx → deserialize, 422 → `ValidationException`, other → `BlueprintApiException`

### S365 Gateway Migration

S365 gateways wrap inter-service calls. With Blueprint.HttpClient:

```csharp
// Before (S365 gateway)
public class GetTemplateHandler : IQueryHandler<GetTemplateQuery, TemplateResource>
{
    private readonly ITemplateServiceClient _client;
    public async Task<TemplateResource> HandleAsync(GetTemplateQuery q, CancellationToken ct)
        => await _client.GetTemplateAsync(q.Id, ct);
}

// After (Blueprint gateway)
public class GetTemplateHandler : IQueryHandler<GetTemplateQuery, TemplateResource>
{
    private readonly BlueprintHttpClient<TemplateServiceApp> _client;
    public async Task<TemplateResource> HandleAsync(GetTemplateQuery q, CancellationToken ct)
        => await _client.SendAsync<TemplateResource>(q, ct);
}
```

### Gaps for S365

- **No Authorization header injection** — Bearer token forwarding must be configured via `HttpClient` delegating handlers
- **No distributed tracing** — Add `HttpClientHandler` for W3C trace context propagation
- **No retry/circuit breaker** — Use Polly via `IHttpClientBuilder.AddPolicyHandler()`
- **No tenant header** — Add delegating handler for `X-Tenant-Id` header

**Recommended:** Configure these via standard `IHttpClientBuilder` extensions:

```csharp
services.AddBlueprintHttpClient<TargetServiceApp>(client =>
{
    client.BaseAddress = new Uri(config["Services:Template:BaseUrl"]!);
})
.AddHttpMessageHandler<BearerTokenHandler>()
.AddHttpMessageHandler<TenantIdHandler>()
.AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(200 * i)));
```

---

## Blueprint.Testing

**NuGet:** `Blueprint.Testing` | **Source:** `src/Blueprint.Testing`

### What It Provides

| Abstraction | Class | Purpose |
|---|---|---|
| In-memory executor | `BlueprintTestHost<TApp, TExec>` | Runs operations without HTTP |
| Fluent builder | `BlueprintTestHostBuilder<TApp, TExec>` | Configure test host |
| Per-test config | `BlueprintTestContext` | Auth, tenant, custom setup |
| Auth lifecycle | `TestAuthenticationManager` | Scoped identity management |
| Full HTTP stack | `BlueprintHttpTestHost<TApp, TExec>` | TestServer + routing + serialization |
| HTTP builder | `BlueprintHttpTestHostBuilder<TApp, TExec>` | Configure HTTP test host |
| Background task double | `BackgroundTaskCollector` | Captures enqueued tasks |
| Recurring task double | `NullRecurringTaskProvider` | No-op recurring tasks |
| Result assertions | `ResultAssertionExtensions` | `ShouldBeOk<T>()`, `ShouldBeValidationFailed()`, etc. |

### S365 Test Pattern Migration

```csharp
// Before (S365)
var factory = new WebApiFixtureFactory<Startup>();
var fixture = factory.CreateFixture();
fixture.AuthenticateAsTenantAdministrator("tenant-1");
var result = await fixture.CommandAsync(new CreateTemplateCommand { Name = "Test" });
Assert.IsNotNull(result);

// After (Blueprint)
var host = new BlueprintTestHostBuilder<MyApp, MyAppExecutor>()
    .ConfigureServices(s => s.AddSingleton<IUnitOfWork, InMemoryUnitOfWork>())
    .Build();

var result = await host.ExecuteAsync(
    new CreateTemplateCommand { Name = "Test" },
    context => context
        .WithTenant("tenant-1")
        .WithUser("admin", roles: ["TenantAdmin"]));

result.ShouldBeCreated<TemplateResource>();
```

### Auth Testing

```csharp
// Test with specific scope pairs
var result = await host.ExecuteAsync(query, ctx => ctx
    .WithUser("user-1")
    .WithScopePairs("Template.Read", "Template.Write")
    .WithTenant("tenant-123"));

// Test without auth (should return 401)
var result = await host.ExecuteAsync(query);
result.ShouldBeUnauthorized();

// Skip auth entirely for handler-focused tests
var result = await host.ExecuteAsync(query, ctx => ctx.SkipAuthorisation());
```

### Background Task Testing

```csharp
var host = new BlueprintTestHostBuilder<MyApp, MyAppExecutor>().Build();

var result = await host.ExecuteAsync(new ProcessTemplateCommand { Id = "1" });
result.ShouldBeOk();

// Assert background tasks were enqueued
var collector = host.Services.GetRequiredService<BackgroundTaskCollector>();
collector.EnqueuedTasks.Should().ContainSingle()
    .Which.Should().BeOfType<SendNotificationTask>();
```

### HTTP Stack Testing

```csharp
var httpHost = new BlueprintHttpTestHostBuilder<MyApp, MyAppExecutor>()
    .ConfigureAuthentication(auth => auth.AddScheme<TestAuthHandler>("Test", null))
    .Build();

var client = httpHost.CreateClient();
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
var response = await client.GetAsync("/api/templates/1");
response.StatusCode.Should().Be(HttpStatusCode.OK);
```

### S365 Pattern Mapping

| S365 Testing Pattern | Blueprint Equivalent |
|---|---|
| `WebApiFixtureFactory<T>` | `BlueprintTestHostBuilder<TApp, TExec>` |
| `ApiFixture` | `BlueprintTestHost<TApp, TExec>` |
| `fixture.CommandAsync(op)` | `host.ExecuteAsync(op)` |
| `fixture.QueryAsync(op)` | `host.ExecuteAsync(op)` |
| `fixture.AuthenticateAsTenantAdmin()` | `ctx.WithTenant("t").WithUser("u", roles: ["Admin"])` |
| `FakeBus` | `BackgroundTaskCollector` |
| `BackgroundTaskCollector` | Same name, same purpose |
| JWT token generation | `TestAuthenticationManager` + `WithUser()` |
| Marten test session | Configure via `ConfigureServices` with test `IDocumentStore` |

---

## Package Readiness Summary

| Package | Core Ready | S365 Patterns | Multi-Tenancy | Gaps |
|---|---|---|---|---|
| **Blueprint.Marten** | ✅ | ✅ UoW, outbox, events | ❌ Built-in | Tenant session isolation |
| **Blueprint.Rebus** | ✅ | ✅ Activator, subscriptions | ❌ Built-in | Tenant from headers |
| **Blueprint.Tasks.Hangfire** | ✅ | ✅ Schedule, recurring, retry | ❌ Built-in | Tenant context restore |
| **Blueprint.HttpClient** | ✅ | ✅ Typed client, URL building | ❌ Built-in | Auth/tracing headers |
| **Blueprint.Testing** | ✅ | ✅ Full test infrastructure | ✅ `WithTenant()` | Audit/event assertions |

### Cross-Cutting: Multi-Tenancy

Multi-tenancy is the primary gap across all integration packages. The recommended approach for S365:

1. **HTTP requests:** ASP.NET Core middleware extracts `sig365_tenant_id` claim → sets `ICurrentTenantAccessor`
2. **Rebus messages:** Custom `IBackgroundTaskContextProvider` restores tenant from message headers
3. **Hangfire tasks:** Custom `IBackgroundTaskContextProvider` restores tenant from job parameters
4. **HTTP client:** Delegating handler forwards `X-Tenant-Id` header to downstream services
5. **Marten:** Configure conjoined tenancy, session reads tenant from `ICurrentTenantAccessor`

This pattern keeps tenant resolution in the application layer (S365-specific) while using Blueprint's pipeline abstractions for everything else.
