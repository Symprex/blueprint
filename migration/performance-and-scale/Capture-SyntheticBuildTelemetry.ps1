[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [ValidateRange(1, [int]::MaxValue)]
    [int]$OperationCount = 100,

    [string]$NamespaceName = 'Synthetic.Scale',

    [string]$AppName = 'SyntheticScaleApp',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function New-CleanDirectory
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path)
    {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Get-RepoRoot
{
    return Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
}

function Write-SyntheticProject
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectDirectory,

        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,

        [Parameter(Mandatory = $true)]
        [string]$NamespaceName,

        [Parameter(Mandatory = $true)]
        [string]$AppName,

        [Parameter(Mandatory = $true)]
        [int]$OperationCount
    )

    New-CleanDirectory -Path $ProjectDirectory
    New-Item -ItemType Directory -Path (Join-Path $ProjectDirectory 'Operations') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $ProjectDirectory 'Shared') -Force | Out-Null

    $runtimeModelProject = Join-Path $RepoRoot 'src\Blueprint.Runtime.Model\Blueprint.Runtime.Model.csproj'
    $runtimeProject = Join-Path $RepoRoot 'src\Blueprint.Runtime\Blueprint.Runtime.csproj'
    $sourceGeneratorProject = Join-Path $RepoRoot 'src\Blueprint.SourceGenerator\Blueprint.SourceGenerator.csproj'

    $projectPath = Join-Path $ProjectDirectory "$AppName.csproj"
    $projectContents = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>artifacts\generated</CompilerGeneratedFilesOutputPath>
    <BaseOutputPath>artifacts\bin\</BaseOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <Compile Remove="artifacts\generated\**\*.cs" />
    <ProjectReference Include="$runtimeModelProject" />
    <ProjectReference Include="$runtimeProject" />
    <ProjectReference Include="$sourceGeneratorProject" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
"@
    Set-Content -LiteralPath $projectPath -Value $projectContents -Encoding utf8

    $appSource = @"
namespace $NamespaceName
{
    [global::Blueprint.Model.BlueprintAppScan]
    public partial class $AppName : global::Blueprint.Model.BlueprintApp
    {
    }
}
"@
    Set-Content -LiteralPath (Join-Path $ProjectDirectory "$AppName.cs") -Value $appSource -Encoding utf8

    $sharedBaseSource = @"
namespace $NamespaceName
{
    public abstract class SyntheticOperationBase
    {
        [global::System.ComponentModel.DataAnnotations.Required]
        public string? Value { get; init; }
    }
}
"@
    Set-Content -LiteralPath (Join-Path $ProjectDirectory 'Shared\SyntheticOperationBase.cs') -Value $sharedBaseSource -Encoding utf8

    foreach ($operationIndex in 1..$OperationCount)
    {
        $operationName = 'SyntheticOperation{0:D3}' -f $operationIndex
        $operationSource = @"
namespace $NamespaceName
{
    public partial class $operationName : SyntheticOperationBase, global::Blueprint.Model.IQuery<string>
    {
        public static global::System.Threading.Tasks.Task<string> InvokeAsync()
        {
            return global::System.Threading.Tasks.Task.FromResult("$operationName");
        }
    }
}
"@
        Set-Content -LiteralPath (Join-Path $ProjectDirectory "Operations\$operationName.cs") -Value $operationSource -Encoding utf8
    }

    return $projectPath
}

function Invoke-BuildCapture
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,

        [Parameter(Mandatory = $true)]
        [string]$CaptureDirectory,

        [Parameter(Mandatory = $true)]
        [bool]$NoRestore
    )

    New-CleanDirectory -Path $CaptureDirectory
    $binLogPath = Join-Path $CaptureDirectory 'msbuild.binlog'
    $arguments = @(
        'build',
        $ProjectPath,
        '--nologo',
        '-v:minimal',
        '-c',
        $Configuration,
        "/bl:$binLogPath"
    )

    if ($NoRestore)
    {
        $arguments += '--no-restore'
    }

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $output = & dotnet @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $stopwatch.Stop()

    if ($exitCode -ne 0)
    {
        $renderedOutput = ($output | Out-String).Trim()
        throw "dotnet build failed for '$CaptureDirectory' with exit code $exitCode.$([Environment]::NewLine)$renderedOutput"
    }

    return [ordered]@{
        Duration = $stopwatch.Elapsed
        Output = (($output | Out-String).Trim())
        BinLogPath = $binLogPath
    }
}

