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
- **AI Context & LLM Slicing:** One-click Markdown/JSON Repo Map and Context Slice export optimized for Claude, ChatGPT, Gemini, and Copilot prompts.
- **VCS & Fast Cache:** Integrated Git / SVN branch switching and sub-second cold starts with persistent local caching.

---

## 💻 System Requirements & Installation

- **OS:** Windows 10 / Windows 11 (64-bit)
- **Distribution:** Single-File Portable Executable (`Macondo.exe` - No installation or unzipping needed)
- **Privacy:** 100% Local & Offline execution (Zero cloud transmission)

### Quick Start:
1. Download `Macondo.exe` directly from [Releases](https://github.com/macondo-studio/macondo/releases/latest).
2. Double-click `Macondo.exe` to launch.
3. Go to **File → Open VB6 Folder...** and select your VB6 project directory.

---

## 📜 License

Macondo is provided free of charge for personal, educational, and commercial use.  
See [LICENSE.md](LICENSE.md) for full terms and conditions.
