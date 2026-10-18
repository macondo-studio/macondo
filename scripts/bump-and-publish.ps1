<#
.SYNOPSIS
    Pipeline unificata di rilascio e pubblicazione di Macondo (VB6 Analyzer):
    test, versionamento automatico, compilazione single-file, installer e note di release.

.DESCRIPTION
    Questo script esegue l'intero ciclo di release in un unico comando:
    1. Esecuzione preliminare della suite completa di test automatizzati xUnit (escludibile con -SkipTests).
    2. Calcolo automatico della nuova versione SemVer (Patch di default, o Minor/Major/NewVersion esplicita).
    3. Estrazione automatica delle note di rilascio dai commit Git se non specificate manualmente.
    4. Sincronizzazione di csproj, UpdateService.cs, version.json e date/versioni su tutte le pagine del sito.
    5. Copia atomica di site/ verso la repo pubblica (../macondo).
    6. Pubblicazione dell'eseguibile unico standalone Windows x64 (Macondo.exe).
    7. Generazione dell'installer con Inno Setup (Macondo-Setup.exe).
    8. Generazione del file markdown pronto per la release GitHub (publish/win-x64/RELEASE_NOTES_GITHUB.md).
    9. Opzionale commit e tag Git automatico (-GitCommit).

.PARAMETER NewVersion
    Versione SemVer esplicita (es. "1.8.1"). Se omessa, viene calcolata in base a BumpType.

.PARAMETER BumpType
    Tipo di incremento SemVer: "Patch" (default), "Minor", "Major".

.PARAMETER NotesIt
    Note di rilascio in italiano (opzionali, auto-estratte da git se omesse).

.PARAMETER NotesEn
    Note di rilascio in inglese (opzionali, auto-estratte da git se omesse).

.PARAMETER SkipTests
    Se specificato, salta l'esecuzione preliminare dei test automatizzati xUnit.

.PARAMETER GitCommit
    Se specificato, esegue automaticamente git commit e git tag per la nuova versione
    sia nella repository privata che nella repository pubblica (../macondo).

.EXAMPLE
    .\scripts\bump-and-publish.ps1
    .\scripts\bump-and-publish.ps1 -BumpType Minor
    .\scripts\bump-and-publish.ps1 -NewVersion "1.8.1" -GitCommit
    .\scripts\bump-and-publish.ps1 -SkipTests
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false, Position = 0)]
    [ValidatePattern('^(\d+\.\d+\.\d+)?$')]
    [string]$NewVersion = "",

    [Parameter(Mandatory = $false)]
    [ValidateSet("Patch", "Minor", "Major", "")]
    [string]$BumpType = "",

    [Parameter(Mandatory = $false)]
    [string]$NotesIt = "",

    [Parameter(Mandatory = $false)]
    [string]$NotesEn = "",

    [Parameter(Mandatory = $false)]
    [switch]$SkipTests,

    [Parameter(Mandatory = $false)]
    [switch]$GitCommit
)

$ErrorActionPreference = "Stop"

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$progettiRoot = Split-Path -Parent $workspaceRoot
$macondoPublicRoot = Join-Path $progettiRoot "macondo"
$bumpScript = Join-Path $PSScriptRoot "bump-version.ps1"
$publishScript = Join-Path $PSScriptRoot "publish-single-file.ps1"
$installerScript = Join-Path $PSScriptRoot "build-installer.ps1"
$testsScript = Join-Path $PSScriptRoot "run-tests.ps1"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Macondo - Full Automated Release Pipeline               " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 0. Esecuzione test suite automatizzata (attiva per default)
if (-not $SkipTests) {
    Write-Host "`n>>> [STEP 1/5] Esecuzione suite di test automatizzati xUnit..." -ForegroundColor Magenta
    if (Test-Path $testsScript) {
        & $testsScript
        if ($LASTEXITCODE -ne 0) {
            Write-Host "`n[BLOCCO PIPELINE] I test automatizzati sono falliti. Release interrotta." -ForegroundColor Red
            exit $LASTEXITCODE
        }
    } else {
        Write-Host "Avviso: Script run-tests.ps1 non trovato in $testsScript. Salto esecuzione test." -ForegroundColor Yellow
    }
} else {
    Write-Host "`n>>> [STEP 1/5] Esecuzione test saltata (-SkipTests specificato)." -ForegroundColor Gray
}

# 1. Bump Versione & Sincronizzazione Sito
Write-Host "`n>>> [STEP 2/5] Aggiornamento versione, date e sincronizzazione sito..." -ForegroundColor Magenta
if (-not (Test-Path $bumpScript)) {
    Write-Host "Errore: Script bump-version.ps1 non trovato in $bumpScript" -ForegroundColor Red
    exit 1
}

$bumpArgs = @{}
if (-not [string]::IsNullOrWhiteSpace($NewVersion)) { $bumpArgs["NewVersion"] = $NewVersion }
if (-not [string]::IsNullOrWhiteSpace($BumpType)) { $bumpArgs["BumpType"] = $BumpType }
if (-not [string]::IsNullOrWhiteSpace($NotesIt)) { $bumpArgs["NotesIt"] = $NotesIt }
if (-not [string]::IsNullOrWhiteSpace($NotesEn)) { $bumpArgs["NotesEn"] = $NotesEn }

$global:LASTEXITCODE = 0
& $bumpScript @bumpArgs
if (-not $? -or $global:LASTEXITCODE -ne 0) {
    Write-Host "`n[BLOCCO PIPELINE] Errore durante l'aggiornamento della versione." -ForegroundColor Red
    exit 1
}

$effectiveVersion = $global:MACONDO_VERSION
$effectiveNotesEn = $global:MACONDO_NOTES_EN
$effectiveNotesIt = $global:MACONDO_NOTES_IT

