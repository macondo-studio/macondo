---
description: Regola automatica per la sincronizzazione delle versioni su tutti i file del progetto e del sito web
trigger: always_on
---

# Sincronizzazione Automatica delle Versioni e del Sito Web

- **Repository privata (sorgenti app e sito)**: `.` (sito in `site/`)
- **Repository pubblica (distribuzione sito web)**: `../macondo`
- **Regola Generale Sito Web**: Copiare SEMPRE l'intero contenuto aggiornato di `site/` dentro `../macondo` (escluso `.git`) ad ogni modifica del sito o della versione.

Quando l'utente richiede di incrementare o aggiornare la versione di Macondo (es. "aggiorna la versione a 1.2.0", "bump version", "nuova release"):

1. Utilizzare lo script di automazione `./scripts/bump-version.ps1 -NewVersion "<X.Y.Z>" -NotesIt "<Note>" -NotesEn "<Notes>"` oppure:
   - Aggiornare contestualmente i target nella repository privata:
     - `VB6AnalyzerPOC/src/VB6Analyzer.App/VB6Analyzer.App.csproj`
     - `VB6AnalyzerPOC/src/VB6Analyzer.App/Services/UpdateService.cs`
     - `site/version.json` (sito web e manifesto aggiornamento)
     - `site/it.html` (sito web italiano)
     - `site/index.html` (sito web inglese)
   - Copiare tutti i file della cartella `site/` nella cartella della repository pubblica `../macondo/` (preservando `.git`) per predisporre il successivo commit pubblico dell'utente.
2. Ricompilare la soluzione con `dotnet build VB6AnalyzerPOC\VB6AnalyzerPOC.sln` per verificare l'integrità.
3. Eseguire lo script `./scripts/publish-single-file.ps1` (oppure `./scripts/bump-and-publish.ps1`) per creare il nuovo eseguibile unico distribuibile (`publish/win-x64/Macondo.exe`).
4. Proporre sempre il testo formattato in Markdown per il campo "Describe this release" su GitHub (senza emoji, pulito e formale).

## Note di Rilascio Ordinarie
- Per ogni modifica o richiesta dell'utente, fornire sempre al termine della risposta una release note sintetica di una sola frase in lingua inglese.


