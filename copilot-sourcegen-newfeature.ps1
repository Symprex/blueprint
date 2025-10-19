<# 
Usage:
  # Interactive (recommended)
  .\copilot-sourcegen-newfeature.ps1 -File '.\feature.md'
  .\copilot-sourcegen-newfeature.ps1 -Text "Add SG2001 when non-partial class is attributed"

  # Non-interactive (one-shot)
  .\copilot-sourcegen-newfeature.ps1 -File '.\feature.md' -NonInteractive -AllowAllTools -AddDir .\src

Notes:
  - Interactive mode copies the merged prompt to your clipboard and starts 'copilot'.
  - In Copilot, just press Ctrl+V and hit Enter.
#>

[CmdletBinding()]
param(
  [string]$File,
  [string]$Text,
  [string]$Template = ".copilot\prompts\sourcegen-feature-prompt.md",

  # Mode switches
  [switch]$NonInteractive,   # default is interactive

  # Optional Copilot CLI options (used only when -NonInteractive is set)
  [ValidateSet("gpt-5","claude-sonnet-4.5","claude-sonnet-4")]
  [string]$Model = "gpt-5",
  [switch]$AllowAllTools,
  [switch]$AllowAllPaths,
  [string[]]$AllowTool,
  [string[]]$AddDir
)

function Fail($m){ Write-Host "❌ $m" -ForegroundColor Red; exit 1 }

# Preconditions
if (-not (Get-Command "copilot" -ErrorAction SilentlyContinue)) {
  Fail "Copilot CLI not found. Install from https://github.com/github/copilot-cli and ensure 'copilot' is on PATH."
}
if (-not (Test-Path $Template)) {
  Fail "Template file not found: $Template"
}

# Load feature text
$feature = ""
if ($File) {
  if (-not (Test-Path $File)) { Fail "Feature file not found: $File" }
  Write-Host "📄 Loading feature description from: `"$File`"" -ForegroundColor Cyan
  $feature = Get-Content -Raw -LiteralPath $File
} elseif ($Text) {
  Write-Host "📝 Using inline feature description." -ForegroundColor Cyan
  $feature = $Text
} else {
  Fail "No feature description supplied. Use -File <path> or -Text <string>."
}

# Merge with template
$templateContent = Get-Content -Raw -LiteralPath $Template
$placeholder = '{{user_input}}'
if ($templateContent -like "*$placeholder*") {
  # Simple placeholder replacement; preserve newlines
  $merged = $templateContent -replace [regex]::Escape($placeholder), [System.Text.RegularExpressions.Regex]::Escape($feature).Replace('\r','`r').Replace('\n','`n')
  $merged = $merged -replace '`r', "`r" -replace '`n', "`n"
} else {
  $merged = $templateContent.TrimEnd() + "`r`n`r`n## Feature Description (from script)`r`n" + $feature
}

if (-not $NonInteractive) {
  # INTERACTIVE: copy to clipboard, save temp file, start Copilot
  Set-Clipboard -Value $merged
  $tmp = Join-Path $env:TEMP ("copilot_merged_prompt_" + [Guid]::NewGuid().ToString() + ".md")
  $merged | Set-Content -LiteralPath $tmp -Encoding UTF8
  Write-Host "✅ Prompt merged and copied to clipboard." -ForegroundColor Green
  Write-Host "🗂  Saved a copy here: $tmp" -ForegroundColor DarkGray
  Write-Host "🚀 Launching Copilot interactively. Paste with Ctrl+V, then Enter." -ForegroundColor Green
  & copilot
  exit $LASTEXITCODE
}

# NON-INTERACTIVE: use -p and explicit permissions
$cliArgs = @("--model", $Model)
if ($AllowAllTools) { $cliArgs += "--allow-all-tools" }
if ($AllowAllPaths) { $cliArgs += "--allow-all-paths" }
if ($AddDir)       { foreach ($d in $AddDir)   { $cliArgs += @("--add-dir", $d) } }
if ($AllowTool)    { foreach ($t in $AllowTool){ $cliArgs += @("--allow-tool", $t) } }

$cliArgs += @("-p", $merged)

Write-Host "⚙️  Running Copilot in NON-INTERACTIVE mode…" -ForegroundColor Yellow
& copilot @cliArgs
if ($LASTEXITCODE -ne 0) { Fail "Copilot exited with code $LASTEXITCODE" }
