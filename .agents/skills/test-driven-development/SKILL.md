---
name: test-driven-development
description: Use before writing or changing any production code, however small — a new feature, a bug fix, a refactor, a one-line behaviour change, adding an option to a list. A reported bug triggers this immediately, before diagnosis and before any fix. Symprex forbids changing behaviour without watching a proof fail first, so this applies even when the change looks too small to test. Supplies the loop, how to pick the narrowest harness, and what counts as failing for the right reason.
---

# Test-driven development

**The rules are in [RULES.md](RULES.md), beside this file.** They are also emitted to `.github/instructions/` and `.claude/rules/` in every repository that vendors this pack, so they load whenever a source file is touched — whether or not this skill fires. Read them first; this file is the *how*, and does not restate them.

That split is deliberate. An obligation that depends on a skill activating is not an obligation: measured against a one-line behaviour change with a stack pack loaded, this skill's description did not fire in five runs out of five. The iron law now reaches the agent through a path-scoped rule instead.

## The loop

1. **Pick the narrowest proving harness** before editing production code. Read `.symprex/config.json` for the scope's `test` command and use the tightest filter it supports.
2. **Read before write.** Read the existing tests in the area and the code under test. What is already covered? What harness and fixtures do the neighbours use? A test that does not look like its neighbours gets maintained badly.
3. **Write one failing proof** that describes the intended behaviour.
4. **Run it and confirm it fails for the right reason.** Read the failure message rather than the exit code.
5. **Make the smallest production change** that makes it pass.
6. **Re-run the focused proof.**
7. **Re-run the broader validation** for the surface you changed.
8. **Refactor only while green.**

## Picking the harness

The narrowest thing that can observe the behaviour, in this order:

- **A unit test**, where the behaviour is a function of its inputs.
- **A component or composable test**, where it is rendering or reactivity.
- **An integration test against the real seam** — a real database, a real HTTP pipeline — where the behaviour *is* the seam. A mocked integration test proves the mock.
- **A browser test**, only where the behaviour is genuinely in the browser: focus, navigation, a CSS-dependent layout. Slow and flaky by comparison; do not reach for it because it is familiar.

Where the repository's own conventions name a harness for the surface you are changing, they win. This skill describes how Symprex generally proves behaviour; a repository describes how the thing being shipped is actually proved.

## What "fails for the right reason" looks like

```
// WRONG REASON — proves the fixture is broken
System.NullReferenceException at InvoiceTests.Setup line 14

// RIGHT REASON — proves the behaviour is absent
Expected: 1349
Actual:   0
```

The second failure names the behaviour you are about to build. The first names a mistake in the test. Only one of them is progress.

## Independent expected values, worked

```
// TAUTOLOGY — the code is asserted to agree with itself
expect(total(items)).toBe(items.reduce((a, i) => a + i.price, 0))

// INDEPENDENT — the code is asserted to agree with a fact
expect(total([{ price: 250 }, { price: 1099 }])).toBe(1349)
```

The first passes if `total` and the assertion share a bug. The second cannot: `1349` came from arithmetic done by hand, so agreeing with it is a claim about the world rather than about the code.

## Red flags

Stop and correct course if you catch yourself thinking:

- "I'll add the tests after"
- "This is too small to bother testing"
- "Manual testing is enough for this one"
- "I already know what the fix is"
- "Let me just get it working first"

The last one is the most dangerous, because it is usually true and always ends with the test being written to match whatever the code turned out to do.

---
*The independence rule and tautology litmus are adapted from [mattpocock/skills](https://github.com/mattpocock/skills) `engineering/tdd` (MIT). The loop comes from the Signature365 `test-driven-development` skill.*
