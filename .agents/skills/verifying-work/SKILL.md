---
name: verifying-work
description: Use before claiming work is fixed, passing, complete, ready to commit, or ready for review. Requires fresh verification evidence for the exact claim being made.
---

# Verifying work

**Evidence before claims, always.**

If you have not run the proving command after the latest change, you cannot honestly say the work passes. Not "it should", not "it looks right" — you do not know.

## When this applies

Before saying a bug is fixed, tests pass, a build succeeds, a type-check is clean. Before committing, pushing, or asking for review. Before moving on to the next task.

## The gate

1. **Identify the exact command** that proves the claim you want to make.
2. **Run it fresh**, after the latest edit.
3. **Read the full output.** Not the last line, not the exit code alone. A suite can report success while having skipped the tests you care about, and a build can succeed against stale artifacts.
4. **Confirm the output proves the claim** — the specific claim, not a neighbouring one.
5. **Then state the result, with the evidence.**

## Match the evidence to the claim

| Claim | What it takes |
|---|---|
| "Bug fixed" | The original failing proof passes now |
| "Tests pass" | Output from the relevant test command, from this run |
| "Type-check passes" | Output from the type-check command itself |
| "Ready for review" | Validation across the whole changed surface, not just the narrowest test |
| "It works" | The behaviour observed, in the running system |

A narrower claim you can actually prove beats a broader one you cannot. "The three CSV tests pass; I have not run the integration suite" is useful. "Everything works" is not.

## Not verification

- "Should pass now"
- "Looks correct"
- Output from before the latest edit
- Partial validation when the claim is broader than the command
- **A delegated worker's success report.** Something else saying it passed is a claim, not evidence. Read the output it produced, or run the command yourself.
- A green suite you have not sanity-checked. If it passed, name one change to the production code that *should* turn it red — and if the tests would not catch that, it passed for the wrong reason.

## When automation would be brittle

Say so explicitly, give the reason, and provide fresh alternative evidence for the exact claim — a lint or type-check run, a story, a runtime observation. The escape hatch is stating it out loud. Quietly downgrading to "looks fine" is not the escape hatch.

---
*Adapted from the Signature365 `verification-before-completion` skill.*
