# VB6 Analyzer — M1 Spike: parser ANTLR4

Spike per validare la decisione presa in sezione 44 del project charter: grammar
ANTLR4 `VisualBasic6` (ProLeap/`antlr/grammars-v4`), rigenerata per il target C#.

> ⚠️ **Stessa nota delle volte precedenti**: scritto senza poter compilare/eseguire
> in questo ambiente (niente SDK .NET, niente accesso a NuGet). Il codice segue le
> convenzioni documentate di ANTLR4 target C# (nomi di classi generate, firme del
> Visitor, error listener) con la massima attenzione, ma **va validato con una build
> reale** — è esattamente lo scopo di questo spike.

## Prerequisiti aggiuntivi rispetto al POC M0

Oltre a .NET 8 SDK, questa volta serve anche:

- **Java (JRE o JDK), versione 11 o superiore** — usato SOLO in fase di build da
  `Antlr4BuildTasks`, che invoca lo strumento `antlr4` per rigenerare lexer/parser
  C# dai file `.g4` ad ogni `dotnet build`. **Non serve a runtime**: il risultato
  della generazione è codice C# puro, l'exe finale non porta con sé alcuna
  dipendenza Java. Se non hai Java: https://adoptium.net (Temurin, gratuito).

`Antlr4BuildTasks` proverebbe di norma a scaricare da solo sia la JRE sia il jar
`antlr4-*-complete.jar` al primo build. **Questo progetto è già configurato per
NON fare quel download automatico** (spesso fallisce dietro proxy/antivirus
aziendali) e usa invece un jar scaricato manualmente — vedi sotto.

### Setup del jar ANTLR (una tantum)

1. Scarica **esattamente questo file**:
   ```
   https://repo1.maven.org/maven2/org/antlr/antlr4/4.13.2/antlr4-4.13.2-complete.jar
   ```
2. Mettilo qui, senza rinominarlo:
   ```
   src/VB6Analyzer.Parser/external/antlr4-4.13.2-complete.jar
   ```
3. Verifica che Java sia sul PATH (apri un **nuovo** terminale dopo l'installazione):
   ```powershell
   java -version
   ```

Il `.csproj` di `VB6Analyzer.Parser` ha già la proprietà `AntlrProbePath` che punta
a `external/`, e la versione di `Antlr4.Runtime.Standard` è fissata a `4.13.2` per
combaciare esattamente con quel jar.

## Troubleshooting

**`error ANT02: Cannot find Antlr4 jar file` / `Went through the complete probe list...`**

Il download automatico del jar è fallito. Segui "Setup del jar ANTLR" sopra. Se hai
già seguito quei passaggi e l'errore persiste, controlla:
- il file si chiama esattamente `antlr4-4.13.2-complete.jar` (non `antlr-...`, senza
  la seconda "4" — è il nome usato dal sito antlr.org, va rinominato se scaricato da lì)
- si trova in `src/VB6Analyzer.Parser/external/` (non altrove)
- la versione nel nome del file combacia con quella di `Antlr4.Runtime.Standard`
  nel `.csproj` (attualmente `4.13.2`)

**Errore relativo a `java` non trovato / non riconosciuto come comando**

Java non è sul PATH. Reinstalla da https://adoptium.net assicurandoti di spuntare
"Add to PATH" durante l'installazione, poi apri un **nuovo** terminale/riavvia
Visual Studio (il PATH si aggiorna solo per i processi aperti dopo la modifica).

## Come eseguire

```bash
cd src/VB6Analyzer.Parser.Spike
dotnet run
```

Per ogni fixture in `Fixtures/*.cls` stampa:
- esito del parsing (OK / errori con linea e colonna)
- i nomi dei `Sub` trovati (dimostrazione minima del pattern Visitor)
- l'albero sintattico in formato LISP (stile identico agli esempi nel README di
  ProLeap, per poterli confrontare a occhio)

## Cosa contiene

