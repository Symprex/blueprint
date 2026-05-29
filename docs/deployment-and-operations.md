# Deployment and operations guide

This guide covers production deployment considerations for applications built with Blueprint's source-generated framework.

## Configuration

### BlueprintHttpOptions

Register via `IOptions<BlueprintHttpOptions>` in DI. Key settings:

| Setting | Default | Description |
|---------|---------|-------------|
| `ExposeExceptionDetailsInErrorResponses` | `false` | When `true`, 500 responses include exception messages and stack traces. **Must be `false` in production.** |
| `PublicHost` | `null` (uses request host) | Override the domain used for absolute link URLs. Set this behind reverse proxies. |

### Reverse proxy setup

When running behind nginx, IIS, or a cloud load balancer, configure:

```csharp
services.Configure<BlueprintHttpOptions>(options =>
{
    options.PublicHost = "https://api.example.com";
});

// Forward proxy headers so HttpContext.Request reflects the original client request
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});
```

### Exception detail control

**Development:**
```csharp
services.Configure<BlueprintHttpOptions>(options =>
{
    options.ExposeExceptionDetailsInErrorResponses = builder.Environment.IsDevelopment();
});
```

This is the only setting that controls whether exception messages and stack traces leak to clients. In production, all 500 responses return a generic `"An unexpected error occurred."` message via RFC 7807 `application/problem+json`.

## Error response surface

All error responses return RFC 7807 `application/problem+json` bodies. Clients can parse every error consistently:

| Status | Trigger | Body type |
|--------|---------|-----------|
| 400 | Binding failure (invalid route/query/header/cookie parameter) | `ProblemDetails` with failure reason |
| 401 | `[MustBeAuthenticated]` without authenticated identity | `ProblemDetails` |
| 403 | `[Resource]` authorization denied | `ProblemDetails` |
| 415 | Non-JSON content type on body-binding operation | `ProblemDetails` |
| 422 | DataAnnotations / IValidatableObject validation failure | `ValidationProblemDetails` with per-member errors |
| 500 | Unhandled exception | `ProblemDetails` (detail hidden unless `ExposeExceptionDetailsInErrorResponses`) |
| 4xx | `ApiException` with custom status | `ProblemDetails` with developer-authored message |

## Middleware ordering

Blueprint maps endpoints via `MapBlueprintApi<TApp, TExecutor>()`. Place it after standard ASP.NET Core middleware:

```csharp
var app = builder.Build();

// 1. Exception handling (outermost)
app.UseExceptionHandler("/error");

// 2. Forwarded headers (if behind proxy)
app.UseForwardedHeaders();

// 3. CORS (if browser clients access the API)
app.UseCors("MyPolicy");

// 4. Authentication
app.UseAuthentication();

// 5. Authorization (ASP.NET Core's built-in — Blueprint handles its own via pipeline)
app.UseAuthorization();

// 6. Blueprint endpoints
app.MapBlueprintApi<MyApp, MyAppExecutor>("api/");
```

### CORS

Blueprint does not include CORS middleware — use ASP.NET Core's built-in CORS:

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("MyPolicy", policy =>
    {
        policy.WithOrigins("https://app.example.com")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
```

### Rate limiting

Use ASP.NET Core's built-in rate limiting middleware:

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("api", config =>
    {
        config.Window = TimeSpan.FromMinutes(1);
        config.PermitLimit = 100;
    });
});

app.UseRateLimiter();
```

### Response compression

Enable Brotli/gzip compression for JSON responses:

```csharp
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);
});

app.UseResponseCompression();
```

## Observability

### OpenTelemetry integration

Blueprint pipelines emit `System.Diagnostics.Activity` spans via `BlueprintActivitySource`. Connect to OpenTelemetry:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(BlueprintActivitySource.Name)
            .AddAspNetCoreInstrumentation()
            .AddOtlpExporter();
    });
