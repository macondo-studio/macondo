---
description: Automatic version synchronization across all project and website files
trigger: always_on
---

# Automated Version Synchronization and Release Pipeline

- **Application and Website Sources (Monorepo)**: Repository root (website under `docs/` for GitHub Pages).
- **No Build for Website-Only Changes**: When making changes strictly to website files (`docs/`, HTML, CSS, JS, images), **DO NOT compile (`dotnet build`)**. Builds should only occur when modifying C#/.NET code or running the release pipeline.

## Token-Efficient Automated Release

When the user requests to publish, update, or release a new version (e.g., "publish a new version", "bump version", "new release"):

1. **Execute the Unified Automation Script Directly**:
   - Run directly:
     `./scripts/release.ps1 [-BumpType Patch|Minor|Major] [-NewVersion "<X.Y.Z>"] [-GitCommit]`
   - The script automatically executes the entire end-to-end cycle:
     1. Automated xUnit test suite execution (halts if any test fails).
     2. Automated SemVer version calculation (default Patch, or Minor/Major/NewVersion).
     3. Automatic extraction of Git commit notes since the last release.
     4. Contextual synchronization of `Macondo.App.csproj`, `UpdateService.cs`, `docs/version.json`, and all `docs/*.html` pages.
     5. Automatic updates of formatted dates ("Last updated" / "Ultimo aggiornamento") in HTML pages.
     6. Publication of the portable single-file executable (`publish/win-x64/Macondo.exe`).
     7. Generation of the Inno Setup installer (`publish/win-x64/Macondo-Setup.exe`).
     8. Creation of the ready GitHub release manifest (`publish/win-x64/RELEASE_NOTES_GITHUB.md`).
2. **No Manual Steps**:
   - The assistant must NOT manually edit version strings, dates, or manifest files: delegate the entire process to `./scripts/release.ps1`.
   - Present the final summary output produced by the script along with the generated release notes.

## Routine Release Notes
- For every user task or modification, always provide a concise, single-sentence English release note at the end of the response.
