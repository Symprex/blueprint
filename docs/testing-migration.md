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
public class TemplateApiTests : IAsyncLifetime
{
    private BlueprintTestHost<PortalApp, PortalAppExecutor> _host = null!;

    public Task InitializeAsync()
    {
        _host = new BlueprintTestHostBuilder<PortalApp, PortalAppExecutor>()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IUnitOfWork, InMemoryUnitOfWork>();
                services.AddSingleton<IDocumentStore>(TestMartenStore.Create());
            })
            .Build();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _host.DisposeAsync().AsTask();

    [Fact]
    public async Task Can_create_template()
    {
        var result = await _host.ExecuteAsync(
            new CreateTemplateCommand { Name = "Test" },
            ctx => ctx
                .WithTenant("tenant-1")
                .WithUser("admin", roles: ["TenantAdmin"]));

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
// Tenant admin
ctx => ctx
    .WithTenant("tenant-1")
    .WithUser("admin", roles: ["TenantAdmin"])
    .WithPermissions("Template.Read", "Template.Write", "Template.Manage")

// Partner
ctx => ctx
    .WithUser("partner-user", claims: [("partner_id", "partner-1")])
    .WithPermissions("Partner.Read")

// Internal user
ctx => ctx
    .WithUser("internal-user", roles: ["Internal"])
    .WithPermissions("*")  // all permissions

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
// All operations use the same method — the pipeline determines behavior
var result = await _host.ExecuteAsync(new CreateTemplateCommand { Name = "Test" }, authContext);
var template = await _host.ExecuteAsync(new GetTemplateQuery { Id = "1" }, authContext);
var list = await _host.ExecuteAsync(new ListTemplatesQuery { Page = 1 }, authContext);
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
var collector = _host.Services.GetRequiredService<BackgroundTaskCollector>();
collector.EnqueuedTasks.Should().ContainSingle()
    .Which.Should().BeOfType<SendNotificationTask>();
```

### Step 6: Migrate Message Bus Assertions

```csharp
// ─── BEFORE (S365) ─────────────────────────────────────────────
var fakeBus = fixture.GetService<FakeBus>();
fakeBus.PublishedMessages.Should().ContainSingle()
    .Which.Should().BeOfType<TemplateCreatedEvent>();

// ─── AFTER (Blueprint) ────────────────────────────────────────
// If using Blueprint.Rebus with test doubles:
var fakeBus = _host.Services.GetRequiredService<IBus>() as FakeBus;
fakeBus!.PublishedMessages.Should().ContainSingle()
    .Which.Should().BeOfType<TemplateCreatedEvent>();

// Or via domain event publisher mock:
var publisher = _host.Services.GetRequiredService<IDomainEventPublisher>() as TestDomainEventPublisher;
publisher!.PublishedEvents.Should().ContainSingle()
    .Which.Should().BeOfType<TemplateCreatedEvent>();
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
- [ ] Replace `FakeBus` with test double registration
- [ ] Replace `BackgroundTaskCollector` usage (same concept, different access path)
- [ ] Verify Marten test session isolation approach
- [ ] Verify OpenAPI snapshot tests still match (if applicable)
- [ ] Run all tests in the project and confirm green
