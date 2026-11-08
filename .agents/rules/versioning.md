---
description: Regola automatica per la sincronizzazione delle versioni su tutti i file del progetto e del sito web
trigger: always_on
---

# Sincronizzazione Automatica delle Versioni e Release Pipeline

- **Sorgenti applicazione e sito web (Monorepo)**: radice del repository (sito in `docs/` per GitHub Pages)
- **Nessuna Build per Modifiche al Solo Sito**: Quando si apportano modifiche unicamente ai file del sito web (`docs/`, HTML, CSS, JS, immagini), **NON eseguire la compilazione (`dotnet build`)**. La build va effettuata solo quando si modifica il codice C#/.NET o durante la pipeline di release.

## Pubblicazione e Release ad Alto Risparmio di Token

Quando l'utente richiede di pubblicare, aggiornare o rilasciare una nuova versione (es. "pubblica una nuova versione", "bump version", "nuova release"):

1. **Eseguire l'Unico Script di Automazione**:
   - Eseguire direttamente:
     `./scripts/release.ps1 [-BumpType Patch|Minor|Major] [-NewVersion "<X.Y.Z>"] [-GitCommit]`
   - Lo script esegue automaticamente l'intero ciclo end-to-end:
     1. Test automatizzati xUnit (blocco se falliscono).
     2. Risoluzione automatica della versione (default Patch, o Minor/Major/NewVersion).
     3. Estrazione automatica note dai commit Git dall'ultimo rilascio.
     4. Aggiornamento contestuale di `Macondo.App.csproj`, `UpdateService.cs`, `docs/version.json` e tutte le pagine `docs/*.html`.
     5. Aggiornamento automatico delle date formattate ("Ultimo aggiornamento" / "Last updated") nelle pagine HTML.
     6. Pubblicazione dell'eseguibile unico portable (`publish/win-x64/Macondo.exe`).
     8. Generazione installer Inno Setup (`publish/win-x64/Macondo-Setup.exe`).
     9. Creazione automatica del file `publish/win-x64/RELEASE_NOTES_GITHUB.md`.
2. **Nessun Passaggio Manuale**:
   - L'assistente NON deve modificare manualmente file, versioni o date né richiedere conferme superflue: delegare l'intero processo a `./scripts/release.ps1`.
   - Riportare all'utente il riepilogo finale prodotto dallo script con le note di rilascio pronte.

## Note di Rilascio Ordinarie
- Per ogni modifica o richiesta dell'utente, fornire sempre al termine della risposta una release note sintetica di una sola frase in lingua inglese.
