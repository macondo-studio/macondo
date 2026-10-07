; ==============================================================================
; Macondo (VB6 Analyzer) - Inno Setup Script
; ==============================================================================

#ifndef MyAppVersion
  #define MyAppVersion "1.5.0"
#endif

#define MyAppName "Macondo"
#define MyAppPublisher "Macondo Studio"
#define MyAppURL "https://macondo-studio.github.io/macondo/"
#define MyAppSupportURL "https://github.com/macondo-studio/macondo/issues"
#define MyAppExeName "Macondo.exe"

[Setup]
; Identificatore univoco GUID dell'applicazione (non modificare tra versioni)
AppId={{7B15E398-32D9-4D88-9BE2-0A5AE589839F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppSupportURL}
AppUpdatesURL={#MyAppURL}

; Modalità di installazione: per singolo utente (senza richiesta UAC) o amministratore
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Output
OutputDir=..\publish\win-x64
OutputBaseFilename=Macondo-Setup
SetupIconFile=..\icons\icon1_star_knot.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

; Metadati del file eseguibile Setup (Windows PE VersionInfo)
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoCopyright=Copyright 2026 {#MyAppPublisher}

; Compressione e architettura a 64 bit
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Gestione aggiornamenti: chiude l'app se è attualmente aperta prima di sovrascrivere
CloseApplications=yes
CloseApplicationsFilter=Macondo.exe
RestartApplications=no

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[CustomMessages]
italian.AppDescription=Analizzatore statico moderno di codice sorgente VB6
english.AppDescription=Modern static code analyzer for VB6 legacy codebases
