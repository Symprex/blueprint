# Testing Migration Guide

This document maps Signature 365's testing patterns to Blueprint.Testing equivalents, providing concrete migration steps for the 42 test projects in S365.

---

## Test Infrastructure Comparison

### S365 Current Stack

| Component | S365 Implementation | Purpose |
|---|---|---|
| `WebApiFixtureFactory<T>` | Creates `ApiFixture` instances | Test host factory |
| `ApiFixture` | Wraps `TestServer` + auth + helpers | Per-test executor |
| `FakeBus` | In-memory Rebus substitute | Message capture |
| `BackgroundTaskCollector` | Collects enqueued tasks | Task assertions |
| `AuthenticateAsTenantAdministrator()` | Sets JWT with tenant claims | Auth helpers |
| `CreateTenantUser()` | Generates identity with claims | User creation |
| Marten test sessions | Per-test document stores | Data isolation |

### Blueprint.Testing Stack

| Component | Blueprint Implementation | Purpose |
|---|---|---|
| `BlueprintTestHostBuilder<TApp, TExec>` | Fluent builder for test host | Test host factory |
| `BlueprintTestHost<TApp, TExec>` | In-memory operation executor | Per-test executor |
| `BlueprintHttpTestHost<TApp, TExec>` | Full HTTP stack with TestServer | HTTP-level testing |
| `BackgroundTaskCollector` | Same concept, captures tasks | Task assertions |
| `NullRecurringTaskProvider` | No-op for recurring tasks | Test double |
| `TestAuthenticationManager` | Scoped auth lifecycle | Auth management |
| `BlueprintTestContext` | Per-execution config | Auth/tenant/setup |
| `ResultAssertionExtensions` | Fluent result assertions | Assertion helpers |

---

## Migration Steps Per Test Project

### Step 1: Update Test Host Setup

```csharp
// ─── BEFORE (S365) ─────────────────────────────────────────────
public class TemplateApiTests : IClassFixture<WebApiFixtureFactory<Startup>>
{
    private readonly WebApiFixtureFactory<Startup> _factory;
    
    public TemplateApiTests(WebApiFixtureFactory<Startup> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Can_create_template()
    {
        var fixture = _factory.CreateFixture();
        fixture.AuthenticateAsTenantAdministrator("tenant-1");
        
        var result = await fixture.CommandAsync(new CreateTemplateCommand { Name = "Test" });
        result.Should().NotBeNull();
    }
}

// ─── AFTER (Blueprint) ────────────────────────────────────────
// Option A: Factory pattern (RECOMMENDED — mirrors S365 shape, minimal churn)
// Create one per-app factory class:
public class TemplateTestFixtureFactory
    : BlueprintTestFixtureFactory<TemplateApp, TemplateAppExecutor>
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IUnitOfWork, InMemoryUnitOfWork>();
        services.AddSingleton<IDocumentStore>(TestMartenStore.Create());
    }
}

// Tests keep the same shape as S365:
public class TemplateApiTests : IClassFixture<TemplateTestFixtureFactory>
{
    private readonly BlueprintTestFixture<TemplateApp, TemplateAppExecutor> _fixture;

    public TemplateApiTests(TemplateTestFixtureFactory factory)
    {
        _fixture = factory.Create();
    }

    [Fact]
    public async Task Can_create_template()
    {
        _fixture.Authentication.Set(TestIdentities.TenantAdmin("tenant-1"));

        var result = await _fixture.CommandAsync(
            new CreateTemplateCommand { Name = "Test" });

        result.ShouldBeCreated<TemplateResource>();
    }
}
```

### Step 2: Migrate Auth Helpers

