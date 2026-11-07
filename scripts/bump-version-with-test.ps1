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
$siteRoot = Join-Path $workspaceRoot "docs"
$macondoPublicRoot = Join-Path $progettiRoot "macondo"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Macondo Version Bump Tool -> $NewVersion" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 0. Verifica preliminare obbligatoria: esecuzione suite di test automatizzati
$slnPath = Join-Path $workspaceRoot "VB6AnalyzerPOC\VB6AnalyzerPOC.sln"
Write-Host "[0/6] Esecuzione suite di test automatizzati preliminare..." -ForegroundColor Yellow
$testProcess = Start-Process -FilePath "dotnet" -ArgumentList "test", "`"$slnPath`"", "--nologo", "-v", "minimal" -NoNewWindow -PassThru -Wait
if ($testProcess.ExitCode -ne 0) {
    Write-Host "`n[BLOCCO RELEASE] Uno o piu test automatizzati sono falliti (ExitCode: $($testProcess.ExitCode))." -ForegroundColor Red
    Write-Host "La procedura di bump versione e stata interrotta per preservare l'integrita del codice." -ForegroundColor Red
    exit $testProcess.ExitCode
}
Write-Host "      -> Tutti i test superati con successo (0 errori)." -ForegroundColor Green

# 1. Aggiornamento VB6Analyzer.App.csproj
$csprojPath = Join-Path $workspaceRoot "VB6AnalyzerPOC\src\VB6Analyzer.App\VB6Analyzer.App.csproj"
if (Test-Path $csprojPath) {
    Write-Host "[1/6] Aggiornamento $csprojPath..." -ForegroundColor Yellow
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
    Write-Host "[2/6] Aggiornamento $updateServicePath..." -ForegroundColor Yellow
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
    Write-Host "[3/6] Aggiornamento $mainWindowAxamlPath..." -ForegroundColor Yellow
    $axamlContent = Get-Content $mainWindowAxamlPath -Raw -Encoding UTF8
    
    $axamlContent = [regex]::Replace($axamlContent, 'attuale: \d+\.\d+\.\d+', "attuale: $NewVersion")
    Set-Content $mainWindowAxamlPath -Value $axamlContent -Encoding UTF8 -NoNewline
    Write-Host "      -> Placeholder finestra aggiornato ad attuale: $NewVersion" -ForegroundColor Green
}

# 4. Aggiornamento site/version.json (Sito Web / Manifesto - Repo Privata)
$versionJsonPath = Join-Path $siteRoot "version.json"
if (Test-Path $versionJsonPath) {
    Write-Host "[4/6] Aggiornamento $versionJsonPath..." -ForegroundColor Yellow
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

# 5. Aggiornamento site/it.html & site/index.html (Repo Privata)
$itHtmlPath = Join-Path $siteRoot "it.html"
if (Test-Path $itHtmlPath) {
    Write-Host "[5/6] Aggiornamento $itHtmlPath e index.html..." -ForegroundColor Yellow
    $itHtml = Get-Content $itHtmlPath -Raw -Encoding UTF8
    $itHtml = [regex]::Replace($itHtml, '"softwareVersion":\s*"[^"]+"', "`"softwareVersion`": `"$NewVersion`"")
    $itHtml = [regex]::Replace($itHtml, 'Macondo v\d+\.\d+\.\d+ per Windows', "Macondo v$NewVersion per Windows")
    Set-Content $itHtmlPath -Value $itHtml -Encoding UTF8 -NoNewline
}

$indexHtmlPath = Join-Path $siteRoot "index.html"
if (Test-Path $indexHtmlPath) {
    $indexHtml = Get-Content $indexHtmlPath -Raw -Encoding UTF8
    $indexHtml = [regex]::Replace($indexHtml, '"softwareVersion":\s*"[^"]+"', "`"softwareVersion`": `"$NewVersion`"")
    $indexHtml = [regex]::Replace($indexHtml, 'Macondo v\d+\.\d+\.\d+ for Windows', "Macondo v$NewVersion for Windows")
    Set-Content $indexHtmlPath -Value $indexHtml -Encoding UTF8 -NoNewline
    Write-Host "      -> Pagine del sito web in $siteRoot allineate a v$NewVersion" -ForegroundColor Green
}

# 6. Sincronizzazione file da site/ alla cartella della repo pubblica macondo
if (Test-Path $macondoPublicRoot) {
    Write-Host "[6/6] Copia file del sito da $siteRoot verso $macondoPublicRoot (repo pubblica)..." -ForegroundColor Yellow
    Get-ChildItem -Path $siteRoot -Exclude ".git" | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $macondoPublicRoot -Recurse -Force
    }
    Write-Host "      -> File sincronizzati in $macondoPublicRoot (pronti per il commit pubblico)" -ForegroundColor Green
} else {
    Write-Host "[6/6] ATTENZIONE: Percorso repo pubblica non trovato: $macondoPublicRoot" -ForegroundColor Magenta
}

Write-Host "`nTutti i file sono stati aggiornati con successo alla versione $NewVersion!" -ForegroundColor Green
