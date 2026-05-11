# OpenAPI Migration Guide

This document covers how Blueprint source-gen renders OpenAPI documents, differences from the legacy Blueprint OpenAPI output, and how to ensure TypeScript client generation produces equivalent output after migration.

---

## OpenAPI Document Location

| Stack | Endpoint |
|---|---|
| Legacy Blueprint (S365) | `/api/openapi.json` or configured path |
| Blueprint Source-Gen | `/{basePath}/.blueprint/openapi.json` |

The new Blueprint pre-renders the OpenAPI document at startup and serves it as a static response, making it faster than runtime-generated documents.

---

## Key Differences

### 1. Operation IDs

| Aspect | Legacy Blueprint | Blueprint Source-Gen |
|---|---|---|
| Format | Class name | Class name (same) |
| Example | `GetTemplateQuery` | `GetTemplateQuery` |

Operation IDs are derived from the operation class name in both versions. No change expected.

### 2. Path Conventions

| Aspect | Legacy Blueprint | Blueprint Source-Gen |
|---|---|---|
| Base path | Configurable | `basePath` parameter |
| Route source | `[Link]`/`[SelfLink]` URLs | Same attributes |
| Parameter style | `{Id}` | `{Id}` (same) |

Routes come from the same `[Link]`/`[SelfLink]` attributes, so paths should be identical.

### 3. HTTP Methods

| Aspect | Legacy Blueprint | Blueprint Source-Gen |
|---|---|---|
| Method source | `[HttpGet]`/`[HttpPost]` etc. | Same attributes |
| Default | GET for queries | GET for queries (same) |

### 4. Response Status Codes

This is the most likely area of difference.

| Scenario | Legacy Blueprint | Blueprint Source-Gen |
|---|---|---|
| Success (query) | 200 | 200 |
| Success (command) | 200 | 200 (or 201 for `CreatedResult`) |
| Validation failure | 422 | 422 |
| Not found | 404 | 404 |
| Unauthorized | 401 | 401 |
| Forbidden | 403 | 403 |
| Server error | 500 | 500 |

**Potential difference:** If S365 operations return `StatusCodeResult.NoContent` (204) or `StatusCodeResult.Accepted` (202), the new Blueprint may document these differently. Check each operation's result type.

### 5. Request/Response Schemas

| Aspect | Legacy Blueprint | Blueprint Source-Gen |
|---|---|---|
| Schema source | Reflection at startup | Source-gen metadata |
| Property names | camelCase (Newtonsoft) | camelCase (STJ) |
| Nullable handling | Optional in Newtonsoft | Explicit in STJ |
| Enum format | String (StringEnumConverter) | String (JsonStringEnumConverter) |
| Date format | ISO 8601 | ISO 8601 (same) |
| NodaTime types | Custom converter shapes | May differ with STJ converter |

**Key risk:** NodaTime serialization shape may differ between Newtonsoft and STJ converters. Test with actual payloads.

### 6. Security Schemes

| Aspect | Legacy Blueprint | Blueprint Source-Gen |
|---|---|---|
| Scheme type | Bearer (JWT) | Bearer (JWT) — configurable |
| Location | Header `Authorization` | Header `Authorization` |
| OpenAPI definition | Via `AddOpenApi()` config | Via `configureOpenApi` parameter |

S365 uses IdentityServer4 for OAuth2. The OpenAPI security scheme should be configured in the `configureOpenApi` callback:

```csharp
app.MapBlueprintApi<MyApp, MyAppExecutor>(
    basePath: "/api/",
    configureOpenApi: options =>
    {
        options.Title = "Templates API";
        options.Version = "1.0";
        // Security scheme configuration is in OpenApiSchemaProcessorOptions
    },
    configureOpenApiSchemas: schemas =>
    {
        // Register schema processors for custom types
    },
    configureOpenApiOperations: operations =>
    {
        // Per-operation processors (e.g., remove TenantId from public spec)
    });
```

---

## OpenAPI Comparison Process

### Step 1: Capture Baseline

Before migrating a service, capture its current OpenAPI output:

```bash
# Start the current S365 service
cd D:\Projects\Signature365
dotnet run --project src/Services/Templates/Symprex.Signature365.Templates.Api

# Capture the OpenAPI document
curl http://localhost:5000/api/openapi.json | python -m json.tool > openapi-baseline.json
```

### Step 2: Capture Candidate

After migrating the service:

```bash
# Start the migrated service
dotnet run --project src/Services/Templates/Symprex.Signature365.Templates.Api

# Capture the new OpenAPI document
curl http://localhost:5001/api/.blueprint/openapi.json | python -m json.tool > openapi-candidate.json
```

### Step 3: Diff

Use `oasdiff` for structured comparison:

```bash
# Install oasdiff
go install github.com/tufin/oasdiff@latest

# Breaking changes only (most critical)
oasdiff breaking openapi-baseline.json openapi-candidate.json

# Full diff
oasdiff diff openapi-baseline.json openapi-candidate.json

# Or use simple JSON diff for manual review
diff <(jq -S . openapi-baseline.json) <(jq -S . openapi-candidate.json)
```

### Step 4: TypeScript Client Generation

```bash
# Generate TypeScript client from baseline
npx nswag openapi2tsclient /input:openapi-baseline.json /output:client-baseline.ts \
    /template:Fetch /generateClientClasses:true /operationGenerationMode:MultipleClientsFromOperationId

# Generate TypeScript client from candidate
npx nswag openapi2tsclient /input:openapi-candidate.json /output:client-candidate.ts \
    /template:Fetch /generateClientClasses:true /operationGenerationMode:MultipleClientsFromOperationId

# Diff the generated clients
diff client-baseline.ts client-candidate.ts
```

### Hard Gate Criteria

A service migration is **blocked** if:

1. **Breaking path changes** — Any path that existed in baseline is missing or renamed
2. **Breaking schema changes** — Required properties added, types changed, enums narrowed
3. **Breaking status code changes** — Success status codes changed (200→201 etc.)
4. **TypeScript client incompatible** — Generated client has different method signatures

A service migration is **acceptable** with documentation if:

1. **Additive changes** — New paths, new optional properties, new status codes
2. **Description changes** — Different descriptions but same structure
3. **Nullable changes** — Properties that were ambiguously nullable become explicitly nullable
4. **Operation ID casing** — Same name but different casing (configure NSwag accordingly)

---

## Common Issues and Solutions

### Issue: NodaTime Schema Shape

NodaTime types (LocalDate, Instant, etc.) may serialize differently:

```json
// Newtonsoft (legacy)
{ "createdAt": "2024-01-15T10:30:00Z" }

// STJ with NodaTime.Serialization.SystemTextJson
{ "createdAt": "2024-01-15T10:30:00Z" }  // Same format, different internal handling
```

**Solution:** Use `NodaTime.Serialization.SystemTextJson` and verify JSON output matches.

### Issue: Enum Serialization

```json
// Newtonsoft with StringEnumConverter
{ "status": "Active" }

// STJ with JsonStringEnumConverter  
{ "status": "Active" }  // Same if configured correctly
```

**Solution:** Ensure `JsonStringEnumConverter` is registered in `JsonSerializerOptions`.

### Issue: Null Handling in OpenAPI Schema

Legacy Blueprint may have `"nullable": true` on properties that new Blueprint marks as `required`.

**Solution:** Use `configureOpenApiSchemas` to adjust nullable annotations:

```csharp
configureOpenApiSchemas: schemas =>
{
    schemas.AddProcessor(new NullablePropertyProcessor());
}
```

### Issue: TenantId in Path Parameters

S365 operations may expose `TenantId` as a path parameter in OpenAPI, but this is resolved from the JWT claim, not the URL.

**Solution:** Use `configureOpenApiOperations` to remove internal parameters:

```csharp
configureOpenApiOperations: operations =>
{
    operations.AddProcessor(new RemoveInternalParametersProcessor("TenantId", "PartnerId"));
}
```

---

## NSwag Configuration

S365 uses NSwag 14.2.0 for TypeScript client generation. Key configuration settings:

```json
{
  "runtime": "Net80",
  "documentGenerator": {
    "fromDocument": {
      "url": "http://localhost:5000/api/.blueprint/openapi.json"
    }
  },
  "codeGenerators": {
    "openApiToTypeScriptClient": {
      "className": "{controller}Client",
      "template": "Fetch",
      "operationGenerationMode": "MultipleClientsFromOperationId",
      "generateClientClasses": true,
      "generateOptionalParameters": true,
      "useTransformOptionsMethod": true,
      "dateTimeType": "string",
      "nullValue": "Undefined"
    }
  }
}
```

**Ensure these settings produce equivalent output before and after migration.**

---

## Per-Service OpenAPI Checklist

Before declaring a service migration "OpenAPI compatible":

- [ ] Captured baseline OpenAPI JSON from current service
- [ ] Generated candidate OpenAPI JSON from migrated service
- [ ] Ran `oasdiff breaking` with zero breaking changes
- [ ] Generated TypeScript client from both and diffed
- [ ] Client method signatures are identical (or changes are documented)
- [ ] Verified NodaTime serialization format matches
- [ ] Verified enum serialization format matches
- [ ] Verified nullable/required property alignment
- [ ] Internal parameters (TenantId) hidden from public spec
- [ ] Security scheme configured correctly