```
src/
  VB6Analyzer.Parser/              — libreria: grammar + codice generato da ANTLR4
    Grammar/
      VisualBasic6Lexer.g4         — copia della grammar ufficiale (MIT, vedi ORIGIN.md)
      VisualBasic6Parser.g4
      ORIGIN.md                    — provenienza e licenza della grammar
  VB6Analyzer.Parser.Spike/        — eseguibile di test
    Fixtures/*.cls                 — piccoli snippet VB6 mirati
    Program.cs
```

## I fixture e perché ci sono

| File | Cosa verifica |
|---|---|
| `00_HelloWorld.cls` | Sanity check — esempio esatto dal README di ProLeap. Se questo fallisce, il problema è nella generazione/integrazione, non nella grammar. |
| `01_QualifiedMemberCall.cls` | Chiamata a membro qualificata (`Customer.Update`) — caso semplice, nessuna ambiguità. |
| `02_WithImplicitCall.cls` | **Il test principale**: chiamata implicita dentro un blocco `With` (`.Update`, senza oggetto esplicito). Verifica il rischio aperto della sezione 44.4. |
| `03_UnqualifiedCallAndCall.cls` | `Call Validate` e chiamata non qualificata `Validate` (sezione 14). |
| `04_SetNewInstantiation.cls` | `Set` / `New` — istanziazione esplicita (sezione 12). |
| `05_LateBindingCreateObject.cls` | `Object` + `CreateObject` — late binding (sezione 11/14): qui è normale e atteso che la relazione non sia risolvibile staticamente, il parser deve solo riconoscere la sintassi. |

## Il test mirato: `With` — cosa guardare esattamente

Confronta l'albero di `01_QualifiedMemberCall.cls` (`Customer.Update`) con quello di
`02_WithImplicitCall.cls` (`.Update`, dentro `With Customer`).

Nella grammar, entrambe le forme passano dalla regola `iCS_S_MembersCall`:

```text
iCS_S_MembersCall
    : (iCS_S_VariableOrProcedureCall | iCS_S_ProcedureOrArrayCall)? iCS_S_MemberCall+ dictionaryCallStmt?
    ;

iCS_S_MemberCall
    : WS? DOT (iCS_S_VariableOrProcedureCall | iCS_S_ProcedureOrArrayCall)
    ;
```

La parte prima di `iCS_S_MemberCall+` è **opzionale**: questo è il meccanismo con cui
la grammar dovrebbe distinguere i due casi:

- `Customer.Update` → `iCS_S_MembersCall` **con** un `iCS_S_VariableOrProcedureCall`
  (`Customer`) prima del/dei `iCS_S_MemberCall`
- `.Update` (dentro `With`) → `iCS_S_MembersCall` **senza** quella parte iniziale,
  solo `iCS_S_MemberCall` (il punto seguito da `Update`)

Se negli alberi stampati vedi questa differenza strutturale, la grammar distingue
correttamente i due casi — buona notizia per la symbol resolution di M3, che dovrà
sapere quando un accesso è "sull'oggetto del `With` corrente" invece che su un
simbolo esplicito.

> Nota: questo NON verifica l'altro caso storicamente problematico citato nella
> sezione 44.4 (`SomeProcedure .Name`, chiamata a una procedura seguita da un accesso
> implicito su una riga sola) — quel costrutto è più raro e ambiguo anche da
> costruire come esempio valido; l'abbiamo lasciato fuori da questo primo spike per
> non introdurre un fixture di dubbia correttezza. Se il caso base (`.Update` da
> solo) funziona, vale la pena aggiungerlo come test successivo.

## ✅ Esito M1 (confermato)

Tutti i fixture parsano correttamente. Il rischio `With` è confermato risolto: la
grammar distingue strutturalmente `Customer.Update` (con `implicitCallStmt_InStmt`
prima della member call) da `.Update` dentro `With` (senza), non tramite whitespace.
Dettagli nella sezione 44.6 del project charter.

---

# M2 — Symbol extraction