```

### Structured logging

Generated pipelines use `LoggerMessage.Define` for AOT-friendly structured logging. Configure Serilog or the built-in logger to capture Blueprint log categories:

```csharp
builder.Logging.AddFilter("Blueprint", LogLevel.Information);
```

### Health checks

Add a health check endpoint for load balancer probes. The sample app demonstrates this with a `PlainTextResult("healthy")` handler at `/system/health`.

## Performance tuning

### GC configuration

Blueprint's per-request allocation is ~264 bytes (context + result + operation instance). At
10K req/s this triggers Gen0 GC every ~2–6 seconds with <1ms pause under Server GC. For
maximum throughput:

```xml
<!-- In .csproj or runtimeconfig.json -->
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
  <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
</PropertyGroup>
```

For latency-sensitive workloads (p99 targets <5ms), consider DATAS (Dynamic Adaptation To
Application Sizes) which is the default in .NET 9+:

```json
{
  "configProperties": {
    "System.GC.DynamicAdaptationMode": 1
  }
}
```

### Kestrel configuration

Blueprint serializes responses to a buffer and writes with `Content-Length`, enabling
Kestrel's non-chunked fast path. For maximum throughput:

```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxConcurrentConnections = null; // unlimited
    options.Limits.MaxRequestBodySize = 10_485_760; // 10MB
    options.AddServerHeader = false; // saves ~20 bytes per response
});
```

### Thread pool

Blueprint pipelines are fully async with sync-completion fast paths. The default thread
pool settings work well. For high-concurrency bursts (>1000 concurrent):

```csharp
ThreadPool.SetMinThreads(workerThreads: Environment.ProcessorCount * 2, completionPortThreads: Environment.ProcessorCount);
```

### Response characteristics

| Metric | Value | Notes |
|--------|-------|-------|
| Per-request allocation | ~264 B | Context + result + operation |
| Pipeline execution | 231–493 ns | Depends on module count |
| JSON serialization | Buffer + sync write | No chunked encoding overhead |
| Framework overhead vs raw | −32% to −37% | Blueprint is faster than minimal API |
| p99 @ 200 concurrent | <1 ms | No lock contention |

## Containerization

### Docker

Blueprint apps are standard ASP.NET Core applications. Use the standard multi-stage Dockerfile:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "MyApp.dll"]
```

### Kubernetes readiness/liveness

Point readiness and liveness probes at the health check endpoint:

```yaml
readinessProbe:
  httpGet:
    path: /api/system/health
    port: 8080
  initialDelaySeconds: 5
  periodSeconds: 10
livenessProbe:
  httpGet:
    path: /api/system/health
    port: 8080
  initialDelaySeconds: 15
  periodSeconds: 30
```

## Development inspection endpoints

When `BlueprintHttpOptions.OperationsManifestResourceName` is configured, Blueprint exposes development-only inspection endpoints:

| Path | Content | Purpose |
|------|---------|---------|
| `/.blueprint/manifests/operations.json` | Canonical manifest JSON | Machine-readable operation registry |
| `/.blueprint/manifests/operations.html` | Interactive HTML page | Visual inspection of registered operations |
| `/.blueprint/manifests/operations.md` | Markdown projection | Documentation-friendly operation list |
| `/.blueprint/openapi.json` | OpenAPI 3.0 document | API contract for client code generation |

These endpoints are always available (not gated to development mode). Consider restricting access via reverse proxy rules or middleware in production if the API surface should not be publicly discoverable.

## Production checklist

- [ ] `ExposeExceptionDetailsInErrorResponses` is `false`
- [ ] `PublicHost` is set if behind a reverse proxy
- [ ] CORS configured for allowed origins
- [ ] Rate limiting configured
- [ ] Response compression enabled
- [ ] OpenTelemetry tracing connected
- [ ] Health check endpoint configured for load balancer
- [ ] Forwarded headers middleware in place (if behind proxy)
- [ ] Kestrel request size limits reviewed (default 30MB)
- [ ] JSON serializer source generators registered (AOT-friendly)
