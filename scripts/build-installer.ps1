<#
.SYNOPSIS
    Compila il pacchetto di installazione Windows (Macondo-Setup.exe) utilizzando Inno Setup.

.PARAMETER Version
    La versione di Macondo da incorporare nell'installer. Se non specificata, viene letta da Macondo.App.csproj.

.EXAMPLE
    .\scripts\build-installer.ps1
    .\scripts\build-installer.ps1 -Version "1.5.0"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false, Position = 0)]
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$csprojPath = Join-Path $workspaceRoot "src\Macondo.App\Macondo.App.csproj"
$issPath = Join-Path $workspaceRoot "installer\macondo.iss"
$publishExe = Join-Path $workspaceRoot "publish\win-x64\Macondo.exe"

# 1. Version recovery
if ([string]::IsNullOrWhiteSpace($Version)) {
    if (Test-Path $csprojPath) {
        $csprojContent = Get-Content $csprojPath -Raw -Encoding UTF8
        if ($csprojContent -match '<Version>([^<]+)</Version>') {
            $Version = $matches[1]
        }
    }
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = "1.0.0"
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Compilazione Installer Inno Setup (Macondo v$Version)   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 2. Check for published executable
if (-not (Test-Path $publishExe)) {
    Write-Host "Eseguibile $publishExe non trovato. Compilazione in corso..." -ForegroundColor Yellow
    $publishScript = Join-Path $PSScriptRoot "publish-single-file.ps1"
    & $publishScript
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $publishExe)) {
        Write-Host "Impossibile trovare o compilare Macondo.exe. Interruzione." -ForegroundColor Red
        exit 1
    }
}

# 3. Inno Setup Compiler Search Executable (ISCC.exe)
$isccCandidates = @(
    (Get-Command iscc.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)

$isccPath = $null
foreach ($candidate in $isccCandidates) {
    if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path $candidate)) {
        $isccPath = $candidate
        break
    }
}

if (-not $isccPath) {
    # Attempting to recover from the registry
    $reg = Get-ItemProperty HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*, `
                            HKLM:\Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*, `
                            HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\* -ErrorAction SilentlyContinue |
           Where-Object { $_.DisplayName -like "*Inno Setup*" -and $_.InstallLocation } |
           Select-Object -First 1

    if ($reg -and (Test-Path (Join-Path $reg.InstallLocation "ISCC.exe"))) {
        $isccPath = Join-Path $reg.InstallLocation "ISCC.exe"
    }
}

if (-not $isccPath) {
    Write-Host "Errore: Compilatore Inno Setup (ISCC.exe) non trovato." -ForegroundColor Red
    Write-Host "Installa Inno Setup tramite winget: winget install JRSoftware.InnoSetup" -ForegroundColor Yellow
    exit 1
}

Write-Host "Utilizzo compilatore Inno Setup: $isccPath" -ForegroundColor Gray
Write-Host "Compilazione installer da script: $issPath..." -ForegroundColor Yellow

# 4. Compile Inno Setup
& $isccPath "/DMyAppVersion=$Version" "/Qp" $issPath

if ($LASTEXITCODE -eq 0) {
    $setupExe = Join-Path $workspaceRoot "publish\win-x64\Macondo-Setup.exe"
    if (Test-Path $setupExe) {
        $item = Get-Item $setupExe
        $sizeMb = [math]::Round($item.Length / 1MB, 2)
        Write-Host "`nInstaller generato con successo!" -ForegroundColor Green
        Write-Host "Percorso Installer: $setupExe ($sizeMb MB)" -ForegroundColor Yellow
    }
} else {
    Write-Host "`nErrore durante la compilazione dell'installer Inno Setup." -ForegroundColor Red
    exit $LASTEXITCODE
}
