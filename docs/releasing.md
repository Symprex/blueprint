# Releasing Blueprint

How a change gets from a pull request to a version on nuget.org, and the conventions that make the
release notes and the version number come out right.

## Pull request titles

Every PR title is [Conventional Commits](https://www.conventionalcommits.org/):

```text
type(scope): description
type(scope)!: description of a breaking change
```

- **`type`** is one of the values below. Lower case.
- **`scope`** is optional: the area touched, e.g. `validation`, `openapi`, `rebus`, `sourcegen`.
- **`!`** before the colon marks a breaking change: a consumer must change code to upgrade.
- **`description`** is the imperative, en-GB summary a release-note reader needs: `accept trailing-dot
  FQDNs in Url validation`, not `fix bug`.

| Type | Use for | Label applied | Release-note section | Implies |
| --- | --- | --- | --- | --- |
| `feat` | New consumer-visible capability | `enhancement` | Features | minor |
| `fix` | A bug fix | `bug` | Fixes | patch |
| `perf` | A performance improvement with no API change | `enhancement` | Features | patch |
| `docs` | Documentation only | `documentation` | Documentation | none |
| `refactor`, `test`, `build`, `ci`, `chore`, `revert` | Everything else | `maintenance` | Maintenance | none |
| any type with `!` | A breaking change | adds `breaking` | Breaking changes | major |

The **PR title** workflow (`.github/workflows/pr-title.yml`) fails a PR whose title does not match, and
applies the label from the type. Retitling a PR re-runs it and swaps the label, so **fix the title,
never the label**. Bot PRs (the skills sync) are exempt and labelled `maintenance`. The rules live in
`.github/scripts/pr-title.mjs`, with tests beside it.

Commits inside a PR need no particular format. The PR title is what reaches the release notes.

## Choosing the version

Blueprint follows [SemVer](https://semver.org/). The person releasing picks the bump; the workflow
does the arithmetic from the latest stable `v*` tag.

| Bump | When | Examples |
| --- | --- | --- |
| **major** | A consumer has to change code, config or project files to upgrade | A public type or member removed or renamed; generated output that no longer compiles against existing consumer code; a new required attribute or registration; a changed target framework; a diagnostic promoted to an error that existing code trips |
| **minor** | New capability that existing consumers can ignore | A new attribute, API, diagnostic (warning or info) or generator feature |
| **patch** | Fixes and internal changes with no public-surface change | A validation regex fix; a generator bug fix whose output for valid code is unchanged; a performance fix |

Take the **largest** bump any PR since the last release implies: any `breaking` label means major,
any `feat` title means at least minor. To list what is unreleased:

```powershell
git fetch --tags
$since = git log -1 --format=%cI (git describe --tags --abbrev=0 --match 'v*' --exclude '*-*' origin/main)
gh pr list --repo Symprex/blueprint --state merged --search "merged:>$since" --json title,labels --jq '.[] | "\(.title)  [\([.labels[].name] | join(","))]"'
```

**Prereleases**: set `prerelease` to a SemVer label such as `rc.1` to publish `1.1.0-rc.1` for a
consumer to try before the stable version. Prerelease tags never act as the base: after
`v1.1.0-rc.2`, a stable `minor` bump from `v1.0.9` is still `1.1.0`.

## Publishing

Publishing is one manually started workflow, **Publish** (`.github/workflows/publish.yml`). Merging
to `main` never publishes, and pushing a tag by hand does nothing.

```powershell
gh workflow run publish.yml --repo Symprex/blueprint --ref main -f bump=patch
```

Or Actions → Publish → Run workflow, on `main`. Add `-f prerelease=rc.1` for a prerelease.

What it does, in order:

1. **Verify and version**: refuses to run off `main`, refuses a commit whose CI **Build and test**
   run did not succeed, then computes the version (`.github/scripts/release-version.mjs`). It
   refuses a version whose tag already exists.
2. **Pack**: tags the commit locally so MinVer stamps that version, builds Release, packs, and checks
   every package carries the version. It then refuses the version if **any** package in the set is
   already on nuget.org or GitHub Packages, and saves the packages as the run's artefact, which every
   later job pushes. It does not re-run tests: the **Require CI** ruleset makes **Build and test** a
   required, strict check on `main`.
3. **Push to GitHub Packages**: pushes the packed artefact.
4. **Publish to nuget.org**: waits in the `nuget` environment for **barclayadam** or
   **michaeldtaylor** to approve, then pushes the same artefact by Trusted Publishing. A nuget.org
   version can be unlisted but never deleted, so the approval is the last point to stop.
5. **Tag and create GitHub release**: creates the `vX.Y.Z` tag at the released commit and a GitHub
   release with generated notes, grouped by `.github/release.yml`, with the `.nupkg` files attached.

The tag is created last, so a run that fails or is rejected leaves no tag and no release behind. Its
packages may already be on a feed, though, and **Pack** refuses a version either feed
already has, so that one version is never two different builds. To recover:

- **The run failed or was rejected part-way:** open that run and use **Re-run failed jobs**. It reuses
  the run's own packages, so the feeds and the release get the same bytes.
- **You want different code under that version instead:** if it never reached nuget.org, delete the
  version from GitHub Packages and dispatch again. Once it is on nuget.org, the version is spent:
  dispatch the next one.

### Settings the workflow depends on

Changing any of these silently breaks publishing:

- **nuget.org Trusted Publishing policy** (Blueprint organisation on nuget.org): trusts repository
  `Symprex/blueprint`, workflow file `publish.yml`, environment `nuget`. Renaming the workflow file or
  environment needs the policy updated first.
- **`nuget` environment**: required reviewers barclayadam and michaeldtaylor; deployment branches
  limited to `main`.
- **Require CI ruleset**: requires the **Build and test** check. `publish.yml` looks the check up by
  that name, so renaming the job in `ci.yml` breaks both.

## Taking a new version in a consumer

In Signature365, every `Blueprint.*` package is pinned to the same version in
`backend/Directory.Packages.props`. Bump them all together, never one at a time.
