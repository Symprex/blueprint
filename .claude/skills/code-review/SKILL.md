---
name: code-review
description: Review changes since a fixed point along separate axes — Standards, Spec, and where they apply Security and prototype Fidelity — run in parallel and reported side by side. Use when reviewing a branch, a PR, working-tree changes, or when asked to review since a commit.
---

# Code review

Separate questions, kept apart:

| Axis | Question | When |
|---|---|---|
| **Standards** | Does this follow the conventions of *this repo*? | Always |
| **Spec** | Did it build what was actually asked for? | Always |
| **Security** | Can this be made to do something it should not? | The diff touches auth, access control, data access, input handling, secrets or a public surface |
| **Fidelity** | Does it match the design it was built from? | A prototype design pack exists for this work |

They must be reported separately and never merged or re-ranked. A change can be beautifully written and solve the wrong problem, or do exactly the right thing in a way this codebase would never accept. Collapsing them into one score lets each hide the other, and the "one big finding" always crowds out the quiet one.

**Say which axes you ran and why.** A review that silently skipped Security reads identical to one that ran it and found nothing, and the difference is the whole value.

## Establish the review point

Ask, or infer from the situation: a commit, a branch, a tag, or the merge-base with the default branch. State which you used — a review of "the changes" without saying since when is not reproducible.

## See everything

```powershell
git status --short
git diff <point>...HEAD
```

Then **read the files themselves**. All three steps.

`git diff` does not show untracked files, so a diff-only review silently skips every file the work created — which, on new features, is most of them.

## Run the axes independently

One pass per applicable axis, reported side by side. Delegate one worker per axis where you can, so the passes run in parallel; run them in sequence where you cannot. Either way **no pass may see another's findings** — that independence is the point, and parallelism is only how it gets cheaper.

### Standards

Judged against the code around it and any repo rules, **not** against your preferences. If the repo consistently does something you would not, the repo wins — a review that fights the house style produces noise and gets ignored.

- Correctness: logic, edge cases, error paths, null and boundary handling.
- Naming, structure and idiom matching the surrounding code.
- Reuse: does this reimplement something that already exists here?
- Simplification: is there a materially simpler shape?
- Depth: is the interface small and the implementation contained, or does it leak internals to its callers?
- Tests: apply the litmus — would each assertion break if the code broke in a plausibly wrong way? A tautological assertion is a finding.

### Spec

Every requirement in the originating issue, spec or task: present, missing, or changed. Also flag what was built that **nobody asked for** — unrequested scope is a finding, not a bonus.

If there is no spec to review against, say so and report Standards only. Do not invent a spec from the diff and then check the diff against it.

### Security

Run this axis when the diff touches authentication, authorisation, data access, input handling, secrets, or any surface reachable from outside. When in doubt, run it — the cost is one extra pass.

- **Authorisation on every new entry point.** Does each new or changed operation carry the access control this repo requires, and is it *specific* enough? A check that grants more than the operation needs is a finding, not a convenience.
- **Tenant and owner isolation.** Can a caller reach another tenant's or another user's data through this path? Look for a query missing its scoping predicate.
- **Input validation.** Unvalidated input reaching a query, a command, a file path, or rendered output. Parameterised queries, not string concatenation. Missing null and range checks on things that came from outside.
- **Secret and data exposure.** Hardcoded credentials or connection strings. Sensitive values written to logs. Internal detail in an error response that reaches a client.
- **Insecure defaults.** Is new behaviour opt-in rather than opt-out, and does a new flag default to the safer side?

Judge severity by consequence, not by category: ask whether this would enable a breach, data exposure, privilege escalation or unauthorised access to real data if it shipped today. A theoretical weakness on an unreachable path is a note; a missing tenant predicate is a must-fix.

The repo's own rules define the mechanism — the attribute, the guard, the fixture. `dotnet-advisor` and `frontend-advisor` say where those rules live. Where a scope's `notes` in `.symprex/config.json` records a pattern that is deliberate, respect it rather than reporting it every time.

### Fidelity

Run this axis only when a design pack is actually there to compare against. `## Prototype` in `.symprex/STATE.md` records the paths where the work came through a prototype stage; the design pack is `SET.md` and the chosen variant's `DESIGN.md`, and an archived pack sits under the specs directory. No `.symprex/STATE.md`, or no `## Prototype` section in it, means this axis does not apply — skip it and say so.

Compare the built thing against the chosen variant — labels, button text, icons, layout, the states the variant showed, and `DESIGN.md`'s rebuild notes.

Differences are not automatically findings. A prototype is a proposal, and implementation reveals things a prototype could not. Report a difference as **[must-fix]** only where it changes what the engineer approved; where the implementation is better, say that plainly and note the pack is now stale. What must never happen is a silent divergence: the pack gets archived as though it were honoured, and the next person reads it as a description of the code.

If no design pack exists, skip the axis and say so. Do not reconstruct a design from the diff.

## Out of scope

Lint, format, typecheck and build. Those belong to the repo's own automation — the formatter that runs on edit, and the commit gate — and re-reporting them here trains people to skim reviews.

Whether it actually runs. That is verification, and it is a separate job.

## The bar

"Done right", not "good enough". Everything worth fixing blocks.

Convergence comes from **scoping the re-review**, not from lowering the bar. On a second pass, ask only whether the previously reported findings were fixed — do not hunt for new issues you could have raised the first time. That is how a review loop runs forever.

## Report

```md
## Axes run
Standards, Spec, Security (diff touches tenant-scoped queries) — Fidelity skipped, no design pack

## Standards
- [must-fix] <finding> (`path:line`) — <why it matters>
- [should-fix] <finding> (`path:line`)
- [nice-to-have] <finding>

## Spec
- [must-fix] <requirement not met> — <what was asked for>
- [must-fix] <built but not asked for> — <what and why it is a problem>

## Security
- [must-fix] <finding> (`path:line`) — <what it would let someone do>

## Fidelity
- [should-fix] <difference from the chosen variant> — <what the pack showed>

## Verdict
pass | fail — <one line>
```

`fail` on a single must-fix on **any** axis. Write "None" rather than inventing findings to fill a section; an empty Standards section on a small, careful change is the correct output. Omit an axis's section entirely when it did not run, and say why in `## Axes run`.

---
*The two-axis structure and the one-pass-per-axis approach are adapted from [mattpocock/skills](https://github.com/mattpocock/skills) `engineering/code-review` (MIT).*