if ([string]::IsNullOrWhiteSpace($effectiveVersion)) {
    $versionJsonPath = Join-Path $workspaceRoot "site\version.json"
    if (Test-Path $versionJsonPath) {
        $json = Get-Content $versionJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $effectiveVersion = $json.version
        if ([string]::IsNullOrWhiteSpace($effectiveNotesEn)) { $effectiveNotesEn = $json.releaseNotes.en }
        if ([string]::IsNullOrWhiteSpace($effectiveNotesIt)) { $effectiveNotesIt = $json.releaseNotes.it }
    }
}

# 2. Pubblicazione Singolo Eseguibile Portable
Write-Host "`n>>> [STEP 3/5] Pubblicazione dell'eseguibile unico (Single-File Windows x64)..." -ForegroundColor Magenta
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
Write-Host "`n>>> [STEP 4/5] Compilazione pacchetto di installazione (Macondo-Setup.exe)..." -ForegroundColor Magenta
if (Test-Path $installerScript) {
    $global:LASTEXITCODE = 0
    & $installerScript -Version $effectiveVersion
    if (-not $? -or $global:LASTEXITCODE -ne 0) {
        Write-Host "`n[BLOCCO PIPELINE] Errore durante la compilazione dell'installer." -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "Attenzione: Script build-installer.ps1 non trovato in $installerScript" -ForegroundColor Yellow
}

# 4. Generazione File Note di Rilascio per GitHub
Write-Host "`n>>> [STEP 5/5] Generazione note di rilascio GitHub..." -ForegroundColor Magenta
$outputDir = Join-Path $workspaceRoot "publish\win-x64"
$notesFile = Join-Path $outputDir "RELEASE_NOTES_GITHUB.md"

$formattedNotesEn = if ($effectiveNotesEn -match '^-') { $effectiveNotesEn } else { "- " + ($effectiveNotesEn -replace "`n", "`n- ") }

$githubNotesContent = @"
## Macondo v$effectiveVersion

### What's New
$formattedNotesEn

### Key Features & Distribution Packages
- Windows Installer (`Macondo-Setup.exe`): automated in-place upgrade and Start Menu / Desktop integration.
- Standalone Portable Executable (`Macondo.exe`): zero-dependency single file optimized for Windows x64 and CI/CD pipelines.
- Complete documentation, release manifests, and web showcases synchronized across English and Italian portals.

### Note di Rilascio (Italiano)
$effectiveNotesIt
"@

Set-Content -Path $notesFile -Value $githubNotesContent -Encoding UTF8
Write-Host "      -> File generato: $notesFile" -ForegroundColor Green

# 5. Git Commit & Tag Opzionale
if ($GitCommit) {
    Write-Host "`n>>> [GIT] Esecuzione commit e tag automatici..." -ForegroundColor Magenta
    
    # Repo Privata
    try {
        git -C $workspaceRoot add VB6AnalyzerPOC/src/VB6Analyzer.App/VB6Analyzer.App.csproj VB6AnalyzerPOC/src/VB6Analyzer.App/Services/UpdateService.cs site/
        git -C $workspaceRoot commit -m "v$effectiveVersion" 2>$null
        git -C $workspaceRoot tag -a "v$effectiveVersion" -m "v$effectiveVersion" 2>$null
        Write-Host "      -> Commit e tag creati nella repository privata (v$effectiveVersion)" -ForegroundColor Green
    } catch {
        Write-Host "      -> Avviso commit privato: $_" -ForegroundColor Yellow
    }

    # Repo Pubblica
    if (Test-Path $macondoPublicRoot) {
        try {
            git -C $macondoPublicRoot add .
            git -C $macondoPublicRoot commit -m "Release v$effectiveVersion" 2>$null
            git -C $macondoPublicRoot tag -a "v$effectiveVersion" -m "v$effectiveVersion" 2>$null
            Write-Host "      -> Commit e tag creati nella repository pubblica macondo (v$effectiveVersion)" -ForegroundColor Green
        } catch {
            Write-Host "      -> Avviso commit pubblico: $_" -ForegroundColor Yellow
        }
    }
}

# 6. Riepilogo Finale
$outputExe = Join-Path $outputDir "Macondo.exe"
$setupExe = Join-Path $outputDir "Macondo-Setup.exe"

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "  RELEASE COMPLETATA CON SUCCESSO!                        " -ForegroundColor Green
Write-Host "  Versione: v$effectiveVersion                            " -ForegroundColor Green
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
Write-Host "  Note di Rilascio:    $notesFile" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green

Write-Host "`n--- [Bozza Note di Rilascio per GitHub] ---" -ForegroundColor Cyan
Write-Host $githubNotesContent -ForegroundColor Gray

if (-not $GitCommit) {
    Write-Host "`n--- [Comandi Git per il Rilascio] ---" -ForegroundColor Cyan
    Write-Host "# Repository Privata:" -ForegroundColor Gray
    Write-Host "git add VB6AnalyzerPOC/src/VB6Analyzer.App/VB6Analyzer.App.csproj VB6AnalyzerPOC/src/VB6Analyzer.App/Services/UpdateService.cs site/" -ForegroundColor Gray
    Write-Host "git commit -m `"v$effectiveVersion`"" -ForegroundColor Gray
    Write-Host "git push && git push --tags`n" -ForegroundColor Gray
    Write-Host "# Repository Pubblica (sito web):" -ForegroundColor Gray
    Write-Host "cd ../macondo" -ForegroundColor Gray
    Write-Host "git add ." -ForegroundColor Gray
    Write-Host "git commit -m `"Release v$effectiveVersion`"" -ForegroundColor Gray
    Write-Host "git push && git push --tags" -ForegroundColor Gray
}
