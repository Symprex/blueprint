---
globs:
  - "**/*.cs"
  - "**/*.ts"
  - "**/*.tsx"
  - "**/*.vue"
  - "**/*.js"
  - "**/*.mjs"
---

# Changing behaviour

**No production behaviour change without a failing proof first.** If you did not watch the proof fail *for the right reason*, you do not know it proves the intended behaviour.

- Write the test before the production change, run it, and read the failure message. A test failing on a typo, a missing import or a null fixture proves nothing.
- A test that passes the moment you write it is testing something you already had. Either it is asserting the wrong thing or the behaviour was already present.
- **Every bug fix carries a regression test** that fails against the old behaviour.
- Tests written after the implementation do not satisfy this unless you recover: break the production change deliberately, watch the test fail, put it back.
- **If a change genuinely has no behaviour surface**, say so out loud and run the strongest alternative verification instead. Saying it is the escape hatch; skipping the test silently is not.

Applies to production behaviour: features, bug fixes, and refactors that change or protect behaviour. Not to documentation, generated output, or configuration with no behavioural surface.

# Writing the assertion

- **Expected values come from an independent source** — a known-good literal, a hand-worked example, the specification. Never from re-running the logic the way the implementation runs it.
- Apply the litmus to every assertion: **break the code in a plausibly wrong way — would this assertion break with it?** If not, it is decoration. Make it real or delete it.
- **Mock only at real boundaries**: external APIs, the database, time, randomness, the filesystem. Never an internal collaborator.
- Do not assert on a mock you configured in the same test.
- Do not add tests for implementation trivia — config constants, class-name presence, static wiring with no behavioural effect. They cost maintenance and prove nothing.

# Running the proof

- **Use the narrowest harness the scope supports.** Read the scope's `test` command from `.symprex/config.json` and apply the tightest filter it takes. Never the whole suite for the inner loop — a slow loop is a loop you will stop running.
- Re-run the focused proof, then the broader validation for the surface you changed.
- **Refactor only while green.**

<!--
Mechanisable, and therefore interim. These belong in tooling rather than in prose,
per adr/0016: a lint rule fails the build for every agent and every human at once,
where a rule only reaches whoever loaded it.

- "assert on a mock configured in the same test" — detectable by an oxlint rule over
  spec files, and by an analyser over xUnit tests.
- "no assertion in a test body" and "assertion that cannot fail" — partially detectable;
  the tautology case above is not, in general.
- "bug fix without a regression test" — not statically detectable, but a coverage
  gate on changed lines approximates it.
-->

---
*The independence rule and tautology litmus are adapted from [mattpocock/skills](https://github.com/mattpocock/skills) `engineering/tdd` (MIT). The loop and carry-forward rules come from the Signature365 `test-driven-development` skill.*