function Copy-GeneratedArtifacts
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectDirectory,

        [Parameter(Mandatory = $true)]
        [string]$CaptureDirectory
    )

    $generatedSourceDirectory = Join-Path $ProjectDirectory 'artifacts\generated'
    $captureGeneratedDirectory = Join-Path $CaptureDirectory 'generated'
    New-CleanDirectory -Path $captureGeneratedDirectory

    if (Test-Path -LiteralPath $generatedSourceDirectory)
    {
        Copy-Item -Path (Join-Path $generatedSourceDirectory '*') -Destination $captureGeneratedDirectory -Recurse -Force
    }

    return $captureGeneratedDirectory
}

function Get-LineCount
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return [System.IO.File]::ReadAllLines($Path).Length
}

function Get-GeneratorSummaries
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$GeneratedDirectory
    )

    $generatorStats = @{}
    if (Test-Path -LiteralPath $GeneratedDirectory)
    {
        $generatedFiles = Get-ChildItem -LiteralPath $GeneratedDirectory -File -Recurse -Filter *.cs | Sort-Object FullName
        foreach ($generatedFile in $generatedFiles)
        {
            $generatorFolderName = Split-Path -Path $generatedFile.DirectoryName -Leaf
            $generatorNameParts = $generatorFolderName -split '\.'
            $generatorName = $generatorNameParts[$generatorNameParts.Length - 1]

            if (-not $generatorStats.ContainsKey($generatorName))
            {
                $generatorStats[$generatorName] = [ordered]@{
                    generatedOutputCount = 0
                    generatedOutputLineCount = 0
                    trackedStages = [ordered]@{}
                }
            }

            $generatorStats[$generatorName].generatedOutputCount++
            $generatorStats[$generatorName].generatedOutputLineCount += Get-LineCount -Path $generatedFile.FullName
        }
    }

    $orderedGeneratorStats = [ordered]@{}
    foreach ($generatorName in ($generatorStats.Keys | Sort-Object))
    {
        $orderedGeneratorStats[$generatorName] = $generatorStats[$generatorName]
    }

    return $orderedGeneratorStats
}

function Get-GeneratorVersion
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $generatorAssemblyPath = Join-Path $RepoRoot "src\Blueprint.SourceGenerator\bin\$Configuration\netstandard2.0\Blueprint.SourceGenerator.dll"
    if (-not (Test-Path -LiteralPath $generatorAssemblyPath))
    {
        return '0.0.0.0'
    }

    return [System.Reflection.AssemblyName]::GetAssemblyName($generatorAssemblyPath).Version.ToString()
}

function Write-TelemetryArtifacts
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$CaptureDirectory,

        [Parameter(Mandatory = $true)]
        [string]$NamespaceName,

        [Parameter(Mandatory = $true)]
        [string]$AppName,

        [Parameter(Mandatory = $true)]
        [int]$OperationCount,

        [Parameter(Mandatory = $true)]
        [string]$CaptureName,

        [Parameter(Mandatory = $true)]
        [TimeSpan]$Duration,

        [Parameter(Mandatory = $true)]
        [string]$GeneratorVersion,

        [Parameter(Mandatory = $true)]
        [string]$BinLogPath,

        [Parameter(Mandatory = $true)]
        [string]$GeneratedDirectory,

        [string]$MutationKind,

        [string]$MutationSourcePath
    )

    $generatorSummaries = Get-GeneratorSummaries -GeneratedDirectory $GeneratedDirectory
    $capturePayload = [ordered]@{
        captureName = $CaptureName
        isDesignTimeBuild = $false
        durationMs = [long][Math]::Round($Duration.TotalMilliseconds, [System.MidpointRounding]::AwayFromZero)
    }

    $payload = [ordered]@{
        scenario = [ordered]@{
            namespaceName = $NamespaceName
            appName = $AppName
            operationCount = $OperationCount
            sourceFileCount = $OperationCount + 2
            trackedGeneratorCount = $generatorSummaries.Count
            diagnosticCount = 0
        }
        capture = $capturePayload
        generators = $generatorSummaries
    }

    if ($MutationKind -and $MutationSourcePath)
    {
        $payload.mutation = [ordered]@{
            kind = $MutationKind
            sourcePath = $MutationSourcePath
        }
    }

    $manifest = [ordered]@{
        schemaVersion = 1
        artifactName = 'build-telemetry'
        artifactFamily = 'build-time-manifest'
        relativePath = '.blueprint/manifests/build-telemetry.manifest.json'
        app = [ordered]@{
            typeName = "$NamespaceName.$AppName"
            assemblyName = $AppName
        }
        generator = [ordered]@{
            productName = 'Blueprint.SourceGenerator'
            version = $GeneratorVersion
        }
        sections = [ordered]@{
            'synthetic-build-harness' = [ordered]@{
                contributorId = 'Blueprint.SourceGenerator.Tests.Scale'
                payload = $payload
            }
        }
        operations = [ordered]@{}
    }

    $manifestDirectory = Join-Path $CaptureDirectory '.blueprint\manifests'
    New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
    $manifestPath = Join-Path $manifestDirectory 'build-telemetry.manifest.json'
    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding utf8

    $generatedOutputCount = 0
    foreach ($generatorSummary in $generatorSummaries.Values)
    {
        $generatedOutputCount += $generatorSummary.generatedOutputCount
    }

    $timingSummary = [ordered]@{
        captureName = $CaptureName
        durationMs = $capturePayload.durationMs
        binLogPath = [System.IO.Path]::GetFileName($BinLogPath)
        manifestPath = '.blueprint/manifests/build-telemetry.manifest.json'
        generatedOutputCount = $generatedOutputCount
    }
    $timingSummary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $CaptureDirectory 'timing-summary.json') -Encoding utf8
}

