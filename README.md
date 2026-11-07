# Macondo • Visual Code Graph & Semantic Analysis Suite

<p align="center">
  <img src="docs/logo.svg" width="96" height="96" alt="Macondo Logo">
</p>

<p align="center">
  <strong>Deterministic Visual Code Graph, Blast Radius Impact Analyzer &amp; Architecture Suite for Legacy and Modern Codebases</strong>
</p>

<p align="center">
  <a href="https://macondo-studio.com"><img src="https://img.shields.io/badge/Website-macondo--studio.com-4F46E5?style=flat-square" alt="Website"></a>
  <a href="https://github.com/macondo-studio/macondo/releases/latest"><img src="https://img.shields.io/badge/Release-v1.8.0-10B981?style=flat-square" alt="Latest Release"></a>
  <a href="LICENSE.md"><img src="https://img.shields.io/badge/License-Source--Available-blue?style=flat-square" alt="License"></a>
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square" alt=".NET 8">
  <img src="https://img.shields.io/badge/UI-Avalonia%2011-EC4899?style=flat-square" alt="Avalonia 11">
  <img src="https://img.shields.io/badge/Privacy-100%25%20Offline-green?style=flat-square" alt="Offline">
</p>

---

## Overview

**Macondo** is a desktop engineering suite and headless static analysis engine built with **.NET 8** and **Avalonia UI**. It is engineered to deconstruct, visualize, and safely refactor mission-critical legacy applications and modern architectures across **Visual Basic 6.0**, **C# / .NET**, **Java**, and **Kotlin**.

Unlike basic heuristic linters, Macondo builds a **deterministic in-memory semantic graph (`CodeGraph`)** with full cross-project type resolution, OOP inheritance hierarchies, call-site mapping, and instantiation tracking.

### Key Capabilities
1. **Multi-Language AST Extraction**: Full semantic parsing of Visual Basic 6.0 (`.vbp`, `.vbg`, `.frm`, `.bas`, `.cls`), C# / .NET (`.csproj`, `.sln`, `.cs`), Java / Android (Maven `pom.xml`, Gradle `build.gradle`, `.java`), and Kotlin (`build.gradle.kts`, `.kt`, `.kts`).
2. **Interactive 60 FPS Visual Code Graph**: GPU-accelerated canvas with 4 layout algorithms (Radial, Affinity, Clustered Subsystems, Force-Directed Physics) supporting massive 5,000+ node graphs.
3. **Blast Radius & Impact Matrix**: Recursive upstream and downstream dependency resolution with automated regression risk scoring (`LOW`, `MEDIUM`, `HIGH`, `CRITICAL`).
4. **20+ Built-In Analytical Views**: Design Structure Matrix (DSM), Cyclomatic Complexity Treemaps, Risk Hotspots, UI Screen Flow diagrams, Control Flow Graphs (CFG), Martin Package Metrics, Dead Code Detection, Code Clones, and Data Flow.
5. **AI Context Slices & Repo Maps**: One-click extraction of token-efficient, hierarchical knowledge graphs optimized for LLM prompts (Claude Code, Cursor, ChatGPT, Gemini, Copilot).
6. **100% Local & Offline**: Absolute data privacy with zero cloud transmission or telemetry.

---

## Solution Architecture

The repository is organized as a clean, modular .NET 8 monorepo:

```text
macondo/
├── docs/                                     # Official documentation and GitHub Pages website
│   ├── index.html                            # Landing page (English)
│   ├── it.html                               # Landing page (Italian)
│   ├── guide.html / guide-it.html            # Complete user manuals
│   └── version.json                          # Release manifest and update endpoint
│
├── scripts/                                  # Fully automated PowerShell release pipelines
│   ├── bump-and-publish.ps1                  # Single-command end-to-end release pipeline
│   ├── build-installer.ps1                   # Inno Setup compiler integration
│   ├── publish-single-file.ps1               # Portable single-file Windows x64 publisher
│   ├── release.ps1                           # Unified release entry point
│   └── run-tests.ps1                         # xUnit automated test suite runner
│
├── VB6AnalyzerPOC/                           # Desktop GUI Application (Avalonia 11)
│   └── src/VB6Analyzer.App/
│       ├── Controls/                         # Graph, DSM, Treemap, Hotspots, ScreenFlow, CFG
│       ├── Layout/                           # Radial, Affinity, Islands, Force-Directed engines
│       ├── Models/                           # VisualCodeGraph, BlastRadiusGridRow, ViewModels
│       ├── Services/                         # LocalizationService, SettingsService, CacheService
│       └── Views/                            # Main 3-pane window and dialogs
│
├── VB6ParserSpike/                           # Core Parsing & Semantic Analysis Engine
│   └── src/
│       ├── VB6Analyzer.Parser/               # ANTLR4 lexer and parser for Visual Basic 6.0
│       └── VB6Analyzer.Semantic/             # Semantic extractors (VB6, C#, Java, Kotlin, VB.NET),
│                                             # Call Graph Builder, Blast Radius, Metrics
│
└── tests/
    └── VB6Analyzer.Tests/                    # Comprehensive xUnit automated test suites
```

---

## Core Features

### 1. Interactive Visual Code Graph (`GraphControl`)
- **Hardware-Accelerated Canvas**: Custom Avalonia vector rendering supporting large-scale codebases with sub-millisecond interaction.
- **Dynamic Relationship Highlighting**: Selecting any routine immediately highlights its upstream callers (*Callers* in amber) and downstream calls (*Callees* in cyan).
- **Container Collapse / Expand**: Double-click Forms, Modules, or Classes to collapse or inspect internal routine structures.
- **4 Algorithmic Layout Engines**:
  - *Standard Radial*: Concentric hierarchical arrangement.
  - *Affinity Radial*: Call-cohesion grouping to surface tight couplings.
  - *Clustered Islands*: Subsystem partitioning for decoupled components.
  - *Force-Directed (Spring Physics)*: Real-time physical attraction/repulsion simulation.

