# Decision 002: Project Conditional Validators Into OpenAPI

**Status:** Accepted
**Date:** 2026-09-15
**Scope:** OpenAPI schema rendering — reverses a documented design decision
**Supersedes:** the omission recorded at `src/Blueprint.Runtime/Http/OpenApiValidationConstraints.cs:41-46`

## Decision

Conditional validators — `RequiredIf`, `RequiredIfNot`, `RequiredItemsIf`, `RequiredItemsIfNot` —
**are** projected into the OpenAPI document, as `allOf` / `if` / `then` conditional schemas.

This reverses the framework's existing decision, documented in code, that conditional validators are
"intentionally skipped" from OpenAPI projection.

## Context

The omission was defensible while the framework had no consumer. It is not defensible now.

Signature365 generates its frontend validation from `openapi.json`. Legacy Blueprint projects these
validators (Signature365 commits `d93230472b`, "Export RequiredIf validation as OpenAPI conditional
schema", and `b83ddba5d6`, "Reject whitespace-only items in exported RequiredItemsIf schema"), and
Portal's generated `validators.ts` carries conditional constraints at six sites as a result.

Under the current omission those six sites lose their client-side constraint **silently** on the next
regeneration. Nothing fails to compile, no test breaks, and no diagnostic fires — the generated
validator simply stops enforcing the condition, and the first sign is a user submitting a form that
the server then rejects.

That is the shape of failure this framework exists to prevent. A compile-time-first framework that
silently drops a constraint at the boundary it generates is worse than a reflective one that keeps it.

## Considered options

* **Project conditional validators into OpenAPI** — chosen.
* **Keep the omission; Signature365 accepts the loss of client-side conditional validation.** Rejected:
  it is a product regression traded for framework tidiness, and it arrives invisibly.
* **Keep the omission; Signature365 hand-maintains the six conditional validators in `validators.ts`.**
  Rejected: hand-maintained code derived from a generated document drifts, and the drift is silent in
  exactly the same way.

## Projected semantics

Legacy Blueprint's `RequiredIfSchemaProcessor` is the behaviour to reproduce, not the implementation to
copy. The rules below are restated deliberately, because this framework's attributes do not mean what
legacy's meant, and because two of legacy's rules disagree with legacy's own runtime.

> **Three of these are deliberate divergences from `d93230472b`/`b83ddba5d6`, not oversights.** They are
> marked **[divergence]** below. Each exists because copying legacy would make the emitted schema reject a
> payload the server accepts, or drop a constraint the server enforces. Do not "fix" the schema back
> toward legacy: the emitted document would start failing valid submissions client-side, invisibly to
> every test that only checks the server.

**All four attributes are projected, not the two legacy exported.** Legacy exports `RequiredIf` and
`RequiredItemsIf` and leaves `RequiredIfNot` as "a possible future extension point". Inheriting that
omission would reproduce, in a new framework, the exact silent-drop failure this decision exists to
close — and `RequiredIfNot` is already in use in the reference app.

**[divergence] A null dependent value is projected here; legacy skips it.** Legacy skips
`DependentValue == null` as "a server-side no-op". That is not true of these attributes:
`DependentValues` is normalised through `value?.ToString()`, so `[RequiredIf(dep, null)]` yields the
dependent-value set `{ null }` and `MatchesDependentValue` returns true exactly when `dep` is null — the
requirement *activates*. Copying legacy's skip would drop a constraint the runtime enforces. The
condition is emitted as `"const": null`.

**When the dependent property is absent from the payload, the condition does not fire.** Every `if`
carries `"required": [<dependent>]`, so a payload omitting the dependent property never activates the
`then`. The server, which sees the CLR default rather than an absence, may still reject it. This
under-enforces on the client and never over-enforces: a generated validator that blocks a payload the
server would have accepted is a worse failure than one that lets a payload through for the server to
reject.

**[divergence] String targets require non-whitespace, not merely non-empty.** Both the runtime attribute
(`HasRequiredValue` → `!string.IsNullOrWhiteSpace`) and the generated fast path
(`ConditionalRequiredValidationFastPathStrategyHelpers.TryGetRequiredStateExpressions`) reject a
whitespace-only string. `"minLength": 1` alone does not, so a string target carries `"pattern": "\\S"`
as well. Legacy made this correction for `RequiredItemsIf` elements in `b83ddba5d6` but never for the
scalar `RequiredIf` case, where its exported schema still disagrees with its own attribute.

**[divergence] Item constraints are element-type aware.** `HasItems` applies
`IsNullOrWhiteSpace(item.ToString())` to every element whatever its type, so a `List<int>` is satisfied
by `[1, 2]`. Legacy hardcodes `items: { "type": "string", … }`, which would reject that payload
client-side. Here a string element type gets the string constraint, and every other element type gets
`{ "not": { "type": "null" } }` — the part of the runtime check that JSON Schema can express without
narrowing the type. An item requirement on a property with no single element type (a dictionary, say) is
skipped rather than guessed at.

**A target that is a non-nullable value type is skipped.** `HasRequiredValue` returns true for any
non-null, non-string value and `TryGetRequiredStateExpressions` produces no fast-path rule for such a
type, so the requirement can never fail server-side. Emitting one would reject a payload omitting the
property that the server accepts.

**Only body properties carry the projection.** `if`/`then` is an object-level construct and needs a
containing schema. Route, query, header and cookie properties render into the operation's `parameters`
array, which has no containing object, so a conditional declared on one — as
`GetTemplateHistoryQuery.EndDate` declares `[RequiredIfNot(nameof(StartDate), null)]` — is skipped
rather than mis-attached.

**A dependent value with no faithful JSON representation drops the whole requirement.** Guessing at a
representation is tolerable only while an unmatched condition can merely fail to fire, which
under-enforces. Under the negated form the test is wrapped in `not`, so a condition that can never match
becomes one that *always* matches: the requirement always applies and the client rejects payloads the
server accepts. Booleans, strings, `null`, every integral and floating-point type, enum members and
`char` are representable — enum members and `char` as strings, matching how both render. Anything else
is skipped rather than approximated. For the same reason every integral type is listed explicitly when
writing a value: an unsigned dependent emitted as a JSON string could never match a property the
document declares `"type": "integer"`.

**Both properties of a condition must already be declared on the containing schema.** Object schemas
here render `"additionalProperties": false`, which rejects any instance member absent from the outer
`properties` map regardless of what an `allOf` branch says. A condition naming a property the schema
does not declare is therefore unsatisfiable rather than merely unhelpful, and is skipped — the same
guard legacy applies for `JsonIgnore`d and differently-owned properties.

## The document is no longer strict OpenAPI 3.0

`if`, `then` and `const` are JSON Schema draft-07/2019 keywords. They are **not** in the OpenAPI 3.0
Schema Object, which the rendered document declares itself to be (`"openapi": "3.0.4"`). Emitting them
is accepted deliberately:

* Legacy Blueprint already ships exactly these keywords in the specs Signature365 generates from, and its
  consumers — AJV-generated validators, the TypeScript client generators — read them without complaint.
  The alternative is a document that is formally cleaner and silently less safe.
* Strict validators tolerate *unknown* keywords far better than known keywords carrying illegal values.
  That distinction decided one constraint's shape: a "not null" target is expressed by the `required`
  entry alone rather than by `{ "not": { "type": "null" } }`, because `"null"` is outside the OAS 3.0
  `type` enum — a known keyword with an illegal value, which spectral, redocly and swagger-parser reject
  the whole document over. The weaker form only stops rejecting an explicit `"x": null`, which
  under-enforces, and that is the safe direction.

If the document is ever moved to OpenAPI 3.1 (whose Schema Object *is* JSON Schema 2020-12), all three
keywords become legal and the "not null" constraint can be restored.

## `NotAllowedIf` stays unprojected

`NotAllowedIfAttribute` is the fifth conditional validator and is deliberately not projected, though
JSON Schema could express it. It is a *prohibition* rather than a requirement, so the natural projection
asserts that a payload must **not** carry a value — the direction that blocks submissions client-side if
the projection is even slightly wrong. Nothing downstream consumes it today, so there is no silent loss
to close, and an unproven prohibition is not worth the risk that the four requirement forms are. Revisit
only with a consumer that needs it and a test that pins its semantics.

## Where the projection lives

The conditional is a fact about a *pair* of properties, so it cannot ride on
`OpenApiValidationConstraints`, which is a flat per-property record. Extraction stays there (a static
that returns conditional descriptors for a `PropertyInfo`); aggregation and rendering sit on the
containing schema:

| Stage | File |
|---|---|
| Extract conditionals from a property's attributes (`ExtractConditionalRequirements`) | `src/Blueprint.Runtime/Http/OpenApiValidationConstraints.cs` |
| Collect and resolve them for an operation's body properties (`ResolveBodyConditionals`) | `src/Blueprint.Runtime/Http/OpenApiOperationRenderingContext.cs` |
| Collect and resolve them for a covered body type's own schema (`ExtractSchemaConditionals`) | `src/Blueprint.Runtime/Http/OpenApiSchemaProjectionContext.cs` |
| Carry them on the request-body model | `src/Blueprint.Runtime/Http/OpenApiParameterModel.cs` |
| Carry them on the projected schema model | `src/Blueprint.Runtime/Http/OpenApiSchemaProjectionContext.cs` |
| Declare the `OpenApiConditionalRequirement` contract and carry it on the component model | `src/Blueprint.Runtime/Http/OpenApiSchemaComponentRenderingContext.cs` |
| Attach them to the request-body component | `src/Blueprint.Runtime/Http/OpenApiDocumentRenderingContext.cs` |
| Write `allOf` into the document (`WriteConditionalRequirement`) | `src/Blueprint.Runtime/Http/OpenApiOutputRenderingContext.cs` |

The last row is load-bearing: `OpenApiOutputRenderingContext` holds the only `Utf8JsonWriter` in the
runtime, and `WriteSchemaComponent` writes a closed set of keys. `OpenApiSchemaComponentModel` has no
vendor-extension passthrough (`x-links` is a dedicated field), and `OpenApiSchemaProcessorContext` is
typed to that model, so neither the schema-processor hook nor any post-render seam can introduce
`allOf` without that writer changing. Legacy could route around its own renderer through NJsonSchema's
`ExtensionData`; this stack has no equivalent, by design.

## Related correction

Commit `48984b93` ("feat(validation): add RequiredItemsIf/RequiredItemsIfNot and collection-misuse
diagnostics") shares a name with the Signature365 feature but is a different one. Its own message
states that the OpenAPI-projection and root-metadata facets of the legacy work were deliberately out
of scope, and that source-gen "avoided" them by design. Two gaps followed from that:

| Gap | Legacy behaviour to reproduce | Status |
|---|---|---|
| `RequiredItemsIf` accepted whitespace-only elements — `HasItems` was a count check only, in both `RequiredItemsIfAttribute.cs` and the generated fast path in `ConditionalRequiredValidationFastPathStrategyHelpers.cs` | Every element non-null and non-whitespace | Closed: both now scan elements |
| No conditional schema emitted at all | `allOf` / `if` / `then` per the two Signature365 commits above | Closed by this decision |

Runtime attribute, generated fast path and projected schema are three implementations of one contract.
Three that disagree is worse than one that is absent, which is why the schema rules above are pinned to
what the other two enforce rather than to what legacy emitted.

## Consequences

* Generated OpenAPI documents grow a conditional schema per conditional validator declared on a body
  property. The reviewed baseline that moves is
  `src/Blueprint.SourceGenerator.Tests.Integration/Http/Snapshots/ReferenceAppOpenApiTests.When_openapi_endpoint_invoked_then_full_document_matches_reviewed_snapshot_Async.verified.txt`
  — the reference app's `ExportTemplatesCommand` declares both a `RequiredIf` and a `RequiredItemsIf`.
  No baseline under `src/Blueprint.SourceGenerator.Tests/Generators/*/Snapshots/` is affected; the only
  OpenAPI-related files there record *diagnostics* for validation shapes the projection rejects.
* The runtime validator and the generated fast path must agree on element-level semantics, not only
  on count — they are two implementations of one contract and both are proven.
* Consumers generating clients from the document gain conditional validation without asking for it.
  This is additive: no existing constraint is removed.
* `ExportTemplatesCommand.Formats` documented in the reference app that its requirement "is intentionally
  absent from the OpenAPI schema". That sentence was part of the reversal and was corrected with it.

## Known limitation — closed

**Closed by task 13.** Every schema component built from serializer metadata now carries its own
conditionals, resolved from CLR property names onto the JSON names that component declares, and dropped
where either name is not one the component declares. The limitation as originally recorded follows.

One part of it remains open and is *not* this decision's to close: a DTO reached from a **request body**
has no schema component of its own in this framework at all. `ResolveCollectionSchema` returns no schema
ref for a complex body property, and
`ReferenceAppOpenApiTests.When_body_property_is_array_of_complex_type_then_items_schema_renders_as_object`
pins that as reviewed behaviour, so such a DTO renders as an inline `"type": "object"` with no
properties. Legacy emits a component for it — `ApplyToDto`, reached from Portal's command forms, is
exactly that shape. Closing *that* is a separate parity gap about which components exist, not about
conditionals, and nothing about the conditional projection changes when it is closed.

Only the **request-body** schema component carries conditionals. A conditional declared on a nested DTO
that a body property `$ref`s is not projected: nested DTO components are built from serializer metadata
in `OpenApiSchemaProjectionContext`, which does not carry the CLR property handles the extraction needs.
Legacy does project those — its `SurveyCommentsSettings` and `ApplyToDto` conditionals are exactly this
shape — so closing it is a prerequisite for the Signature365 swap-over, not an optional extra. It is
deliberately out of scope here rather than overlooked, and it fails the same silent way as the omission
this decision reverses, so it needs its own task rather than a note.

## Wider point this decision records

No parity-tracking mechanism exists between this framework and the legacy Blueprint it replaces.
`migration/legacy-blueprint/` is a one-time architecture snapshot, and `migration/plan.md:16` explicitly
disclaims byte-for-byte parity as a goal. Eight functional legacy-Blueprint commits landed on
Signature365's master in the four months after the migration branch forked; five were absent here and
two partial, this one included. Until Signature365 is swapped over, every further legacy commit widens
that gap unobserved.
