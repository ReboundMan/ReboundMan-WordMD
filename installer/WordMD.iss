; ==========================================================================
;  WordMD ("Word Doctor") -- Inno Setup installer script
;  Builds a single .exe installer for the self-contained app.
;  Usage:  & "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\WordMD.iss
; ==========================================================================

#define MyAppName       "WordMD"
#define MyAppVersion    "1.4.5"
#define MyAppPublisher  "ReboundMan"
#define MyAppURL        "https://github.com/ReboundMan/ReboundMan-WordMD"
#define MyAppExeName    "WordMD.exe"
#define MyAppId         "{{A6E3D6F8-2B5E-4B10-9C40-7C3F3B5E1D70}"

#define SourceDir "..\src\WordMD\bin\Release\net8.0-windows10.0.26100.0\win-x64\publish"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=WordMD-Setup-{#MyAppVersion}
SetupIconFile=..\src\WordMD\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "fileassoc";   Description: "Associate &.md files with WordMD"; GroupDescription: "File associations:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\WordMD.MarkdownDocument"; ValueType: string; ValueName: ""; ValueData: "Markdown Document"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\WordMD.MarkdownDocument\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\WordMD.MarkdownDocument\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#MyAppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""

Root: HKA; Subkey: "Software\Classes\.md";        ValueType: string; ValueName: ""; ValueData: "WordMD.MarkdownDocument"; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.markdown";  ValueType: string; ValueName: ""; ValueData: "WordMD.MarkdownDocument"; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids";       ValueType: string; ValueName: "WordMD.MarkdownDocument"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "WordMD.MarkdownDocument"; ValueData: ""; Flags: uninsdeletevalue

[Run]
; shellexec: launch via ShellExecuteEx instead of CreateProcess so SmartScreen / Defender
;            real-time scan locks on the freshly-extracted exe don't surface as "code 5".
; runasoriginaluser: drop elevation if the installer was launched elevated (e.g. user clicked
;            "More info > Run anyway" on the SmartScreen prompt) so the launched app inherits
;            the original interactive user's token.
Filename: "{app}\{#MyAppExeName}"; Description: "Launch WordMD"; Flags: nowait postinstall skipifsilent shellexec runasoriginaluser
