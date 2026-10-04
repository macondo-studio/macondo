# Regole e Convenzioni del Progetto Macondo (VB6 Analyzer)

## Struttura Repository e Sincronizzazione Sito Web
- **Sorgenti del sito web (repository privata Macondo / Vb6Analyzer)**: `.\site`
- **Repository pubblica del sito web**: `../macondo`
- **Regola di Sincronizzazione Obbligatoria**: Ogni volta che vengono modificati file nella cartella `site/` (pagine HTML, immagini, CSS, script o file `version.json`), copiare SEMPRE e immediatamente l'intero contenuto aggiornato della cartella `site/` dentro `../macondo` (preservando la cartella `.git` della repo pubblica), in modo che sia sempre allineata e pronta per il commit dell'utente.

## Gestione Versioni e Release (SemVer)

Ogni volta che l'utente richiede di aggiornare la versione del software, rilasciare una nuova versione o eseguire un "bump" di versione:

1. **Procedura Automatica Obbligatoria**:
   - Eseguire lo script di sincronizzazione `./scripts/bump-version.ps1 -NewVersion "<NuovaVersione>" [-NotesIt "<Note IT>" -NotesEn "<Note EN>"]` oppure verificare e aggiornare in modo sincronizzato e atomico tutti i seguenti file:
     1. `VB6AnalyzerPOC/src/VB6Analyzer.App/VB6Analyzer.App.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`)
     2. `VB6AnalyzerPOC/src/VB6Analyzer.App/Services/UpdateService.cs` (`CurrentVersion`, `CurrentVersionString`)
     3. `site/version.json` (sito web repo privata: `"version"`, timestamp UTC `publishedAt`, `releaseNotes` in IT ed EN)
     4. `site/it.html` (`softwareVersion`, hero badge)
     5. `site/index.html` (`softwareVersion`, hero badge)
     6. **Sincronizzazione Repo Pubblica**: Copiare tutti i file aggiornati da `site/` a `../macondo/` (cartella della repo pubblica, preservando `.git`), in modo che sia pronta per il commit dell'utente.

2. **Verifica Post-Aggiornamento e Generazione Eseguibile Distribuibile**:
   - Eseguire `dotnet build VB6AnalyzerPOC\VB6AnalyzerPOC.sln` per confermare che la compilazione vada a buon fine con 0 errori.
   - Eseguire lo script `./scripts/publish-single-file.ps1` (oppure `./scripts/bump-and-publish.ps1`) per creare il nuovo file eseguibile unico distribuibile (`publish/win-x64/Macondo.exe`).

3. **Proposta Note di Rilascio per GitHub**:
   - Proporre sempre all'utente il testo formattato in Markdown pronto da incollare nel campo "Describe this release" di GitHub (strutturato con What's New, Key Features, Bug Fixes / Improvements, stile formale e pulito SENZA emoji).

## Esecuzione dei Test Unitari
- **NON eseguire mai i test (`VB6Analyzer.Tests` / `dotnet test`)** durante le iterazioni e modifiche ordinarie di sviluppo.
- L'esecuzione dei test sarà effettuata o richiesta dall'utente unicamente prima di una pubblicazione/rilascio.
- Per la verifica ordinaria del codice, limitarsi alla compilazione rapida con `dotnet build VB6AnalyzerPOC\VB6AnalyzerPOC.sln`.

## Stile di Codice (C#)
- **Assegnazioni Semplici e Guard Clauses senza Graffe (a capo)**: Per guard clauses immediate o semplici assegnazioni/controlli di proprietà (es. `if (prop != val)` a capo `    prop = val;`), non usare parentesi graffe mantenendo la formattazione pulita con istruzione a capo riga indentata.

## Release Note per ogni Modifica
- **Per ogni modifica o funzionalità richiesta dall'utente**, fornire sempre al termine della risposta una **release note di una sola frase in lingua inglese**, chiara, sintetica e formale.


