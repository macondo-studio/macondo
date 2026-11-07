<#
.SYNOPSIS
    Aggiorna automaticamente e sincronizza la versione dell'applicazione Macondo (VB6 Analyzer)
    su tutti i file del progetto .NET, del sito web e del manifesto di aggiornamento.

.PARAMETER NewVersion
    La nuova versione in formato SemVer (es. "1.8.1"). Se omessa, viene calcolata automaticamente
    in base a BumpType a partire dalla versione attuale.

.PARAMETER BumpType
    Tipo di incremento SemVer se NewVersion non e specificata: "Patch" (default), "Minor", "Major".

.PARAMETER NotesIt
    Note di rilascio opzionali in lingua italiana. Se omesse, vengono estratte automaticamente
    dalla cronologia commit di git.

.PARAMETER NotesEn
    Note di rilascio opzionali in lingua inglese. Se omesse, vengono estratte automaticamente
    dalla cronologia commit di git.

.EXAMPLE
    .\scripts\bump-version.ps1 -BumpType Patch
    .\scripts\bump-version.ps1 -NewVersion "1.8.1"
    .\scripts\bump-version.ps1 -BumpType Minor -NotesEn "Added Kotlin support" -NotesIt "Aggiunto supporto a Kotlin"
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
    [string]$NotesEn = ""
)

$ErrorActionPreference = "Stop"

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$progettiRoot = Split-Path -Parent $workspaceRoot
$siteRoot = Join-Path $workspaceRoot "docs"
$macondoPublicRoot = Join-Path $progettiRoot "macondo"
$csprojPath = Join-Path $workspaceRoot "VB6AnalyzerPOC\src\VB6Analyzer.App\VB6Analyzer.App.csproj"

# 1. Determinazione versione attuale
$currentVersion = "1.0.0"
if (Test-Path $csprojPath) {
    $csprojContent = Get-Content $csprojPath -Raw -Encoding UTF8
    if ($csprojContent -match '<Version>([^<]+)</Version>') {
        $currentVersion = $matches[1]
    }
}

# 2. Calcolo nuova versione se non passata esplicitamente
if ([string]::IsNullOrWhiteSpace($NewVersion)) {
    if ([string]::IsNullOrWhiteSpace($BumpType)) {
        $BumpType = "Patch"
    }

    $parts = $currentVersion.Split('.')
    $major = [int]$parts[0]
    $minor = [int]$parts[1]
    $patch = [int]$parts[2]

    switch ($BumpType) {
        "Major" {
            $major++
            $minor = 0
            $patch = 0
        }
        "Minor" {
            $minor++
            $patch = 0
        }
        default {
            $patch++
        }
    }
    $NewVersion = "$major.$minor.$patch"
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Macondo Version Bump Tool -> $currentVersion => $NewVersion" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 3. Estrazione automatica note di rilascio da Git se non specificate
if ([string]::IsNullOrWhiteSpace($NotesEn) -or [string]::IsNullOrWhiteSpace($NotesIt)) {
    try {
        $gitLines = git log --pretty=format:"%H|%s" -n 50 2>$null
        $extractedCommits = @()
        $skipFirst = $true
        foreach ($line in $gitLines) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $lineParts = $line.Split('|', 2)
            $subject = $lineParts[1].Trim()

            if ($skipFirst) {
                $skipFirst = $false
                if ($subject -match '^v?\d+\.\d+\.\d+$') { continue }
            }

            if ($subject -match '^v?\d+\.\d+\.\d+$') {
                break
            }

            if ($subject -notmatch '^Merge ' -and -not [string]::IsNullOrWhiteSpace($subject)) {
                $extractedCommits += $subject
            }
        }

        if ($extractedCommits.Count -gt 0) {
            if ([string]::IsNullOrWhiteSpace($NotesEn)) {
                $NotesEn = ($extractedCommits | ForEach-Object { "- $_" }) -join "`n"
            }
            if ([string]::IsNullOrWhiteSpace($NotesIt)) {
                $NotesIt = "Aggiornamento v$NewVersion di Macondo con miglioramenti di stabilita, correzioni e funzionalita architetturali:`n" + (($extractedCommits | ForEach-Object { "- $_" }) -join "`n")
            }
        }
    } catch {
        Write-Host "Avviso: Impossibile estrarre commit git: $_" -ForegroundColor Yellow
    }

    if ([string]::IsNullOrWhiteSpace($NotesEn)) {
        $NotesEn = "- Maintenance release v$NewVersion with stability and performance updates."
    }
    if ([string]::IsNullOrWhiteSpace($NotesIt)) {
        $NotesIt = "Rilascio v$NewVersion di Macondo con aggiornamenti di stabilita e prestazioni."
    }
}

