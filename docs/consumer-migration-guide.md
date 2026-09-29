# Consumer migration guide

This guide walks through migrating a legacy Blueprint application (such as a Signature365 service) to the source-generated Blueprint stack. The new stack replaces runtime middleware discovery with compile-time code generation, giving you deterministic pipelines, build-time validation, and zero-reflection execution.

## Prerequisites

- .NET 10+ SDK
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
| `AddHateoasLinks()` | Automatic when `[SelfLink]`/`[RootLink]` exist; an app that never called it needs `DisableHateoasLinks()` | Auto-fixable (BPMIG002) where present; configuration-required where absent |
| `AddAuthorisation()` | Automatic when `[Resource]`/`[MustBeAuthenticated]` exist | Auto-fixable (BPMIG003) |
| `AddOpenApi(Action<OpenApiOptions>)` | `configureOpenApi:` parameter on `MapBlueprintApi` | Configuration-required (BPMIG100) |
| `AddAuthentication(...)` | ASP.NET Core `AddAuthentication()` + `UseAuthentication()` | Configuration-required (BPMIG101) |
| `[FromResource(...)]` attribute | `[Resource("noun", "verb")]` + `[FromResource(typeof(X))]` | Consumer rewrite (BPMIG200) |
| `{Auth:...}` placeholders | Source-generated auth metadata | Consumer rewrite (BPMIG201) |
| `[Resource(noun, verb, path)]` resource key template | `[Resource(noun, verb)]` when every placeholder is an auth claim (`{Auth:...}`/`{Auth....}`); otherwise an explicit `{Property:Auth.Claim}` binding | Consumer rewrite (BPMIG203) |
| Legacy namespace usings | Updated namespaces | Auto-fixable (BPMIG300) |
| Runtime middleware pipeline | Generated `IOperationExecutorPipeline` per operation | Automatic |
| `IApiOperationHandler<T>` | Same interface, same registration | No change |
| `ICommand` / `IQuery` | Same interfaces | No change |
| `ValidationException` | `ValidationFailedResult` (no-exception fast path) | Transparent |
| `IAuditSink` | Same interface, registered via `BlueprintServiceBuilder` | No change |
| `SetTenantMiddleware` | `TenantModule` (compile-time) via `ITenantResolver` + `ITenantAccessor` DI registration, and `[BlueprintAppTenancy(TenantIdType = ...)]` on the app root to fill only legacy's `TenantId` type | Registration change |
| `StartRebusTransactionScope` | `IMessageOutbox` via `AddMessageOutbox<T>()` | Consumer rewrite |
| `SaveMartenChanges` middleware | `IUnitOfWork` via `AddUnitOfWork<T>()` | Registration change |
| `IUnitOfWork` middleware | Built-in `UnitOfWorkModule` via `AddUnitOfWork<T>()` | No change |
| `IBackgroundTaskScheduler` | Same interface via `AddBackgroundTaskScheduler<T>()` | No change |
| `app.UseBlueprintTaskRunner()` (`Blueprint.Tasks.AspNetCore`) | `app.UseBlueprintTaskRunner()` in `Blueprint.Runtime`, which releases nothing for a request routed to a Blueprint operation | No change |
| `IUserAuthorisationContext` | Same interface via `AddUserAuthorisationContextFactory<T>()` | No change |
| `IApiAuthoriser` | Same interface via `AddApiAuthoriser<T>()`, or focused `IOperationAuthoriser` / `ILinkVisibilityAuthoriser` | Recommended split |
| `StateBasedLinkChecker<T,R>` | Now implements `ILinkVisibilityAuthoriser` via `AddLinkVisibilityAuthoriser<T>()` | Registration change |
| `Blueprint.Tasks.Hangfire` | `Blueprint.Tasks.Hangfire` package: `HangfireBackgroundTaskScheduleProvider` + `HangfireRecurringTaskProvider` | Package reference change |
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

**Behaviour change for operation names (v1.0.3).** Descriptor names — and so OpenAPI `operationId`s, request-body schema
keys, `x-links` and anything reading `ApiOperationDescriptor.Name`, such as an authorisation manifest — are now formed
exactly as legacy Blueprint formed them: `Query` and then `Command` are removed wherever they occur in the type name (not
only as a suffix), and legacy's PascalCasing then folds acronyms. Coming from legacy Blueprint nothing changes. Coming
from v1.0.2 or earlier, any operation whose name has adjacent capitals, a capital before a digit, or `Query`/`Command`
mid-name is renamed: `GenerateVCardQuery` `GenerateVCard` → `GeneratevCard`, `GetInvoicesV2Query` `GetInvoicesV2` →
`GetInvoicesv2`, `GetHTMLPreviewQuery` `GetHTMLPreview` → `GethtmlPreview`, `ExportCommandSample` `ExportCommandSample` →
`ExportSample`. Regenerate clients from the new document, and rename an operation if `BP009` reports a new collision.

**Behaviour change for an operation at several URLs (v1.0.3).** An operation published at more than one URL is given a
distinct `operationId` at each — the first keeps the plain name, the second is named `{Name}2`, and so on — instead of
the same `operationId` repeated. This is what legacy's NSwag document did. See `docs/openapi-migration.md`'s
"operationId of an operation at several URLs" section for the full rule and the request-body de-duplication that goes
with it. Regenerate clients that called a second or later URL by a duplicated `operationId`.

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

The base path may be given as `""`, `"/"`, `"api"`, `"api/"` or `"/api/"`; each is normalised to end with one slash, and
every route, whether declared with a leading slash or not, is mapped under it with one slash, as legacy mapped it.