```csharp
// ─── BEFORE (S365) ─────────────────────────────────────────────
fixture.AuthenticateAsTenantAdministrator("tenant-1");
fixture.AuthenticateAsPartner("partner-1");
fixture.AuthenticateAsInternalUser();

// ─── AFTER (Blueprint) ────────────────────────────────────────
// Tenant admin — scope pairs match S365's ScopeApiAuthoriser model
ctx => ctx.WithTenantScopePairs("tenant-1", "Template.Read", "Template.Create", "Template.Delete")

// Partner — custom claims
ctx => ctx.WithUser(claims =>
{
    claims.Add(new Claim("sub", "partner-user"));
    claims.Add(new Claim("sig365_partner_id", "partner-1"));
    claims.Add(new Claim("scope", "Partner.Read"));
})

// Internal user
ctx => ctx.WithUser(claims =>
{
    claims.Add(new Claim("sub", "internal-user"));
    claims.Add(new Claim("sig365_role_group", "Internal"));
    claims.Add(new Claim("scope", "Signature365Admin.ReadWrite.All"));
})

// Machine user
ctx => ctx.WithScopePairs("api:read", "api:write")

// Skip auth entirely (focus on handler logic)
ctx => ctx.SkipAuthorisation()

// Unauthenticated (default — no context setup needed)
await _host.ExecuteAsync(query);  // no context = anonymous
```

### Step 3: Migrate Command/Query Execution

```csharp
// ─── BEFORE (S365) ─────────────────────────────────────────────
var result = await fixture.CommandAsync(new CreateTemplateCommand { Name = "Test" });
var template = await fixture.QueryAsync(new GetTemplateQuery { Id = "1" });
var list = await fixture.QueryAsync(new ListTemplatesQuery { Page = 1 });

// ─── AFTER (Blueprint) ────────────────────────────────────────
// QueryAsync and CommandAsync are available directly on the fixture:
var result = await _fixture.CommandAsync(new CreateTemplateCommand { Name = "Test" });
var template = await _fixture.QueryAsync(new GetTemplateQuery { Id = "1" });
var list = await _fixture.QueryAsync(new ListTemplatesQuery { Page = 1 });
```

### Step 4: Migrate Result Assertions

```csharp
// ─── BEFORE (S365) ─────────────────────────────────────────────
result.Should().NotBeNull();
result.Should().BeOfType<TemplateResource>();
Assert.Equal("Test", result.Name);

// Validation
Assert.Throws<ValidationException>(() => ...);

// Not found
result.Should().BeNull();  // or check for 404

// ─── AFTER (Blueprint) ────────────────────────────────────────
// Success
var resource = result.ShouldBeOk<TemplateResource>();
resource.Name.Should().Be("Test");

// Created
var created = result.ShouldBeCreated<TemplateResource>();

// Validation failure
result.ShouldBeValidationFailed()
    .WithError("Name", "Name is required");

// Not found
result.ShouldBeNotFound();

// Unauthorized
result.ShouldBeUnauthorized();

// Forbidden
result.ShouldBeForbidden();
```

### Step 5: Migrate Background Task Assertions

```csharp
// ─── BEFORE (S365) ─────────────────────────────────────────────
var tasks = fixture.GetEnqueuedBackgroundTasks();
tasks.Should().ContainSingle().Which.Should().BeOfType<SendNotificationTask>();

// ─── AFTER (Blueprint) ────────────────────────────────────────
// Accessed directly from the fixture:
_fixture.Tasks.GetSingleEnqueued<SendNotificationTask>()
    .Should().NotBeNull();
```

### Step 6: Migrate Message Bus Assertions

```csharp
// ─── BEFORE (S365) ─────────────────────────────────────────────
var fakeBus = fixture.GetService<FakeBus>();
fakeBus.PublishedMessages.Should().ContainSingle()
    .Which.Should().BeOfType<TemplateCreatedEvent>();

// ─── AFTER (Blueprint) ────────────────────────────────────────
// Direct access via fixture.Bus (S365-compatible alias for Messages):
var createdEvent = _fixture.Bus!.HavePublishedSingle<TemplateCreatedEvent>();
createdEvent.TemplateId.Should().NotBeEmpty();

// Or using the Messages property directly:
_fixture.Messages!.GetSinglePublished<TemplateCreatedEvent>()
    .TemplateId.Should().NotBeEmpty();

// Assert nothing was published:
_fixture.Bus!.NotHavePublished<TemplateDeletedEvent>();
```

---

## HTTP-Level Tests

For tests that need to verify HTTP routing, status codes, content negotiation, or middleware:

