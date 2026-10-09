---
name: shell-powershell
description: Use when composing any shell command for a Symprex engineer to run, or when writing commands into documentation, a README, a PR body or a skill. Symprex engineers run PowerShell on Windows; a bash one-liner is a command that fails when pasted.
---

# Shell commands are PowerShell

Symprex engineers work in PowerShell 7 (`pwsh`) on Windows. **Every command you hand an engineer must be PowerShell**, in every repository, always.

**The rule is in [RULES.md](RULES.md), beside this file**, and is emitted to `.github/instructions/` and `.claude/rules/` so it loads whenever a script or a markdown file is touched — whether or not this skill fires. This file carries the translations.

This includes commands in documentation, README files, PR bodies, issue comments, commit messages and other skills — anywhere a human might copy and paste. Internal tool use may use whatever shell the tool needs; this is about what you *give* people.

## Substitutions that matter

| Instead of | Use |
|---|---|
| `export VAR=value` | `$env:VAR = 'value'` |
| `VAR=x command` | `$env:VAR = 'x'; command` — PowerShell has no inline env-var prefix |
| `command 2>/dev/null` | `command 2>$null` |
| `mkdir -p path` | `New-Item -ItemType Directory -Force path` |
| `which tool` | `(Get-Command tool).Source` |
| `cmd \| head -20` | `cmd \| Select-Object -First 20` |
| `cmd \| tail -20` | `cmd \| Select-Object -Last 20` |
| `wc -l file` | `(Get-Content file \| Measure-Object -Line).Lines` |
| `touch file` | `if (-not (Test-Path file)) { New-Item -ItemType File file }` |
| `rm -rf path` | `Remove-Item -Recurse -Force path` — and see the warning below |
| `ln -s target link` | `New-Item -ItemType SymbolicLink -Path link -Target target` |
| `if [ -f x ]` | `if (Test-Path x)` |
| `` `cmd` `` | `$(cmd)` |

`&&` and `\|\|` work as expected in PowerShell 7. Ternary (`$c ? $a : $b`), null-coalescing (`??`) and null-conditional (`?.`) are available.

## Multi-line strings

Use a **single-quoted here-string** so `$` and backticks are not expanded. The closing `'@` **must be at column 0** — indenting it is a parse error.

```powershell
git commit -F - @'
[Export] Add CSV export to the reports page

Handles the $literal dollar sign correctly.
'@
```

This one is not pedantry. `@'...'@` is a valid PowerShell here-string but bash takes it literally and prepends a stray `@` to the commit subject — which then fails the subject-format check for a reason nobody can see.

For anything long, write a temp file and use `git commit -F <file>`.

## Capture output you will need to read

```powershell
dotnet build 2>&1 | Tee-Object -FilePath build.log
```

PowerShell's TTY buffering drops output from long-running processes, so a build that failed can look like it produced nothing. If you intend to read the output, capture it.

## Escaping and quoting

- Escape character is the backtick, not the backslash.
- Quote any path containing spaces with double quotes.
- Call an executable whose path has spaces via the call operator: `& "C:\Program Files\App\app.exe" arg`.
- For arguments PowerShell would parse as operators, use the stop-parsing token: `git log --% --format=%H`.

## Exit codes

`-ErrorAction SilentlyContinue` hides the error *output* but the cmdlet still fails, and the shell still reports a non-zero exit. To make a failure genuinely non-fatal:

```powershell
try { Cmdlet -ErrorAction Stop } catch { }
```

Without `-ErrorAction Stop`, a non-terminating error skips the `catch` entirely.

## Never hand over

- `Read-Host`, `Get-Credential`, `Out-GridView`, `pause` — anything that blocks on input.
- `New-Item -Force` against an existing **file**. It truncates the content.
- `Remove-Item -Recurse -Force`, `git reset --hard`, `git clean -fd` — Symprex tooling blocks these for good reason, and the reason holds where nothing blocks them. If the engineer genuinely needs one, tell them the command and let them run it themselves.
