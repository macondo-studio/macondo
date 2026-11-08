# Regole e Convenzioni del Progetto Macondo (VB6 Analyzer)

## Struttura Repository e Sito Web (Monorepo)
- **Sorgenti del sito web**: cartella `docs/` nella radice del repository (servito nativamente da GitHub Pages).
- **Nessuna Build per Modifiche al Solo Sito**: Quando le modifiche riguardano unicamente file del sito web (`docs/`, HTML, CSS, script, immagini) e non il codice sorgente C#/.NET, **NON eseguire la compilazione (`dotnet build`)**. La build va eseguita solo quando si modifica il codice C# o durante la procedura di release.

## Procedura Automatica di Rilascio e Pubblicazione (Zero-Token Release)

Ogni volta che l'utente richiede di pubblicare, aggiornare o rilasciare una nuova versione (es. "voglio pubblicare una nuova versione", "rilascia una versione patch/minor", "bump version"):

1. **Eseguire Direttamente lo Script Unificato**:
   - Eseguire in un unico comando senza passaggi manuali intermedi:
     `./scripts/release.ps1 [-BumpType Patch|Minor|Major] [-NewVersion "<X.Y.Z>"] [-GitCommit]`
   - *(Oppure `./scripts/bump-and-publish.ps1`)*

2. **Automazioni Integrate Eseguite Interamente dallo Script**:
   - **Test automatizzati**: esegue automaticamente la suite xUnit (`run-tests.ps1`) bloccando il rilascio in caso di fallimenti.
   - **Calcolo versione SemVer**: se non specificata, calcola automaticamente la nuova versione (Patch di default, o in base a BumpType).
   - **Estrazione automatica note**: se non fornite, estrae automaticamente i commit git rilevanti dall'ultima release.
   - **Allineamento atomico dei file**: aggiorna `csproj`, `UpdateService.cs`, `docs/version.json` e tutte le pagine `docs/*.html`.
   - **Date del sito**: aggiorna automaticamente le date formattate ("Ultimo aggiornamento" / "Last updated") nelle pagine HTML.
   - **Binari distribuibili**: compila `publish/win-x64/Macondo.exe` (Single-File) e `publish/win-x64/Macondo-Setup.exe` (Inno Setup).
   - **Manifesto GitHub Release**: genera il file pronto `publish/win-x64/RELEASE_NOTES_GITHUB.md`.

3. **Output per l'Utente**:
   - Mostrare all'utente l'esito restituito dallo script e il testo delle note per GitHub già generato nel file `RELEASE_NOTES_GITHUB.md`.
   - Non richiedere passaggi intermedi né eseguire modifiche manuali a singoli file: delegare sempre l'intero flusso allo script.

## Esecuzione dei Test Unitari
- **NON eseguire i test (`dotnet test`)** durante le iterazioni e modifiche ordinarie di sviluppo.
- La suite di test viene eseguita automaticamente dallo script `./scripts/release.ps1` prima di ogni pubblicazione.
- Per la verifica ordinaria del codice, limitarsi alla compilazione rapida con `dotnet build Macondo.sln`.

## Stile di Codice (C#)
- **Assegnazioni Semplici e Guard Clauses senza Graffe (a capo)**: Per guard clauses immediate o semplici assegnazioni/controlli di proprietà (es. `if (prop != val)` a capo `    prop = val;`), non usare parentesi graffe mantenendo la formattazione pulita con istruzione a capo riga indentata.

## Release Note per ogni Modifica
- **Per ogni modifica o funzionalità richiesta dall'utente**, fornire sempre al termine della risposta una **release note di una sola frase in lingua inglese**, chiara, sintetica e formale.