```csharp
public class TemplateHttpTests : IAsyncLifetime
{
    private BlueprintHttpTestHost<PortalApp, PortalAppExecutor> _httpHost = null!;

    public Task InitializeAsync()
    {
        _httpHost = new BlueprintHttpTestHostBuilder<PortalApp, PortalAppExecutor>()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IUnitOfWork, InMemoryUnitOfWork>();
            })
            .ConfigureAuthentication(auth =>
            {
                auth.AddScheme<TestAuthHandler>("Test", null);
            })
            .Build();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _httpHost.DisposeAsync().AsTask();

    [Fact]
    public async Task GET_templates_returns_200_with_list()
    {
        var client = _httpHost.CreateClient();
        client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Test", "tenant-admin:tenant-1");

        var response = await client.GetAsync("/api/templates");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        
        var body = await response.Content.ReadFromJsonAsync<ListApiResource<TemplateResource>>();
        body!.Values.Should().NotBeEmpty();
    }

    [Fact]
    public async Task POST_template_with_invalid_body_returns_422()
    {
        var client = _httpHost.CreateClient();
        client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Test", "tenant-admin:tenant-1");

        var response = await client.PostAsJsonAsync("/api/templates", new { });
        
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
```

---

## Test Data Isolation

### Marten Test Sessions

```csharp
// Create isolated test store
public static class TestMartenStore
{
    public static IDocumentStore Create()
    {
        return DocumentStore.For(opts =>
        {
            opts.Connection("Host=localhost;Database=test_" + Guid.NewGuid().ToString("N")[..8]);
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            // Apply same schema as production
            opts.Schema.For<Template>();
        });
    }
}

// Use in test
_host = new BlueprintTestHostBuilder<PortalApp, PortalAppExecutor>()
    .ConfigureServices(s =>
    {
        var store = TestMartenStore.Create();
        s.AddSingleton(store);
        s.AddScoped(sp => sp.GetRequiredService<IDocumentStore>().LightweightSession());
    })
    .Build();
```

---

## S365-Specific Test Patterns

### Authentication Helper Migration

S365 uses an `Authentication` class on `ApiFixture` with `Set()` and scoped `As()` methods:

```csharp
// ─── BEFORE (S365) ─────────────────────────────────────────────
// Set for entire fixture
api.Authentication.Set(new ClaimsIdentity(
    new[]
    {
        new Claim("sub", "user-1"),
        new Claim("sig365_tenant_id", "tenant-1"),
        new Claim("sig365_assigned_scope_pair", "Signature.Read"),
    },
    "Bearer"));

// Scoped (reverts on dispose)
using (api.Authentication.As(tenantAdminIdentity))
{
    var result = await api.QueryAsync(new GetSignaturesQuery());
    // ...
}

// ─── AFTER (Blueprint) ────────────────────────────────────────
// Ambient auth on the fixture — matches S365 exactly:
_fixture.Authentication.Set(new ClaimsIdentity(
    new[]
    {
        new Claim("sub", "user-1"),
        new Claim("sig365_tenant_id", "tenant-1"),
        new Claim("sig365_assigned_scope_pair", "Signature.Read"),
    },
    "Bearer"));

// Scoped (reverts on dispose) — same API as S365:
using (_fixture.Authentication.As(tenantAdminIdentity))
{
    var result = await _fixture.QueryAsync(new GetSignaturesQuery());
    // ...
}

// Or per-call context:
var result = await _fixture.ExecuteAsync(
    new GetSignaturesQuery(),
    ctx => ctx.WithUser(new ClaimsIdentity(
        new[]
        {
            new Claim("sub", "user-1"),
            new Claim("sig365_tenant_id", "tenant-1"),
        },
        "Bearer")));
```

### FakeBus Assertion Migration

S365 uses custom FluentAssertions extensions on `FakeBus`:

```csharp
// ─── BEFORE (S365) ─────────────────────────────────────────────
api.Bus.Should().HavePublishedSingle<UserCreatedEvent>();
api.Bus.Should().HavePublished<SignatureUpdatedEvent>();
api.Bus.Should().NotHavePublished<TenantDeletedEvent>();
api.Bus.Should().HaveSent<ProcessImportTask>();

// ─── AFTER (Blueprint) ────────────────────────────────────────
// The fixture.Bus property is an alias for fixture.Messages with
// S365-compatible assertion method names:
var evt = fixture.Bus!.HavePublishedSingle<UserCreatedEvent>();
var events = fixture.Bus!.HavePublished<SignatureUpdatedEvent>();
fixture.Bus!.NotHavePublished<TenantDeletedEvent>();
var sent = fixture.Bus!.HaveSent<ProcessImportTask>();

// Note: S365 uses FluentAssertions `.Should().HavePublishedSingle<T>()`.
// Blueprint uses direct method calls: `fixture.Bus!.HavePublishedSingle<T>()`.
// The migration is a regex-replaceable transformation:
//   api.Bus.Should().HavePublishedSingle  →  fixture.Bus!.HavePublishedSingle
//   api.Bus.Should().NotHavePublished     →  fixture.Bus!.NotHavePublished
```

### Background Task Assertion Migration

Both frameworks use `BackgroundTaskCollector` with the same API:

```csharp
// ─── BEFORE (S365) ─────────────────────────────────────────────
var tasks = api.Tasks.Enqueued.OfType<SendWelcomeEmailTask>();
tasks.Should().HaveCount(1);
tasks.First().UserId.Should().Be("user-1");

// ─── AFTER (Blueprint) ────────────────────────────────────────
// Access directly from fixture.Tasks property:
var task = _fixture.Tasks!.GetSingleEnqueued<SendWelcomeEmailTask>();
task.UserId.Should().Be("user-1");

// Or iterate all:
var tasks = _fixture.Tasks!.GetAllEnqueued<SendWelcomeEmailTask>();
tasks.Should().HaveCount(1);
```

### Authorization Test Fixture Migration

S365 has a sophisticated `OperationAuthTestFixture<T>` pattern with declarative expectations:

```csharp
// ─── BEFORE (S365) ─────────────────────────────────────────────
public class CreateUserAuthTest : OperationAuthTestFixture<CreateUserCommand>
{
    public CreateUserAuthTest(PortalApiFixtureFactory factory) : base(factory)
    {
        this.ExpectTenantUserAllowed(S365Roles.TenantAdministrator);
        this.ExpectTenantUserAllowed(S365Roles.TenantUsersManage);
        // Remaining roles are implicitly expected to be denied
    }
}

// ─── AFTER (Blueprint) ────────────────────────────────────────
// Blueprint provides compile-time scope pair validation, so the
// declarative authorization test pattern is less critical.
// Integration tests that verify 403 responses are recommended:
[Fact]
public async Task Unauthorized_role_returns_403()
{
    var result = await host.ExecuteAsync(
        new CreateUserCommand { Name = "Test" },
        ctx => ctx.WithUser(CreateIdentity(role: "Reader")));
    result.Should().BeOfType<ForbiddenResult>();
}

[Fact]
public async Task Admin_role_succeeds()
{
    var result = await host.ExecuteAsync(
        new CreateUserCommand { Name = "Test" },
        ctx => ctx.WithUser(CreateIdentity(role: "TenantAdministrator")));
    result.Should().BeOfType<ResourceEventResult<UserApiResource>>();
}
```

### Database Extension Migration

S365 uses `ApiFixtureDbExtensions` for tenant-scoped Marten queries:

```csharp
// ─── BEFORE (S365) ─────────────────────────────────────────────
var user = await api.LoadFromDatabaseAsync<User>(tenantId, userId);
user.Should().NotBeNull();

await api.WithDatabaseAsync(tenantId, async session =>
{
    var all = await session.Query<Template>().ToListAsync();
    all.Should().HaveCount(5);
});

// ─── AFTER (Blueprint) ────────────────────────────────────────
// Access Marten directly from the DI container:
using var scope = host.Services.CreateScope();
var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
var user = await session.LoadAsync<User>(userId);
user.Should().NotBeNull();
```

### Shared Fixture for Performance