As legacy Blueprint's `RootMetadataOperation` did, `GET <basePath>` answers anonymously with the root-metadata document:
`{"$object":"root","$links":{...},"$partial":false}`, one link per `[RootLink]` or `[Link(null, ...)]` of every exposed
operation, keyed by the attribute's `Rel` or, without one, by the operation's own descriptor name before any
`[BlueprintAppOperationNames]` removal, its href absolute with placeholders left as a template. It is mapped with the
app's other endpoints, so conventions applied to the returned builder (for example `RequireAuthorization`) apply to it
too; it is not described by the OpenAPI document; and an app that routes its own `GET` operation at the base path keeps
that operation there instead.

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

**Behaviour change for a `TenantId` (v1.0.3).** An operation's `TenantId` is bound from the request — route, query
string or body — like any other property, and appears in the OpenAPI document where the request carries it. `TenantModule`
fills it from the caller's tenant only when the caller is authenticated and the request left it unset; a tenant the
caller names is what the authorisers see, so an authoriser such as the one above is what stops a caller using another
tenant's. For a type-converter `TenantId` (such as Signature365's `ApiId`), an authenticated caller with no tenant that
leaves it unset has the converter called with null, as legacy's `ApiId.Hydrate(null)` was. Mark a `TenantId`
`[FromAuthContext]` to take it from the caller's claims alone. Coming from legacy Blueprint nothing changes. Coming from
v1.0.2, a non-route `TenantId` was never read from the request, was left out of the OpenAPI document, and was filled from
the caller's tenant whether or not the caller was authenticated.

**Behaviour change for which `TenantId` is filled (v1.0.3).** Only a `TenantId` with a public setter that is not `init` is
filled; an init-only one, or one whose setter is not public, is never filled, as legacy's `SetTenantMiddleware` skipped an
init-only one. An app root can also restrict filling to one type:

```csharp
[BlueprintAppScan]
[BlueprintAppTenancy(TenantIdType = typeof(ApiId))]
public partial class PortalBlueprintApp : BlueprintApp;
```

With `TenantIdType` set, only a `TenantId` of that type or its `Nullable<T>` is filled, an unset non-nullable one
included; a `TenantId` of any other type, such as a `string`, is bound from the request and otherwise left as the request
left it. Without it, a `string`, `IParsable<T>`, `TryParse` or `[TypeConverter]` `TenantId` is filled. Coming from legacy
Blueprint, set `TenantIdType = typeof(ApiId)` on every app root that ran `SetTenantMiddleware`: legacy filled only an
`ApiId` or `ApiId?`, so a `string` `TenantId` the request left out stayed null. A `[FromAuthContext]` `TenantId` without
a `ClaimType` is filled by the same rule, so in an app that declares another type it is never set: give it a `ClaimType`,
or the app's type. Coming from v1.0.2, every supported type was filled whatever the app, and an init-only `TenantId`
outside the route failed the build in its generated pipeline (CS8852), as did one with a private setter on an operation
with no HTTP binding (CS0272).

For authorisers that need **both** execution and link visibility, implement `IApiAuthoriser` (which extends both `IOperationAuthoriser` and `ILinkVisibilityAuthoriser`):

```csharp
blueprint.AddApiAuthoriser<FullAuthoriser>();
```

**Behaviour change for authorisation denials (v1.0.3).** A denial is answered with the body legacy Blueprint wrote for it:
`{"type":"unauthorized","title":"<Message>","status":403}` for an `Authorisation` failure,
`{"type":"security_failure","title":"<Message>","status":401}` for any other, and
`{"type":"security_failure","title":"Access denied. Anonymous access is not allowed.","status":401}` for an anonymous caller,
each with a `traceId` when the operation has an activity. The `Reason` is never written; it is logged at Information under
`Blueprint.Authorisation.AuthorisationMiddlewareBuilder`. The `ForbiddenResult` or `UnauthenticatedResult` a pipeline returns
carries the denial as `Failure`. Coming from legacy Blueprint nothing changes in the body, except a `traceId` (see the
exception bodies below); the content type is `application/problem+json` unless `NegotiateErrorContentType` is set (see
"Error content type" below). Coming from v1.0.2, the fixed "Unauthorized" / "Forbidden" bodies are gone, and a denial
whose failure type is not `Authorisation` (a `default` `ExecutionAllowed` included) is a 401 where it was a 403.

**Behaviour change for exception bodies (v1.0.3).** An exception is written with the body legacy Blueprint wrote:
- an `ApiException` with its own type, title, status, detail and extensions, a null one left out, whatever its status and
  `ExposeExceptionDetailsInErrorResponses`;
- any other exception as `{"type":"unknown_error","title":"Something has gone wrong, please try again","status":500}`, or,
  with details exposed, titled with its message and detailed with its stack trace;
- in each case a `traceId` extension last, when the operation has an activity.

`UnauthorizedException(message)` puts the message in the title, with no detail. Coming from legacy Blueprint nothing
changes in the body (for the content type see "Error content type" below), except one deliberate departure: a body
without an activity never has a `traceId`. Legacy's could. An `ApiException` made without extensions shared one static
dictionary, which legacy wrote each body's `traceId` into, so a later body with no activity carried another request's
`traceId`, and concurrent requests raced on it. This applies to denials too. Coming from v1.0.2:
- a null title or detail was filled in ("Server error", "Request error", the exception message);
- a 5xx `ApiException`'s detail was replaced;
- exposed stack traces went in a `stackTrace` extension;
- no exception body carried `traceId`;
- `UnauthorizedException` was titled "Unauthorized", with the message as its detail.

