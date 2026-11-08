<#
.SYNOPSIS
    Esegue la suite di test automatizzati xUnit per Macondo (VB6 Analyzer).

.EXAMPLE
    .\scripts\run-tests.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$slnPath = Join-Path $workspaceRoot "Macondo.sln"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Macondo (VB6 Analyzer) - Test Suite Execution           " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$startTime = Get-Date

try {
    Write-Host "`n[1/2] Compilazione ed esecuzione dei test xUnit..." -ForegroundColor Yellow
    
    # Esegue dotnet test
    $process = Start-Process -FilePath "dotnet" -ArgumentList "test", "`"$slnPath`"", "--nologo", "-v", "minimal" -NoNewWindow -PassThru -Wait
    
    $elapsed = (Get-Date) - $startTime
    
    if ($process.ExitCode -eq 0) {
        Write-Host "`n[2/2] TUTTI I TEST SONO STATI SUPERATI CON SUCCESSO! [Tempo: $($elapsed.TotalSeconds.ToString('F1'))s]" -ForegroundColor Green
        exit 0
    } else {
        Write-Host "`n[2/2] ERRORI RILEVATI: Uno o piu test o progetti sono falliti. (ExitCode: $($process.ExitCode))" -ForegroundColor Red
        exit $process.ExitCode
    }
}
catch {
    Write-Host "`nErrore durante l'esecuzione dei test: $_" -ForegroundColor Red
    exit 1
}