Secondo spike, in `src/VB6Analyzer.Semantic` + `src/VB6Analyzer.Semantic.Spike`.
Trasforma il parse tree ANTLR (M1) in `Symbol`/`Relationship` del `CodeGraph` reale
(sezioni 8-10 del charter) — non più il modello fittizio del POC Avalonia (M0).

## Come eseguire

```bash
cd src/VB6Analyzer.Semantic.Spike
dotnet run
```

## Scope di questo spike (deliberatamente limitato)

Per rispettare il principio della sezione 6 ("non bisogna confondere parsing e
semantic analysis"):

**Estratto**: `Method` (Sub/Function), `Property` (Get/Let/Set uniti per nome se
condividono lo stesso nome), `Field` (variabili sia a livello di modulo che locali
dentro un Sub/Function/Property), `Constant`, `Enum` (solo il nome, non ancora i
singoli valori). Relazione `CONTAINS` (strutturale, sempre `Confidence.Exact`).

**Fuori scope, rimandato a M3/M4** (richiede symbol resolution, non solo parsing):
`CALLS`, `USES_TYPE`, `INSTANTIATES`, `REFERENCES`, `READS`, `WRITES`,
`HANDLES_EVENT` vero (richiederebbe i controlli dichiarati nel `.frm`, non ancora
parsati — vedi sezione 44.4), `INHERITS`, `IMPLEMENTS`. Il tipo dichiarato di una
variabile (`As Customer`) non è ancora estratto in un campo strutturato — il
modello `Symbol` non ha ancora un campo `Type` dedicato (sezione 9 lo lista come
possibile aggiunta futura).

Un Sub il cui nome somiglia a `Controllo_Evento` (es. `Command1_Click`) viene
segnalato in `LikelyEventHandlerNames` come euristica di **reporting**, non come
relazione nel grafo: creare una relazione verso un controllo che non abbiamo ancora
estratto dal `.frm` violerebbe il principio della sezione 11 ("il sistema non deve
inventare una relazione").

## Fixture

Riusa i 6 fixture di M1, più uno nuovo mai testato prima:

| File | Cosa verifica |
|---|---|
| `06_FieldsPropertiesConst.cls` | Field a livello di modulo (`mCount`), `Const`, `Property Get`/`Let` con lo stesso nome (devono unificarsi in **un solo** Symbol `Property`), Field locale dentro un Sub (`Dim i`) |

## Cosa guardare nell'output

Per ogni fixture, l'albero `CONTAINS` stampato dovrebbe rispecchiare la struttura
del file. Punti specifici da verificare per `06_FieldsPropertiesConst.cls`:

- `Count` deve comparire **una sola volta** come `[Property] Count`, con le righe
  che coprono l'intervallo di entrambi gli accessor (Get e Let), non duplicato
- `mCount` deve essere figlio del modulo (`06_FieldsPropertiesConst`), non di `Count`
  né di `Reset`
- `i` (il `Dim i As Integer` dentro `Reset`) deve essere figlio di `Reset`, non del
  modulo — verifica che lo stack dei container funzioni correttamente anche per
  variabili locali

Il riepilogo finale aggrega i simboli per `Kind` su **tutti** i fixture in un unico
`CodeGraph` condiviso — prima dimostrazione concreta che il grafo può accumulare
più file, come previsto dalla sezione 18.

## Prossimi passi dopo aver validato questo spike

1. Se la gerarchia e l'unificazione delle Property sono corrette: si passa a
   costruire M3 (symbol resolution) — risolvere `c.Save` sapendo che `c` è di tipo
   `Customer`, gestire `Me`, `With`, chiamate non qualificate, introducendo
   `ResolutionConfidence` sul serio (finora sempre `Exact` perché non risolviamo
   ancora nulla di ambiguo).
2. Estendere l'estrazione a `.bas` e `.frm` **senza** la sezione designer (il corpo
   codice dei form dovrebbe funzionare identico a una classe); il parsing della
   sezione `VERSION`/`BEGIN...END` dei `.frm` resta un rischio noto non ancora testato.
3. Aggiungere gli altri fixture della sezione 32 (`Implements`, `Inheritance`, `Events`).

