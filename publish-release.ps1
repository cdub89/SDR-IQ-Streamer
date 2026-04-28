param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "SmartSDRIQStreamer.csproj"
$publishDir = Join-Path $PSScriptRoot "bin/$Configuration/net8.0-windows/$Runtime/publish"

Write-Host "== SmartStreamer4 release publish ==" -ForegroundColor Cyan
Write-Host "Project: $projectPath"
Write-Host "Runtime: $Runtime"
Write-Host "Configuration: $Configuration"

if (-not $SkipTests) {
    Write-Host "`n[1/3] Running tests..." -ForegroundColor Yellow
    dotnet test
}
else {
    Write-Host "`n[1/3] Skipping tests (--SkipTests)." -ForegroundColor Yellow
}

Write-Host "`n[2/3] Publishing self-contained single-file executable..." -ForegroundColor Yellow
dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugSymbols=false `
    -p:DebugType=None

Write-Host "`n[3/3] Cleaning non-exe publish artifacts..." -ForegroundColor Yellow
$dllConfigPath = Join-Path $publishDir "SmartStreamer4.dll.config"
if (Test-Path $dllConfigPath) {
    Remove-Item $dllConfigPath -Force
}

Write-Host "`nPublish output:" -ForegroundColor Green
Get-ChildItem $publishDir | Sort-Object Name | Format-Table Name, Length, LastWriteTime -AutoSize

Write-Host "`nDone." -ForegroundColor Green
