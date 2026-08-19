# Skill mechanics

The skill-specific branch of [writing for agents](../SKILL.md): what changes when the document is a skill. Everything else about writing it is the universal reference in `SKILL.md`.

## Choose the invocation mode first

| Frontmatter | Human can invoke | Agent can invoke | Description in context |
|---|---|---|---|
| *(neither key)* | Yes | Yes | Always |
| `disable-model-invocation: true` | Yes | No | Only when invoked |
| `user-invocable: false` | No | Yes | Always |

The two choices trade the two loads against each other:

- **Model-invoked** keeps a `description`, so the agent can fire it and other skills can reach it. Model invocation *includes* human reach — a description only ever adds agent discovery, never removes the human's. The description is the skill's top-level context pointer, permanently loaded: context load in exchange for discoverability. Mechanics: omit `disable-model-invocation` and write a model-facing description carrying the trigger branches, under the pointer rules in `SKILL.md`.
- **User-invoked** strips the description from the agent's reach. Only a human typing its name can invoke it, and no other skill can. Zero context load, but it spends cognitive load: the engineer is the index that must remember it exists. Mechanics: set `disable-model-invocation: true`; the `description` becomes human-facing, a one-line summary with trigger lists stripped.

Pick model invocation only when the agent must reach the skill on its own, or another skill must. If it only ever fires by hand, make it user-invoked and pay no context load.

In this marketplace that resolves to two standing rules:

- **Workflow stages** — `disable-model-invocation: true`. A stage that starts itself because the conversation drifted near it is a disaster; the human decides when a session begins.
- **Guidelines** — neither key. They exist to fire when relevant, which is the whole point.

Setting both keys makes the skill uninvokable. Do not.

**Shared reference two user-invoked skills both need can live in neither** — with no descriptions, neither can fire the other. Push it to a plain file outside the skill system, and point at it from both.

## Splitting by invocation

The invocation cut, alongside the sequence and branch cuts in `SKILL.md`: split off a model-invoked skill when you have a distinct leading word that should trigger it on its own — a word you actually type in prompts — or when another skill must reach it. You pay context load for a new always-loaded description, so that independent reach has to be worth it.

## Router skills

When user-invoked skills multiply past what an engineer can remember, that piled-up cognitive load is cured by a **router skill**: one user-invoked skill naming the others and when to reach for each, so there is one thing to remember instead of many. It can only hint, never fire them — user-invoked skills have no description, so nothing but the human can reach them.

## House rules for the Symprex marketplace

These apply when authoring in `Symprex/ai-engineering` itself. They are stated in full rather than linked, because this file is vendored into product repositories where those links do not resolve.

- `name` in frontmatter **must match the directory name** — it sets the slash command, and a mismatch is almost always a mistake. `Invoke-RepoChecks.ps1` enforces it.
- **No `model` or `effort` on a skill.** Skills inherit the session tier so the engineer's choice is respected. Agents pin theirs instead, which is what keeps a worker's tier a property of the agent rather than of whoever invoked it.
- Every command shown to an engineer is PowerShell — see `shell-powershell`.
- Read repo conventions from `.symprex/config.json`, or `.symprex/prototype.json` for anything prototyping. Never hard-code a path, command or tool name belonging to one product.
- **Attribute adapted work** in a footer naming the source and its licence, in the file itself rather than in a parent document — the packs are redistributed, so the notice has to travel with the copy. Never strip an attribution footer to tidy a skill up.
- **Bump the plugin's `version` in the same change.** Organization sync only picks up a marketplace change when a version bump merges to the default branch, so without it the work reaches nobody. CI enforces this.

## Plugin-shipped agents

`hooks`, `mcpServers` and `permissionMode` are **silently ignored** in a plugin-shipped agent. Their presence is a bug, and `Invoke-RepoChecks.ps1` fails on them.

Supported: `name`, `description`, `model`, `effort`, `maxTurns`, `tools`, `disallowedTools`, `skills`, `memory`, `background`, `isolation`.

`skills:` preloads the **full content** of a skill into the agent at startup, not just its description — a deliberate spend of context load on a worker that will definitely need it.

---
*The invocation trade-off, the splitting-by-invocation cut and the router-skill pattern are adapted from [mattpocock/skills](https://github.com/mattpocock/skills) `productivity/writing-for-agents` (MIT).*
