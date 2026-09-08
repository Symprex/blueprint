# Vendored Symprex skills

Everything listed below is **generated**. It is copied from the Symprex marketplace
by the `Symprex Skills Sync` workflow, and a hand edit is overwritten by the next run
without comment. To change one, open a pull request in
[Symprex/ai-engineering](https://github.com/Symprex/ai-engineering).

The same skills are emitted to `.agents/skills/` for Codex and to `.claude/skills/`
for Claude Code. Copilot reads both and gives `.agents/skills/` precedence. The two
copies are physical files rather than symlinks so Windows and cloud checkouts agree.

Anything under either root that is **not** listed here belongs to this repository,
and the export never touches it. `MANIFEST.json` beside this file is the
machine-readable ownership record and identifies the source commit.

The layout is flat because Agent Skills requires it: a skill is a single directory
directly under a discovery root holding a `SKILL.md`, so grouping these under a
folder per pack would make every one of them undiscoverable. `.gitattributes` marks them
generated instead, which is what collapses them in a pull request.

This file and `MANIFEST.json` are generated too, but they are deliberately left out
of that `.gitattributes` block, so GitHub does **not** collapse them. They are the
summary the collapsing exists to leave visible, and `MANIFEST.json` is the one file
that changes on every sync — mark them and a routine sync pull request shows nothing
at all.

## symprex-practices 0.12.0

- `agent-conduct`
- `code-review`
- `delivering-a-change`
- `diagnosing-bugs`
- `domain-context`
- `gh-stack`
- `shell-powershell`
- `test-driven-development`
- `verifying-work`
- `writing-for-agents`

