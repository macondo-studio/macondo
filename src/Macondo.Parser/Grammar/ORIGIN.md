# Origine e licenza della grammar

I file in questa cartella (`VisualBasic6Lexer.g4`, `VisualBasic6Parser.g4`) provengono da:

https://github.com/antlr/grammars-v4/tree/master/vb6

che a sua volta li deriva da:

https://github.com/uwol/proleap-vb6-parser

Copyright (C) 2017, Ulrich Wolffgang <ulrich.wolffgang@proleap.io>. Tutti i diritti riservati.

Distribuiti sotto licenza MIT (vedi intestazione di `VisualBasic6Parser.g4`).

## Perché sono qui invece che referenziati come pacchetto

ANTLR4 richiede i file `.g4` sorgente per generare lexer/parser nel linguaggio target
(C#, nel nostro caso) in fase di build. Non esiste un pacchetto NuGet "grammar VB6
precompilata per C#" — vanno rigenerati localmente. Per questo i `.g4` sono copiati
qui invece che scaricati automaticamente: il progetto deve poter compilare anche
offline, una volta ripristinati i pacchetti NuGet una prima volta.

Non sono state apportate modifiche al contenuto dei file rispetto all'origine.
