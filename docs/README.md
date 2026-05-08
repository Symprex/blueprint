# Project docs

This folder is the detailed source of truth for repository-specific architecture, principles, and AI-agent workflow.

`README.md` and `.github/copilot-instructions.md` are the entry points. The durable detail lives here.

## Start here

- New to the repo: read `guiding-principles.md`, then `architecture.md`.
- Migrating a legacy Blueprint or Signature365 app: read `consumer-migration-guide.md`.
- Changing generators, diagnostics, or compile-time rules: read `compile-time-validation.md`.
- Changing runtime behavior, telemetry, or inspection surfaces: read `observability-and-dev-experience.md`.
- Deploying to production or configuring middleware: read `deployment-and-operations.md`.
- Working as an AI agent: read `ai-agent-playbook.md`.
- Planning or running a large refactor or a legacy-app migration: read `refactoring-workflow.md`.
- Implementing new features: keep `implementation-checklists.md` open while you work.

## Maintenance contract

- If a change affects architecture, generator flow, diagnostics, observability, developer tooling, or project principles, update the relevant `/docs` file in the same change.
- Treat Blueprint-specific skills as versioned guidance artifacts. Keep `using-blueprint` aligned with the current consumer contract and keep `extending-blueprint` aligned with the repo's extension workflow.
- If a change alters how Blueprint is consumed or extended, update the relevant Blueprint skill in the same change as the code and docs.
- Treat `migration\progress.json` as the single source of truth for live migration progress during migration sessions. Keep it aligned with the durable rationale and acceptance criteria in `migration\plan.md` and the relevant workstream plan whenever the next ready slice, active workstream, last completed slice, or open review gate changes. Do not infer live progress from markdown history when `migration\progress.json` is present.
- Prefer updating an existing doc over creating a new one. Add a new doc only when the topic no longer fits the current structure cleanly.
- Keep docs repo-specific and actionable. Link to concrete types, files, generators, diagnostics, and tests whenever possible.
- Prefer deterministic rules over vague advice. If the repo wants something enforced, document both the principle and the harness that should enforce it.
- When human feedback or tool output exposes a repeated mistake, strengthen the guidance or harness instead of relying on memory.
- If `README.md`, `.github/copilot-instructions.md`, and `/docs` disagree, reconcile them immediately. `/docs` should carry the detailed truth.

## Document map

| Document | Purpose |
| --- | --- |
| `guiding-principles.md` | The design principles that should shape new code and refactors. |
| `architecture.md` | The compile-time and runtime flow of the system, plus the main extension points. |
| `consumer-migration-guide.md` | Step-by-step guide for migrating legacy Blueprint or Signature365 apps to source-generated Blueprint. |
| `compile-time-validation.md` | How the repo uses diagnostics, snapshots, and incremental-generator checks as a deterministic harness. |
| `observability-and-dev-experience.md` | Expectations for telemetry, diagnostics, and tooling that make large API surfaces inspectable. |
| `deployment-and-operations.md` | Production deployment, middleware ordering, reverse proxy setup, and operations checklist. |
| `ai-agent-playbook.md` | Repo-specific AI workflow guidance adapted from harness-engineering ideas. |
| `refactoring-workflow.md` | The layered toolchain plus the concrete workflow for larger refactors and legacy-app migrations. |
| `implementation-checklists.md` | Practical checklists for common change types. |

## What these docs are not

- They are not a complete API reference.
- They are not a replacement for tests, diagnostics, or the build.
- They are not a place for generic .NET guidance that already lives in broader tooling or ecosystem docs.

They exist to capture the repo-specific intent that future humans and AI agents need in order to make correct changes quickly.
