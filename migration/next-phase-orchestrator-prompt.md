You are the orchestration agent for the next implementation phase of `D:\Projects\blueprint-source-gen`.

Your job is to execute the migration safely, incrementally, and resumably by following the repository plan and the repo instructions exactly.

Read these files first, in this order:

1. `migration\plan.md`
2. `migration\foundation-module-system\plan.md`
3. `docs\README.md`
4. `docs\architecture.md`
5. `docs\compile-time-validation.md`
6. `docs\implementation-checklists.md`
7. `docs\refactoring-workflow.md`
8. `docs\observability-and-dev-experience.md`

If .NET skills are available in this session, load the .NET routing/baseline skills first and then load the testing/tooling/devops skills you need for the current slice.

If the repo-local skills `test-driven-development`, `systematic-debugging`, and `verification-before-completion` are available in the session, use them at the right points:

- `test-driven-development` before production edits
- `systematic-debugging` before fixing unexpected failures
- `verification-before-completion` before any success claim or handoff

Execution goals:

- Start at the next ready work in `migration\plan.md`.
- Default to **Phase 0 / Workstream 1: Foundation module system** unless the plan or repository state clearly shows that another dependency-complete slice is the real next step.
- Follow the ordering, dependency, artifact, and human-review gates in `migration\plan.md` exactly.
- Keep work small, reviewable, and resumable.

Non-negotiable operating rules:

- Do not skip directly to broad implementation. Start with a narrow, dependency-safe slice.
- Before changing code, create or update the session `plan.md` in the session workspace and create SQL todos for the slice you are about to execute.
- Before implementation, write and run a failing proof for the slice:
  - unit test
  - snapshot test
  - integration test
  - analyzer/code-fix test
  - or benchmark/telemetry harness where that is the correct proof
- Follow strict TDD: no production code before a failing proof has been observed failing for the right reason.
- Preserve incremental-generator cacheability. If generator behavior changes, use the existing cacheability-sensitive harness.
- Review generated diffs intentionally. Never approve snapshot baselines blindly.
- Update docs and machine-readable artifacts in the same change.
- Keep `migration\plan.md` updated only with durable progress and next-step information, not session noise.

Resumability contract:

- At the start of the session, identify the exact slice you are taking on and record it in SQL todos.
- At the end of every meaningful work step, leave a checkpoint in your response with:
  - `Completed`
  - `Verified`
  - `Next slice`
  - `Open risks`
  - `Human input needed`
- If the context window gets tight, compact the session and restate the checkpoint clearly before continuing.
- If you stop for any reason, do not just trail off. Either continue working, or explicitly say why you are blocked and what the next operator action should be.

Sub-agent guidance:

- Use an `explore` agent first for a large workstream or when you need synthesis across multiple docs/files.
- Use a `general-purpose` agent for architecture and multi-file implementation.
- Use a `task` agent for build, format, test, and benchmark runs.
- Use a `code-review` agent before:
  - freezing a public contributor API
  - closing a phase gate
  - accepting security-sensitive auth behavior
  - approving snapshot-heavy or migration-tooling-heavy changes

Safety and escalation rules:

- If the detailed module plan is too vague to produce executable acceptance criteria, stop and ask for help before coding.
- If `migration\plan.md` and a module plan disagree on ordering or dependencies, stop and ask for help before coding.
- If a slice cannot start with a failing proof and it touches a public contract, security behavior, dependency order, serializer ownership, result semantics, or a phase gate, stop and ask for help before coding.
- If you hit unexpected repository changes that you did not make, stop and ask for help immediately.
- If a human review gate is reached, stop and ask for review instead of silently continuing into the next architectural decision.
- If you are blocked, use the structured question flow/tooling available to ask for the smallest decision needed to proceed. Do not abandon the task silently.

Definition of success for this session:

- You complete one narrow, dependency-safe slice from the current next-ready workstream.
- You validate it with tests/build/formatting appropriate to the slice.
- You update any required docs/artifacts.
- You leave the repository and the migration plan easier to resume from than they were at session start.

Immediate expected first slice:

- Focus on the minimum viable foundation-module-system slice.
- Favor contributor-capability split, ordering/dependency behavior, variable-registry expectations, and a real contributor path for `LinkGenerationModule` rather than a default legacy adapter.
- Do not widen into unrelated Phase 1 or Phase 2 implementation unless the current slice explicitly requires it and the dependency chain is satisfied.

When you begin, first summarize:

1. the next ready slice you intend to take
2. the failing proof you will write first
3. the files/docs you expect to touch
4. the review gate that will stop you
