Set-Location $PSScriptRoot

$promptPath = Join-Path $PSScriptRoot 'migration\next-phase-orchestrator-prompt.md'
$prompt = Get-Content $promptPath -Raw
$progressPath = Join-Path $PSScriptRoot 'migration\progress.json'
$diagramDirectories = @(
    (Join-Path $HOME '.agent\diagrams')
)

if (Test-Path $progressPath)
{
    $progress = Get-Content $progressPath -Raw
    $prompt = $prompt + "`r`n`r`n<progress_snapshot path=`"$progressPath`">`r`n$progress`r`n</progress_snapshot>"
}

foreach ($diagramDirectory in $diagramDirectories)
{
    New-Item -ItemType Directory -Path $diagramDirectory -Force | Out-Null
}

$commonArgs = @(
    '--experimental'
    '--allow-all-tools'
    '--enable-all-github-mcp-tools'
)

foreach ($diagramDirectory in $diagramDirectories)
{
    $commonArgs += '--add-dir'
    $commonArgs += $diagramDirectory
}

copilot @commonArgs --model gpt-5.4 --interactive $prompt

if ($LASTEXITCODE -ne 0)
{
    Write-Host 'GPT-5.4 was not available; retrying with Claude Sonnet 4.6.' -ForegroundColor Yellow
    copilot @commonArgs --model claude-sonnet-4.6 --interactive $prompt
}
