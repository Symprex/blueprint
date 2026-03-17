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

### While you change code

- Favor compile-time enforcement over runtime repair.
- Keep runtime behavior simple and inspectable.
- Reuse the existing generator and CodeWriter patterns before inventing ad-hoc output logic.

### After tools or humans give feedback

- fix the immediate issue
- decide whether a stronger diagnostic, test, doc, or tool should exist
- add the improvement when it is reasonable and directly related

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
