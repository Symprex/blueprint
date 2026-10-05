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
| `StartRebusTransactionMiddleware` | `RebusTransactionModule` → a generated `RebusTransactionScope` (`IMessageOutbox`, committed by `MessageOutboxModule`, is a separate seam legacy did not have) |
| `UseMartenAsOutboxMiddleware` | Consumer-supplied: an `IRebusTransactionEnlistment` plus a drain listener added with `options.AddBlueprintOutboxListener(...)` (see [Transactional outbox](#transactional-outbox)) |
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
| `RebusHandleMessageOperationExecutorBuilderScanner` giving a subscribed message the handlers of its base types | `AddBlueprintRebusConsumer` also registers a scanned `IHandleMessages<TBase>` as the handler of every subscribed message type assignable to `TBase` (a derived class, or an implementer of the interface), in the scanner's order; a type that is not subscribed gets its own handlers only |
| Per-message DI scope | Fresh scope created per message automatically |
| `SaveMartenChangesMiddleware` around a subscribed message's `IHandleMessages<T>` | A plain (non-operation) handler's scoped `IUnitOfWork`, when registered, is saved when Rebus commits the message's transaction — after every handler has returned, before the messages they sent go out, and never for a delivery that failed or was moved to the error queue |
| `BackgroundTaskRunnerMiddleware` around a subscribed message's `IHandleMessages<T>` | The tasks a plain handler enqueued through its scope's `IBackgroundTaskScheduler` are released (`RunNowAsync`) in the same commit callback, straight after that save and only once it has returned, with or without a unit of work — as legacy's runner followed its save (`AccessTokenRenewalFailedEventExecutorPipeline.cs:74-82` at Signature365 `8b40ecc961`) and matched every operation |

### Transaction Scope

Rebus messages are automatically wrapped in a `RebusTransactionScope`. The flow is:

1. Message received → new DI scope
2. `RebusTransactionScope` started (unless `[NoRebusTransaction]`)
3. Pipeline executes: validate → auth → handler → save (UoW) → complete the scope
4. The Rebus transaction commits, sending the messages published during the handler and the save

The save commits before the messages are sent, so the two are not atomic: if sending fails after the save, the
data stays and the messages are not sent (a received message is redelivered; nothing retries an HTTP request).
Atomic delivery needs a transactional outbox, which Blueprint leaves to the consumer.

### Transactional outbox

Blueprint provides the two seams an outbox needs, and nothing else:

- **`IRebusTransactionEnlistment`** (`Blueprint.Rebus`), registered in DI, is called with the Rebus transaction a unit
  of work will be saved inside, before any handler runs in it: by a generated pipeline that saves a unit of work — any
  operation without `[DoNotSaveChanges]`, queries included since v1.0.3, but not a query run nested (a self query) —
  once it has resolved the tenant and authorised and validated the operation, straight before the handler (as legacy's
  `UseMartenAsOutboxMiddleware` did), and by `BlueprintApiActivator` for a plain
  `IHandleMessages<T>`, whose unit of work it saves from the transaction's first commit callback. Neither enlists when
  no `IUnitOfWork` is registered.
- **`options.AddBlueprintOutboxListener(listener)`** (`Blueprint.Marten`) adds the consumer's drain listener, which
  writes the messages sent so far in the transaction into the session during `SaveChangesAsync`, after
  `DomainEventDispatchingSessionListener`, whose published domain events it must see. Adding it to
  `options.Listeners` directly, before `AddBlueprintMarten()`'s `ConfigureMarten` callback runs, puts it first and it
  drains nothing.

The save does not close the transaction to new messages. A generated pipeline commits `IMessageOutbox`, runs the audit
sink and flushes background tasks after the save and before it completes the Rebus scope; a plain handler's background
tasks are released straight after its save, in the same commit callback; and a received message's later commit
callbacks run after both. A message sent from any of those joins the transaction after the drain has
run, so it is not in the saved session. The outbox must refuse it, or send it without the outbox; queued into the saved
session, it would never be written. Legacy had no such window, because it completed its scope before it saved, and
flushed and audited after it had completed the scope — still ambient, as its `using var` lasted to the end of the
method (`AzureAdOauthReturnCommandExecutorPipeline.cs:117`, `:133-149` at Signature365 `8b40ecc961`). A send there
joined nothing:

- if it was the scope's first, the transport's `GetOrAdd` factory registered its commit callback on the completed
  context, which Rebus 8.6 refuses (`TransactionContext.ThrowCompletedException`, wrapped in a
  `RebusApplicationException`);
- otherwise the queue already existed and had been sent, so the message was silently dropped.

Legacy's transport sent that way (`Common/Rebus/Outbox/OutboxClientTransportDecorator.cs:39-43`, and Rebus's own
`AbstractRebusTransport.Send` without an outbox connection). `RebusCommitAmbientContextTests` pins both.

That is exactly where a direct port of legacy's `UseMartenSessionAsOutbox` goes wrong: its transport queued each
message's outbox row on the session from a commit callback, which now runs after the save, so the rows were silently
lost. The mistake is now loud. Once a transaction with an enlistment has run its commit callbacks,
`RebusTransactionEnlistment` calls `IUnitOfWorkSaveGuard.ThrowIfChangedSinceSave()` on the scope's unit of work, and
`MartenUnitOfWork` throws when anything (SQL from `QueueSqlCommand`, a stored document, an event) was queued on the
session since its save. A generated pipeline then fails the operation. For a received message, the transport has
already acknowledged it, so Rebus logs the failure and does not redeliver.

`MartenUnitOfWork` makes its own scope `MartenScopeAccessor.Current` for the save, so the listeners also run for a
plain handler's save, where no generated pipeline has set it.

A limit nothing reaches today and nothing detects: a message sent from a commit callback can be silently lost. Rebus
8.6's transport sends a transaction's messages from one commit callback of its own, registered only with the
transaction's first send (`AbstractRebusTransport.Send`, inside its `GetOrAdd("outgoing-messages")` factory). So a send
made after that callback has run, on either kind of transaction, is never sent. A first send made from a commit callback
registers the callback too late for the `RebusTransactionScope` a generated pipeline opens, which runs only the callbacks
it held when it started completing, though a received message's transaction runs it. A send from a callback that runs
before the transport's gets through: one registered before the transaction's first send, or a received message's first,
where a plain handler's unit of work is saved. All of it is pinned by `RebusCommitAmbientContextTests`. An enlistment
must not send from a commit callback it registers, or must send outside the transaction.

Two Marten 6.4.2 facts the drain depends on, pinned against a real PostgreSQL by `MartenSaveListenerPostgresTests`:

- SQL a listener queues with `QueueSqlCommand` during `BeforeSaveChangesAsync` is executed in the same batch and
  transaction as the session's documents: they commit together and roll back together.
- Marten runs **no** listener for a session with nothing to save. So for an operation that sends a message but writes
  nothing, a drain would never run. `MartenUnitOfWork` closes that gap for a drain added with
  `AddBlueprintOutboxListener`: when the session has nothing to save, it queues `DO $$BEGIN END$$` first, so that the
  listeners run and what the drain writes is saved. It costs one round trip for such a save, and none where no such
  drain is added. A drain attached any other way, such as only to a session's own listeners as legacy's
  `UseMartenSessionAsOutbox` attached its listener, gets no such guarantee.
- A drain that is not an `IOutboxDrainListener` is run that way on **every** save with nothing to save, so an operation
  that writes nothing and sends nothing then needs the database: a plain handler is retried and dead-lettered while
  PostgreSQL is down, where legacy's empty save touched no database (its Rebus commit queued the outbox rows on the
  session, so the session had work exactly when a message had been sent). A drain that implements
  `IOutboxDrainListener.HasPendingMessages(session)` — the ambient Rebus transaction has messages it has not drained yet —
  is run that way only when it says so, which is legacy's behaviour. It is asked only for a session with nothing else to
  save, before the save, and must not change the session; answering `true` when unsure costs a round trip, answering
  `false` wrongly loses messages.

