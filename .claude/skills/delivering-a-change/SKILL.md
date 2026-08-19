---
name: delivering-a-change
description: What a change must contain before it is offered for review — the branch, the tests, the evidence, the scope, the pull request body. Use when preparing to open a pull request, when working autonomously on an assigned issue or task, or when deciding whether a change is finished. Not a substitute for test-driven-development or verifying-work, which govern how the work is done; this governs what is handed over.
---

# Delivering a change

The contract for handing work over. An agent working through an assigned issue has no human watching each step, so **this is the checklist that stands in for one.**

Nothing here replaces `test-driven-development` or `verifying-work`. Those govern how the work is done. This governs what may be handed over, and it assumes both were followed.

## Read the repository first

`AGENTS.md` at the repository root, and any `.github/instructions/*.instructions.md` whose `applyTo` matches the files being changed. **Where they conflict with anything here, the repository wins** — this describes how Symprex generally works; the repository describes how the thing being shipped actually works.

Commit format, branch naming, the pull-request template and the issue-linking rule are repository facts. Take them from there, not from habit.

## Before opening anything

- **A branch, never the default branch.** If the working tree is already on the default branch, branch before the first commit.
- **The change is the requested change.** No opportunistic refactors, no unrelated formatting, no drive-by renames. Read the diff before offering it and remove anything nobody asked for. A reviewer who has to separate your cleanup from your fix is doing work you created.
- **A test that failed before the change and passes after it**, for any behaviour change however small. If the change is genuinely untestable, say which and why in the pull request rather than leaving it unremarked.
- **The repository's own gate passes** — its build, its tests, its linters, run as CI runs them. Not a subset chosen for speed.
- **No secrets, tokens, keys or customer data** in the diff, the tests, the fixtures or the commit messages.

## What the pull request must say

Written for someone who was not there and will not ask.

- **What changed and why**, in that order, in the first paragraph.
- **What was verified, and how.** Name the commands and what they returned. "Tests pass" is not evidence; the failing-then-passing test is.
- **What was deliberately not done**, and why. Scope you declined, a follow-up you spun off, a case you could not reproduce.
- **What you are unsure about.** A reviewer told where to look reviews better than one told everything is fine.
- **Follow the repository's pull-request template and its issue-linking rule exactly.** A closing keyword only works as a literal line in the body — `Closes #1234`, in whatever section the template designates. An issue linked in the sidebar closes nothing, and neither does prose saying the change resolves it. **Never remove a `Closes #` line that is already there** — not when reformatting, not when updating the description. A missing link is usually the difference between an issue closing and an issue being forgotten.

## Do not

- **Do not force-push over a branch you did not create**, or rewrite history someone may have pulled.
- **Do not merge your own change** unless the repository says an agent may.
- **Do not mark work complete on a red gate.** Report the failure with its output instead. A failure surfaced early costs a comment; one discovered after merge costs a revert.
- **Do not silently narrow the task.** If part of it is blocked, deliver the rest in full and say plainly what was left and why. Scaling the work down is the requester's decision.

## When you are stuck

Say so, with what you tried and what you observed. Two failed attempts at the same wall is the signal to stop and report rather than try a third — an agent looping on a wrong assumption burns the reviewer's goodwill along with the budget.
