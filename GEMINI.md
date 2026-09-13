# Regole e Convenzioni del Progetto Macondo (VB6 Analyzer)

## Gestione Versioni e Release (SemVer)

Ogni volta che l'utente richiede di aggiornare la versione del software, rilasciare una nuova versione o eseguire un "bump" di versione:

1. **Procedura Automatica Obbligatoria**:
   - Eseguire lo script di sincronizzazione `./scripts/bump-version.ps1 -NewVersion "<NuovaVersione>" [-NotesIt "<Note IT>" -NotesEn "<Note EN>"]` oppure verificare e aggiornare in modo sincronizzato e atomico tutti i seguenti file:
     1. `VB6AnalyzerPOC/src/VB6Analyzer.App/VB6Analyzer.App.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`)
     2. `VB6AnalyzerPOC/src/VB6Analyzer.App/Services/UpdateService.cs` (`CurrentVersion`, `CurrentVersionString`)
     3. `VB6AnalyzerPOC/src/VB6Analyzer.App/MainWindow.axaml` (testo placeholder modale di aggiornamento)
     4. `../macondo/version.json` (sito web: `"version"`, timestamp UTC `publishedAt`, `releaseNotes` in IT ed EN)
     5. `../macondo/it.html` (`softwareVersion`, hero badge)
     6. `../macondo/index.html` (`softwareVersion`, hero badge)

2. **Verifica Post-Aggiornamento**:
   - Eseguire sempre `dotnet build VB6AnalyzerPOC\VB6AnalyzerPOC.sln` per confermare che la compilazione vada a buon fine con 0 errori.
