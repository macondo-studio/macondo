# Macondo Project Rules and Conventions

## Repository Structure and Website (Monorepo)
- **Website Sources**: Located in the `docs/` folder in the repository root (natively served by GitHub Pages).
- **No Build for Website-Only Changes**: When modifications affect only website files (`docs/`, HTML, CSS, JavaScript, images) and not C#/.NET source code, **DO NOT run compilation (`dotnet build`)**. Builds should only be executed when C# code is modified or during the release pipeline.

## Automated Release and Publishing Pipeline (Zero-Token Release)

Whenever the user requests to publish, update, or release a new version (e.g., "publish a new version", "release patch/minor", "bump version"):

1. **Execute the Unified Script Directly**:
   - Run in a single command without intermediate manual steps:
     `./scripts/release.ps1 [-BumpType Patch|Minor|Major] [-NewVersion "<X.Y.Z>"] [-GitCommit]`
   - *(Or `./scripts/bump-and-publish.ps1`)*

2. **Integrated Automations Handled Fully by the Script**:
   - **Automated Tests**: Automatically runs the xUnit test suite (`run-tests.ps1`) and halts the release if any test fails.
   - **SemVer Version Calculation**: Automatically calculates the next version (Patch by default, or according to BumpType/NewVersion).
   - **Automatic Release Notes Extraction**: Automatically extracts relevant Git commit messages since the previous release.
   - **Atomic File Synchronization**: Updates `src/Macondo.App/Macondo.App.csproj`, `UpdateService.cs`, `docs/version.json`, and all `docs/*.html` pages.
   - **Website Date Formatting**: Automatically updates formatted dates ("Last updated" / "Ultimo aggiornamento") in HTML pages.
   - **Distributable Binaries**: Compiles `publish/win-x64/Macondo.exe` (Single-File) and `publish/win-x64/Macondo-Setup.exe` (Inno Setup).
   - **GitHub Release Manifest**: Generates the ready-to-use `publish/win-x64/RELEASE_NOTES_GITHUB.md`.

3. **User Output**:
   - Display the execution summary and the release notes ready for GitHub from `RELEASE_NOTES_GITHUB.md`.
   - Do not require manual intermediate steps: always delegate the full workflow to the script.

## Unit Test Execution
- **DO NOT run tests (`dotnet test`)** during ordinary development iterations or minor edits.
- The unit test suite is executed automatically by `./scripts/release.ps1` before each release.
- For ordinary code verification, rely on quick compilation with `dotnet build Macondo.sln`.

## Code Style (C#)
- **Simple Assignments and Guard Clauses Without Braces (Newline)**: For immediate guard clauses or simple property assignments/checks (e.g., `if (prop != val)` newline `    prop = val;`), omit curly braces, keeping clean formatting with an indented newline statement.

## Release Note for Every Task
- **For every task, edit, or feature requested by the user**, always provide at the very end of the response a **formal, single-sentence release note in English**.
