# Script per generare l'eseguibile unico (Single-File Portable Self-Contained) di Macondo per Windows x64

$ErrorActionPreference = "Stop"

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $workspaceRoot "VB6AnalyzerPOC\src\VB6Analyzer.App\VB6Analyzer.App.csproj"
$OutputDir = Join-Path $workspaceRoot "publish\win-x64"

Write-Host "Compilazione e creazione pacchetto eseguibile unico (Macondo.exe)..." -ForegroundColor Cyan

dotnet publish $ProjectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $OutputDir

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nEseguibile unico generato con successo!" -ForegroundColor Green
    Write-Host "Percorso file: $OutputDir\Macondo.exe" -ForegroundColor Yellow
} else {
    Write-Host "`nErrore durante la compilazione." -ForegroundColor Red
}
