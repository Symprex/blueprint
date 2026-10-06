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

#### Where else does this input arrive?

**For each guarantee this change adds, find every other path that reaches it.** A fix that holds where the author was looking and not one call site over is the commonest defect in a multi-part change, and it reads as correct in isolation — because it *is* correct, for the input its author had in mind.

Across one branch of nine tasks this was the shape of most must-fixes. Not wrong logic: incomplete reach. A `$null` guard that was load-bearing only via a chained call from a sibling script. A flag fixed to respect an unreadable row while the routing decision beside it still keyed on a literal token, so the same input took the destructive branch. Malformed rows made visible while an unclosed fence still swallowed them. A comparison whose permissive pre-flight and strict read-back disagreed, so it created issues and then refused them. Every one was found by a reviewer holding two files at once, and missed by every reviewer scoped to a single file.

**The cheap detector is mechanical, not editorial.** Before reasoning about it, grep for the other call sites of the symbol, the other readers of the field, the other end of the comparison. Several of those were found that way rather than by thinking, and one agent caught its own over-claim with it after two attempts at the same comment.

Concretely, ask it of:

- a helper the change added or altered — who else calls it, and with what shapes?
- a guard added to one read — are there sibling reads of the same value that skip it?
- a comparison — do both ends use the same rules? A case-insensitive probe against a case-sensitive read-back is two behaviours, not one.
- a branch on a signal — can the thing producing that signal report others?
- a reassurance in prose or a message — is it true on every branch that can print it, or only the one the author had open?

This sits in Spec rather than Standards because these are contract failures, not convention slips. It is the same question a seam review asks one level up, between tasks — a per-task reviewer is scoped away from its siblings and structurally cannot see the join. The same argument applies *within* one task, between two call sites of one helper or the two ends of one comparison.

### Security

Run this axis when the diff touches authentication, authorisation, data access, input handling, secrets, or any surface reachable from outside. When in doubt, run it — the cost is one extra pass.

- **Authorisation on every new entry point.** Does each new or changed operation carry the access control this repo requires, and is it *specific* enough? A check that grants more than the operation needs is a finding, not a convenience.
- **Tenant and owner isolation.** Can a caller reach another tenant's or another user's data through this path? Look for a query missing its scoping predicate.
- **Input validation.** Unvalidated input reaching a query, a command, a file path, or rendered output. Parameterised queries, not string concatenation. Missing null and range checks on things that came from outside.
- **Secret and data exposure.** Hardcoded credentials or connection strings. Sensitive values written to logs. Internal detail in an error response that reaches a client.
- **Insecure defaults.** Is new behaviour opt-in rather than opt-out, and does a new flag default to the safer side?

Judge severity by consequence, not by category: ask whether this would enable a breach, data exposure, privilege escalation or unauthorised access to real data if it shipped today. A theoretical weakness on an unreachable path is a note; a missing tenant predicate is a must-fix.

The repo's own rules define the mechanism — the attribute, the guard, the fixture. `dotnet-advisor` and `frontend-advisor` say where those rules live. Respect deliberate patterns recorded in repository-owned `AGENTS.md`, path-specific instructions or skills rather than reporting them every time.

### Fidelity

Run this axis only when a design pack is actually there to compare against. `## Prototype` in `.symprex/STATE.md` records the working paths where the work came through a prototype stage; the design pack is `SET.md` and the chosen variant's `DESIGN.md`. No `.symprex/STATE.md`, or no `## Prototype` section in it, means this axis does not apply — skip it and say so.

Compare the built thing against the chosen variant — labels, button text, icons, layout, the states the variant showed, and `DESIGN.md`'s rebuild notes.

Differences are not automatically findings. A prototype is a proposal, and implementation reveals things a prototype could not. Report a difference as **[must-fix]** only where it changes what the engineer approved; where the implementation is better, say that plainly and note the pack is now stale. What must never happen is a silent divergence: the pack gets archived as though it were honoured, and the next person reads it as a description of the code.

If no design pack exists, skip the axis and say so. Do not reconstruct a design from the diff.

## Out of scope

Lint, format, typecheck and build. Those belong to the repo's own automation — the formatter that runs on edit, and the commit gate — and re-reporting them here trains people to skim reviews.

Whether it actually runs. That is verification, and it is a separate job.

## The bar

"Done right", not "good enough" — but only what blocks may be written as blocking. **`[must-fix]` is for a finding you can name a consequence for**: it changes behaviour, it misleads a reader, or it breaches a rule this repo states. Everything else is `[should-fix]` or `[nice-to-have]`, and neither of those fails a review. A finding whose whole case is that you would have written it differently is a `[nice-to-have]` at most, however strongly you would have.

That is not a lower bar. It is the same bar, applied to the label — "everything worth fixing blocks" with must-fix stretched to cover taste produces a review nobody can pass, and a task that cannot pass is a task that gets reviewed five times.

**A second pass on the same change is a confirm pass and nothing else.** For each finding you already raised: fixed, or not fixed, with the evidence. Do not re-run the axes and do not raise anything new. Anything you notice anyway goes in a `## Carried` list at the end — unranked, no verdict attached, for whoever holds the loop to schedule. Hunting fresh findings on a second pass is how a review loop runs forever, and no instruction to be proportionate prevents it: a reviewer that has not seen the change before cannot confirm anything, so the caller must resume the reviewer that raised the findings rather than dispatch a new one.

Prose diffs — documentation, skills, agent instructions, ADRs — get the same bar, and it binds harder there because nothing compiles. Ask what a reader would **do** wrong. Sentence length, word choice and table alignment are not findings unless the repo states a rule about them.

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

## Carried
- <noticed on a confirm pass, outside the findings under confirmation — no verdict>

## Verdict
pass | fail — <one line>
```

`fail` on a single must-fix on **any** axis. Write "None" rather than inventing findings to fill a section; an empty Standards section on a small, careful change is the correct output. Omit an axis's section entirely when it did not run, and say why in `## Axes run`. `## Carried` appears only on a confirm pass, and never affects the verdict.

---
*The two-axis structure and the one-pass-per-axis approach are adapted from [mattpocock/skills](https://github.com/mattpocock/skills) `engineering/code-review` (MIT).*