# 4. Aggiornamento VB6Analyzer.App.csproj
if (Test-Path $csprojPath) {
    Write-Host "[1/5] Aggiornamento $csprojPath..." -ForegroundColor Yellow
    $csprojContent = Get-Content $csprojPath -Raw -Encoding UTF8
    
    $assemblyVer = "$NewVersion.0"
    $csprojContent = [regex]::Replace($csprojContent, '<Version>[^<]+</Version>', "<Version>$NewVersion</Version>")
    $csprojContent = [regex]::Replace($csprojContent, '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$assemblyVer</AssemblyVersion>")
    $csprojContent = [regex]::Replace($csprojContent, '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$assemblyVer</FileVersion>")
    
    Set-Content $csprojPath -Value $csprojContent -Encoding UTF8 -NoNewline
    Write-Host "      -> Version: $NewVersion, AssemblyVersion: $assemblyVer" -ForegroundColor Green
}

# 5. Aggiornamento UpdateService.cs
$updateServicePath = Join-Path $workspaceRoot "VB6AnalyzerPOC\src\VB6Analyzer.App\Services\UpdateService.cs"
if (Test-Path $updateServicePath) {
    Write-Host "[2/5] Aggiornamento $updateServicePath..." -ForegroundColor Yellow
    $updateContent = Get-Content $updateServicePath -Raw -Encoding UTF8
    
    $parts = $NewVersion.Split('.')
    $major = $parts[0]
    $minor = $parts[1]
    $patch = $parts[2]
    
    $updateContent = [regex]::Replace($updateContent, 'public static readonly Version CurrentVersion = new Version\(\d+,\s*\d+,\s*\d+\);', "public static readonly Version CurrentVersion = new Version($major, $minor, $patch);")
    $updateContent = [regex]::Replace($updateContent, 'public static readonly string CurrentVersionString = "[^"]+";', "public static readonly string CurrentVersionString = `"$NewVersion`";")
    
    Set-Content $updateServicePath -Value $updateContent -Encoding UTF8 -NoNewline
    Write-Host "      -> CurrentVersion: ($major, $minor, $patch), CurrentVersionString: `"$NewVersion`"" -ForegroundColor Green
}

# 6. Aggiornamento site/version.json (Sito Web / Manifesto - Repo Privata)
$versionJsonPath = Join-Path $siteRoot "version.json"
if (Test-Path $versionJsonPath) {
    Write-Host "[3/5] Aggiornamento $versionJsonPath..." -ForegroundColor Yellow
    $jsonObj = Get-Content $versionJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
    
    $jsonObj.version = $NewVersion
    $jsonObj.title.it = "Macondo v$NewVersion"
    $jsonObj.title.en = "Macondo v$NewVersion"
    $jsonObj.publishedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    
    if (-not [string]::IsNullOrWhiteSpace($NotesIt)) {
        $jsonObj.releaseNotes.it = $NotesIt
    }
    if (-not [string]::IsNullOrWhiteSpace($NotesEn)) {
        $jsonObj.releaseNotes.en = $NotesEn
    }
    
    $updatedJson = $jsonObj | ConvertTo-Json -Depth 10
    Set-Content $versionJsonPath -Value $updatedJson -Encoding UTF8
    Write-Host "      -> version.json impostato a v$NewVersion con timestamp UTC" -ForegroundColor Green
}

# 7. Aggiornamento automatico file HTML del sito (versioni + date formattate)
$now = Get-Date
$itMonths = @("", "gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno", "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre")
$itDateStr = "$($now.Day) $($itMonths[$now.Month]) $($now.Year)"
$enMonths = @("", "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December")
$enDateStr = "$($enMonths[$now.Month]) $($now.Day), $($now.Year)"

$htmlFiles = Get-ChildItem -Path $siteRoot -Filter "*.html"
foreach ($htmlItem in $htmlFiles) {
    $html = Get-Content $htmlItem.FullName -Raw -Encoding UTF8
    
    # Versioni
    $html = [regex]::Replace($html, '"softwareVersion":\s*"[^"]+"', "`"softwareVersion`": `"$NewVersion`"")
    $html = [regex]::Replace($html, 'Macondo v\d+\.\d+\.\d+', "Macondo v$NewVersion")
    $html = [regex]::Replace($html, '<span class="nav-version-tag">v\d+\.\d+\.\d+</span>', "<span class=`"nav-version-tag`">v$NewVersion</span>")
    $html = [regex]::Replace($html, '(\?v=)\d+\.\d+\.\d+', "`${1}$NewVersion")
    
    # Date localizzate automatiche
    if ($htmlItem.Name -match '-it\.html$' -or $htmlItem.Name -eq 'it.html') {
        $html = [regex]::Replace($html, '(Ultimo aggiornamento:\s*)[^<]+', "`${1}$itDateStr")
    } else {
        $html = [regex]::Replace($html, '(Last updated\s*)[^<]+', "`${1}$enDateStr")
    }
    
    Set-Content $htmlItem.FullName -Value $html -Encoding UTF8 -NoNewline
}
Write-Host "      -> Pagine del sito web in $siteRoot allineate a v$NewVersion con date aggiornate" -ForegroundColor Green

# 8. Sincronizzazione automatica file da site/ alla cartella della repo pubblica macondo
if (Test-Path $macondoPublicRoot) {
    Write-Host "[5/5] Sincronizzazione file da $siteRoot verso $macondoPublicRoot (repo pubblica)..." -ForegroundColor Yellow
    robocopy $siteRoot $macondoPublicRoot /E /XD .git /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
    if ($LASTEXITCODE -ge 8) {
        Write-Host "      -> ATTENZIONE: Errore durante robocopy (exit code $LASTEXITCODE)" -ForegroundColor Red
    } else {
        $global:LASTEXITCODE = 0
        Write-Host "      -> File sincronizzati in $macondoPublicRoot (pronti per il commit pubblico)" -ForegroundColor Green
    }
} else {
    Write-Host "[5/5] ATTENZIONE: Percorso repo pubblica non trovato: $macondoPublicRoot" -ForegroundColor Magenta
}

Write-Host "`nTutti i file sono stati aggiornati con successo alla versione $NewVersion!" -ForegroundColor Green

# Esporta variabili utili per script chiamanti
$global:MACONDO_VERSION = $NewVersion
$global:MACONDO_NOTES_EN = $NotesEn
$global:MACONDO_NOTES_IT = $NotesIt
