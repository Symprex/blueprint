---
name: writing-for-agents
description: Reference for writing any document an agent reads — a skill, AGENTS.md or CLAUDE.md, a spec, a reference doc reached by a pointer. Covers context pointers and what makes them fire, the information hierarchy and progressive disclosure, completion criteria, leading words, and pruning. Use when creating, editing or reviewing a skill, when changing AGENTS.md or CLAUDE.md, or when adding a skill to the Symprex marketplace.
---

# Writing for agents

Every document here is read by an agent: a skill, `AGENTS.md`, `CLAUDE.md`, a spec, a reference doc reached by a pointer. The packaging differs; the writing does not. The same levers decide whether each one works, and the target is a **predictable process** — the agent taking the same route every run — not identical output.

**The rules are in [RULES.md](RULES.md), beside this file**, and are emitted to `.github/instructions/` and `.claude/rules/` so they load whenever an `AGENTS.md`, `CLAUDE.md`, instruction file, `SKILL.md` or rules file is touched — whether or not this skill fires. Read them first; this file is the *how*.

When the document is a skill, [references/skill-mechanics.md](references/skill-mechanics.md) covers what is specific to it: frontmatter, the invocation choice, router skills, and this marketplace's house rules.

Two things decide whether a document is any good: **whether it is reached at the right moment**, and **whether its body changes behaviour**. Everything below serves one or the other.

## Context pointers

A **context pointer** is a reference that names out-of-context material and encodes the condition for reaching it. A skill's `description` is one. A line in `AGENTS.md` naming a doc is the same object.

The pointer's **wording**, not its target, decides when the agent reaches the material and how reliably. Must-have material behind a weakly worded pointer is the most common failure in this repository, and it is invisible: the document is fine and simply never loads. Sharpen the wording first; inline the material only when sharpening fails.

A pointer does two jobs — say what the material is, and name the **branches** that should trigger reaching it. An always-loaded pointer costs on every turn of every engineer's session, so it earns harder pruning than the body:

- **Front-load the leading word.** The pointer is where it does its triggering work.
- **One trigger per branch.** Synonyms renaming a single branch are one branch written twice. Collapse them.
- **Cut identity the body already carries.**
- **Say what it is NOT for** when a sibling is confusable. `diagnosing-bugs` and `test-driven-development` both fire on "this test is failing" unless one rules itself out.
- `description` plus `when_to_use` is truncated at 1536 characters in the listing, and stops being read carefully long before that.

Then **prove it with an activation eval**. A pointer is a hypothesis about triggering, and `evals/activation/` is the harness for testing hypotheses. A description change is a behaviour change — assert both directions: what must fire, and which sibling must not.

## The two loads

Every document and pointer you add spends one of two budgets:

- **Context load** — the cost of always-loaded material on the window. A guideline's description, an `AGENTS.md` line: tokens and attention spent every turn whether or not it fires. This is the real cost of adding a guideline skill, and it is why the *count* of them matters more than the length of any one.
- **Cognitive load** — the cost on the engineer: which documents exist, and when to reach for each. The human is the index. Not a cost to minimise — it is the price of human agency. Spend it where human judgement matters, remove it where it does not.

Material reached only through a pointer escapes context load at the price of the pointer's own line. Material with no pointer at all rides entirely on cognitive load.

## The information hierarchy

A document is built from **steps** (ordered actions the agent performs) and **reference** (definitions, rules and facts consulted on demand). They mix freely — all steps, all reference, or both. The decision is where each piece sits on a ladder ranked by how immediately the agent needs it:

1. **In-file step** — the primary tier: what the agent does, in order.
2. **In-file reference** — consulted on demand. Often a legitimately flat peer-set, like every rule of a review on one rung. That is a fine arrangement, not a smell.
3. **Disclosed reference** — pushed into a separate file behind a pointer, loaded only when the pointer fires. A sibling `references/*.md`, or fully external material any document can point at.

Push too little down and the top bloats; push too much and you hide what the agent needs. That tension is the whole decision.

**Progressive disclosure** is the move down the ladder. Not primarily a token optimisation — it is how the hierarchy is protected. **Branching is the cleanest test: inline what every branch needs, and disclose what only some branches reach.** In a document with steps, in-file reference that should have been disclosed buries them, and attending to them becomes a coin-flip.

**Co-location** is the within-file companion. The ladder decides how far down a piece sits; co-location decides what sits beside it. Keep a concept's definition, rules and caveats under one heading rather than scattered, so reading one part brings its neighbours. Distinct from duplication: duplication repeats one meaning in two places, scattering fragments one meaning across many.

**Sprawl** is the failure mode: a document simply too long, even when every line is live and unique. Attention thins across the excess, and every extra line is one more to keep true. The cure is the ladder — disclose reference, and split by branch or sequence so each path carries only what it needs.

## Steps and completion criteria

Every step ends on a **completion criterion** — the condition telling the agent the work is done. Two properties make it a lever:

**Clarity** — can the agent tell done from not-done? A vague bound ("understanding reached") invites **premature completion**: ending the step before it is genuinely done, attention slipping to *being done*. The visible steps still ahead supply the pull; the criterion's clarity is the resistance. Defend in order — **sharpen the bound first**, because it is local and cheap. Only if it is irreducibly fuzzy *and* you observe the rush, split the sequence to hide the later steps. Hiding works only across a real context boundary: a hand-off or a subagent dispatch. An inline call leaves the later steps in context and clears nothing.

**Demand** — how much it requires. "Every modified model accounted for" forces thorough work where "produce a change list" does not. Demand drives the **legwork** the agent does within a step, latent in the wording rather than written as its own step. It is not step-bound: "every rule applied" binds a body of flat reference exactly as "every step done" binds a sequence, which is how an all-reference document still carries an exhaustiveness bar.

