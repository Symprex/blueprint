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
| Format | Class name with `Query`/`Command` stripped | Class name with `Query`/`Command` stripped (same) |
| Example | `GetTemplate` (from `GetTemplateQuery`) | `GetTemplate` (from `GetTemplateQuery`) |

Operation IDs are derived from the operation class name with trailing `Query` and `Command`
suffixes removed. This matches legacy Blueprint convention and ensures TypeScript client
generation compatibility. Operation types that do not end in `Query` or `Command` keep
their full name as the operationId.

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

S365 uses **custom Handlebars templates**, NOT NSwag. Run the S365 generator:

```bash
# From the relevant frontend app directory
cd D:\Projects\Signature365\frontend\apps\portal
pnpm run openapi

# This loads the OpenAPI spec from disk and generates api.ts
```

Verify the generated `api.ts` compiles and has equivalent method signatures:

```bash
npx tsc --noEmit src/openapi/generated/api.ts
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

### Issue: Schema Naming Convention

Blueprint source-gen uses fully-qualified type names with underscores as schema IDs by default.
For **generic types** (e.g., `ResourceEvent<TemplateApiResource>`), Blueprint now produces
human-readable `NameOfArgument` format automatically:

```
ResourceEventResultOfTemplate     (from ResourceEventResult<TemplateApiResource>)
ListApiResourceOfTemplate         (from ListApiResource<TemplateApiResource>)
PagedApiResourceOfTemplate        (from PagedApiResource<TemplateApiResource>)
```

Common suffixes (`ApiResource`, `Resource`, `DTO`, `Response`) are stripped from the generic
argument name automatically.

For **non-generic types**, the default format is `Namespace_Type_Name` (dots → underscores).
S365's custom TypeScript templates use `$ref` keys for type resolution, so the exact format
matters. Use `SchemaNameFormatter` to produce short names:

```csharp
app.MapBlueprintApi<MyApp, MyAppExecutor>(
    basePath: "/api/",
    configureOpenApi: options =>
    {
        options.Title = "Templates API";
        options.SchemaNameFormatter = clrName =>
        {
            // Use last segment only: "Symprex.S365.Api.TemplateApiResource" → "TemplateApiResource"
            var lastDot = clrName.LastIndexOf('.');
            return lastDot >= 0 ? clrName[(lastDot + 1)..] : clrName;
        };
    });