Marten also creates a document type's storage before it runs the listeners, so a drain that stores a document type the
session did not already hold needs that type's schema in place beforehand, as it is once a deployed app has applied
its schema. SQL queued with `QueueSqlCommand` is unaffected.

### Multi-Tenancy Gap

Blueprint.Rebus does **not** extract tenant context from message headers. S365 services that propagate tenant ID via Rebus headers should implement a custom `IBackgroundTaskContextProvider` or extract headers in a pipeline contribution.

For a plain `IHandleMessages<T>` handler, Blueprint resolves no tenant at all. `BlueprintApiActivator` calls the
`IRebusTransactionEnlistment`s, and resolves the handlers and their scoped dependencies, in a scope where no tenant has
been set, so a tenant-scoped service resolved there (Signature365's Marten session, for example) gets its untenanted
fallback. An enlistment must therefore not depend on a tenant. Legacy Blueprint ran a received message the same way:
its message pipelines set the tenant only for an authenticated identity (for example
`FieldDefinitionDeletedEventExecutorPipeline.cs:51-56` at Signature365 `8b40ecc961`), and its activator built the
operation's context with no identity (`Symprex.Signature365.BlueprintRebus/BlueprintApiActivator.cs:90-100`). So the
session that its `UseMartenAsOutboxMiddleware` and the handler shared was the untenanted `IdentitySession()`
(`Symprex.Signature365.Common/Data/Marten/MartenRegistry.cs:87-96`).

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
| Fault handling | `BlueprintApiException` | An `ApiException` carrying the failed call's problem (see below) |

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
4. For GET: remaining properties → query string
5. For every other method (POST/PUT/PATCH/DELETE): operation serialized as JSON body, as legacy Blueprint's client sent it
6. Response: 2xx → deserialize, 422 → `ValidationException`, other → `BlueprintApiException`

For a `Results<T1, T2>` response type the client reads the member the host wrote, trying them in order: a
`StatusCodeResult` (e.g. `StatusCodeResult.Accepted`) when the status is its own and there is no body, a
`PlainTextResult` when the body is not JSON, and any other non-`OperationResult` type when it is JSON. A 204 that no
member claims returns the default, as for any other response type; any other response that is none of them throws
`InvalidOperationException`. Wrappers such as `OkResult<T>` are not read back.

`BlueprintApiException` is an `ApiException` with the problem the failure describes, as legacy S365's client threw,
so a caller matches it by `Type`/`Title`/`HttpStatus` and a pipeline that lets it escape answers with the same problem:

| Failure | Title / Type / HttpStatus / Detail |
|---|---|
| Problem details response, 4xx | carried through, with `Extensions` (all but `instance`, which legacy never carried) |
| Problem details response, 5xx | carried through, but `Detail` is "The remote server returned an internal error response"; the original is the `InnerException`'s message. `Extensions` are carried without `instance` or the `stackTrace` a host exposing exception details writes, so a gateway does not pass a development upstream's trace on |
| 404 whose body is not JSON | "The remote server responded Not Found" / `not_found` / 404 / "Response from {path} was 404" (the path, never the host) |
| Other body that is not JSON | "The remote server did not produce a valid JSON response" / `invalid_json` / the response status / the parser's message |
| Server unreachable | "The remote server did not respond" / `service_unavailable` / 500 / same as title; the `HttpRequestException` (which names the host) is the `InnerException` |
| Timed out | "Timed out waiting for service response" / `service_timeout` / 408 / the cancellation's message |

`StatusCode` and `ResponseBody` still carry the raw response.

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