**Behaviour change for a handler's `Identity` (v1.0.3).** A handler property of type `IUserAuthorisationContext`
(`BlueprintApiOperations.Identity`, or a consumer base class's) is given the operation's user as it is:
`null` when there is no authenticated user, as legacy Blueprint gave it. Coming from legacy Blueprint nothing changes.
Coming from v1.0.2, it was `AnonymousUserAuthorisationContext.Instance`: empty ids and the name `Anonymous`.

**Behaviour change for form-level validation errors (v1.0.3).** A validation failure that names no member — an
`IValidatableObject` result without member names, `new ValidationException(message)`, or
`ValidationFailures.AddFailure(new ValidationResult(message))` — is keyed `form` in a 422's `errors`, and
`ValidationFailures.FormLevelPropertyName` is `"form"`, as in legacy Blueprint. Coming from legacy Blueprint nothing
changes. Coming from v1.0.2, the key was `""`.

**Error content type (v1.0.3, opt-in).** Every error is written as `application/problem+json; charset=utf-8` by default.
Legacy Blueprint negotiated it:
- no `Accept`, or one that includes `*/*`: exactly `application/json`;
- otherwise the highest-quality accepted `application/json`, `text/json` or `application/*+json` type, written as the
  `Accept` line it came from;
- failing that: `application/json`.

A client that reads a body as JSON only when its content type contains `application/json` needs legacy's behaviour. Set
`BlueprintHttpOptions.NegotiateErrorContentType = true` for it, and `OpenApiInfoOptions.UsePlainJsonForErrorResponses =
true` so the document agrees:

```csharp
services.Configure<BlueprintHttpOptions>(o => o.NegotiateErrorContentType = true);
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
| `RequiredIfAttribute` | Conditionally required (scalar) when another property equals a value |
| `RequiredIfNotAttribute` | Conditionally required (scalar) when another property does not equal a value |
| `RequiredItemsIfAttribute` | Collection must be non-empty when another property equals a value |
| `RequiredItemsIfNotAttribute` | Collection must be non-empty when another property does not equal a value |
| `NotAllowedIfAttribute` | Forbidden when another property equals a value |
| `MustBeTrueAttribute` | Boolean must be true (consent/terms validation) |
| `NotEmptyListAttribute` | Collection must not be empty (unconditional) |
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

**Behaviour change for `RequiredItemsIfAttribute`'s message (v1.0.3).** Its failure message is "The `{0}` field must
contain at least one non-empty item if `{1}` field is `{2}`.", legacy Blueprint's exact text — it rejects a blank or
`null` item as well as an empty collection, and the message now says so. Before v1.0.3 it said "at least one item".
`RequiredItemsIfNotAttribute` (new-only, no legacy equivalent) still says "at least one item".

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

**Behaviour change for a `PlainTextResult` with no content (v1.0.3).** `new PlainTextResult(null)` is written as an
empty body at the result's status, as legacy Blueprint wrote it; before v1.0.3 its constructor threw
`ArgumentNullException`. Its `Content` is then empty. A `?? string.Empty` added only to avoid that throw can go.

**Behaviour change for a query's resource event (v1.0.3).** A resource event's `SelfQuery` runs only when the operation is
a command, as legacy Blueprint ran it. A query that returns a resource event with a self query hands it on as it is:
`Data` stays `null`, and `SelfQuery` is kept. A query's `DeferredResourceEventResult<T>` is not resolved either. Coming
from legacy Blueprint nothing changes. Coming from v1.0.2, a query's self query ran and filled `Data`.

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

**Behaviour change for a `[Link]` without a `Rel` (v1.0.3).** Such a link is keyed — in the resource's `$links` and in
`x-links` — by its operation's descriptor name, taken before any `[BlueprintAppOperationNames]` removal, exactly as legacy
Blueprint keyed it: `DeleteGroupCommand`'s link on `GroupApiResource` is `$links.DeleteGroup`. Coming from legacy Blueprint
nothing changes. Coming from v1.0.2 or earlier, the key moves from one made from the resource type (`$links.group`) to the
operation's, and a second Rel-less link on the same resource, which v1.0.2 silently dropped, is written again. Regenerate
clients from the new document. `BPLG006` is retired.

**Behaviour change for a link without a type (v1.0.3).** A link whose `Type` is null is written with `"type":null`:
`{"href":"...","type":null}`. This covers a resource's own links, those of a resource event's data (and so what an audit
stores), and the root-metadata document's. Legacy Blueprint wrote them that way. Coming from legacy nothing changes.
Coming from v1.0.2 or earlier, each such link gains the member, which the OpenAPI document already described as
nullable and not required.

Link generation is automatic when link metadata exists — no `AddHateoasLinks()` call needed, and `BPMIG002` removes one
that is present.

**An app whose legacy `AddBlueprintApi(...)` never called `AddHateoasLinks()` generated no links at all**: legacy had no
link step without it (`BlueprintApiBuilderExtensions.AddHateoasLinks`), so every resource it returned, and every
resource-event payload it audited, carried `"$links":{}`. To keep that output, opt the app out when migrating it:

```csharp
services.AddBlueprintApi<MyBlueprintApp, MyBlueprintAppExecutor>(blueprint => blueprint.DisableHateoasLinks());
```

No tool can do this for you, because the call to look for is absent. `DisableHateoasLinks()` applies to that app only,
to every one of its pipelines (a resource event's self query included), and to every registered `IResourceLinkGenerator`.
Register one app per service provider. A message Rebus delivers, and `IApiLinkGenerator`, use the provider's last
registered `BlueprintApp`, so two apps in one provider do not each keep their own setting for those.
It keeps links a handler adds itself, a resource event's `href`, the root-metadata document, the OpenAPI `x-links` and
`IApiLinkGenerator`, as legacy did. A `201`'s `Location` header, taken from the created resource's `self` link, is then
not written unless the handler adds that link.

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

#### Scheduling outside an operation

Only a generated pipeline releases the tasks enqueued during it. A task enqueued outside any operation — from a Razor
page, a controller or a service resolved from the request's services — is released by the opt-in middleware legacy's
`Blueprint.Tasks.AspNetCore` provided, under the same name (since v1.0.3):

```csharp
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.UseBlueprintTaskRunner();

app.UseEndpoints(endpoints => endpoints.MapRazorPages());
```

As legacy's `TaskRunnerMiddleware` did (`Blueprint.Tasks.AspNetCore/TaskRunnerMiddleware.cs:32-36` at Signature365
`8b40ecc961`), it awaits the rest of the pipeline and then calls `RunNowAsync()` on the request's
`IBackgroundTaskScheduler`, whatever status the request was answered with, and releases nothing when the rest of the
pipeline throws. Two differences:

- **A request routed to a Blueprint operation releases nothing.** Legacy ran each operation in a DI scope of its own
  (`Blueprint.Http/ApplicationBuilderExtensions.cs:213`), so the request's scheduler never held an operation's tasks;
  this host runs an operation in the request's scope. The operation's pipeline releases its tasks when it succeeds, and
  an operation that failed after enqueuing — a validation failure, a handler that threw, a save that threw — must not
  have them released. A task that code outside the operation enqueued during that same request, which legacy released,
  is not released.
- **No scheduler registered releases nothing.** Legacy's middleware took the scheduler as an `Invoke` parameter, so every
  request failed without one.

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

**Behaviour change for plain Rebus handlers (v1.0.3).** Before v1.0.3, a background task that a plain (non-operation)
`IHandleMessages<T>` enqueued through its `IBackgroundTaskScheduler` was never released: only a generated pipeline
flushed the scheduler, and `BlueprintApiActivator` ran no pipeline around such a handler, so the task was discarded with
the message's scope. Legacy ran every subscribed message as an operation whose pipeline called `RunNowAsync()` after its
save (`AccessTokenRenewalFailedEventExecutorPipeline.cs:74-82` at Signature365 `8b40ecc961`). The activator now releases
the scope's tasks when Rebus commits the message's transaction, straight after it saves the scope's unit of work, and:

- only when the delivery succeeded — not for a handler that threw, a save that threw, or a message moved to the error
  queue;
- with or without a registered `IUnitOfWork`, as legacy's runner matched every operation;
- before the commit callbacks an `IRebusTransactionEnlistment` or the transport's first send registered, so, as for a
  generated pipeline's flush, a task provider must not send a Rebus message.

A consumer whose plain handlers enqueue tasks sees them dispatched. One with no `IBackgroundTaskScheduler` registered
sees no change.

### 13. Persistence

Register a unit-of-work implementation to auto-save changes after an operation succeeds:

```csharp
blueprint.AddUnitOfWork<MartenUnitOfWork>();
```

The generated pipeline calls `SaveChangesAsync()` on `IUnitOfWork` after any operation — command, query or background
task — succeeds. Opt out per-operation:

```csharp
[DoNotSaveChanges]
public partial class PreviewCommand : ICommand<OkResult<Preview>> { }
```

**Behaviour change for queries (v1.0.3).** Before v1.0.3 only commands and background tasks saved the unit of work,
opened a Rebus transaction scope and enlisted it (`IRebusTransactionEnlistment`); a query did none of them. Legacy
Blueprint did all three after any operation: Signature365's `SaveMartenChangesMiddleware` and
`UseMartenAsOutboxMiddleware` matched every operation without `[DoNotSaveChanges]`, and
`StartRebusTransactionScopeMiddleware` every one without `[NoRebusTransaction]`
(`Common/Data/Marten/SaveMartenChangesMiddleware.cs:24-29` at Signature365 `8b40ecc961`). A query now does too:

- a document a query stores in the scope's session is saved, and a message it sends is held in its Rebus transaction
  and sent when the query completes, rather than at once (through the enlisted outbox, where one is registered);
- `[DoNotSaveChanges]` on a query stops the save and the enlistment (the scope still opens), and no longer raises
  `BPP001`, which is retired; `[NoRebusTransaction]` on a query stops the scope;
- a query run nested — a resource event's self query, which runs inside its command's scope and session — opens no
  scope, enlists nothing and saves nothing, as legacy ran a nested operation through a pipeline without these steps. A
  nested command or background task behaves as before: it saves, and opens and enlists a scope of its own.

A query that stores or sends something it must not keep needs `[DoNotSaveChanges]`. A query that must not share a Rebus
transaction needs `[NoRebusTransaction]`.

### 14. Messaging

Register a message outbox to publish messages after successful command or background task execution:

```csharp
blueprint.AddMessageOutbox<RebusMessageOutbox>();
```

The generated pipeline calls `CommitAsync()` on `IMessageOutbox` after a command or background task succeeds (and after unit-of-work save). Opt out per-operation:

```csharp
[DoNotPublishMessages]
public partial class InternalSyncCommand : ICommand<NoResultOperationResult> { }
```

`IMessageOutbox` is Blueprint's own seam; legacy Blueprint had nothing like it, and it is not the Marten session outbox
legacy's `UseMartenAsOutboxMiddleware` enlisted (for that, see `IRebusTransactionEnlistment` in
[integration-packages.md](integration-packages.md#transactional-outbox)).

**Behaviour change for background tasks (v1.0.3).** Before v1.0.3, only `ICommand` pipelines committed the
`IMessageOutbox`, so every message a background task enqueued through it was discarded with the task's scope. Background
tasks (`Blueprint.Tasks.IBackgroundTask`) now commit it as commands do, after the unit of work save and only on
success. For a consumer that registers an `IMessageOutbox`:

- messages its background tasks enqueue through it are now published;
- `[DoNotPublishMessages]` on a background task now opts that task out, and no longer raises `BPM001`;
- `BPM001` still warns for `[DoNotPublishMessages]` on any operation that is neither a command nor a background task.

A task that relied on its outbox messages being dropped needs `[DoNotPublishMessages]`. A consumer with no
`IMessageOutbox` registered sees no change.

**Behaviour change for handlers of a base message type (v1.0.3).** Before v1.0.3, `AddBlueprintRebusConsumer`
registered each scanned `IHandleMessages<T>` for its own `T` only, and `BlueprintApiActivator` resolves a message's
handlers by the message's exact type, so a handler of a base class or interface never ran for a derived message:
Signature365's `SurveyThankYouKvPushHandler`, an `IHandleMessages<SurveyEditedEvent>`, did nothing for a
`SurveyPausedEvent : SurveyEditedEvent`, which Rebus then failed to dispatch. Legacy made every subscribed message an
operation and gave it every scanned handler of a type it could be assigned to
(`Symprex.Signature365.BlueprintRebus/RebusHandleMessageOperationExecutorBuilderScanner.cs:50-75` at Signature365
`8b40ecc961`). `AddBlueprintRebusConsumer` now does the same when it scans the calling assembly:

- a scanned `IHandleMessages<TBase>` is also registered as the handler of every subscribed message type assignable to
  `TBase`, whether `TBase` is a base class or an interface;
- a subscribed message's handlers run in the order legacy's scanner gave them, by handler type as the assembly lists
  them, not its own type's first;
- a message type that is not subscribed gets only its own handlers, as before (legacy ran none for it);
- a handler of a type derived from the message's type is not registered for it, as it cannot be given the message.

A handler that implements both `IHandleMessages<TBase>` and `IHandleMessages<TDerived>` now runs twice for a subscribed
`TDerived`, both times through its `TDerived` method, as legacy's pipeline called it twice: its scanner matched the
handler once per interface and kept both (`RebusHandleMessageOperationExecutorBuilderScanner.cs:54-74`,
`ApiOperationDescriptor.cs:179-194`). Handlers registered by hand rather than by the scan are unchanged.

**A host with no operations.** A host that uses Blueprint only for Rebus — it declares no operations and maps no
Blueprint routes, as Signature365's GraphProxy does — registers its app directly and does not call `AddBlueprintApi`:

```csharp
services.AddSingleton<BlueprintApp, GraphProxyBlueprintApp>();
services.AddBlueprintRebusConsumer(_ => { });
services.AddBlueprintRebus(/* the host's Rebus configuration */);
```

`AddBlueprintApi<TApp, TExec>` needs the executor the generator emits for an app with a `[BlueprintAppScan]`, and a scan
that finds no operations is `BP003`, so such an app has no executor to name. `BlueprintApiActivator` needs only the app,
plus the `BlueprintRebusConfiguration` that `AddBlueprintRebusConsumer` registers. The executor is needed only to run an
operation, and this host has none, so it sends, publishes and handles plain `IHandleMessages<T>` messages without one.
Legacy's activator needed the same. `[BlueprintAppSerializerContext]` does nothing on an app without a
`[BlueprintAppScan]`.

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

**Behaviour change for absent route values (v1.0.3).** When a route property's value is not in the request's route
values, and the property is a placeholder in *every* one of the operation's route templates, the operation keeps the
value it already has, whatever the property's type. This is what legacy Blueprint did. It is what happens when an
operation is executed in process with a fresh `HttpContext`, as an `ApiFixture` or a CLI executes one. Before v1.0.3
that was only true of `string` and `[TypeConverter]` properties: a typed one (`int`, `Guid`, …) was a binding failure,
so an in-process caller had to fill `HttpContext.Request.RouteValues` itself. It no longer has to. A route value that
is present but cannot be parsed is still a binding failure.

**Behaviour change for a placeholder in only some of an operation's links (v1.0.3).** A property that is a route
placeholder in some of the operation's links but not all of them is not exclusively route-owned: through a link that
carries the placeholder it still binds from the route, but through one that leaves it out it now falls back to the
query string (a GET) or the body (any other method) instead of keeping its type's default (`0`, `Guid.Empty`, an unset
`TenantId`, …). This is what legacy Blueprint did — its route source only ever owned a placeholder property
exclusively when every link carried it; otherwise the query/body source read it too, and ran first, so a route value
present for the matched link still won. Before v1.0.3 the property was left at its default through a placeholder-less
link and the operation still ran, silently answering for the wrong (or no) value. `TenantId` is filled from the
caller's tenant only when neither the route nor the fallback sets it, as before.

**Behaviour change for alternate-named route placeholders (v1.0.3).** A `[SelfLink]` or `[Link]` placeholder that names
an alternate resource property, `{UserTenantId:TenantId}`, binds the operation's `UserTenantId` from the path, as
`{UserTenantId}` does. This is what legacy Blueprint did. Before v1.0.3 the property was read from the query string (a
GET) or the body instead, and filled from the caller's tenant if it was a `TenantId`, although the URL it was routed at,
and the URL the generated client sent, carried it in the path. `[FromRoute]` on such a property now compiles (it reported
BPPO005), and the OpenAPI document publishes it as a path parameter. A root link's URL is still routed as declared, so a
colon there is ASP.NET's inline constraint syntax.

**Behaviour change for a DELETE (v1.0.3).** A DELETE's properties that are not in its route are read from its JSON body,
as a POST's are, and no longer from its query string. `BlueprintHttpClient` sends a DELETE's operation as its JSON body
with no query string, and the OpenAPI document describes those properties as its request body. This is what legacy
Blueprint did: it read the body of every request but a GET, and the query string of a GET alone. A DELETE with no body
is bound from its route alone, as before. `[FromBody]` on a DELETE property now compiles; BPPO004 applies to a GET only.
A caller that sent a DELETE's properties in the query string must send them in the body.

**Behaviour change (v1.0.3).** A request whose `Content-Type` is set but is not JSON no longer gets a 415 for any
method: the operation's body properties are simply left unpopulated, for every request but a GET — legacy Blueprint
never rejected a body it could not parse; it silently left the operation as it was
(`HttpBodyMessagePopulationSource.cs:80-86`). A body-less DELETE, HEAD or OPTIONS that nonetheless carries a stray,
non-JSON `Content-Type` (a proxy or client default) is bound from its route alone, not 415'd, as before v1.0.3
brought DELETE/HEAD/OPTIONS under the same check as POST/PUT/PATCH. POST, PUT and PATCH now match too: a stray or
wrong content type on one of those leaves the operation's body properties unset rather than short-circuiting to a
415 — if those properties are required, validation reports them missing instead.

This is *not* what legacy Blueprint did in one respect: legacy registered a `FormBodyParser` by default for exactly
`application/x-www-form-urlencoded` and `multipart/form-data`, and bound form fields and `IFormFile` properties from a
request carrying one of those (`BlueprintHttpOptionsSetup.cs:25`, `Formatters/FormBodyParser.cs:29-30`). This
framework has no form binding, so it cannot reproduce that behaviour; silently ignoring the body would let the
handler run with those properties unset instead. A request with one of those two content types and a body still gets
a 415, on any method that reads a body — this is the one non-JSON content type family this framework refuses rather
than ignores. One with no body (`Content-Length: 0`, or an empty chunked body) runs the handler with the operation as
it was, as legacy did for `Content-Length: 0` and for an empty chunked urlencoded body: it read those as an empty form
and bound nothing (an empty chunked multipart body, which legacy's form reader failed on with a 500, runs the handler
too). Every other non-JSON content type (`text/plain`, `application/xml`, …) is ignored as described above.

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

### OpenAPI document changes in v1.0.3 that apply under every schema naming

These change the document a consumer on `OpenApiSchemaNaming.Default` reads too, not only one on `Legacy` naming. Coming
from v1.0.2 or earlier, regenerate the clients from the new document and expect the differences below.

**Behaviour change for two enums of one simple name (v1.0.3).** Two distinct enum types with the same simple name (two
nested `Status` enums, or two apps' own `AuthEnforcementType`) are now named apart by the same counter that names two
same-named classes: the second is `X2`, with its own member list. Before, both properties referenced the one component and
the second enum's members were lost, so a client saw the wrong values. A single enum is named as before.

**Behaviour change for an enum-array query parameter (v1.0.3).** A collection query parameter of an enum
(`InvoiceStatus[]? Statuses`) now describes its `items` inline with the member names — `type: string`, `enum`,
`x-enumNames` and `x-enum-varnames` — where it was a bare `{"type": "string"}` and a generated client lost the enum. The
enum is written inline, as a scalar enum query parameter already is, not as a `$ref` to a component. A `Header` or
`Cookie` array parameter is unchanged.

**Behaviour change for a NodaTime request-body or query property (v1.0.3).** A NodaTime property of a request body or a
parameter now carries its ISO-8601 `format`, as the same type already did in a response schema: `Instant`,
`OffsetDateTime`, `ZonedDateTime` and `LocalDateTime` are `date-time`, `LocalDate` is `date`, `LocalTime` is `time`, and
`Duration`, `Period` and `Offset` are `duration`. Before, they were a bare `string`, so a generated client typed the field
as a plain string.

**Behaviour change for the ResourceEvent envelope's text (v1.0.3).** The `changeType` and `href` properties of a
`ResourceEvent` envelope are now described by the framework's own summaries ("Gets the change type of this event,
indicating whether the resource was created, updated or deleted." and the `href` one) unless `SchemaNaming` is `Legacy`,
where they keep legacy Blueprint's wording (typo included). Before, every consumer got legacy's wording, which refers to
a `Type` property `ResourceEvent` does not have. The wording is the only difference. A caller of
`OpenApiDocumentRenderingContext.Create` chooses it with its new optional `useLegacyResourceEventText` argument, which
defaults to `false`.

**Behaviour change for a non-public `ApiExceptionFactory` (v1.0.3).** An operation's error statuses now come from every
`static` field of type `ApiExceptionFactory` the operation declares, whatever its accessibility and whether or not it is
`readonly`, as legacy Blueprint's `GetFields(Static | NonPublic | Public)` read them. Before, only `public static
readonly` fields counted, so a private factory's status was missing from the document, from the operation's
response metadata and from a client's error catalogue. Expect responses (for example a `410`) to appear on operations that
declare their factories privately.

**Behaviour change for when an operation's factories are read (v1.0.3).** The generated descriptor reads each factory's
`Title` and `Type` when it is built — in the generated app's constructor, for every operation that declares a factory,
under either naming — as legacy read each field's value. Reading a `static` field runs the static initialisation of the
type that declares it, so an operation's static initialiser (and that of a class it borrows a factory from, such as
`= PartnerApiExceptions.NotFound`) now runs when the app is constructed instead of on first use. An initialiser that
throws, or a factory that is still `null` then, fails at construction. Declare a factory as a `static readonly` field with
a plain initialiser, and keep other work out of an operation's static initialiser.

**Behaviour change for an enum reached only as an array's items or a dictionary's values (v1.0.3).** A response
resource's `IEnumerable<SignatureUsage>` property (or a `Dictionary<string, SignatureUsage>`, or a dictionary of arrays of
it) wrote `items.$ref` to the enum's component, but the component was collected only from a property's own enum, so it was
not written and the reference dangled: a generated client could not resolve the type. Such an enum now has its component,
with its members and the description of its type, as a property's own enum has. Regenerate clients: expect components such
as `SignatureUsage` to appear.

**Behaviour change for a property typed as `IEnumerable<T>` (v1.0.3).** A request-body property declared as the interface
itself (`IEnumerable<string>? ApplyTo`) is now an array with typed `items`. Before, only a type that implements the
interface (`List<T>`, an array, a read-only list) was recognised, and the interface was documented `"type": "string"`, so a
generated client typed the field as a string.

**Behaviour change for a `System.Type` property (v1.0.3).** A property of `System.Type`, which an app's converter writes as
the type's name, is now documented `"type": "string"`, and an `IEnumerable<Type>` as an array of string `items`. Before, a
response property had no schema type at all, a collection of them was an array with no `items` (which is not valid OpenAPI
3.0 and stopped client generators that cannot type it), and a request-body property was an `object`.

**Behaviour change for an exclusive numeric bound under `Default` naming (v1.0.3).** `[GreaterThan]` and `[LessThan]` are
no longer written as `"minimum": 0, "exclusiveMinimum": true` (and the maximum's pair): that is OpenAPI 3.0's form, and
draft-07 validators, such as the AJV a generated client uses, reject the boolean as an invalid schema and stop. No document
is valid in both, so none is written. An integer's exclusive bound is written as the next integer (`[GreaterThan(0)]` is
`"minimum": 1`, `[LessThan(1001)]` is `"maximum": 1000`), which is the same set of values; a number's (`decimal`, `double`,
`float`) as the inclusive bound (`"minimum": 0`), which rejects nothing the server accepts and admits the bound itself.
`Legacy` naming writes neither, as legacy Blueprint's NJsonSchema did not know the attributes. Check a validator built on
these bounds if it relied on the exclusive keyword.

**Behaviour change for path keys (v1.0.3).** Every key of `paths` now has exactly one leading slash. A link declared
without one (`[Link(..., "signatures/{SignatureId}/publish")]`) was a key without one, which is not a valid OpenAPI path
and which a client generator cannot join to a base URL, as legacy Blueprint's `"/" + route` never was. A route declared both
with and without the slash is now one path item, not two. Regenerate clients that called those operations.

**Behaviour change for the order of an operation's link attributes (v1.0.3).** The URLs, resource links and root links of an
operation are now registered in the order its `[SelfLink]`, `[RootLink]` and `[Link]` attributes are declared, whichever
kinds they are, as legacy Blueprint's scanner registered them. Before, every root link came before every self link, and
those before every `[Link]`. It changes the order of the operation's URLs, of its resource links and of its root links,
which the root document lists. The first URL declared also keeps the operation's plain `operationId` in every naming (see
the note above on an operation at several URLs), so an operation declaring a `[SelfLink]` before a `[RootLink]` now
numbers them the other way round: `GetAccountInjectionStatus` at the self link's URL and `GetAccountInjectionStatus2` at
the root link's.

**Behaviour change for `x-links` on a resource reached through another (v1.0.3).** A resource that only ever appears as the
items of a list, or as a nested property, of another resource (a `Product` in a list) now carries the `x-links` of the
action links declared against it. Before, links were attached only to a type the app returned directly, so a generated
client lost the `canExecute` actions of a resource it received nested. A resource that is itself a nested class is found
by its full name too. Regenerate clients and expect more resource components to carry `x-links`.

**Behaviour change for a deprecated property that references a component (v1.0.3).** A deprecated property written as a
reference to a component (an enum, or an object that is nullable or described) now carries `deprecated: true` and its
message (`x-deprecated-reason`, or `x-deprecatedMessage` under `Legacy` naming), beside the `oneOf` around the `$ref`.
Before, only a plain property did, and the deprecation was dropped from a
reference. A deprecated reference that is neither nullable nor described was a bare `$ref`, which cannot carry either, so
it is now wrapped in `oneOf` too.

**Behaviour change for the `Link` component (v1.0.3).** The well-known `Link` schema component now has
`"additionalProperties": false`, as every other object component the framework writes has. Before it allowed further
properties, so a client that generates strict types from `additionalProperties` got a looser `Link` than every other model.
## Compile-time diagnostics

The source generator catches contract errors at build time. Key diagnostics:

| Range | Category | Examples |
|---|---|---|
| BP001–BP008 | Core contracts | Missing partial, missing handler, unsupported return type |
| BP101–BP104 | Validation | Invalid CompareAttribute, GreaterThanProperty, LessThanProperty references |
| BPA001 | Auditing | [Sensitive] on non-public property |
| BPM001 | Messaging | [DoNotPublishMessages] on an operation that is neither a command nor a background task |
| BPP001 | Persistence | Retired in v1.0.3 ([DoNotSaveChanges] now has effect on a query) |
| BPT001 | Background tasks | BackgroundTask operation not a command |
| BPMOD001–004 | Module system | Ordering conflicts, duplicate contributors, missing variables |
| BPMIG001–201 | Migration | Legacy patterns requiring attention |

See `docs/compile-time-validation.md` for the full diagnostic reference.

**Behaviour change for `BP109`/`BP110` and a `KeyOnlyResourceEventResult` union (v1.0.3).** The host writes a
`KeyOnlyResourceEventResult` as the empty 201, 200 or 204 of its change type, known only at run time, so `BP109`
(same host response shape) and `BP110` (same HTTP status, no discrimination strategy) now judge it against all three
statuses, not only 201. A union that passed before v1.0.3 — for example `Results<KeyOnlyResourceEventResult,
NoResultOperationResult>`, indistinguishable on an update's empty 200 — now fails to build. See
`docs/compile-time-validation.md`'s `BP109`/`BP110` entries for the full rule. `KeyOnlyResourceEventResult` is
new-only, so no legacy Blueprint consumer is affected.

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
- [ ] Unit of work saves after successful commands and queries (not after `[DoNotSaveChanges]` ones)
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

Blueprint includes a built-in `IMessageOutbox` pipeline module that commits enqueued messages after a command or background task succeeds. It does not replace legacy Blueprint's `StartRebusTransactionScopeMiddleware` / `CommitRebusTransactionScopeMiddleware` pair; the generated `RebusTransactionModule` does that, opening and completing a `RebusTransactionScope` itself. `IMessageOutbox` is an additional seam legacy did not have, for example:

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

The generated pipeline calls `CommitAsync` in the PostExecution stage of a command or background task (after UnitOfWork save, before auditing) only when `handlerResult.WasSuccessful` is true. Operations decorated with `[DoNotPublishMessages]` are excluded.

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
| HATEOAS link population (self included, as legacy made it) | Auth-gated link visibility requires runtime user context; every link is absolute under the request's base URI |
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

### Service-to-service calls (BlueprintHttpClient)

**Behaviour change for a 204 (v1.0.3).** `SendAsync<T>` (and `CallAsync<T>`) read a 204 No Content as legacy Blueprint's
client read it: as a result the host writes as an empty 204 (`HttpNoContentResult`, a deleted
`KeyOnlyResourceEventResult`, `StatusCodeResult.NoContent`), as `StatusCodeResult.NoContent.Instance` when `T` can hold a
`StatusCodeResult` (`object`, `OperationResult`), and otherwise by throwing `InvalidOperationException` ("Could not
understand the response of calling …"). Coming from legacy Blueprint nothing changes. Coming from v1.0.2, where a 204
returned `default`, a caller that checked for `null` after a 204 must declare a result that can hold one.

**Behaviour change for a JSON `null` success body (v1.0.3).** A success response whose JSON body reads as `null` (a
`null` body for a reference type, a nullable value type or a resource event) throws a `BlueprintApiException` with
legacy Blueprint's `response_empty` problem: title "Response deserialisation failed", detail "The response could not be
deserialised", status 502. Its `StatusCode` and `ResponseBody` are the response's own. This is what legacy's client did.
A `JsonElement` is still the `null` element. Coming from v1.0.2, where such a body returned `null`, a caller that checked
for `null` must catch the exception instead.

**Behaviour change for a 422's `errors` (v1.0.3).** A 422 is read into a `ValidationException` from the member named
exactly `errors` (the name the host writes) — not any other casing. With no such member, or a JSON `null` body, the
exception's dictionary is empty rather than a form-level entry made up from the response's detail. This is what
legacy Blueprint's client did (`ValidationProblemDetailsJsonConverter`, `ValueTextEquals` on the exact name). Coming
from v1.0.2, where the read was case-insensitive and a missing/`null` `errors` produced a form-level message, a caller
relying on either must not.

### Hangfire (background task scheduling)

Blueprint provides the `Blueprint.Tasks.Hangfire` package with ready-to-use implementations:

| Legacy | New |
|--------|-----|
| `HangfireBackgroundTaskScheduleProvider` | `Blueprint.Tasks.Hangfire.HangfireBackgroundTaskScheduleProvider` |
| `HangfireRecurringTaskProvider` | `Blueprint.Tasks.Hangfire.HangfireRecurringTaskProvider` |
| `BlueprintTasksBuilderExtensions.UseHangfire()` | `services.AddBlueprintHangfire()` |

```csharp
// Register in startup:
services.AddBlueprintHangfire();

// The provider receives BackgroundTaskEnvelope with W3C traceparent headers
// for distributed trace correlation across async boundaries.
```

### Rebus (transactional outbox)

Blueprint provides the `Blueprint.Rebus` companion generator and `Blueprint.Marten.Rebus` bridge:

| Legacy | New |
|--------|-----|
| `StartRebusTransactionScopeMiddleware` | `Blueprint.Rebus` companion generator (auto-generates transaction scope) |
| `CommitRebusTransactionScopeMiddleware` | (automatic — generated pipeline calls `CommitAsync`) |
| Marten outbox + Rebus publish | `Blueprint.Marten.Rebus.AddBlueprintMartenRebus()` |

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

// The generated pipeline calls SaveChangesAsync() after any operation
// succeeds, before background task flush. Skipped for validation failures,
// [DoNotSaveChanges] operations, and a query run nested (a self query).
```

### Pipeline execution order

The generated pipeline executes consumer seams in this order:

1. **Authentication** → identity check
2. **Property population** → `IPropertyPopulator` (e.g. set TenantId from JWT)
3. **Authorization** → `[Resource]`, `IApiAuthoriser`
4. **Validation** → data annotation + `IValidator` checks
5. **Handler execution** → `HandleAsync()`
6. **Unit of work** → `IUnitOfWork.SaveChangesAsync()` (any operation without `[DoNotSaveChanges]`, on success)
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

A command returning a legacy `Blueprint.Http.ResourceEvent<T>` (or `ResourceCreated<T>` and its siblings) has
the `Expandable<T>` properties of its data expanded too, as legacy did: the paths are rooted at the event
(`?expand=Data.Department`), and the expander receives the event itself as its `response`. Data the event's
`SelfQuery` loaded is expanded only by that query's own pipeline, as legacy expanded before resolving it.

An expander that cannot expand because the upstream system failed (for example an authentication
error calling Microsoft Graph) leaves `Value` null and sets `Expandable<T>.ExpandError` to a reason
such as `"authentication_error"`. It is serialised as `expandError` and written as `null` when unset,
the same wire shape as Signature365's legacy `Expandable<T>`.

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