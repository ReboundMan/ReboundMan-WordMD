# Installing WordMD

WordMD ships as a self-contained Windows installer. No external runtime install is required.

## Installer

1. Download `WordMD-Setup-<version>.exe` (e.g. `WordMD-Setup-1.4.4.exe`) from the GitHub release.
2. Double-click and follow the prompts.
3. Optional checkboxes during setup:
   - **Create a desktop shortcut**
   - **Associate `.md` files with WordMD** (so right-click → *Open* uses WordMD)

The installer writes to `%LOCALAPPDATA%\Programs\WordMD` by default and does **not** require admin rights.

## After install

- **Launch:** Start menu → *WordMD*, or double-click any `.md` file (if associated).
- **Right-click any `.md` → Open with → WordMD** is always available, even without the file association checkbox.
- **Pass a file on the command line:** `WordMD.exe "C:\path\to\file.md"`.

## Uninstall

- Settings → Apps → *WordMD* → Uninstall, or
- Start menu → *WordMD* → *Uninstall WordMD*.

User settings live in `%APPDATA%\WordMD\settings.json` and recovery snapshots in `%LOCALAPPDATA%\WordMD\recovery\`. Uninstall does **not** remove these by design.

## Building the installer from source

```powershell
# 1. Publish a self-contained release build.
#    NOTE: do NOT pass `-p:Platform=x64` -- that would land output at
#    bin\x64\Release\... but the Inno Setup script reads from bin\Release\...
cd src\WordMD
dotnet publish -c Release -r win-x64 `
               -p:WindowsAppSDKSelfContained=true `
               -p:SelfContained=true `
               -p:PublishTrimmed=false

# 2. Compile the Inno Setup installer
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" ..\..\installer\WordMD.iss
# Output: dist\WordMD-Setup-<version>.exe  (version is set in installer\WordMD.iss)
```

Prerequisites:
- .NET 8 SDK -- `winget install Microsoft.DotNet.SDK.8`
- **Node.js 18+** -- `winget install OpenJS.NodeJS.LTS` (used to build the `web/` Milkdown + CodeMirror bundle; MSBuild invokes `npm install` + `npm run build` automatically on first build)
- Inno Setup 6 -- `winget install JRSoftware.InnoSetup`
- WebView2 Runtime (preinstalled on Windows 10 21H2+ / Windows 11)

## Code-signing (deferred)

The MVP installer is **unsigned**. Windows SmartScreen will show a "Windows protected your PC" warning the first time it runs. Click *More info → Run anyway*. Code-signing is on the roadmap for a future release.

## Telemetry

WordMD has an opt-in telemetry hook (off by default). When enabled, anonymous events
are written locally to `%LOCALAPPDATA%\WordMD\telemetry\events-YYYYMMDD.jsonl`.
No document content or file paths are ever logged. Toggle with **Help → Send Anonymous Usage Data**.
