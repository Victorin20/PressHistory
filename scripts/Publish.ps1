[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$pressHistoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$pressHistoryProject = Join-Path $pressHistoryRoot 'src\PressHistory\PressHistory.csproj'
$pressHistoryOutput = Join-Path $pressHistoryRoot "artifacts\PressHistory-$Runtime"
$pressHistorySelfContained = if ($SelfContained) { 'true' } else { 'false' }

$pressHistoryArguments = @(
    'publish',
    $pressHistoryProject,
    '--configuration', 'Release',
    '--runtime', $Runtime,
    '--self-contained', $pressHistorySelfContained,
    '--output', $pressHistoryOutput,
    '-p:PublishSingleFile=true',
    '-p:PublishTrimmed=false',
    '-p:NuGetAudit=false'
)

& dotnet @pressHistoryArguments
if ($LASTEXITCODE -ne 0) {
    throw "La publication de PressHistory a echoue avec le code $LASTEXITCODE."
}

Write-Host "PressHistory publie dans : $pressHistoryOutput" -ForegroundColor Green
