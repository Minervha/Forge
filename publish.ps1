#!/usr/bin/env pwsh
# Build DataTableForge and copy it into a Minervha Studio install.
#
# Provide the target bin folder via -StudioBin or the MINERVHA_STUDIO_BIN
# environment variable, e.g.:
#   $env:MINERVHA_STUDIO_BIN = "C:\path\to\Minervha Studio\resources\forge\DataTableExtractor_bin"
param(
    [string]$StudioBin = $env:MINERVHA_STUDIO_BIN
)

$ErrorActionPreference = "Stop"

if (-not $StudioBin) {
    Write-Host "Set -StudioBin or the MINERVHA_STUDIO_BIN environment variable to the Studio forge bin folder." -ForegroundColor Red
    exit 1
}

Write-Host "Building DataTableForge..." -ForegroundColor Cyan

# Reproducible build: strip absolute local paths from PDB so the hash is stable
# across machines. Helps SmartScreen / Defender reputation accrue across releases.
$env:ContinuousIntegrationBuild = "true"

# Explicit anti-AV-heuristic flags (also pinned in the .csproj as defence-in-depth):
#  - no single-file bundle (looks like a self-extracting dropper)
#  - no ReadyToRun / AOT (pre-jitted code is frequently flagged)
#  - no trimming (breaks UAssetAPI reflection AND looks suspicious)
#  - framework-dependent (smaller, less heuristic surface)
dotnet publish -c Release `
    --no-self-contained `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:PublishTrimmed=false `
    -p:Deterministic=true `
    -p:ContinuousIntegrationBuild=true `
    -o ./publish/

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Cleaning target directory..." -ForegroundColor Cyan
# Preserve extractor.config.json if it exists
$preserveConfig = $null
$configPath = Join-Path $StudioBin "extractor.config.json"
if (Test-Path $configPath) { $preserveConfig = Get-Content $configPath -Raw }

Get-ChildItem $StudioBin -Exclude "extractor.config.json" | Remove-Item -Force -Recurse

if ($preserveConfig) { Set-Content $configPath $preserveConfig -NoNewline }

Write-Host "Copying to Minervha Studio..." -ForegroundColor Cyan
Copy-Item ./publish/* $StudioBin -Force -Recurse

$exe = Join-Path $StudioBin "DataTableForge.exe"
$size = [math]::Round((Get-Item $exe).Length / 1KB, 1)
Write-Host "Published: $exe ($size KB)" -ForegroundColor Green