### 2. Blast Radius & Change Impact Analysis
- **Recursive Transitive Impact**: Compute the upstream blast radius across arbitrary depths to identify every method, UI form, and business entity affected before modifying legacy code.
- **Risk Categorization**: Automatic risk scoring based on Fan-In, Fan-Out, Cyclomatic Complexity, and distance from entry points.
- **Call Chain Visualization**: Full trace path from trigger UI events (buttons, menu clicks) down to internal database helpers.

### 3. Deep Architectural & Quality Views
- **Design Structure Matrix (DSM)**: Identify circular dependencies and architectural layers at a glance.
- **Metrics Treemap**: Hierarchical area-proportional visualization colored by Cyclomatic Complexity (CC) with interactive drill-down.
- **Risk Hotspots**: 2D scatter plots isolating critical methods (High Complexity vs High Churn / In-Degree).
- **Screen Flow Diagram**: Visual state machine displaying VB6 Form transitions, modal dialogs, and navigation triggers.
- **Control Flow Graph (CFG)**: Basic-block flow graphs for individual routines with branch complexity calculations.
- **Tabular Blast Radius Grid**: Sortable, searchable workspace audit with risk-level filtering.

### 4. AI Agent & LLM Harness Integration
`Macondo.exe` acts as a deterministic knowledge extraction tool for LLMs and autonomous agents:
- **Repo Map Export (`--export-md`)**: Condensed, token-efficient Markdown summarizing modules, classes, methods, parameters, and calls.
- **Semantic JSON Export (`--export-json`)**: Complete typed `CodeGraph` serialization ready for graph databases or programmatic reasoning.
- **Context Slice (1-Click)**: Extracts the isolated semantic subgraph for any selected symbol directly to the clipboard or `.md` file, ready to paste into Claude, ChatGPT, Cursor, or Gemini.

---

## Building from Source

### Prerequisites
- **[.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** (or later).
- **[Java JRE/JDK 11+](https://adoptium.net)** (required **only** at build time for the ANTLR4 grammar compilation task; pure C# runtime).

### Build Solution
```bash
dotnet build VB6AnalyzerPOC/VB6AnalyzerPOC.sln
```

### Run Automated Tests
```bash
dotnet test VB6AnalyzerPOC/VB6AnalyzerPOC.sln --nologo -v minimal
```

### Launch Desktop Application
```bash
dotnet run --project VB6AnalyzerPOC/src/VB6Analyzer.App
```

---

## Headless & CLI Usage

The single executable `Macondo.exe` can be executed directly from a terminal or CI/CD pipeline to analyze projects and generate reports without opening the GUI:

```bash
# Export hierarchical Markdown Repo Map for AI context
Macondo.exe analyze "C:\Projects\LegacyApp" --export-md "repo-map.md"

# Export complete semantic CodeGraph to structured JSON
Macondo.exe analyze "C:\Projects\LegacyApp\Billing.vbp" --export-json "graph.json"

# Export both formats simultaneously
Macondo.exe analyze "C:\Projects\LegacyApp" --export-md "repo-map.md" --export-json "graph.json"
```

### Command Options:
- `--export-md <path>`, `-m <path>`: Output condensed Markdown architecture summary.
- `--export-json <path>`, `-j <path>`: Output full typed `CodeGraph` JSON.
- `--help`, `-h`: Display command-line options and syntax.

---

## Internationalization (i18n) & Contributor Rules

> [!IMPORTANT]
> **MANDATORY REQUIREMENT FOR CODE MODIFICATIONS:**  
> The application natively supports full **bilingual localization (English and Italian)** via `Macondo.App.Services.LocalizationService`.

When submitting code or adding features:
1. **Never Hardcode User-Facing Strings**: All labels, menu items, tooltips, dialogs, status messages, and errors must use `LocalizationService.Instance.Get("Key")`.
2. **Update Both Dictionaries in `LocalizationService.cs`**:
   - `_enStrings`: English translation.
   - `_itStrings`: Italian translation.
3. **Format Parameters**: Use standard placeholders (`{0}`, `{1}`) via `LocalizationService.Instance.Get("Key_Name", arg0, arg1)`.

---

## License & Intellectual Property

Macondo is distributed under the **[Macondo Source-Available License](LICENSE.md)**:

* **Permitted Use & Audit**: You are free to inspect, study, compile, and run the software for personal, educational, research, and **internal commercial/business purposes** (including analyzing proprietary corporate software).
* **Community Contributions**: Bug reports, feature suggestions, and Pull Requests are welcomed under these license terms.
* **Prohibition of Commercial Redistribution**: You may not sell, sublicense, monetize, package, or embed Macondo (including parsers, semantic models, and algorithms) into competing or commercial third-party products and services.
* **Branding & Assets Reserved**: The name "Macondo", the Macondo Studio brand, application icons, and visual assets remain the exclusive intellectual property of Macondo Studio.

For complete terms and legal details, please read **[LICENSE.md](LICENSE.md)**.

---

## Contact & Support

- **Bug Reports & Issues:** [GitHub Issues](https://github.com/macondo-studio/macondo/issues)
- **Official Website:** [macondo-studio.com](https://macondo-studio.com)
- **Direct Inquiries:** [info@macondo-studio.com](mailto:info@macondo-studio.com)
