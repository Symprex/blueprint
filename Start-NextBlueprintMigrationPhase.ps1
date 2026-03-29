param(
    [switch]$PreviewPromptOnly
)

Set-Location $PSScriptRoot

function Get-RequiredProgressValue
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value))
    {
        throw "migration\progress.json is missing required value '$Name'."
    }

    return [string]$Value
}

$promptPath = Join-Path $PSScriptRoot 'migration\next-phase-orchestrator-prompt.md'
$prompt = Get-Content $promptPath -Raw
$progressPath = Join-Path $PSScriptRoot 'migration\progress.json'
$reviewArtifactDirectory = Join-Path $HOME '.agent\diagrams'
$diagramDirectories = @(
    $reviewArtifactDirectory
)

if (Test-Path $progressPath)
{
    $progress = Get-Content $progressPath -Raw
    $progressData = $progress | ConvertFrom-Json -Depth 10

    $referencePlans = if ($null -ne $progressData.referencePlans) { $progressData.referencePlans } else { $progressData.sourceOfTruth }
    $masterPlan = Get-RequiredProgressValue -Value $referencePlans.masterPlan -Name 'referencePlans.masterPlan'
    $workstreamPlan = Get-RequiredProgressValue -Value $referencePlans.workstreamPlan -Name 'referencePlans.workstreamPlan'
    $activeWorkstream = Get-RequiredProgressValue -Value $progressData.current.activeWorkstream -Name 'current.activeWorkstream'
    $nextReadySlice = Get-RequiredProgressValue -Value $progressData.current.nextReadySlice.title -Name 'current.nextReadySlice.title'
    $lastCompletedSlice = Get-RequiredProgressValue -Value $progressData.current.lastCompletedSlice.title -Name 'current.lastCompletedSlice.title'

    $masterPlanPath = Join-Path $PSScriptRoot $masterPlan
    $workstreamPlanPath = Join-Path $PSScriptRoot $workstreamPlan

    if (-not (Test-Path $masterPlanPath))
    {
        throw "migration\progress.json points at a missing master plan: $masterPlan"
    }

    if (-not (Test-Path $workstreamPlanPath))
    {
        throw "migration\progress.json points at a missing workstream plan: $workstreamPlan"
    }

    $openReviewGate = if ($null -eq $progressData.current.openReviewGate -or [string]::IsNullOrWhiteSpace([string]$progressData.current.openReviewGate))
    {
        'none'
    }
    else
    {
        [string]$progressData.current.openReviewGate
    }

    $progressSummary = @"
<progress_summary path="$progressPath">
single_source_of_truth: migration\progress.json
master_plan: $masterPlan
active_workstream: $activeWorkstream
workstream_plan: $workstreamPlan
next_ready_slice: $nextReadySlice
last_completed_slice: $lastCompletedSlice
open_review_gate: $openReviewGate
</progress_summary>
"@

    $prompt = $prompt + "`r`n`r`n$progressSummary`r`n<progress_snapshot path=`"$progressPath`">`r`n$progress`r`n</progress_snapshot>"
}

$batchExecutionSummary = @"
<orchestration_batch_contract>
max_related_slices_per_invocation: 5
batching_rule: Prefer up to 5 related slices per launcher invocation, executing them one-by-one and refreshing migration\progress.json after each completed slice.
per_slice_loop:
- identify the current next ready slice from migration\progress.json
- write and observe the failing proof before production edits
- implement the minimum safe change
- verify, produce a visual review document, incorporate any review feedback, and commit if no explicit review gate remains open
- update migration\progress.json and continue with the next related slice when ordering/dependencies still allow it
stop_conditions:
- explicit human stop instruction
- an open human review gate recorded in migration\progress.json
- an absolutely CRITICAL approval need that remains unresolved after different sub-agents cannot agree on a safe next step
- unexpected repository changes or another concrete blocker
</orchestration_batch_contract>
"@

foreach ($diagramDirectory in $diagramDirectories)
{
    New-Item -ItemType Directory -Path $diagramDirectory -Force | Out-Null
}

$reviewArtifactSummary = @"
<review_artifact_directory path="$reviewArtifactDirectory">
Write visual review handoffs and similar HTML review artifacts here. This directory is allow-listed on the Copilot command line for this launcher session.
</review_artifact_directory>
"@

    $prompt = $prompt + "`r`n`r`n$batchExecutionSummary`r`n`r`n$reviewArtifactSummary"

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

if ($PreviewPromptOnly)
{
    $prompt
    exit 0
}

copilot @commonArgs --model gpt-5.4 --interactive $prompt

if ($LASTEXITCODE -ne 0)
{
    Write-Host 'GPT-5.4 was not available; retrying with Claude Sonnet 4.6.' -ForegroundColor Yellow
    copilot @commonArgs --model claude-sonnet-4.6 --interactive $prompt
}
