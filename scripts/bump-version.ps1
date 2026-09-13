<#
.SYNOPSIS
    Aggiorna automaticamente e sincronizza la versione dell'applicazione Macondo (VB6 Analyzer)
    su tutti i file del progetto .NET e del sito web/manifesto di aggiornamento.

.PARAMETER NewVersion
    La nuova versione in formato SemVer (es. "1.2.0" o "2.0.0").

.PARAMETER NotesIt
    Note di rilascio opzionali in lingua italiana.

.PARAMETER NotesEn
    Note di rilascio opzionali in lingua inglese.

.EXAMPLE
    .\scripts\bump-version.ps1 -NewVersion "1.2.0" -NotesIt "Nuova feature X" -NotesEn "New feature X"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$NewVersion,

    [Parameter(Mandatory = $false)]
    [string]$NotesIt = "",

    [Parameter(Mandatory = $false)]
    [string]$NotesEn = ""
)

$ErrorActionPreference = "Stop"

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$progettiRoot = Split-Path -Parent $workspaceRoot
$macondoWebRoot = Join-Path $progettiRoot "macondo"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Macondo Version Bump Tool -> $NewVersion" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Aggiornamento VB6Analyzer.App.csproj
$csprojPath = Join-Path $workspaceRoot "VB6AnalyzerPOC\src\VB6Analyzer.App\VB6Analyzer.App.csproj"
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

# 2. Aggiornamento UpdateService.cs
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

# 3. Aggiornamento MainWindow.axaml (Fallback placeholder)
$mainWindowAxamlPath = Join-Path $workspaceRoot "VB6AnalyzerPOC\src\VB6Analyzer.App\MainWindow.axaml"
if (Test-Path $mainWindowAxamlPath) {
    Write-Host "[3/5] Aggiornamento $mainWindowAxamlPath..." -ForegroundColor Yellow
    $axamlContent = Get-Content $mainWindowAxamlPath -Raw -Encoding UTF8
    
    $axamlContent = [regex]::Replace($axamlContent, 'attuale: \d+\.\d+\.\d+', "attuale: $NewVersion")
    Set-Content $mainWindowAxamlPath -Value $axamlContent -Encoding UTF8 -NoNewline
    Write-Host "      -> Placeholder finestra aggiornato ad attuale: $NewVersion" -ForegroundColor Green
}

# 4. Aggiornamento macondo/version.json (Sito Web / Manifesto)
$versionJsonPath = Join-Path $macondoWebRoot "version.json"
if (Test-Path $versionJsonPath) {
    Write-Host "[4/5] Aggiornamento $versionJsonPath..." -ForegroundColor Yellow
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

# 5. Aggiornamento macondo/it.html & macondo/index.html
$itHtmlPath = Join-Path $macondoWebRoot "it.html"
if (Test-Path $itHtmlPath) {
    Write-Host "[5/5] Aggiornamento $itHtmlPath e index.html..." -ForegroundColor Yellow
    $itHtml = Get-Content $itHtmlPath -Raw -Encoding UTF8
    $itHtml = [regex]::Replace($itHtml, '"softwareVersion":\s*"[^"]+"', "`"softwareVersion`": `"$NewVersion`"")
    $itHtml = [regex]::Replace($itHtml, 'Macondo v\d+\.\d+\.\d+ per Windows', "Macondo v$NewVersion per Windows")
    Set-Content $itHtmlPath -Value $itHtml -Encoding UTF8 -NoNewline
}

$indexHtmlPath = Join-Path $macondoWebRoot "index.html"
if (Test-Path $indexHtmlPath) {
    $indexHtml = Get-Content $indexHtmlPath -Raw -Encoding UTF8
    $indexHtml = [regex]::Replace($indexHtml, '"softwareVersion":\s*"[^"]+"', "`"softwareVersion`": `"$NewVersion`"")
    $indexHtml = [regex]::Replace($indexHtml, 'Macondo v\d+\.\d+\.\d+ for Windows', "Macondo v$NewVersion for Windows")
    Set-Content $indexHtmlPath -Value $indexHtml -Encoding UTF8 -NoNewline
    Write-Host "      -> Pagine del sito web allineate a v$NewVersion" -ForegroundColor Green
}

Write-Host "`nTutti i file sono stati aggiornati con successo alla versione $NewVersion!" -ForegroundColor Green
