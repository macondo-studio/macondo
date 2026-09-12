<p align="center">
  <img src="logo.svg" width="96" height="96" alt="Macondo Logo">
</p>

# Macondo

**High-Performance Semantic Code Graph & Reverse Engineering Tool for Visual Basic 6.0**

[🌐 Official Website](https://macondo-studio.github.io/macondo/) • [⬇️ Download Latest Release](https://github.com/macondo-studio/macondo/releases/latest) • [📄 License](LICENSE.md)

---

## ⚡ Overview

**Macondo** is a desktop developer tool designed to deconstruct, visualize, and safely refactor complex legacy Visual Basic 6.0 (`.vbp`, `.frm`, `.bas`, `.cls`, `.ctl`) codebases.

- **Deterministic AST Parser:** Instant cross-module call resolution, class hierarchy mapping, and routine dependency tracking.
- **Interactive Code Graph:** 4 layout algorithms (Standard Radial, Affinity, Clustered Islands, Force-Directed Physics) with 60 FPS hardware acceleration.
- **Blast Radius & Impact Matrix:** Recursive upstream caller analysis with automatic regression risk scoring (Low, Medium, High, Critical).
- **Symbol Inspector:** Full bidirectional call chain inspection (*Who Calls Me* / *What Do I Call*) with source line coordinate mapping.
- **AI Context & LLM Slicing:** One-click Markdown/JSON Repo Map and Context Slice export optimized for Claude, ChatGPT, Gemini, Copilot, etc. prompts.
- **VCS & Fast Cache:** Integrated Git / SVN branch switching and sub-second cold starts with persistent local caching.

---

## 💻 System Requirements & Installation

- **OS:** Windows 10 / Windows 11 (64-bit)
- **Distribution:** Single-File Portable Executable (`Macondo.exe` - No installation or unzipping needed)
- **Privacy:** 100% Local & Offline execution (Zero cloud transmission)

### Quick Start (GUI):
1. Download `Macondo.exe` directly from [Releases](https://github.com/macondo-studio/macondo/releases/latest).
2. Double-click `Macondo.exe` to launch.
3. Go to **File → Open Project / Folder...** and select your VB6 project directory.

### 🤖 CLI & LLM Harness Integration:
`Macondo.exe` can also be invoked in headless mode directly from a terminal, CI/CD pipeline, or as a deterministic tool inside an **LLM Harness / AI Agent** (Claude Code, Cursor, Antigravity, MCP servers, etc.) to export structured knowledge graphs without UI:

```bash
# Export Markdown Repo Map for AI context
Macondo.exe analyze "C:\Projects\MyVB6Project" --export-md "repo-map.md"

# Export complete semantic CodeGraph to JSON
Macondo.exe analyze "C:\Projects\MyVB6Project\App.vbp" --export-json "graph.json"

# Export both formats simultaneously
Macondo.exe analyze "C:\Projects\MyVB6Project" --export-md "repo-map.md" --export-json "graph.json"
```

---

## 📜 License

Macondo is provided free of charge for personal, educational, and commercial use.  
See [LICENSE.md](LICENSE.md) for full terms and conditions.