The strongest criteria are both checkable and exhaustive. An **acceptance checklist** at the end of a procedural skill is the cheapest way to get both, and it remains the single highest-value structural device available here — a self-audit mirroring the body catches the step that got skipped.

## When to split

Splitting spends one of the two loads, so split only when the cut earns it:

- **By sequence** — split a run of steps where the later ones tempt the agent to rush the one in front of it. Beware the reverse: merging sequences exposes each step to what follows, inviting premature completion.
- **By branch** — detail only one branch needs goes to a sibling `references/*.md`. The body stays scannable and the detail loads only when that branch is taken.
- **By invocation** — skill-specific; see [references/skill-mechanics.md](references/skill-mechanics.md).

Do not split because a file feels long. **Thin aliases over a fat primitive** work well when several framings should reach one behaviour: one skill holds the logic, short entry points delegate.

## Leading words

A **leading word** is a compact concept already in the model's pretraining that the agent thinks with while running the document — *lesson*, *fog of war*, *tracer bullets*, *tight*, *red*. Repeated as a token, never as a sentence, it accumulates a distributed definition and anchors a region of behaviour in very few tokens, by recruiting priors the model already holds. Coining your own works if you define it clearly, but a made-up word recruits nothing: you pay in definition tokens what a pretrained word gives free.

It anchors twice. In the body, *execution*: the agent reaches for the same behaviour every time the word appears. In a pointer, *invocation*: when the same word lives in your prompts, your docs and your codebase, the agent links that shared language to the material and reaches it more reliably.

Hunt for passages that collapse into one token. A triad spelled out at three sites, a pointer spending a sentence to gesture at one idea:

- "fast, deterministic, low-overhead" → *tight* (a *tight* loop).
- "a loop you believe in" → *red* — a fuzzy gate becomes a binary observable state.

Also put the decision at the **start of the line**, so it survives skimming: "**Never** commit during a task" beats "During a task, you should not commit."

**State the failure mode.** A rule with a reason gets followed under pressure; a rule without one gets rationalised away. "Never run a working-tree-wide revert — it erases whatever a parallel task has in flight" is followed. "Avoid reverting" is not. Where the point is subtle, a ❌/✅ pair teaches faster than prose.

## Negation, and where this repository stands

Steering by prohibition drags the forbidden behaviour into context and makes it *more* available. *Don't think of an elephant*, and the elephant is all there is: the negation is a weak modifier that the strongly-activated concept overruns, so the ban half-reads as an instruction. **Prompt the positive** — state the target behaviour so the banned one is never spoken.

This cuts against a house style full of `Never`, so the position is explicit rather than left to taste:

- **Keep the prohibition for a hard guardrail** — destructive git operations, publishing without being asked, a hook that must not block. These are the cases where the cost of the forbidden action is severe and there is no positive phrasing that carries the same force.
- **Pair every one with its positive target**, so attention lands on what to do. "Never `reset --hard` someone else's work — show `git status` and `git diff`, then ask" works. The bare ban does less.
- **Stop reaching for negation as ordinary guidance.** "Do not write long descriptions" is a no-op wearing a prohibition. "Front-load the leading word" changes what gets written.

## Pruning

- **Single source of truth.** One authoritative place per meaning, so changing behaviour is a one-place edit. **Duplication** costs maintenance and tokens, and inflates a meaning's prominence past its real rank. It is the accidental inverse of a leading word, which repeats a token on purpose and never the meaning.
- **The environment is a source of truth too** — `.symprex/config.json`, `package.json` scripts, the directory layout, `--help` output. A document restating it is a **cache**: a copy of a lookup, earning its load only when the lookup is expensive. Cache what the agent cannot find by looking — the unwritten convention, the reason behind a choice, the gotcha no config confesses. Leave the one-command lookups to the environment, where they cannot go stale. **Give the command that produces a number rather than the number.**
- **Relevance, line by line.** Does it still bear on what the document does? A line loses relevance by never bearing on the task, or by going stale as the world it describes changes. Without a pruning discipline the default fate is **sediment**: stale layers that settle because adding feels safe and removing feels risky.
- **Hunt no-ops.** An instruction the model already obeys by default pays load to say nothing — "write clean code", "consider edge cases", "be thorough". The test is model-relative, not reader-relative: two people disagreeing about a no-op disagree about the default, and settle it by running the document, not by debating it. When a sentence fails, delete the whole sentence rather than trimming words. The test grades leading words too: a word too weak to beat the default (*be thorough*, when the agent is already thorough-ish) is a no-op, and the fix is a stronger word (*relentless*), not a different technique.
- **Assume a capable reader.** Opus-class orchestration does better with a thin, sharp prompt than an exhaustive one. State the gate, the artifact and the failure mode; leave the method alone. Over-constraining produces worse work, not safer work.

## Failure modes

- **A pointer that never fires.** Common and invisible. Only an eval catches it.
- **A pointer that fires constantly.** Engineers learn to distrust the whole plugin.
- **Advice with no teeth.** No gate, no artifact, no stated consequence — it will not survive a long session.
- **Documenting the tool instead of the decision.** Explaining what a command does without saying when to reach for it is a man page.
- **Drift from reality.** A document naming a file, flag or command that no longer exists is worse than none, because it will be followed. Check before you cite.

---
*The context-pointer model, the two loads, the information hierarchy, completion criteria, leading words, negation and the pruning levers are adapted from [mattpocock/skills](https://github.com/mattpocock/skills) `productivity/writing-for-agents` (MIT), plus Anthropic's `skill-creator`.*