function Invoke-CaptureScenario
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,

        [Parameter(Mandatory = $true)]
        [string]$ProjectDirectory,

        [Parameter(Mandatory = $true)]
        [string]$CaptureName,

        [Parameter(Mandatory = $true)]
        [bool]$NoRestore,

        [string]$MutationKind,

        [string]$MutationSourcePath
    )

    $captureDirectory = Join-Path $OutputDirectory $CaptureName
    $buildResult = Invoke-BuildCapture -ProjectPath $ProjectPath -CaptureDirectory $captureDirectory -NoRestore:$NoRestore
    $generatedDirectory = Copy-GeneratedArtifacts -ProjectDirectory $ProjectDirectory -CaptureDirectory $captureDirectory
    Write-TelemetryArtifacts `
        -CaptureDirectory $captureDirectory `
        -NamespaceName $NamespaceName `
        -AppName $AppName `
        -OperationCount $OperationCount `
        -CaptureName $CaptureName `
        -Duration $buildResult.Duration `
        -GeneratorVersion (Get-GeneratorVersion -RepoRoot $repoRoot) `
        -BinLogPath $buildResult.BinLogPath `
        -GeneratedDirectory $generatedDirectory `
        -MutationKind $MutationKind `
        -MutationSourcePath $MutationSourcePath
}

$repoRoot = Get-RepoRoot
New-CleanDirectory -Path $OutputDirectory

$workspaceDirectory = Join-Path $OutputDirectory 'workspace'
$projectDirectory = Join-Path $workspaceDirectory $AppName
$projectPath = Write-SyntheticProject `
    -ProjectDirectory $projectDirectory `
    -RepoRoot $repoRoot `
    -NamespaceName $NamespaceName `
    -AppName $AppName `
    -OperationCount $OperationCount

Invoke-CaptureScenario -ProjectPath $projectPath -ProjectDirectory $projectDirectory -CaptureName 'cold-build' -NoRestore:$false
Invoke-CaptureScenario -ProjectPath $projectPath -ProjectDirectory $projectDirectory -CaptureName 'no-op-build' -NoRestore:$true

$mutationIndex = [Math]::Min(50, [Math]::Max(1, $OperationCount))
$mutationSourcePath = 'Operations\SyntheticOperation{0:D3}.cs' -f $mutationIndex
$mutationFilePath = Join-Path $projectDirectory $mutationSourcePath
$mutationSource = Get-Content -LiteralPath $mutationFilePath -Raw
$updatedMutationSource = $mutationSource.Replace(
    'public static global::System.Threading.Tasks.Task<string> InvokeAsync()',
    'public static global::System.Threading.Tasks.Task<string> InvokeAsync(global::Blueprint.Runtime.Exec.ApiOperationContext context)')

if ($updatedMutationSource -eq $mutationSource)
{
    throw "Could not apply the synthetic single-operation mutation to '$mutationSourcePath'."
}

Set-Content -LiteralPath $mutationFilePath -Value $updatedMutationSource -Encoding utf8
Invoke-CaptureScenario `
    -ProjectPath $projectPath `
    -ProjectDirectory $projectDirectory `
    -CaptureName 'single-edit-build' `
    -NoRestore:$true `
    -MutationKind 'single-operation' `
    -MutationSourcePath $mutationSourcePath

Write-Host "Synthetic build telemetry captured in '$OutputDirectory'."
