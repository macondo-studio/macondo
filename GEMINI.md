# Regole e Convenzioni del Progetto Macondo (VB6 Analyzer)

## Gestione Versioni e Release (SemVer)

Ogni volta che l'utente richiede di aggiornare la versione del software, rilasciare una nuova versione o eseguire un "bump" di versione:

1. **Procedura Automatica Obbligatoria**:
   - Eseguire lo script di sincronizzazione `./scripts/bump-version.ps1 -NewVersion "<NuovaVersione>" [-NotesIt "<Note IT>" -NotesEn "<Note EN>"]` oppure verificare e aggiornare in modo sincronizzato e atomico tutti i seguenti file:
     1. `VB6AnalyzerPOC/src/VB6Analyzer.App/VB6Analyzer.App.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`)
     2. `VB6AnalyzerPOC/src/VB6Analyzer.App/Services/UpdateService.cs` (`CurrentVersion`, `CurrentVersionString`)
     3. `../macondo/version.json` (sito web: `"version"`, timestamp UTC `publishedAt`, `releaseNotes` in IT ed EN)
     4. `../macondo/it.html` (`softwareVersion`, hero badge)
     5. `../macondo/index.html` (`softwareVersion`, hero badge)

2. **Verifica Post-Aggiornamento**:
   - Eseguire sempre `dotnet build VB6AnalyzerPOC\VB6AnalyzerPOC.sln` per confermare che la compilazione vada a buon fine con 0 errori.

3. **Proposta Note di Rilascio per GitHub**:
   - Proporre sempre all'utente il testo formattato in Markdown pronto da incollare nel campo "Describe this release" di GitHub (strutturato con What's New, Key Features, Bug Fixes / Improvements, stile formale e pulito SENZA emoji).

## Esecuzione dei Test Unitari
- **NON eseguire mai i test (`VB6Analyzer.Tests` / `dotnet test`)** durante le iterazioni e modifiche ordinarie di sviluppo.
- L'esecuzione dei test sarà effettuata o richiesta dall'utente unicamente prima di una pubblicazione/rilascio.
- Per la verifica ordinaria del codice, limitarsi alla compilazione rapida con `dotnet build VB6AnalyzerPOC\VB6AnalyzerPOC.sln`.

## Release Note per ogni Modifica
- **Per ogni modifica o funzionalità richiesta dall'utente**, fornire sempre al termine della risposta una **release note di una sola frase in lingua inglese**, chiara, sintetica e formale.


