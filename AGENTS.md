# blueprint — agent instructions

**[`.github/copilot-instructions.md`](.github/copilot-instructions.md) is the canonical instruction file for this repository** — commands, conventions, the test matrix a pipeline slice owes, the docs entry points. Read it. This file exists so tools that look for `AGENTS.md` find their way there, and to record the things it does not cover.

## Repository conventions are in `.symprex/config.json`

Scopes, the format and test commands, and the commit gate. A Symprex workflow stage reads them from there rather than inferring them. They are the same commands `copilot-instructions.md` lists.

The stages are human-invoked: `/symprex:plan`, `/symprex:execute`, `/symprex:complete`. `.symprex/STATE.md` is the session's own scratch file, gitignored and ephemeral — it is not a record of anything, and `/symprex:plan` refuses to run until it is gitignored. Durable session state stays where `copilot-instructions.md` already puts it: `migration/progress.json`, the migration plans, and `.scratch/` for disposable files.

## Two sets of skills, and which one wins on .NET

- **`.claude/skills/`** — the vendored `symprex-practices` pack from [Symprex/ai-engineering](https://github.com/Symprex/ai-engineering), kept level by `.github/workflows/sync-agent-skills.yml`. Organisation-wide conduct standards. Do not edit them here; a local edit is overwritten by the next sync.
- **`.agents/skills/`** — this repository's own, including the full `dotnet-*` reference library. **Untouched, and the authority on .NET here.** Those files are substantially more detailed than any organisation-wide pack, which is why the `symprex-dotnet` pack is deliberately *not* vendored into this repository.

## Pull requests and releases

PR titles are Conventional Commits, and only the hand-started **Publish** workflow releases. Read [`docs/releasing.md`](docs/releasing.md) (or the `releasing-blueprint` skill) before titling a PR or publishing.