```

**Note:** For generic types, `FormatTypeName` is applied *before* `SanitiseSchemaKey`/`SchemaNameFormatter`,
so generic types already produce clean names regardless of custom formatter configuration.

### Legacy-compatible schema names

A consumer whose generated clients import types by the names legacy Blueprint gave them can reproduce those names:

```csharp
configureOpenApi: options =>
{
    options.SchemaNaming = OpenApiSchemaNaming.Legacy;

    // Signature365's own rule, as its legacy BlueprintSchemaNameGenerator applied it.
    options.SchemaNameFormatter = name => name.Replace("ApiResource", "").Replace("PagedOf", "Paged");
}
```

- `OpenApiSchemaNaming.Legacy` names types as NJsonSchema 11.1's `DefaultSchemaNameGenerator` did:
  `PagedApiResource<PartnerCompanyApiResource>` is `PagedApiResourceOfPartnerCompanyApiResource`, recursively for
  nested generics and joined with `And` for several arguments; `short`/`int`/`long` are `Short`/`Integer`/`Long`;
  `int?` is `NullableInteger`; an array is `X[]`; a nested type is its own simple name.
- Under it the formatter receives those names for every component — types, request bodies, ResourceEvent envelopes —
  never CLR type names, so the rule above yields `PagedPartnerCompany`, `PartnerCompany` and
  `ResourceEventOfPartnerCompany`.
- `[OpenApiSchemaName("ImportRun")]` (`Blueprint.Model.Attributes`) gives a type a name of its own, as legacy's
  `[JsonSchema("ImportRun")]` did.
- Under it every parameter's schema is titled, as legacy's root parameter schemas were, with the legacy name of the type
  a `Nullable<T>` wraps, passed through the formatter: an `ApiId` route parameter is `"ApiId"`, an `int?` query
  parameter `"Integer"`, a `string[]` one `"String[]"`.
- `options.QueryParameterNaming = OpenApiParameterNaming.PropertyName` names query parameters by their CLR properties
  (`Page`), as legacy did, instead of the camel-case name the framework's client sends (`page`). Generators that use a
  parameter's name as a method argument or query key need it; the host binds a query parameter whatever its case.
- `options.RequiredProperties = OpenApiRequiredProperties.NonNullable` marks required what legacy marked required. That
  is every property of a component or request body, and every query, header or cookie parameter, whose type is not
  nullable: a value type that is not `Nullable<T>`, or a reference type annotated as not nullable. It also marks every
  `[Required]` one, which it then does not document as nullable. The default marks required only what is declared so:
  a serializer-required member of a response type, a `[Required]` request-body property or parameter, and a route
  parameter. Generators that make an unrequired property optional (`?:`) need it to type properties as legacy clients
  did.
- `options.RequestBodyNaming = OpenApiRequestBodyNaming.OperationType` keys each request body by the operation's CLR
  type (`CreatePartnerCompanyCommand`), as legacy did, instead of the operation's name (`CreatePartnerCompany`).
  Generators that tell a command from a query by a `Command` suffix on the body's name need it.
- Types that end up with one key — two types of one simple name, or two a formatter maps together, such as
  `RuleMatchApiResource` and a `RuleMatch` of another namespace — each keep a component of their own, named as legacy
  named them: the first the document reaches keeps the name and each later one takes it with the first free counter
  from 2 (`RuleMatch2`). The document is read in the order it is written: path by path, a request body before the
  responses, a type before the types its properties reach. Legacy read it in the order its assembly declared the
  operations, which the document does not know, so where two operations reach a pair of such types in another order
  than legacy did, the two names are the other way about. And where legacy documented a response whose type took a
  name already in use as the schema already under it — a different type — this gives the response its own.
- Two components that still end up with one key, such as a request body and a type both named for one CLR type name,
  fail the render with an `InvalidOperationException` naming both, rather than producing a document in which one
  silently replaces the other.

Whatever the naming, an object nested in a body — a property, a collection's items, a dictionary's values, at any
depth — is a component of its own that the property references (wrapped in `oneOf`, beside `nullable` or
`description`, where it has either), as legacy made it. The graph is followed through the app's serializer context,
so a type that context has no metadata for (for example one used only in a request body the context does not
declare) is still an inline `object`; a type written by a converter, such as a strongly typed id, is not a
component. A request-body property whose type is an operation's own type needs no metadata: it references that
operation's request body, the one schema legacy made for the type (VCard's `PreviewVCardFieldsQuery.Body` references
`UpdateVCardFieldsCommand`). For any other undescribed type in a request body, `MapBlueprintApi` logs a warning
(category `Blueprint.OpenApi`) naming the operation, the property and the type to declare on the context. The warning
is also listed on `PreRenderedOpenApiDocument.Warnings`.

A type written by a converter is documented as legacy documented it, whatever the naming. One whose type converter
converts it to string, such as Signature365's `ApiId`, is `{"type": "string"}` as a property, a parameter or a request
body property, where it was an unknown `{}` or an `object`. A `byte[]` is `{"type": "string", "format": "byte"}`.
Legacy took both from Newtonsoft.Json's contracts. One with only a System.Text.Json converter is still `{}`, because
legacy described its CLR properties, which are not what the converter writes.

An operation whose handler returns a legacy `Blueprint.Http.ResourceEvent<T>` (or a subclass of it) documents its
success response as the `ResourceEventOf{T}` envelope component, since that is what the host writes, not `T` alone.
Whatever the event's change type — `ResourceCreated<T>` and `ResourceDeleted<T>` included — the host sends it as a 200 with
the envelope and no `Location`, as legacy did, and the document says 200.

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

## S365 Custom TypeScript Generator

> **S365 does NOT use NSwag.** It uses custom Handlebars templates in each frontend app.

### Generator Architecture

| Component | Location |
|-----------|----------|
| Orchestrator | `frontend/apps/{portal,admin,partners}/src/openapi/generator/index.mts` |
| API template | `apiInner.handlebars` — generates `<Tag>Api` classes |
| Model template | `model.handlebars` — generates TypeScript interfaces |
| Entry point | `npm run openapi` — loads OpenAPI JSON from disk |

### Key Generator Behaviors

- **`determineDataType()`** resolves schema types — **throws on plain `type: object` without `$ref`**
- **Groups operations** by `tags` into `export class <Tag>Api`
- **Reads `operationId`** for method names, URL builders, and lookup keys
- **Uses `x-links`** to generate typed resource methods on model classes
- **Only reads `responses["200"]`** for return type — ignores `201`, `202`
- **Iterates only** `GET/POST/PUT/DELETE/HEAD/OPTIONS` — **skips `PATCH`**

### Compatibility Gaps (Blueprint-side Fixes)

| Gap | Description | Status |
|-----|-------------|--------|
| Inline request body schemas | `determineDataType()` rejects inline `type: object` | ✅ Fixed — now emits `$ref` to component schemas |
| Inline enum naming | Enums need component-level `title` or named types | Investigate during migration |

### Compatibility Gaps (S365 Template-side Fixes)

| Gap | Description | Effort |
|-----|-------------|--------|
| PATCH operations skipped | Add `patch` to HTTP method iteration | Trivial |
| 201 responses treated as void | Read first `2xx` response with content | Small |
| Array query param serialization | Handle `style: form, explode: true` | Small |
| Opaque `type: object` properties | Map to `Record<string, unknown>` | Small |

These template changes are backwards-compatible with legacy Blueprint OpenAPI output.

---

## Per-Service OpenAPI Checklist

Before declaring a service migration "OpenAPI compatible":

- [ ] Captured baseline OpenAPI JSON from current service
- [ ] Generated candidate OpenAPI JSON from migrated service
- [ ] Ran `oasdiff breaking` with zero breaking changes
- [ ] Generated TypeScript client from candidate (`pnpm run openapi`)
- [ ] TypeScript client compiles cleanly (`npx tsc --noEmit`)
- [ ] Client method signatures equivalent (or changes documented)
- [ ] Verified NodaTime serialization format matches
- [ ] Verified enum serialization format matches
- [ ] Verified nullable/required property alignment
- [ ] Internal parameters (TenantId) hidden from public spec
- [ ] Security scheme configured correctly

---

## S365-Specific OpenAPI Configuration

### Operation Naming Convention

S365's Portal service uses `PortalOperationScannerConvention` which strips the "Portal" prefix
from operation names. In Blueprint source-gen, declare it on the app root:

```csharp
[BlueprintAppScan(FromAssemblyOf = typeof(PortalResetActiveUsersCommand))]
[BlueprintAppOperationNames(Remove = "Portal")] // Equivalent of PortalOperationScannerConvention
public partial class PortalBlueprintApp : BlueprintApp;
```

This renames the descriptor itself, so the OpenAPI `operationId`, the request-body schema key, `x-links`
and anything reading `link.OperationDescriptor.Name` (such as `GetMyAuthManifestQuery`) all see
`ResetActiveUsers`, as they did under legacy. Do not rename through an OpenAPI operation processor
instead: that changes only the rendered `operationId` and leaves every other reader on the prefixed name.
Resource schema names keep the prefix, and `BP011` fails the build if the removal gives two operations
the same name or leaves one with an empty name.

### TenantId Auto-Exclusion

S365 uses a `ConfigureOperation` callback to remove `TenantId` from OpenAPI parameters for
authenticated operations. Blueprint source-gen handles this automatically via the tenant
convention — when `ITenantAccessor` and `ITenantResolver` are in the compilation, `TenantId`
properties are treated as `AuthContext`-sourced and excluded from OpenAPI. No manual
configuration needed.

### Security Scheme

Blueprint defaults to Bearer JWT (`bearerAuth`, type=`http`, scheme=`bearer`, bearerFormat=`JWT`),
which matches S365's OAuth2/JWT authentication. Override via `OpenApiInfoOptions.SecuritySchemes`
if a different scheme is needed.
