<p align="center">
  <img src="logo.svg" width="96" height="96" alt="Macondo Logo">
</p>

# Macondo

**High-Performance Semantic Code Graph, Blast Radius & Analysis Suite for Visual Basic 6.0 and C# / .NET**

[Official Website](https://macondo-studio.com) • [Download Latest Release](https://github.com/macondo-studio/macondo/releases/latest) • [License](LICENSE.md)

---

## Overview

**Macondo** is a desktop developer tool designed to deconstruct, visualize, and safely refactor complex legacy and modern codebases in **Visual Basic 6.0** (`.vbp`, `.frm`, `.bas`, `.cls`, `.ctl`) and **C# / .NET** (`.csproj`, `.sln`, `.cs`).

- **Deterministic Multi-Language AST Parser:** Instant cross-module call resolution, class hierarchy mapping, and routine dependency tracking.
- **Interactive Code Graph:** 4 layout algorithms (Standard Radial, Affinity, Clustered Islands, Force-Directed Physics) with 60 FPS hardware acceleration.
- **Blast Radius & Impact Matrix:** Recursive upstream caller analysis with automatic regression risk scoring (Low, Medium, High, Critical).
- **20+ Analytical Tools:** DSM Matrix, Metrics Treemap, Risk Hotspots, Screen Flow, Control Flow Graph (CFG), Martin Package Metrics, Dead Code Detection, Shared State, Temporal Coupling, Code Clones, Data Flow, CRUD Matrix, and more.
- **Symbol Inspector:** Full bidirectional call chain inspection (*Who Calls Me* / *What Do I Call*) with source line coordinate mapping.
- **AI Context & LLM Slicing:** One-click Markdown/JSON Repo Map and Context Slice export optimized for Claude, ChatGPT, Gemini, Copilot, Cursor, etc.
- **VCS & Fast Cache:** Integrated Git / SVN branch switching and sub-second cold starts with persistent local caching.

---

## System Requirements & Installation

- **OS:** Windows 10 / Windows 11 (64-bit)
- **Distribution:** Single-File Portable Executable (`Macondo.exe` - No installation or unzipping needed)
- **Privacy:** 100% Local & Offline execution (Zero cloud transmission)

### Quick Start (GUI)
1. Download `Macondo.exe` directly from [Releases](https://github.com/macondo-studio/macondo/releases/latest).
2. Double-click `Macondo.exe` to launch.
3. Go to **File → Open Project / Folder...** and select your VB6 or C# project directory.

### CLI & LLM Harness Integration
`Macondo.exe` can also be invoked in headless mode directly from a terminal, CI/CD pipeline, or as a deterministic tool inside an **LLM Harness / AI Agent** (Claude Code, Cursor, Antigravity, MCP servers, etc.) to export structured knowledge graphs without UI:

```bash
# Export Markdown Repo Map for AI context
Macondo.exe analyze "C:\Projects\MyCSharpApp\App.csproj" --export-md "repo-map.md"

# Export complete semantic CodeGraph to JSON
Macondo.exe analyze "C:\Projects\MyVB6Project\App.vbp" --export-json "graph.json"

# Export both formats simultaneously
Macondo.exe analyze "C:\Projects\MyProject" --export-md "repo-map.md" --export-json "graph.json"
```

---

## Feedback, Support & Bug Reports

Have a feature request, encountered an AST edge-case, or found a bug?
- **Public Discussions & Issues:** Open an issue on [GitHub Issues](https://github.com/macondo-studio/macondo/issues)
- **Direct & Confidential Inquiries:** Email us at [info@macondo-studio.com](mailto:info@macondo-studio.com)

---

## License

Macondo is provided free of charge for personal, educational, and commercial use.  
See [LICENSE.md](LICENSE.md) for full terms and conditions.
