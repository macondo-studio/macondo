<#
.SYNOPSIS
    Installa e attiva i Git Hooks locali (pre-commit e pre-push) nel repository.
    Garantisce che ogni commit o push esegua automaticamente l'intera suite di test
    e blocchi l'operazione in caso di errori di compilazione o test falliti.

.EXAMPLE
    .\scripts\install-git-hooks.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$hooksDir = Join-Path $workspaceRoot ".git\hooks"

if (-not (Test-Path $hooksDir)) {
    Write-Host "Cartella .git\hooks non trovata! Assicurati di eseguire lo script nella radice del repository Git." -ForegroundColor Red
    exit 1
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Installazione Git Hooks Pre-Commit & Pre-Push           " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$hookContent = @'
#!/bin/sh
# Macondo Git Hook: Esegue build e test suite automatizzata prima di procedere
echo ""
echo "=========================================================="
echo " [Git Hook] Esecuzione suite di test automatizzati..."
echo "=========================================================="
echo ""

dotnet test "Macondo.sln" --nologo -v minimal
RESULT=$?

if [ $RESULT -ne 0 ]; then
    echo ""
    echo "=========================================================="
    echo " [BLOCCO GIT] Build o test falliti!"
    echo " Operazione annullata per preservare l'integrita del codice."
    echo " Correggi gli errori o riavvia i test prima di procedere."
    echo "=========================================================="
    echo ""
    exit 1
fi

echo ""
echo " [Git Hook] Tutti i test sono passati con successo (0 errori)!"
echo ""
exit 0
'@

$preCommitPath = Join-Path $hooksDir "pre-commit"
$prePushPath = Join-Path $hooksDir "pre-push"

# Scrittura con terminatori di linea LF tipici degli script shell UNIX/Git
$hookContentLf = $hookContent.Replace("`r`n", "`n")

[System.IO.File]::WriteAllText($preCommitPath, $hookContentLf, [System.Text.Encoding]::UTF8)
Write-Host "  [OK] Hook pre-commit installato in: $preCommitPath" -ForegroundColor Green

[System.IO.File]::WriteAllText($prePushPath, $hookContentLf, [System.Text.Encoding]::UTF8)
Write-Host "  [OK] Hook pre-push installato in:   $prePushPath" -ForegroundColor Green

Write-Host "`nGit Hooks installati e attivi con successo!" -ForegroundColor Green
