# Signature 365 → Blueprint Source-Gen: Migration Findings

> Living document tracking migration progress, problems, and open questions.
> Updated by AI agent sessions as migration work proceeds.

## Status Summary

| Area | Status | Notes |
|------|--------|-------|
| Compile-time proof | ✅ Done | `.scratch/s365-proof/` compiles, 46 generated files |
| Codemod tool | ✅ Done | Per-project SARIF, analyzer injection |
| .NET 10 upgrade | ✅ Done | All projects target net10.0 |
| Blueprint.Testing basics | ✅ Done | WithTenant, WithScopePairs, WithTenantScopePairs |
| Blueprint.Marten | ✅ Done | IUnitOfWork, outbox, multi-tenancy session scoping |
| Blueprint.Rebus | ✅ Done | Companion generator, transaction scope |
| Blueprint.Tasks.Hangfire | ✅ Done | Schedule provider, recurring tasks |
| `[Resource]` built-in | 🔄 In progress | Noun/verb/template with compile-time optimization |
| First-class tenancy | ⬜ Not started | Pipeline integration, claim-based tenant resolution |
| Pipeline body extraction | ⬜ Not started | Reduce IL duplication across generated pipelines |
| Minimize test churn | ⬜ Not started | QueryAsync/CommandAsync, ambient auth |
| OpenAPI well-known endpoint | ⬜ Not started | Research best practice |
| TypeScript generation compat | ⬜ Not started | Custom Handlebars templates, not NSwag |
| Load tests (NBomber) | ⬜ Not started | Old vs new comparison |
| Runtime benchmarks | ⬜ Not started | BenchmarkDotNet pipeline benchmarks |

---

## Confirmed Facts

### Serialization
- **S365 uses System.Text.Json (STJ) for API responses**, not Newtonsoft.Json
- Newtonsoft is only used for Marten persistence serialization (JSONB storage)
- Blueprint source-gen's STJ-first approach is fully compatible
- S365 will need `JsonSerializerContext` per service for AOT-friendly serialization
- NodaTime types are used extensively; `NodaTime.Serialization.SystemTextJson` is available

### Authorization Model
- S365 exclusively uses `[Resource(Noun, Verb)]` — no `[Permission]`/`[PermissionFor]`
- `ScopeApiAuthoriser` checks JWT `scope` claims against expected `{Noun}.{Verb}` pairs
- `AccountId = TenantId ?? PartnerId ?? "Symprex"` — tenant-first hierarchy
- `IS365ResourceKeyExpander` does hierarchical resource key expansion via Marten queries
- Ignored scopes: `openid`, `profile`, `Signature365.ReadWrite.All`, `Signature365Admin.ReadWrite.All`

### Multi-Tenancy
- `ICurrentTenantAccessor.Current` is set by ASP.NET Core middleware before Blueprint pipeline
- `IDocumentSession` is scoped: `store.IdentitySession(tenantId)` when tenant is present
- Tenant ID comes from JWT claim `sig365_tenant_id`

### Messaging (Rebus)
- `UseMartenOutbox()` stores outgoing messages in Marten outbox table
- `StartRebusTransactionScopeMiddleware` wraps pipeline with `RebusTransactionScope`
- `CommitRebusTransactionScopeMiddleware` calls `CompleteAsync()` after handler
- `[NoRebusTransaction]` attribute opts out specific handlers
- `[DoNotSaveChanges]` opts out of `SaveMartenChangesMiddleware`

### TypeScript Generation
- **NOT NSwag** — S365 uses custom Handlebars templates in 3 applications
- Templates load OpenAPI JSON from disk and generate `api.ts`
- OpenAPI shape must be compatible with existing templates

### SMTP Subsystem
- 59 projects, does NOT use Blueprint — only an HTTP client
- **Excluded from migration scope**

---

## S365 Scale

| Metric | Count |
|--------|-------|
| Total projects | ~158 |
| Blueprint-consuming services | 16+ |
| Operations (ICommand/IQuery) | ~240+ |
| `[Resource(...)]` usages | 349 |
| `[FromResource(...)]` (legacy) | 158 |
| S365Nouns constants | 96 |
| Gateway projects | 5 |
| Contract assemblies | ~14 |
| Test projects | 42 |
| SMTP subsystem (excluded) | 59 |

---

## Problems Encountered

### 1. Codemod SARIF Concatenation (Fixed)
- **Problem**: `/p:ErrorLog=path.sarif` applied globally caused all 158 projects to write to same file
- **Fix**: Per-project SARIF via `.targets` file with `$(MSBuildProjectName)` expansion
- **Commit**: `851b7c2`

### 2. WithTenant Regression (Fixed)
- **Problem**: Adding `WithScopePairs` accidentally removed `WithTenant` methods
- **Fix**: Restored methods alongside new helpers
- **Commit**: `fa03a48`

### 3. HasConcreteProperty False Positive (Fixed, Prior Session)
- **Problem**: Source generator included inherited `Object` properties (ToString, etc.)
- **Fix**: Filter to only properties declared on the concrete type
- **Impact**: Blocked S365 proof project compilation

---

## Open Questions

1. **OpenAPI endpoint path**: What is the current best practice? `/.well-known/openapi`?
2. **Pipeline IL size**: How much duplication exists? What's the actual IL savings from extraction?
3. **Tenant middleware ordering**: Should tenant resolution be ASP.NET middleware or pipeline stage?
4. **Property populator elimination**: Can ALL S365 property populators become compile-time?
5. **Resource key template**: How should `{Auth:AccountId}` expansion integrate with generated code?
6. **Handlebars template compatibility**: What specific OpenAPI shape properties do the templates expect?

---

## Session Log

| Date | Session | Key Changes |
|------|---------|-------------|
| 2025-07 | Initial | S365 proof project, codemod tool, .NET 10 upgrade, testing helpers |
| 2025-07 | Current | Migration findings doc, resource auth builtin, tenant first-class |
