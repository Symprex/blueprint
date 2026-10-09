---
name: diagnosing-bugs
description: Diagnosis loop for hard bugs and performance regressions. Use when tests, builds, runtime behaviour or browser flows fail unexpectedly, or when the user says something is broken, throwing, failing, flaky or slow.
---

# Diagnosing bugs

**No fixes without root-cause investigation first.**

Six phases with gates between them. Skip a phase only when you can say why.

Start by reading the glossary at `config.docs.glossary` if there is one, plus any ADRs covering the area — a bug that looks like a defect is sometimes a deliberate decision you have not read yet.

## Redact before you show

Every phase here has you show something: the loop's invocation and its output, a probe's log line, a captured artifact. **Write `<REDACTED>` over every secret first**, and keep it out in the first place — build loops against environment variables (`$env:SYMPREX_API_KEY`), so the credential never enters the command you paste.

Captured artifacts are the sharp edge: a HAR carries every `Authorization` header and cookie of the session that produced it, a log dump carries connection strings. Quote only the lines that carry the signal, never the whole file.

If the redacted output is genuinely not enough to diagnose the bug, **say so and ask the engineer** rather than pasting the unredacted version. Diagnosis is worth a round trip; a token in a transcript, a PR comment or a commit message is a credential to rotate.

## Phase 1 — Build a feedback loop

**This is the skill.** Everything after it is mechanical.

With a tight pass/fail signal that goes red on *this* bug, you will find the cause. Without one, no amount of reading code will get you there — you will build a plausible theory, fix something adjacent, and believe you are done.

Ranked by preference:

1. A failing test at an existing seam
2. A script hitting the endpoint directly (`Invoke-WebRequest`, `curl`)
3. A CLI invocation with a fixture, diffed against known-good output
4. A headless browser walk
5. A replay of a captured trace, HAR or log
6. A throwaway minimal harness
7. A property or fuzz loop over many random inputs
8. A bisection harness for `git bisect run`
9. A differential loop — old version against new
10. A human-in-the-loop script, as a last resort

**Then tighten it.** Treat the loop as the product for a moment: make it faster, sharper (assert the *symptom*, not "did not crash"), and more deterministic (pin the clock, seed the RNG, isolate the filesystem, freeze the network). A 30-second flaky loop is barely better than nothing; a 2-second deterministic one finds the bug.

For a non-deterministic bug the goal is a **higher reproduction rate**, not a clean repro — loop it a hundred times, run it in parallel, inject delays at suspicious points. 50% is debuggable. 1% is not.

**The gate:** name one command you have **already run at least once**, and show its invocation and output, redacted. It must be red-capable, deterministic, fast and runnable by you.

No red-capable command, no Phase 2. If you catch yourself reading code to build a theory before that command exists — stop.

**If no loop is possible:** say so, list what you tried, and ask for what would make one possible — environment access, a **redacted** captured artifact (HAR, log dump, core dump, timestamped recording), or permission for temporary instrumentation. Then **stop**. Do not hypothesise into the void; a confident theory with no way to test it is the most expensive thing you can hand someone.

## Phase 2 — Reproduce and minimise

Confirm the loop reproduces the **user's** failure, not a nearby one. Wrong bug, wrong fix.

Then shrink to the smallest still-red scenario. Cut inputs, callers, config, data and steps **one at a time**, re-running after each cut. Done when every remaining element is load-bearing.

This pays twice: it shrinks the hypothesis space in Phase 3, and the minimised case becomes the regression test in Phase 5.

## Phase 3 — Hypothesise

**Generate 3–5 ranked hypotheses before testing any of them.** Producing one at a time anchors you on the first plausible idea, which is where most wasted debugging goes.

Each must be falsifiable, in this shape:

> If `<X>` is the cause, then `<changing Y>` will make the bug disappear, and `<changing Z>` will make it worse.

No prediction means it is not a hypothesis, it is a vibe.

Show the ranked list to the engineer — it is cheap, and they may re-rank it instantly or know something is already ruled out. Do not block on them if they are away.

## Phase 4 — Instrument

One probe per prediction. **One variable at a time.**

- A debugger or REPL first. One breakpoint beats ten logs.
- Then targeted logging at the boundary that distinguishes your hypotheses.
- Never "log everything and grep".

**Tag every debug log with a unique prefix** — `[DEBUG-a4f2]` — so cleanup is one search rather than an archaeology exercise.

**Performance regressions branch here.** Logs are usually the wrong tool. Establish a baseline measurement first — a timing harness, a profiler, a query plan — then bisect. Measure first, fix second; a performance "fix" with no before-and-after number is a guess.

## Phase 5 — Fix and regression-test

Write the test before the fix — **but only if a correct seam exists**: one that exercises the real bug pattern as it occurs at the call site. A seam that is too shallow gives false confidence, and a test at the wrong level will pass while the bug survives.

**If no correct seam exists, that is itself the finding.** The architecture is preventing the bug being locked down. Say so, and treat it as a structural problem rather than forcing a bad test.

Otherwise: minimised repro → failing test → watch it fail → smallest fix → watch it pass → re-run the Phase 1 loop against the **original, un-minimised** scenario.

## Phase 6 — Clean up and account for it

- The original reproduction is gone.
- The regression test passes, or the absent seam is documented.
- Every `[DEBUG-...]` line removed — search for the prefix.
- Throwaway harnesses deleted, **and any captured artifact with them**. A HAR left in the working tree is a live session token sitting in a repository.
- **The correct hypothesis stated in the commit or PR message.** This is how the next person learns what actually happened rather than just what changed.

Then ask what would have prevented it. If the answer is structural, say so with specifics — but say it **after** the fix, not instead of it.

---
*The six-phase structure, the Phase 1 gate, the debug-tagging convention and the redaction rule are adapted from [mattpocock/skills](https://github.com/mattpocock/skills) `engineering/diagnosing-bugs` (MIT). The one-hypothesis-at-a-time and escalation rules come from the Signature365 `systematic-debugging` skill.*
