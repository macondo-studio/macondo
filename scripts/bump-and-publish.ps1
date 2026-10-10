<#
.SYNOPSIS
    Esegue in sequenza l'aggiornamento di versione (bump-version) e la generazione dell'eseguibile unico (publish-single-file).

.DESCRIPTION
    Questo script automatizza l'intero workflow di release per Macondo (VB6 Analyzer):
    1. Aggiorna e sincronizza la versione SemVer su tutti i target del progetto .NET e del sito web/manifesto di aggiornamento.
    2. Compila e pubblica il pacchetto standalone single-file per Windows x64 (Macondo.exe).

.PARAMETER NewVersion
    La nuova versione in formato SemVer (es. "1.2.0" o "2.0.0").

.PARAMETER NotesIt
    Note di rilascio opzionali in lingua italiana.

.PARAMETER NotesEn
    Note di rilascio opzionali in lingua inglese.

.PARAMETER RunTests
    Se specificato, esegue preliminarmente la suite di test automatizzati xUnit bloccando il rilascio in caso di fallimenti.

.EXAMPLE
    .\scripts\bump-and-publish.ps1 -NewVersion "1.2.0" -NotesIt "Aggiunta nuova funzionalità X" -NotesEn "Added new feature X"

.EXAMPLE
    .\scripts\bump-and-publish.ps1 -NewVersion "1.2.0" -RunTests
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$NewVersion,

    [Parameter(Mandatory = $false)]
    [string]$NotesIt = "",

    [Parameter(Mandatory = $false)]
    [string]$NotesEn = "",

    [Parameter(Mandatory = $false)]
    [switch]$RunTests
)

$ErrorActionPreference = "Stop"

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$bumpScript = Join-Path $PSScriptRoot "bump-version.ps1"
$publishScript = Join-Path $PSScriptRoot "publish-single-file.ps1"
$installerScript = Join-Path $PSScriptRoot "build-installer.ps1"
$testsScript = Join-Path $PSScriptRoot "run-tests.ps1"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Macondo - Release Pipeline: Bump Version & Publish      " -ForegroundColor Cyan
Write-Host "  Versione Target: v$NewVersion                           " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 0. (Opzionale) Esecuzione preliminare test suite
if ($RunTests) {
    Write-Host "`n>>> [STEP 0/3] Esecuzione suite di test automatizzati..." -ForegroundColor Magenta
    if (Test-Path $testsScript) {
        & $testsScript
        if ($LASTEXITCODE -ne 0) {
            Write-Host "`n[BLOCCO PIPELINE] I test automatizzati sono falliti. Pipeline interrotta." -ForegroundColor Red
            exit $LASTEXITCODE
        }
    } else {
        Write-Host "Attenzione: Script di test non trovato in $testsScript. Salto esecuzione test." -ForegroundColor Yellow
    }
}

# 1. Bump Versione
Write-Host "`n>>> [STEP 1/3] Sincronizzazione della versione v$NewVersion..." -ForegroundColor Magenta
if (-not (Test-Path $bumpScript)) {
    Write-Host "Errore: Script bump-version.ps1 non trovato in $bumpScript" -ForegroundColor Red
    exit 1
}

$global:LASTEXITCODE = 0
& $bumpScript -NewVersion $NewVersion -NotesIt $NotesIt -NotesEn $NotesEn
if (-not $? -or $global:LASTEXITCODE -ne 0) {
    Write-Host "`n[BLOCCO PIPELINE] Errore durante l'aggiornamento della versione." -ForegroundColor Red
    exit 1
}

# 2. Pubblicazione Singolo Eseguibile Portable
Write-Host "`n>>> [STEP 2/3] Pubblicazione dell'eseguibile unico (Single-File Windows x64)..." -ForegroundColor Magenta
if (-not (Test-Path $publishScript)) {
    Write-Host "Errore: Script publish-single-file.ps1 non trovato in $publishScript" -ForegroundColor Red
    exit 1
}

$global:LASTEXITCODE = 0
& $publishScript
if (-not $? -or $global:LASTEXITCODE -ne 0) {
    Write-Host "`n[BLOCCO PIPELINE] Errore durante la pubblicazione del pacchetto portable." -ForegroundColor Red
    exit 1
}

# 3. Creazione Pacchetto Installer (Inno Setup)
Write-Host "`n>>> [STEP 3/3] Compilazione pacchetto di installazione (Macondo-Setup.exe)..." -ForegroundColor Magenta
if (Test-Path $installerScript) {
    $global:LASTEXITCODE = 0
    & $installerScript -Version $NewVersion
    if (-not $? -or $global:LASTEXITCODE -ne 0) {
        Write-Host "`n[BLOCCO PIPELINE] Errore durante la compilazione dell'installer." -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "Attenzione: Script build-installer.ps1 non trovato in $installerScript" -ForegroundColor Yellow
}

$outputExe = Join-Path $workspaceRoot "publish\win-x64\Macondo.exe"
$setupExe = Join-Path $workspaceRoot "publish\win-x64\Macondo-Setup.exe"

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "  RELEASE COMPLETATA CON SUCCESSO!                        " -ForegroundColor Green
Write-Host "  Versione: v$NewVersion                                  " -ForegroundColor Green
if (Test-Path $outputExe) {
    $exeItem = Get-Item $outputExe
    $sizeMb = [math]::Round($exeItem.Length / 1MB, 2)
    Write-Host "  Eseguibile Portable: $outputExe ($sizeMb MB)" -ForegroundColor Green
}
if (Test-Path $setupExe) {
    $setupItem = Get-Item $setupExe
    $setupSizeMb = [math]::Round($setupItem.Length / 1MB, 2)
    Write-Host "  Windows Installer:   $setupExe ($setupSizeMb MB)" -ForegroundColor Green
}
Write-Host "==========================================================" -ForegroundColor Green

# Output note di rilascio pronte per GitHub Release
Write-Host "`n--- [Bozza Note di Rilascio per GitHub] ---" -ForegroundColor Cyan
Write-Host @"
## Macondo v$NewVersion

### What's New
$(if (-not [string]::IsNullOrWhiteSpace($NotesEn)) { "- " + $NotesEn } else { "- Release v$NewVersion" })

### Key Features & Improvements
- Windows Installer package (`Macondo-Setup.exe`) for automated in-place updates and Start Menu / Desktop integration.
- Standalone portable single-file binary for Windows x64 (`Macondo.exe`) for portable and CI/CD use.
- Synchronized manifests, semantic versioning, and documentation.

### Italian Notes / Note in Italiano
$(if (-not [string]::IsNullOrWhiteSpace($NotesIt)) { $NotesIt } else { "Release v$NewVersion di Macondo." })
"@ -ForegroundColor Gray
