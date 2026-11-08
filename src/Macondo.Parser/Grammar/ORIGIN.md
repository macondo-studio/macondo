# Grammar Origin and Licensing

The ANTLR4 grammar files located in this directory (`VisualBasic6Lexer.g4`, `VisualBasic6Parser.g4`) originate from:

https://github.com/antlr/grammars-v4/tree/master/vb6

which in turn derives them from:

https://github.com/uwol/proleap-vb6-parser

Copyright (C) 2017, Ulrich Wolffgang <ulrich.wolffgang@proleap.io>. All rights reserved.

Distributed under the MIT License (see the header of `VisualBasic6Parser.g4`).

## Rationale for In-Tree Inclusion

ANTLR4 requires source `.g4` grammar files to generate the target language lexer and parser (C# in our case) during the build process. There is no official precompiled "VB6 Grammar for C#" NuGet package available — code generation must happen locally.

For this reason, the `.g4` grammar files are committed directly to the repository rather than downloaded dynamically at build time. This ensures deterministic, reproducible builds and allows full offline compilation once standard NuGet packages are restored.

No modifications have been made to the original grammar contents.
