---
description: Regola automatica per la sincronizzazione delle versioni su tutti i file del progetto e del sito web
trigger: always_on
---

# Sincronizzazione Automatica delle Versioni

Quando l'utente richiede di incrementare o aggiornare la versione di Macondo (es. "aggiorna la versione a 1.2.0", "bump version", "nuova release"):

1. Utilizzare lo script di automazione `./scripts/bump-version.ps1 -NewVersion "<X.Y.Z>" -NotesIt "<Note>" -NotesEn "<Notes>"` oppure aggiornare contestualmente tutti i 6 target:
   - `VB6AnalyzerPOC/src/VB6Analyzer.App/VB6Analyzer.App.csproj`
   - `VB6AnalyzerPOC/src/VB6Analyzer.App/Services/UpdateService.cs`
   - `VB6AnalyzerPOC/src/VB6Analyzer.App/MainWindow.axaml`
   - `../macondo/version.json` (sito web e manifesto)
   - `../macondo/it.html` (sito web italiano)
   - `../macondo/index.html` (sito web inglese)
2. Ricompilare la soluzione con `dotnet build VB6AnalyzerPOC\VB6AnalyzerPOC.sln` per verificare l'integrità.
3. Proporre sempre il testo formattato in Markdown per il campo "Describe this release" su GitHub (senza emoji, pulito e formale).

## Note di Rilascio Ordinarie
- Per ogni modifica o richiesta dell'utente, fornire sempre al termine della risposta una release note sintetica di una sola frase in lingua inglese.


