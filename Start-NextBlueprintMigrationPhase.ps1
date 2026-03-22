Set-Location $PSScriptRoot

$promptPath = Join-Path $PSScriptRoot 'migration\next-phase-orchestrator-prompt.md'
$prompt = Get-Content $promptPath -Raw
$progressPath = Join-Path $PSScriptRoot 'migration\progress.json'

if (Test-Path $progressPath)
{
    $progress = Get-Content $progressPath -Raw
    $prompt = $prompt + "`r`n`r`n<progress_snapshot path=`"$progressPath`">`r`n$progress`r`n</progress_snapshot>"
}

$commonArgs = @(
    '--experimental'
    '--allow-all-tools'
    '--enable-all-github-mcp-tools'
)

copilot @commonArgs --model gpt-5.4 --interactive $prompt

if ($LASTEXITCODE -ne 0)
{
    Write-Host 'GPT-5.4 was not available; retrying with Claude Sonnet 4.6.' -ForegroundColor Yellow
    copilot @commonArgs --model claude-sonnet-4.6 --interactive $prompt
}
