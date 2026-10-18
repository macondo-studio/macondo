<#
.SYNOPSIS
    Punto di ingresso principale per il rilascio e la pubblicazione di Macondo.

.DESCRIPTION
    Delega l'esecuzione a bump-and-publish.ps1 supportando tutte le opzioni:
    - Incremento automatico Patch, Minor, Major o versione esplicita
    - Esecuzione automatica dei test unitari
    - Note di rilascio estratte automaticamente dalla cronologia commit
    - Aggiornamento di tutti i sorgenti, manifesti, date e pagine del sito
    - Sincronizzazione con la repo pubblica macondo
    - Generazione binari distribuiti (Macondo.exe, Macondo-Setup.exe, RELEASE_NOTES_GITHUB.md)
    - Opzione -GitCommit per versionare automaticamente i commit su Git

.EXAMPLE
    .\scripts\release.ps1
    .\scripts\release.ps1 -BumpType Minor
    .\scripts\release.ps1 -NewVersion "1.8.1" -GitCommit
    .\scripts\release.ps1 -SkipTests
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

$targetScript = Join-Path $PSScriptRoot "bump-and-publish.ps1"
& $targetScript @PSBoundParameters
exit $LASTEXITCODE
