#!/usr/bin/env pwsh
# Build DataTableForge for the Linux VM (self-contained linux-x64).
# The Windows build + Studio packaging lives in publish.ps1 and is unaffected:
# same codebase, this script only changes the publish target (RID + self-contained).

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot   # run from the repo root regardless of caller's cwd
$OutDir = "./publish-linux"

Write-Host "Building DataTableForge for linux-x64 (self-contained)..." -ForegroundColor Cyan

# Reproducible build (stable hashes across machines). Harmless on Linux.
$env:ContinuousIntegrationBuild = "true"

# Self-contained => the VM needs no .NET runtime installed.
# This overrides the .csproj's SelfContained=false default, which only exists to keep
# the *Windows* publish framework-dependent for Defender/SmartScreen reputation — a
# Windows-only concern that does not apply to Linux.
# Single-file / ReadyToRun / Trimmed stay OFF (trimming breaks UAssetAPI reflection).
# InvariantGlobalization=true => no libicu dependency on the VM (self-contained .NET does
# NOT bundle ICU). Safe here: the app only uses culture-invariant string ops.
dotnet publish DataTableForge.csproj -c Release -r linux-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:PublishTrimmed=false `
    -p:InvariantGlobalization=true `
    -p:Deterministic=true `
    -p:ContinuousIntegrationBuild=true `
    -o $OutDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

$bin = Join-Path $OutDir "DataTableForge"
if (-not (Test-Path $bin)) {
    Write-Host "Expected Linux binary not found at $bin" -ForegroundColor Red
    exit 1
}
$total = [math]::Round((Get-ChildItem $OutDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "Published self-contained bundle: $OutDir ($total MB)" -ForegroundColor Green
Write-Host "Next steps (see docs/plans/features/forge-linux/linux-setup.md):" -ForegroundColor DarkGray
Write-Host "  1. transfer '$OutDir' to the VM   2. chmod +x DataTableForge   3. point repakExe at the Linux repak" -ForegroundColor DarkGray
