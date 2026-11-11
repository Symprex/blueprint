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
| Start-up convention | `IOperationDescriptorConvention` registered by `AddBlueprintRebusConsumer` | Marks every subscribed message anonymous and unexposed as the app is made, as legacy's `RebusScannerConvention` did (since v1.0.6) |
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
| `bus.Advanced.TransportMessage.Defer`/`Forward` and an injected `IMessageContext` in a subscribed message's handler | Work in an operation's handler too (since v1.0.7): the `IBus` that `AddBlueprintRebus` registers defers and forwards the message being handled in its own transaction, past the pipeline's `RebusTransactionScope`, and `IMessageContext` is that message's context. In a plain handler whose message's transaction has an outbox enlisted in it, the copy is sent in a transaction of its own once that transaction commits, past the outbox, so deferring needs no database. `Reply` is not covered ([consumer-migration-guide.md](consumer-migration-guide.md#14-messaging)) |
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
| Typed HTTP client | `BlueprintHttpClient<TApp>` | Calls Blueprint APIs using operation types. Generated extension methods on it are emitted only for an app carrying `[BlueprintHttpClientExtensions]` |
| Concrete transport | `BlueprintHttpClient` | The non-generic transport every Blueprint HTTP call goes through: sends a call with the HTTP method and URL its caller resolved, maps failures, and hands a success response to the reader its caller chose. `BlueprintHttpClient<TApp>` and generated typed clients both call it |
| Generated typed client | `[BlueprintClient]` + `[BlueprintClientInclude]` | A hand-written `partial` class completed by the source generator into `I{Class}` plus its implementation (see [Typed Clients](#typed-clients)) |
| Upstream credentials | `IUpstreamCredential`, `BearerTokenCredential` | Authenticates every request a typed client sends |
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

Steps 3 and 4 are generated onto each routed operation as a public nested class, `Op.Http` (`BuildUrl`, and
`AppendQueryString` for a GET), unless the operation already uses the name `Http`. A class deriving from an operation
inherits `Http`, so inside it qualify an alias or namespace of that name (`global::System.Net.Http.HttpMethod`) or alias
it under another name.

For a `Results<T1, T2>` response type the client reads the member the host wrote, trying them in order: a
`StatusCodeResult` (e.g. `StatusCodeResult.Accepted`) when the status is its own and there is no body, a
`PlainTextResult` when the body is not JSON, and any other non-`OperationResult` type when it is JSON. `OkResult<T>`,
`CreatedResult<T>` and `AcceptedResult<T>` members are read back from their content at their own status (200, 201, 202),
wrapped in the result. A response that no member claims, a 204 included, throws `InvalidOperationException`: a default
union would hold no member at all.

A 204 for any other declared result is read by that result's reader, which is chosen once per result type. A result the
host writes with no body (`StatusCodeResult`, `HttpResult`, `OperationResult`, `object`, `HttpNoContentResult`,
`NoResultOperationResult`, `HttpAcceptedResult`, `KeyOnlyResourceEventResult`) is the singleton, or status-only result,
of the status; a 204 for any other result (a DTO, a `string`, an `OkResult<T>` read alone) is a contract mismatch and
throws `InvalidOperationException`, as legacy's client did, rather than returning the default. A JSON `null` for a
type that can hold one fails as a `response_empty` problem rather than returning `null`. The generated typed clients
below use these same readers, chosen at compile time.

`BlueprintApiException` is an `ApiException` with the problem the failure describes, as legacy S365's client threw,
so a caller matches it by `Type`/`Title`/`HttpStatus` and a pipeline that lets it escape answers with the same problem:

| Failure | Title / Type / HttpStatus / Detail |
|---|---|
| Problem details response, 4xx | carried through, with `Extensions` (all but `instance`, which legacy never carried) |
| Problem details response, 5xx | carried through, but `Detail` is "The remote server returned an internal error response"; the original is the `InnerException`'s message. `Extensions` are carried without `instance` or the `stackTrace` a host exposing exception details writes, so a gateway does not pass a development upstream's trace on |
| 404 whose body is not JSON | "The remote server responded Not Found" / `not_found` / 404 / "Response from {path} was 404" (the path, never the host) |
| Other body that is not JSON | "The remote server did not produce a valid JSON response" / `invalid_json` / the response status / the parser's message |
| Server unreachable, or the connection fails while the body is read | "The remote server did not respond" / `service_unavailable` / 500 / same as title; the `HttpRequestException`, or the `HttpIOException` of a body the send did not buffer (either names the host), is the `InnerException` |
| Timed out | "Timed out waiting for service response" / `service_timeout` / 408 / the cancellation's message |

`StatusCode` and `ResponseBody` still carry the raw response.

An `HttpClient` that answers at the headers (`HttpCompletionOption.ResponseHeadersRead`, or a handler that streams)
leaves the body to the reader, and `HttpClient.Timeout` does not cover that read: a body that stalls is bounded only by
the call's `CancellationToken`, so pass one with a deadline.

### Typed Clients

A typed client is a hand-written `partial` class that the source generator completes into an `I{Class}` interface (one method per included operation) and the class's implementation of it over a `BlueprintHttpClient`. It has the shape Signature 365's `ServiceApiSourceGenerator` clients have, so S365 gateways move over without changing their call sites. A call looks up no app or descriptor at run time: the HTTP method is a constant, the URL comes from the request builders generated onto the operation itself (`Op.Http`), and the response reader is chosen at compile time. The one run-time type check is on a body: an operation whose run-time type is derived from the declared one is written as that run-time type, because the declared type's metadata would drop the derived type's own members.

#### Attributes

| Attribute | Placement | Purpose |
|---|---|---|
| `[BlueprintClient]` | A `partial`, top-level, non-generic, non-static class, `public` or `internal` | Marks the class for completion. Optional `SerializerContext` property, see below |
| `[BlueprintClientInclude(typeof(Op))]` | The same class; repeatable | Adds one operation to the client. `Op` must be a concrete, non-generic class implementing `IOperation` that declares a result (`IReturn<T>`). An `[UnexposedOperation]` may be included: it is hidden from the published contract but still routed, so another service can call it |

Both attributes are `[Conditional("CODE_ANALYSIS")]`, so they leave no trace in the compiled assembly. An operation from a referenced contract assembly is included the same way as one in the same project, provided that assembly was built with Blueprint's source generator (it carries the request builders as metadata).

#### Generated signature and naming

Each include becomes exactly this method, on the interface and the class:

```csharp
Task<R> {Name}Async(Op operation, CallOptions? options = null, CancellationToken cancellationToken = default);
```

- `{Name}` is the operation's type name with one trailing `Command` or `Query` removed: `GetTemplateQuery` becomes `GetTemplateAsync`, `SignupTenantCommand` becomes `SignupTenantAsync`, and a type with neither suffix keeps its whole name (`HealthCheck` becomes `HealthCheckAsync`).
- `R` is the result the operation declares, exactly as written, not a wrapper. The HTTP method is the operation's own: its `[HttpGet]`, `[HttpPost]`… attribute, otherwise POST for an `ICommand` and GET for anything else. A GET carries the operation's other properties in the query string; every other method sends the operation as its JSON body.
- `CallOptions.ModifyRequest` is the per-call hook; it runs on the built request.
- The interface also declares `I{Class} WithCredential(IUpstreamCredential credential)` and a copy of each public method the class declares.

#### Result types

The reader for `R` is the one `BlueprintHttpClient<TApp>` chooses at run time, so the two clients read every response identically. In order of precedence: a nested `StatusCodeResult` by its singleton; `Results<T1, T2>` by its members' readers (see How It Works); the bodiless reader for `StatusCodeResult`, `HttpResult`, `OperationResult` and `object`; `PlainTextResult`; a resource event; `ResourceEventResult<T>`; `OkResult<T>`, `CreatedResult<T>` and `AcceptedResult<T>` by their content; and JSON for anything else, `string` included. An include with no reader (for instance a `StatusCodeResult` subclass with no public static `Instance`) is reported as BPCL008 and left out.

#### Credentials

`IUpstreamCredential` is a context-free strategy: `ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)` is given the request alone, never the operation or the caller being served. `BearerTokenCredential(string token)` sets `Authorization: Bearer {token}`. `WithCredential` returns a new client that applies the credential to every request; it replaces any credential already set, and the original client is unchanged, so a shared client can be given a per-call credential cheaply:

```csharp
var result = await api
    .WithCredential(new BearerTokenCredential(jwt))
    .GetTemplateAsync(new GetTemplateQuery { Id = id }, cancellationToken: ct);
```

The credential is applied last, so it wins over an `Authorization` header the caller or a modifier set.

#### Extension points

The generated half leaves four hooks to the hand-written half. The worked examples are `RegionalOperationsApi` and `SampleContractApi` in `src/Blueprint.HttpClient.Tests`.

1. **Public methods are copied onto the interface.** Every public, non-generic instance method the class declares is repeated on `I{Class}`, so it is callable through the interface. A generic one is BPCL006; one named like a generated operation method or `WithCredential` is BPCL009.
2. **Readonly fields become constructor parameters.** A `readonly` instance field with no initialiser, whatever its accessibility, is taken by the generated public constructor, after `client`, as a parameter named after the field without its underscore and in camel case (`_endpoints` becomes `endpoints`), and is carried by every clone. A null is refused for a non-nullable reference type. A field named `_client`, `_credential` or `_requestModifier`, or one whose parameter would be `client`, `source`, `credential`, `requestModifier` or `modify`, is BPCL009.
3. **`partial void OnConfigure(Op operation, ref CallOptions? options)`** is declared once per included operation. Implement it to read the operation and shape the options for that call; assigning `options` replaces them. A call of an operation whose hook is not implemented is removed by the compiler. A replacement should chain the options' existing `ModifyRequest`, or the caller's modifier is lost.
4. **`protected I{Class} WithRequestModifier(Action<HttpRequestMessage> modify)`** returns a clone that also runs `modify` on every request. It is `private` when the class is `sealed`. It is not on the interface; expose it by writing a public method that calls it. Modifiers accumulate in the order added.

```csharp
[BlueprintClient]
[BlueprintClientInclude(typeof(FindTemplateCopiesQuery))]
[BlueprintClientInclude(typeof(SignupTenantCommand))]
internal partial class RegionalOperationsApi
{
    private readonly RegionEndpoints _endpoints;   // constructor parameter: endpoints

    // Copied onto IRegionalOperationsApi; built on the protected WithRequestModifier.
    public IRegionalOperationsApi ForRegion(string region)
    {
        var regionalBase = this._endpoints.Regions[region];

        return this.WithRequestModifier(message =>
        {
            var uri = message.RequestUri!;

            // A request is relative to the base address until a modifier resolves it, so one an earlier ForRegion ran on is
            // already absolute, and an absolute URI ignores the base it is resolved against: swap its host instead.
            message.RequestUri = uri.IsAbsoluteUri
                ? new UriBuilder(uri) { Scheme = regionalBase.Scheme, Host = regionalBase.Host, Port = regionalBase.Port }.Uri
                : new Uri(regionalBase, uri);
        });
    }

    // Per-operation hook: reads the operation, chains the caller's modifier.
    partial void OnConfigure(SignupTenantCommand operation, ref CallOptions? options)
    {
        var caller = options?.ModifyRequest;
        var key = operation.IdempotencyKey;

        options = new CallOptions
        {
            ModifyRequest = request =>
            {
                caller?.Invoke(request);
                request.Headers.Add("Idempotency-Key", key);
            },
        };
    }
}
```

**Request URIs are relative when the first modifier runs, but not necessarily the next.** The URL the operation builds is relative to the `HttpClient`'s base address (a leading `/` is stripped), and `HttpClient` only makes it absolute on send. A modifier that rewrites the host, as `ForRegion` does, must combine a relative `RequestUri` against a base itself (`new Uri(regionalBase, message.RequestUri!)`); but a modifier that runs after another that did so is given an absolute one, which ignores the base it is combined with, so it must check `IsAbsoluteUri` and change the host of the URI it was given, as `ForRegion` does, or `ForRegion("eu").ForRegion("us")` would still send to the EU host.

#### Request-shaping order

For each call, in this order:

1. `OnConfigure` runs and may replace the call's options.
2. The request is built: the HTTP method, the relative URL (with a GET's query string) and, for any other method, the JSON body.
3. The call's `CallOptions.ModifyRequest` (the caller's, or the one `OnConfigure` installed).
4. Each `WithRequestModifier` modifier, in the order added.
5. The credential from `WithCredential`.
6. The request is sent.

#### Construction and registration

A client is constructed over a `BlueprintHttpClient`, which wraps a configured `HttpClient` (its base address is what the operations' URLs are relative to) and takes optional `JsonSerializerOptions`, defaulting to Blueprint's camelCase options (one read-only instance shared by every transport given none). Options passed in are locked when a typed client is first made over them, as their first serializer call would lock them. Readonly-field parameters follow:

```csharp
var transport = new BlueprintHttpClient(httpClient, jsonOptions);
IRegionalOperationsApi api = new RegionalOperationsApi(transport, endpoints);
```

A client is cheap, immutable and thread-safe, because `WithCredential` and `WithRequestModifier` make clones that share the transport and fields. Register it with `services.AddBlueprintClient<IItemsApi, ItemsApi>((sp, client) => client.BaseAddress = ...)`, which returns the named client's `IHttpClientBuilder` (so handlers such as retry still apply), makes the client transient, and resolves each readonly field the class declares from the container through the generated `Create` (a field declared nullable is left null where the container has none).

The named client, `typeof(TImplementation).Name`, is configured on every call, but the interface is registered only once:

- **An interface already registered stands.** Where `TInterface` is in the container before `AddBlueprintClient` runs (a test double, a hand-written factory), no second registration is added, and resolving `TInterface` gives the earlier one. The named client is still given the callback, so a gateway's generated forward, which takes the named client by name through `BlueprintClientTransport.Create<TClient>` and not through `TInterface`, has its base address and handlers.
- **A second call adds to the same named client.** Its callback runs after the first, as `IHttpClientBuilder` configuration always composes, so a base address set by both is the second's, and a header set only by the first still applies. Handlers chained on either returned builder apply to the one client.

#### Serializer context

`[BlueprintClient(SerializerContext = typeof(MyContext))]` optionally names a `JsonSerializerContext` for the client to read and write with. The generated constructor calls `client.WithSerializerContext(MyContext.Default)`, which derives read-only options with the context first in the resolver chain. The derived options are cached per read-only options and context (options still open to change are derived afresh for each client, so a change is never missed), so converters on the transport's options (NodaTime, for example) still apply. Each included operation's request and result type infos, and its reader, are resolved once per options instance into a plan that every client over those options shares, clones included: making a client is one lookup, and a call looks up no metadata, except for a derived operation's body or a type the plan could not resolve. A GET request type needs no context entry, as it has no body. A type reachable from one the context names (through its public properties and fields, collection and array elements, and generic arguments) is covered, as the System.Text.Json source generator gives the context metadata for it too; a request or result type the context does not cover is warning BPCL007. Without a context, the metadata comes from the JSON options' own resolver, resolved into the plan the same way; a transport given no options shares one read-only default instance, so its clients share one plan.

#### Diagnostics

Problems are reported at the include (BPCL002 to BPCL005, BPCL007, BPCL008), at the member (BPCL006, BPCL009) or at the class (BPCL001). For an error, the offending include or member is left out so the rest of the client is still generated. BPCL007 is a warning: the call is still generated, and only the request or result type the serializer context does not cover falls back to the JSON options' metadata. See the [BPCL table in the diagnostic reference](diagnostic-reference.md#typed-http-clients-bpcl) for each ID.

### Forwarding

A gateway handler that sends an inner operation on, unchanged, calls `ForwardAsync` on the transport:

```csharp
public async Task<Results<OkResult<GatewayTemplateResource>, NotFoundResult<GatewayTemplateResource>>> Handle(
    GetGatewayTemplateQuery query, BlueprintHttpClient upstream, CancellationToken ct)
{
    var inner = await upstream.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
        new GetTemplateQuery { Id = query.Id }, cancellationToken: ct);

    return inner.IsT2
        ? new NotFoundResult<GatewayTemplateResource>(new GatewayTemplateResource { Id = query.Id, Name = string.Empty })
        : new OkResult<GatewayTemplateResource>(Map(inner.AsT1.Content));
}
```

- **No app, no lookup.** Every operation the source generator gives a nested `Http` class also implements `IHttpRequestBuilder<TSelf>` (in `Blueprint.Runtime.Model`), explicitly, over those builders. `ForwardAsync<TInner, TResult>` is constrained on it, so it sends the inner operation with the same method and URL a typed client of it sends. An operation without the `Http` class (one with its own member named `Http`) cannot be forwarded: the call does not compile.
- **A fresh instance of the exact type.** A forward sends only `TInner`. An instance of a derived type, such as the gateway's own operation, is refused with an `ArgumentException`, because it would be written with every property of its own.
- **`TResult` is the type read back**: the inner operation's result, or a `Results<…>` of it, read exactly as a typed client reads it. It is not reshaped; map it in the handler.
- **An upstream 404 becomes the declared `NotFoundResult<T>`** when `TResult` is a `Results<…>` with one, and the upstream wrote it as that result (its content as JSON). Every other failure, a thrown not-found's problem document included (the content type tells them apart: a forward sends `Accept: application/problem+json`, so even a host with `NegotiateErrorContentType` on writes its problems as `application/problem+json`; it replaces any `Accept` the caller sets through `CallOptions.ModifyRequest`), escapes as the `BlueprintApiException` the transport raises (or `ValidationException` for a 422), so the gateway answers the upstream problem unchanged. A typed client's own calls do not do this: there, a 404 always fails.
- **Inner `$links` are never forwarded.** `ApiResource.Links` is get-only and marked `JsonObjectCreationHandling.Replace`, so deserialising a forwarded resource, even with options that populate read-only members, drops the inner service's `$links`, which name the inner host. The gateway generates its own links from its own route attributes.

#### Credentials and error translation

Pass the gateway operation's `ApiOperationContext` (a handler parameter) to forward as that operation declares:

```csharp
[TranslateError("not_found", nameof(TemplateMissing))]
public partial class RenameGatewayTemplateCommand : ICommand<GatewayTemplateResource>
{
    public static readonly ApiExceptionFactory TemplateMissing = new("Gateway template missing", "gateway_template_missing", HttpStatusCode.NotFound);
    // ...
}

public async Task<GatewayTemplateResource> Handle(RenameGatewayTemplateCommand command, ApiOperationContext context, CancellationToken ct)
{
    var updated = await upstream.ForwardAsync<UpdateTemplateCommand, ResourceEventResult<TemplateApiResource>>(
        context,
        new UpdateTemplateCommand { Id = command.Id, Name = command.Name, HtmlSource = command.HtmlSource, PlainTextSource = command.PlainTextSource },
        cancellationToken: ct);
    // ...
}
```

- **The caller's credential by default (D7).** The forward is authenticated, last, with the `Authorization` header of the request the gateway is serving (`CallerAuthorizationCredential`), captured per call. Nothing is set on the shared `HttpClient`, so this replaces `HttpClientTokenForwarding.ForwardAuthorizationHeader` for forwards. A caller with no `Authorization` header gets none forwarded.
- **`[UpstreamCredential<TStrategy>]` replaces it per operation.** `TStrategy` is an `IUpstreamCredentialStrategy` registered in the gateway's services; for each forward it is given the context, the inner operation being forwarded and the attribute, and returns the `IUpstreamCredential` to apply. A strategy that takes arguments, such as which property holds a tenant id, declares its own attribute deriving from `UpstreamCredentialAttribute<TStrategy>` with them as properties. An unregistered strategy fails the forward with an `InvalidOperationException` before anything is sent. Because the attribute names the strategy type, an operation that uses one is declared in the gateway's own assembly, not a contract assembly. A strategy that authenticates as the gateway's own service identity means the upstream no longer checks the caller, so give that operation its own `[Resource(noun, verb)]` and register an `IResourceAuthorizationProvider` in the gateway: the sample's `GetServiceTemplateQuery` refuses a caller without `template:read` before it forwards.
- **Only the context-taking overload applies this policy.** `ForwardAsync<TInner, TResult>(inner, …)`, without the context, adds no per-call credential (only what the `HttpClient` itself is configured to send) and translates nothing.
- **`[TranslateError(fromType, to)]` answers an upstream problem as a local one (D8).** On the gateway operation or on its `BlueprintApp`, it maps a `BlueprintApiException` whose problem `type` is `fromType` to the static `ApiExceptionFactory` member named `to` on the attributed class. The operation's translation of a type wins over the app's. The translated problem keeps the upstream detail and has the upstream failure as its inner exception; its upstream extensions are not carried. Untranslated failures escape unchanged.
- **Read a strategy's arguments from the inner operation.** A property a gateway binding sets, such as a tenant id bound from a claim, exists only on the inner operation, so a strategy reads an argument like `TenantIdProperty` from the `inner` it is given, never from the gateway operation.
- **Read once, checked at build time.** A forward reads these attributes the first time its operation forwards and caches them. The source generator reports a second credential attribute (BPGW015), a credential type that is not an `IUpstreamCredentialStrategy` (BPGW016) and a `TranslateError` target that is not a static `ApiExceptionFactory` (BPGW017) on any operation or app, so they fail the build rather than the first forward.

### Exposing an Operation

A gateway operation that only forwards an inner operation is declared with `[Expose<TInner>]` (in `Blueprint.Model.Attributes`), and needs no properties or handler:

```csharp
[Expose<GetTemplateQuery>]
[MustBeAuthenticated]
[SelfLink(typeof(TemplateApiResource), "/templates/{Id}")]
[HttpGet]
public partial class GetGatewayTemplateQuery : IQuery<Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>;
```

- **Properties.** The source generator gives the class each public settable property of `TInner` that is not obsolete, with its type, attributes (validation and binding included; one naming a type the gateway's assembly cannot access is BPGW018), an `<inheritdoc>` of its documentation, and the default `TInner`'s initialiser gives it, a required property's included. Every generator reads them from one shared property model, so the operation binds, validates, documents and links them as `TInner` does. A property the class declares itself wins over the inner one.
- **JSON bodies.** The System.Text.Json source generator cannot see the generated properties, so the app's serializer context has no metadata for them. That does not affect binding: Blueprint reads the body through its own generated population, which includes them. A primitive property needs no metadata. Any other type is read with the app's JSON options, which fall back to reflection for a type the context does not cover, so a trimmed or AOT app lists those property types in its context.
- **Defaults.** Each operation instance reads every default from a `TInner` created for it alone, so no two operations share a mutable default such as a list, and a per-construction one such as `Guid.NewGuid()` differs for each.
- **Handler.** The generated handler creates a fresh `TInner` from those properties and forwards it with `ForwardAsync`, passing the operation's context, so it authenticates and translates errors exactly as a hand-written forward does (see [Credentials and error translation](#credentials-and-error-translation)), reading the response as the class's declared result. It forwards through the one `[BlueprintClient]` that includes `TInner`, using the named client and JSON options `AddBlueprintClient` registered for it and the client's `SerializerContext` (`BlueprintClientTransport.Create<TClient>`), so register that client with its base address. A client in a referenced assembly is found by the `[BlueprintClientIncluded]` its generated half carries for each include. A property bound from a header is not sent upstream yet: no request builder writes headers.
- **What the class declares.** Its operation interface (the inner operation's, for its result), route, HTTP method and authorisation. Authorisation must be one of `[AllowAnonymous]`, `[MustBeAuthenticated]`, `[Resource]` or `[FromResource]`; the inner operation's is never copied.
- **Response types.** The gateway reads the upstream response with the JSON options it writes its own with. A type its serializer context covers needs a constructor the source-generated metadata can call: a public one, not a private `[JsonConstructor]`.
- **Minimal mode is for first-party apps.** A property added to `TInner` becomes settable at the gateway with no change to its source. A public API sets `<BlueprintGatewayAllowExpose>false</BlueprintGatewayAllowExpose>`, which makes every `[Expose<TInner>]` error BPGW001.

#### Binding inner properties

A property the gateway must fill itself, rather than take from its caller, is bound. A bound or hidden property is not in the gateway operation's shape at all: it is not emitted, so it is in neither the OpenAPI document nor the request builders, and no caller can set it.

```csharp
// Every exposed operation in this assembly whose inner operation has a TenantId takes it from the caller's claim.
[assembly: GatewayBind("TenantId", FromClaim = "tenant_id")]

[Expose<AddTenantNoteCommand>]
[Bind(nameof(AddTenantNoteCommand.TenantId), FromClaim = "account_id")]   // overrides the assembly's binding here
[Bind(nameof(AddTenantNoteCommand.Source), Value = "partner-portal")]     // a fixed value
[Hide(nameof(AddTenantNoteCommand.InternalNote))]                          // not exposed, left unset
[MustBeAuthenticated]
[RootLink("/partner-notes")]
[HttpPost]
public partial class AddGatewayPartnerNoteCommand : ICommand<TenantNoteApiResource>
{
    // After the copy and the bindings, before sending. Later parameters are injected as a handler's are.
    internal ValueTask OnMapAsync(AddTenantNoteCommand inner, IS365RoleCatalogue roles)
    {
        if (!roles.MayAddNotes(inner.TenantId))
        {
            throw new ForbiddenException("Partners may not add notes to this tenant.");
        }

        return ValueTask.CompletedTask;
    }
}
```

- **Tiers.** An operation's `[Bind]` or `[Hide]` overrides the assembly's `[GatewayBind]` for the same property.
- **Sources.** `[Bind]` sets exactly one of `FromClaim`, `FromRoute` (a placeholder every one of the gateway's own routes carries, read from the property the route fills), `FromProperty` (a property the gateway class declares, with a public getter) or `Value`. `[GatewayBind]` takes `FromClaim`.
- **Claims.** A claim converts to the property's type by the rules route binding uses: a string, a scalar, an enum, an `IParsable<T>`, a static `TryParse` or a `[TypeConverter]`. A caller without the claim, or whose claim does not convert, is refused with 403 before anything is sent upstream.
- **The hook.** `void OnMap(TInner inner)`, not `async`, or `ValueTask`/`Task OnMapAsync(TInner inner, …)`, an `internal` or `public` instance method. An exception it throws stops the forward, so it is the guard too.
- **Inner routes.** A placeholder of the inner operation's first route, the URL the forward sends, that the gateway's route lacks has to be copied, bound or hidden (BPGW010): the inner request builder needs a value for it. A copied property satisfies it: one the gateway contributes, or declares with the inner property's type, is read from wherever the gateway's route leaves it, such as the query string, and set on `new TInner { … }`. So `GET /gateway/templates/find?id=…` can forward to `GET /templates/{Id}`. For a caller who leaves the value out to get 422 from the gateway, the gateway's property needs `[Required]`: a contributed property takes it from the inner property, but a declared one has only its own annotations, so give it `[Required]` too. Without it the forward has no URL to send. A property the gateway declares with another type is its own and is not copied, so it does not satisfy the placeholder.
- **Hide.** A property the inner operation requires, by `required` or `[Required]`, cannot be hidden (BPGW009); bind it instead.
- **Rules.** A contributed property's validation rule that names a bound or hidden property, such as `[RequiredIf(nameof(Enabled), true)]` with `Enabled` bound, is left to the inner operation, which applies it with the bound value in place.
- **Declared properties.** A property the gateway class declares itself cannot be bound or hidden from anything but that declaration (BPGW021): callers would see it while the forward ignored it. Remove it, or forward it with `[Bind("X", FromProperty = "X")]`.

The `BPGW` diagnostics are listed in [diagnostic-reference.md](diagnostic-reference.md#gateway-operations-bpgw).

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

- **No automatic Authorization header injection** — On a typed client use `WithCredential(new BearerTokenCredential(jwt))`; otherwise configure bearer token forwarding via `HttpClient` delegating handlers
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

## Blueprint.Mcp

**NuGet:** `Blueprint.Mcp` | **Source:** `src/Blueprint.Mcp`

Serves a Blueprint app's operations as Model Context Protocol tools, over the official C# MCP SDK
(`ModelContextProtocol.AspNetCore`) and its Streamable HTTP transport (D22). It is a package of its own so that only an
app that serves MCP takes the SDK.

### What It Provides

| Abstraction | Member | Purpose |
|---|---|---|
| DI registration | `AddBlueprintMcp()` | Registers the app's MCP server, stateless, giving each MCP endpoint its own app's tools |
| Endpoint mapping | `MapBlueprintMcp<TApp>(path)` | Serves one tool per operation `TApp` exposes over HTTP at `path` |

### Registration

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddBlueprintApi<GatewayBlueprintApp, GatewayBlueprintAppExecutor>(_ => { });
    services.AddBlueprintMcp();
}

public void Configure(IApplicationBuilder app)
{
    app.UseRouting();
    app.UseAuthentication();

    app.UseEndpoints(endpoints =>
    {
        endpoints.MapBlueprintApi<GatewayBlueprintApp, GatewayBlueprintAppExecutor>("gateway/");

        // After MapBlueprintApi: the tools are made from the API's input schemas.
        endpoints.MapBlueprintMcp<GatewayBlueprintApp>("gateway/mcp");
    });
}
```

`Blueprint.SourceGenerator.Sample.Gateway` maps it at `gateway/mcp`, and `Blueprint.EndToEnd.Tests/McpToolsTests.cs`
calls it there with the SDK's own MCP client.

### How It Works

- **Tools.** One per operation with a URL, named by its OpenAPI `operationId`, with the operation's flattened input
  schema (`IOperationInputSchemaProvider`, D21) as its `inputSchema` and its summary and description as its
  description. A GET-only operation is annotated `readOnlyHint`. An operation whose schema cannot be made fails
  `MapBlueprintMcp`, as it fails the OpenAPI document, unless `RenderOpenApiDocumentOnFirstRequest` defers both.
- **Calls run through the pipeline.** A call makes the HTTP request the URL and method its `operationId` names answer
  (of an operation answering GET and POST, the method that reads its body): path arguments into the route, query and
  header arguments as an HTTP caller writes them, and body arguments as the JSON body. The request runs in process
  through the operation's own endpoint, so the ASP.NET Core authorisation the middleware would apply to that endpoint
  (its policies, `[Authorize]` data and requirements, or the fallback policy when it has none), binding, validation,
  authentication and authorisation are those of HTTP. It carries the MCP request's `Authorization` header, scheme, host
  and path base (so links point at the HTTP API), and is signed in from that header by the host's default
  authentication scheme, as the authentication middleware signs in an HTTP request. It is not given the MCP request's
  `HttpContext.User`, which the MCP endpoint may have signed in with a scheme of its own that the API does not use. It
  has its own service scope and execution context, as an HTTP request has, carrying only the MCP request's tracing
  activity and culture; `IHttpContextAccessor` gives its handler, its callbacks and its scope's disposals the
  operation's request, while middleware around the MCP endpoint still reads the MCP request from it; and its response's `OnStarting` and `OnCompleted`
  callbacks run, so what it registered for disposal is disposed. An API mapped in a route group is called with the
  group's prefix and conventions, a route value in the prefix (`tenants/{TenantId}`) taken from the URL the tools were
  called at, and a HEAD operation's result has its status and no body, as HTTP gives it.
- **One mapping per tool set.** The tools run through the endpoints of the `MapBlueprintApi` call mapped last before
  `MapBlueprintMcp`, so an app mapped under two base paths, each with its own conventions, can have tools for each.
  `MapBlueprintMcp` refuses to map when that call was another app's, rather than serve its schemas and policies.
- **Route values are HTTP's.** They are read back from the path the arguments make, as routing reads them, so a segment holding two values (`{From}-{To}`) splits as it does over HTTP. An empty path argument is refused as missing, as no HTTP route matches an empty
  segment, except a catch-all's, which matches an empty remainder, and a defaulted parameter's, which is given its
  default. A slash within one reaches the operation escaped (`a%2Fb`), as routing gives it. The route's constraints
  (`{Id:int:min(1)}`) and host (`RequireHost`) are evaluated as routing evaluates them, and a value or host they refuse gets routing's 404.
  The host's own routing then matches the request, and the operation runs only when routing selects its endpoint: a
  call to `/items/{Name}` with `special` where `/items/special` is mapped too is refused, as HTTP sends that request
  to the literal route's operation.
- **Results are the HTTP response.** The body is the tool result's text, read in the charset its content type
  declares (UTF-8 when it declares none), and its structured content when it is a JSON object. The status is `_meta.httpStatus`, and a 4xx or 5xx is a tool error: an unauthenticated call gets the 401
  problem body an HTTP caller gets, a call that fails validation the same 422.
- **Stateless.** Every MCP request is answered within its own HTTP request, so a tool call always runs as the caller
  that request authenticated. A stateful session would answer later calls as whoever opened it.

### Limits

- Listing tools needs no authentication, as reading the OpenAPI document needs none. Add `.RequireAuthorization()` to
  the builder `MapBlueprintMcp` returns to require it.
- An operation whose endpoint requires rate limiting (`RequireRateLimiting`) is refused as a tool: the rate-limiting
  middleware, which a tool call does not go through, offers no way to apply a named policy outside it.
- A caller signed in by anything but the `Authorization` header, such as a cookie, is anonymous to the operation: the
  operation's request is signed in from that header alone.
- Only the `Authorization` header is carried from the MCP request; any other header an operation reads must be a tool
  argument.
- There are no output schemas yet: `IOperationInputSchemaProvider` gives inputs only.
- A body that is not text (a file or stream result), text with a content encoding (`gzip`, `br`), or text longer than 1 MiB, is described in the result rather than
  returned. Its bytes are counted, not kept, so a large download does not fill the server's memory.
- Blueprint owns the app's MCP server: tools registered with the SDK's own `WithTools` are not served at a
  `MapBlueprintMcp` endpoint.

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
| Upstream double (package `Blueprint.Testing.HttpClient`) | `BlueprintClientStub<TClient>` (`services.StubBlueprintClient<TClient>()`) | Answers and records a `[BlueprintClient]`'s calls by operation type |

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

### Stubbing a Blueprint client

An `[Expose<TInner>]` gateway's generated forward sends through the named `HttpClient` of the `[BlueprintClient]` that includes `TInner`. It never resolves the client's interface, so substituting that interface intercepts nothing. `StubBlueprintClient<TClient>()`, from the `Blueprint.Testing.HttpClient` package, replaces that named client's upstream with a stub keyed by **operation type**. It ships separately so that `Blueprint.Testing` does not take on `Blueprint.HttpClient`'s `Microsoft.Extensions.*` package dependencies; on Marten 6 they fail restore with NU1608. It intercepts a generated forward and a typed-client call alike, and no test restates a route.

```csharp
var upstream = services.StubBlueprintClient<CountriesApi>();
upstream.Respond<GetCountryQuery>(q => new CountryApiResource { IsoCode = q.IsoCode });

// … exercise the gateway …

upstream.Received<GetCountryQuery>().Should().ContainSingle(q => q.IsoCode == "GB");
upstream.Requests.Single().RequestAuthorization.Should().Be(new AuthenticationHeaderValue("Bearer", callerToken));
```

- **Where it sits.** It is the named client's primary handler, applied after the app's own configuration in whichever order the two are registered. Requests are still built by each operation's generated request builder, and responses are still read by the client's own readers, so routes, query strings, bodies and reading stay covered. The app's delegating handlers still run, and nothing reaches the network.
- **How a request is matched.** `BlueprintHttpClient` tags every request with the operation it was built from (`BlueprintRequestOptions.Operation`). The stub dispatches on that operation's exact type.
- **What it answers with.** A response is written as a Blueprint host writes the same result:

  | The responder returns or throws | The client sees |
  |---|---|
  | A value | 200 JSON, written with the client's JSON options and serializer context, asynchronously, so an `IAsyncEnumerable<T>` is streamed as an array |
  | `null` | 204 |
  | `NoResultOperationResult` | An empty 200, as the host writes it |
  | `OkResult`, `CreatedResult`, `AcceptedResult`, `ConflictResult` | Its content, with its status |
  | `NotFoundResult<T>` | A JSON 404, read as a declared `NotFoundResult<T>` member |
  | `StatusCodeResult`, `NotModifiedResult<T>` | Its status |
  | `PlainTextResult` | Its text, with its status and its own content type; at 204, 205 or 304, its status alone |
  | `throw new ApiException(…)` | Its problem document, named as the host names it (an extension the client's serializer context does not register is still written), raised as `BlueprintApiException` (or translated by `[TranslateError]`) |
  | `throw new ValidationException(…)` or `ValidationFailedResult` | A 422 whose keys the host's own naming produces (`form` for a form-level error), raised as `ValidationException` |

- **A missing response fails loudly.** An operation with no `Respond<TOp>` fails the call with an `InvalidOperationException` naming the client and the operation. A request the client did not build from an operation is refused in the same way.
- **What is recorded.** `Requests` holds each request's operation, method, URI, headers (content headers such as `Content-Type` included) and body. `Authorization` is the header the upstream received. `RequestAuthorization` is the one the request set itself, through a forward's credential or `CallOptions.ModifyRequest`. Assert on `RequestAuthorization` to prove a forward applied its credential: an app that also copies the caller's token onto the client's default headers sends the same token either way.
- **Base address.** A named client given no base address, for example because the test leaves out the app's `AddBlueprintClient` registration, gets `BlueprintClientStub<TClient>.DefaultBaseAddress`.
- **Thread safety.** Calling `StubBlueprintClient` again for the same client returns the same stub. Responses and records may be added and read from any thread.

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
| **Blueprint.HttpClient** | ✅ | ✅ Typed service clients, reflection-free registration, credentials, compile-time call plans | ❌ Built-in | Tenant/tracing headers (delegating handlers) |
| **Blueprint.Mcp** | ✅ | ✅ One tool per op, same auth as HTTP | ✅ Through the op's own pipeline | Output schemas, file results |
| **Blueprint.Testing** | ✅ | ✅ Full test infrastructure | ✅ `WithTenant()` | Audit/event assertions |

### Cross-Cutting: Multi-Tenancy

Multi-tenancy is the primary gap across all integration packages. The recommended approach for S365:

1. **HTTP requests:** ASP.NET Core middleware extracts `sig365_tenant_id` claim → sets `ICurrentTenantAccessor`
2. **Rebus messages:** Custom `IBackgroundTaskContextProvider` restores tenant from message headers
3. **Hangfire tasks:** Custom `IBackgroundTaskContextProvider` restores tenant from job parameters
4. **HTTP client:** Delegating handler forwards `X-Tenant-Id` header to downstream services
5. **Marten:** Configure conjoined tenancy, session reads tenant from `ICurrentTenantAccessor`

This pattern keeps tenant resolution in the application layer (S365-specific) while using Blueprint's pipeline abstractions for everything else.
