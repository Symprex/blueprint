---
name: domain-context
description: Build and sharpen a project's domain model — pin down terminology in the glossary and record architecture decisions as ADRs. Use when the user wants to settle domain vocabulary or a ubiquitous language, capture an architectural decision, or when another skill needs the domain model maintained.
---

# Domain context

Actively build and sharpen the project's domain model as you work: challenge terms, invent edge-case scenarios, and write things down the moment they crystallise.

This is the *active* discipline. Merely reading the glossary for vocabulary is a habit any skill can have; this skill is for when you are **changing** the model, not consuming it.

## Where things live

Read `.symprex/config.json`:

- `docs.glossary` — the glossary. Commonly `GLOSSARY.md` at the root, or `wiki/GLOSSARY.md`.
- `docs.adr` — the ADR directory.

Neither configured? Ask where they should go, then propose adding them to the config so the next session does not have to ask.

Create files **lazily** — only when there is something to write. No glossary yet? Create it when the first term is resolved. No ADR directory? Create it when the first ADR is needed.

## During the work

**Challenge against the glossary.** When someone uses a term that conflicts with what the glossary already says, call it out immediately: "the glossary defines *cancellation* as X, but you seem to mean Y — which is it?" A glossary that quietly disagrees with how people talk is a glossary nobody consults.

**Sharpen fuzzy language.** When a term is vague or overloaded, propose a precise one: "you are saying *account* — do you mean the Customer or the User? Those are different things here."

**Stress-test with scenarios.** When domain relationships are being discussed, invent specific awkward cases that force precision about where one concept ends and the next begins. "What happens when a partially-cancelled Order is refunded?" finds boundary problems that abstract discussion does not.

**Cross-reference the code.** When someone states how something works, check whether the code agrees. A contradiction is worth surfacing: "your code cancels whole Orders, but you just said partial cancellation is possible — which is right?"

## Updating the glossary

Write terms **inline, as they resolve.** Never batch them up — batched updates are the ones that get dropped when a session runs long.

```md
# <Context name>

<One or two sentences: what this context is and why it exists.>

## Language

**Order**:
A customer's committed request for goods, priced and confirmed.
_Avoid_: Purchase, Transaction

**Invoice**:
A request for payment issued after delivery.
_Avoid_: Bill, Payment request
```

Rules:

- **Be opinionated.** Pick one word and list the rest under `_Avoid_`. A glossary offering three acceptable synonyms has not decided anything.
- One or two sentences. **Define what it IS, not what it does.**
- Project-specific terms only. General programming concepts do not belong.
- Group under subheadings once clusters emerge.

**The glossary is a glossary and nothing else.** No implementation details, no spec content, no scratch notes. The moment it becomes a design document, people stop being able to look a word up in it — which was the only reason it existed.

## Offering ADRs

Offer one only when **all three** are true:

1. **Hard to reverse** — changing your mind later costs something real.
2. **Surprising without context** — a future reader will ask "why is it like this?"
3. **The result of a real trade-off** — there were genuine alternatives, and one was chosen for stated reasons.

Any one missing? Skip it. ADRs for routine decisions are how a team learns to stop reading ADRs, and then the one that mattered goes unread too.

Offer, do not write unasked: the title and the decision in one line each, and let the engineer choose.

```md
# <NNNN>. <Title in the form of the decision taken>

Date: <YYYY-MM-DD>
Status: Accepted

## Context
<The forces at play. What made this a decision rather than an obvious step.>

## Decision
<What was decided, in the active voice: "We will …">

## Consequences
<What becomes easier, what becomes harder, and what this commits us to.>

## Alternatives considered
<Each real alternative and the specific reason it lost.>
```

The alternatives section is what makes an ADR worth keeping. Without it, a future reader cannot tell whether their "obvious" idea was already considered and rejected, or simply never occurred to anyone.

---
*Adapted from [mattpocock/skills](https://github.com/mattpocock/skills) `engineering/domain-modeling` (MIT).*
