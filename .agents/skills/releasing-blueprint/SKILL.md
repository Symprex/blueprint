---
name: releasing-blueprint
description: Use when titling a Blueprint pull request, deciding whether a change is a major, minor or patch release, publishing a Blueprint version to nuget.org, or when a publish run, PR title check or release-notes section misbehaves
---

# Releasing Blueprint

The full guide is [`docs/releasing.md`](../../../docs/releasing.md). Read it before publishing. The rules
that must hold:

## PR titles

- **Conventional Commits: `type(scope): description`**, with `!` before the colon for a breaking
  change. Types: `feat`, `fix`, `perf`, `docs`, `refactor`, `test`, `build`, `ci`, `chore`, `revert`.
- Not the bracketed `[Area] Message` style used in Signature365. The PR title check fails it.
- The title drives the label and the release-note section. **Fix a wrong label by fixing the title.**
- Breaking means a consumer must change code to upgrade (see the table in `docs/releasing.md`). Mark it
  with `!`, or the release goes out as the wrong version.

## Publishing

- **Only the Publish workflow releases**:
  `gh workflow run publish.yml --repo Symprex/blueprint --ref main -f bump=<patch|minor|major>`.
  Never push a `v*` tag by hand. Nothing listens for it, and a hand-made tag makes the next computed
  version wrong.
- **Pick the bump from the unreleased PRs**, the largest any of them implies. When unsure between two,
  ask the human; do not guess down.
- **Starting a publish is a release decision. Get the human's explicit go-ahead first.** The nuget.org
  step needs barclayadam's or michaeldtaylor's approval anyway, and a nuget.org version cannot be
  deleted.
- **A run that failed or was rejected is recovered with "Re-run failed jobs" on that run**, not with a
  fresh dispatch. The fresh one is refused when the version is already on a feed, by design.
- Do not rename `publish.yml`, the `nuget` environment, or the `Build and test` job in `ci.yml`.
  Trusted Publishing, the environment and the CI ruleset are tied to those names.

## Version and title logic

`.github/scripts/release-version.mjs` and `.github/scripts/pr-title.mjs`, with `node:test` specs beside them.
Change them test-first, and run `node --test ".github/scripts/*.test.mjs"`. CI runs the same command in
**Build and test**.
