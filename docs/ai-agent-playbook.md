# AI agent playbook

This document adapts harness-engineering ideas into repository-specific rules for future AI sessions.

The goal is not just to have agents write code, but to have them improve the environment that tells future agents whether the code is correct.

## Core idea

The best AI workflow in this repo is:

1. read the relevant docs
2. find the deterministic harness
3. make the change
4. use tool feedback to correct it
5. improve the harness or guidance if the failure mode was avoidable

Do not rely on memory, tribal knowledge, or "someone will catch this in review".

## Non-negotiables for future AI sessions

### 1. Prefer deterministic feedback over intuition

Use the strongest available feedback loop:

- build diagnostics
- generator diagnostics
- snapshot diffs
- incremental-cacheability checks
- formatters and repository hooks
- structured runtime metadata

If the repo already has a deterministic check, use it before making assumptions.

### 2. When a mistake repeats, strengthen the system

If a tool failure or human review points out a repeated problem, the best fix is usually not "remember harder next time".

Prefer one of these instead:

- add or improve a diagnostic
- add or tighten a test
- improve a helper API
- update `/docs`
- add inspection tooling or metadata
- add a repo-local validation or inspection tool when that creates a clearer deterministic feedback loop

The repository should become more self-correcting over time.

### 3. Keep knowledge in version control

If guidance matters for future work, it belongs in the repository.

For this repo that means:

- `/docs` is the detailed source of truth
- `README.md` and `.github/copilot-instructions.md` should point into `/docs`
- architecture, diagnostics, observability expectations, and implementation checklists should not live only in chat history
- for migration-plan work, durable sequencing or review decisions belong in `migration\plan.md` and the relevant `migration\...\plan.md` file, not only in session-local state such as chat, SQL todos, or the session `plan.md`
- Blueprint-specific usage and extension knowledge should also live in repo-local skills: keep `using-blueprint` as the canonical consumer-installable Blueprint skill, keep `extending-blueprint` as the repo-local overlay, and update the relevant skill in the same change when Blueprint behavior or workflow changes

### 4. Prefer machine-actionable outputs

When adding feedback, prefer formats that a human or AI can act on quickly:

- stable diagnostic IDs
- precise source locations
- structured manifests or registries
- generated outputs that can be diffed
- SARIF and JSON reports for larger refactors or migrations
- telemetry and metadata that explain the active surface

Vague prose is weaker than a concrete diagnostic, a failing snapshot, or a generated manifest.

### 5. Update docs when the contract changes

If a change affects:

- architecture
- generator flow
- diagnostics
- observability expectations
- developer tooling
- implementation checklists

then the relevant `/docs` file should be updated in the same change.

## Repo-specific working loop

### Before you change code

- Start in `docs/README.md`.
- Read the most relevant detailed docs for the change.
- Identify the build, snapshot, and generator harness you expect to exercise.
- For any feature, bugfix, refactor, or behavior change, use the repo-local `test-driven-development` skill mindset first: write the failing proof before production edits and watch it fail for the right reason.
- Do not use reflection in tests to assert that a contract exists or to invoke private/internal helpers. If the right behavior is not testable through a supported seam, improve that seam first.

### While you change code

- Favor compile-time enforcement over runtime repair.
- Keep runtime behavior simple and inspectable.
- Reuse the existing generator and CodeWriter patterns before inventing ad-hoc output logic.
- For new planning helpers, registries, or orchestration seams, cover the happy path, the failure/diagnostic path, and the determinism edge cases that keep generation stable.
- Keep contributor naming and layout explicit: concrete module types end with `Module`, live in dedicated subfolders under `src\Blueprint.SourceGenerator\Modules`, module-specific helper files live beside the owning module in that folder, and seam methods should be named `ContributeTo<Thing>` instead of a generic `Contribute`.
- For pipeline planning, prefer helper APIs that imply stage participation when contributors register render steps or variables; do not rely on manual `AddContribution(...)` bookkeeping when the type design can enforce the contract directly.
- Planning context classes may expose a small read-only metadata surface when it keeps module code clearer, but do not leave mutable plan/execution objects or record-style grab-bags available for modules to bypass the helper surface.

### After tools or humans give feedback

- If something fails unexpectedly, use the `systematic-debugging` skill mindset before proposing fixes: investigate the root cause first, then change the smallest thing that proves the hypothesis.
- fix the immediate issue
- decide whether a stronger diagnostic, test, doc, or tool should exist
- add the improvement when it is reasonable and directly related

### Before you ask for commit-level human review

- Use the `verification-before-completion` skill mindset before any success claim: rerun the fresh verification command, read the output, and only then say the slice is passing or ready for review.
- Finish your own review pass first, including tests, formatting, generated-output review, and any review-agent follow-up that the slice needs.
- Generate a visual review handoff with the `visual-explainer` skill before commit for any migration-plan slice or phase gate.
- If review closes a decision about ordering, next slice selection, or architecture direction, write that decision back into `migration\plan.md` before ending the session.
- If a slice is obviously complete and no review gate remains open, make a small, focused git commit before switching to unrelated work or ending the session.
- Treat that visual handoff as part of the artifact set, not as a presentation extra. It should show:
  - the architectural delta and why it matters
  - key code snippets and file touch points
  - representative generated outputs or artifacts such as pipelines, descriptors, manifests, reports, or fixture outputs
  - review findings already addressed plus any remaining risks or confidence gaps
  - explicit feedback prompts for the human reviewer that should be fed back into the next AI review round before commit
- Open the generated HTML locally and confirm it is readable before handing off to a human.
- When handing a review question to a human, do not present a raw question alone; give your recommendation first, include 2-3 concrete options when useful, and mention any material consulted-agent agreement or dissent.

## What "self-learning" means here

In this repo, self-learning does not mean inventing hidden heuristics.

It means:

- learning from build and test output
- learning from snapshot diffs
- learning from generated output
- learning from human review
- then writing the result back into code, diagnostics, tests, docs, or tooling, including repo-local tools that validate or explain the codebase better

Future agents should inherit a better repo, not just a patched one.

## Large-scale refactoring workflow

When a refactor is too large for ordinary local edits:

- choose the lightest safe layer from `docs\refactoring-workflow.md`
- prefer Roslyn analyzers/code fixes or a workspace-based codemod over regex/manual batch edits
- require a dry-run and a machine-readable report for large codemods
- update the refactoring workflow docs if the toolchain, commands, or artifact contract changes

## Good outputs for large API surfaces

As the API surface grows, future work should favor tooling and metadata that answer "what exists?" quickly:

- operation and route manifests
- descriptor summaries
- serializer-context coverage signals
- dev-only introspection endpoints or CLI tools
- telemetry that maps runtime activity back to generated artifacts

If a surface is too large to inspect manually, build a better surface for inspection.
