# WordMD Deployment and Release Runbook

## Where it lives

| | |
|---|---|
| Product | WordMD Windows desktop app |
| Distribution | GitHub Releases, installer artifact |
| Installer | Inno Setup, `installer\WordMD.iss` |
| Production URL | `https://github.com/ReboundMan/ReboundMan-WordMD/releases/latest` |
| Railway service | `n/a` |
| Firebase project | `n/a` |
| Runtime auth | None |

## Environment variables

WordMD has no required runtime environment variables. It stores settings, recovery snapshots, telemetry, and feedback in per-user local app data folders.

Build and report tooling may use GitHub-provided environment variables inside GitHub Actions. Those are workflow context, not app runtime configuration.

## Build prerequisites

- .NET 8 SDK.
- Node.js 18 or later for the WebView2 editor bundle.
- Inno Setup 6 for installer generation.
- WebView2 Runtime on target Windows machines.

## Build from source

```powershell
Set-Location 'C:\Users\jeffjame\OneDrive\Code\ReboundMan-WordMD'
Set-Location 'web'
npm ci
npm run build
Set-Location '..'
dotnet publish 'src\WordMD\WordMD.csproj' -c Release -r win-x64 -p:WindowsAppSDKSelfContained=true -p:SelfContained=true -p:PublishTrimmed=false
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" 'installer\WordMD.iss'
```

Expected installer output: `dist\WordMD-Setup-<version>.exe`. The installer version is currently declared in `installer\WordMD.iss`.

## CI

`.github\workflows\ci.yml` runs on Windows, restores Node and .NET dependencies, builds the web bundle, and builds the WinUI project with the already-built web bundle.

## Release checklist

1. Update the version in `VERSION` and `installer\WordMD.iss` together.
2. Update `CHANGELOG.md`.
3. Run the build from source steps above.
4. Smoke test the generated installer on a clean Windows profile when practical.
5. Create a GitHub release and upload `dist\WordMD-Setup-<version>.exe`.

## Smoke test

- [ ] Install without admin rights.
- [ ] Launch WordMD from the Start menu.
- [ ] Open, edit, and save a Markdown file.
- [ ] Switch Source, Formatted, and Split modes.
- [ ] Toggle Light, Dark, and System themes.
- [ ] Send feedback from Help, then verify a local feedback JSONL file is written.
- [ ] Uninstall from Settings or the Start menu shortcut.

## Rollback

WordMD is distributed by GitHub release. To roll back, mark the previous known-good release as latest or direct users to install the earlier installer. User settings and recovery files are not removed by uninstall.

## Cost notes

No Railway, Firebase, or hosted runtime cost applies. GitHub Actions and GitHub Releases are the primary infrastructure surfaces.

## Incident playbook

1. Reproduce against the latest installer from GitHub Releases.
2. Check local logs under `%LOCALAPPDATA%\WordMD\`.
3. Review GitHub Issues labeled `feedback`, `bug`, or `performance`.
4. Add confirmed issues to `spec\punchlist.md`.
5. Ship a fixed installer or direct users to the last known-good release.