```csharp
// For test classes that share expensive setup
public class SharedApiFixture : IAsyncLifetime
{
    public BlueprintTestHost<PortalApp, PortalAppExecutor> Host { get; private set; } = null!;
    public IDocumentStore Store { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Store = TestMartenStore.Create();
        Host = new BlueprintTestHostBuilder<PortalApp, PortalAppExecutor>()
            .ConfigureServices(s =>
            {
                s.AddSingleton(Store);
                s.AddScoped(sp => sp.GetRequiredService<IDocumentStore>().LightweightSession());
                s.AddSingleton<IUnitOfWork, MartenUnitOfWork>();
            })
            .Build();
        
        // Seed test data
        await using var session = Store.LightweightSession();
        session.Store(new Template { Id = "seed-1", Name = "Seeded Template" });
        await session.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
        Store.Dispose();
    }
}

[CollectionDefinition("Portal API")]
public class PortalApiCollection : ICollectionFixture<SharedApiFixture> { }

[Collection("Portal API")]
public class TemplateQueryTests
{
    private readonly SharedApiFixture _fixture;
    public TemplateQueryTests(SharedApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Can_get_seeded_template()
    {
        var result = await _fixture.Host.ExecuteAsync(
            new GetTemplateQuery { Id = "seed-1" },
            ctx => ctx.SkipAuthorisation());
        result.ShouldBeOk<TemplateResource>().Name.Should().Be("Seeded Template");
    }
}
```

---

## Checklist Per Test Project

For each of the 42 S365 test projects:

- [ ] Replace `WebApiFixtureFactory<T>` with `BlueprintTestHostBuilder<TApp, TExec>`
- [ ] Replace `fixture.CommandAsync` / `fixture.QueryAsync` with `host.ExecuteAsync`
- [ ] Replace auth helpers with `BlueprintTestContext` fluent API
- [ ] Replace result assertions with `ResultAssertionExtensions`
- [ ] Replace `FakeBus` with `TestMessageCollector` registration
- [ ] Replace `BackgroundTaskCollector` usage (same concept, different access path)
- [ ] Replace Marten UoW with `InMemoryUnitOfWork` for pure logic tests
- [ ] Verify Marten test session isolation approach for persistence tests
- [ ] Verify OpenAPI snapshot tests still match (if applicable)
- [ ] Run all tests in the project and confirm green

---

## Built-in Test Doubles

Blueprint.Testing ships with several test doubles that replace real infrastructure in tests:

### InMemoryUnitOfWork

Replaces `MartenUnitOfWork` (or any `IUnitOfWork`) to verify the generated pipeline calls
`SaveChangesAsync` at the right point without needing a real database.

```csharp
var uow = new InMemoryUnitOfWork();

var host = new BlueprintTestHostBuilder<PortalApp, PortalAppExecutor>()
    .ConfigureServices(services =>
    {
        services.AddSingleton<IUnitOfWork>(uow);
    })
    .Build();

await host.ExecuteAsync(new CreateTemplateCommand { Name = "Test" },
    ctx => ctx.SkipAuthorisation());

uow.WasSaved.Should().BeTrue();
uow.SaveCount.Should().Be(1);

// Reset between tests in shared fixtures
uow.Reset();
```

### TestMessageCollector

Captures published and sent messages during operation execution, replacing `FakeBus`.
The collector is bus-agnostic — it works whether the handler uses Rebus, MassTransit, or
a custom message abstraction.

```csharp
var messages = new TestMessageCollector();

// Register as a singleton so handlers can resolve it
var host = new BlueprintTestHostBuilder<PortalApp, PortalAppExecutor>()
    .ConfigureServices(services =>
    {
        services.AddSingleton(messages);
        // Replace real IBus with a thin wrapper that calls messages.Publish<T>()
    })
    .Build();

await host.ExecuteAsync(new CreateTemplateCommand { Name = "Test" },
    ctx => ctx.SkipAuthorisation());

// Assert a specific event was published
var evt = messages.GetSinglePublished<TemplateCreatedEvent>();
evt.TemplateId.Should().Be("tmpl-1");

// Assert nothing was sent point-to-point
messages.AssertNoneSent<SendEmailCommand>();

// Clear between tests
messages.ClearAll();
```

### BackgroundTaskCollector

Captures enqueued background tasks without dispatching them to Hangfire:

```csharp
// Already registered by default in BlueprintTestHostBuilder
var host = new BlueprintTestHostBuilder<PortalApp, PortalAppExecutor>()
    .Build();

await host.ExecuteAsync(new CreateTemplateCommand { Name = "Test" },
    ctx => ctx.SkipAuthorisation());

var task = host.Services
    .GetRequiredService<IBackgroundTaskScheduleProvider>()
    .As<BackgroundTaskCollector>()
    .GetSingleEnqueued<SendNotificationTask>();

task.TemplateId.Should().Be("tmpl-1");
```

### NullRecurringTaskProvider

A no-op `IRecurringTaskProvider` that prevents recurring task registration from
attempting to connect to Hangfire during tests. Registered automatically.

---

## BlueprintTestFixture API Reference

`BlueprintTestFixture<TApp, TExec>` wraps `BlueprintTestHost` and provides the
high-level API that mirrors S365's `ApiFixture` pattern:

### Properties

| Property | Type | Description |
|---|---|---|
| `Authentication` | `TestAuthenticationManager` | Ambient auth — `.Set()`, `.As()`, `.MakeAnonymous()` |
| `Tasks` | `BackgroundTaskCollector?` | Captured background tasks (null if not registered) |
| `Messages` | `TestMessageCollector?` | Captured published/sent messages |
| `UnitOfWork` | `InMemoryUnitOfWork?` | Unit of work test double |
| `Services` | `IServiceProvider` | Root DI container |

### Execution Methods

| Method | Description |
|---|---|
| `QueryAsync<T>(IQuery<T>, configure?)` | Execute query, unwrap to `T` |
| `CommandAsync<T>(ICommand<T>, configure?)` | Execute command, unwrap to `T` |
| `ExecuteAsync(IOperation, configure?)` | Execute any operation, return raw `OperationResult` |
| `BackgroundTaskAsync(IBackgroundTask, configure?)` | Execute task (no ambient auth applied) |
| `MessageAsync(IOperation, configure?, retries?)` | Execute message handler with optional retry |

### Service Resolution

| Method | Description |
|---|---|
| `GetRequiredService<T>()` | Resolve service from DI |
| `GetOptions<T>()` | Resolve `IOptions<T>.Value` |
| `GetServiceAs<TService, TImpl>()` | Resolve interface, cast to impl (for test doubles) |

### Lifecycle

| Method | Description |
|---|---|
| `ClearCollectors()` | Reset all test doubles (tasks, messages, UoW) |
| `DisposeAsync()` | Clean up host and DI container |

---

## Quick Reference: S365 → Blueprint API Mapping

| S365 (`ApiFixture`) | Blueprint (`BlueprintTestFixture`) | Churn |
|---|---|---|
| `fixture.CommandAsync(cmd)` | `fixture.CommandAsync(cmd)` | ✅ None |
| `fixture.QueryAsync(query)` | `fixture.QueryAsync(query)` | ✅ None |
| `fixture.BackgroundTaskAsync(task)` | `fixture.BackgroundTaskAsync(task)` | ✅ None |
| `fixture.MessageAsync(msg)` | `fixture.MessageAsync(msg)` | ✅ None |
| `fixture.Authentication.Set(identity)` | `fixture.Authentication.Set(identity)` | ✅ None |
| `fixture.Authentication.As(identity)` | `fixture.Authentication.As(identity)` | ✅ None |
| `fixture.Authentication.MakeAnonymous()` | `fixture.Authentication.MakeAnonymous()` | ✅ None |
| `fixture.GetRequiredService<T>()` | `fixture.GetRequiredService<T>()` | ✅ None |
| `fixture.GetOptions<T>()` | `fixture.GetOptions<T>()` | ✅ None |
| `fixture.Bus` (returns `FakeBus`) | `fixture.GetServiceAs<IBus, FakeBus>()` | ⚠️ Minor |
| `fixture.Tasks.Enqueued` | `fixture.Tasks.Enqueued` | ✅ None |
| `fixture.CreateClient()` | Use `BlueprintHttpTestHost` | 🔄 Separate |
| `fixture.Bus.Should().HavePublishedSingle<T>()` | `fakeBus.Events.OfType<MessagePublished<T>>()` | ⚠️ Minor |

> **Tip:** S365 can eliminate the `Bus` churn by adding a one-liner property
> to its derived fixture: `public FakeBus Bus => GetServiceAs<IBus, FakeBus>();`
