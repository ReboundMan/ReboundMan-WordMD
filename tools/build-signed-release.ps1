<#
.SYNOPSIS
    Builds (and by default signs) a WordMD release installer end to end. Spec 10024.

.DESCRIPTION
    Signing is local-only: no signing credential exists in GitHub, so CI runs this
    same script with -SkipSigning to validate the chain and never publishes its
    output (spec 10024, Open question 4). One script for both paths keeps CI from
    drifting away from the real release build.

    Two orderings in here are load-bearing:

    1. WordMD.exe is signed BEFORE Inno Setup packages it. [Files] copies whatever
       bytes are in the publish folder when ISCC runs, so signing afterwards leaves
       a validly-signed installer wrapping an unsigned app - which still passes a
       check on the installer alone.
    2. Verification asserts the SIGNER IDENTITY, not just chain validity. The dlib
       falls back to the local certificate store when misconfigured, and a dev cert
       in Trusted Root would satisfy `signtool verify /pa`. Spec 10024 calls that
       silent fallback the worst outcome; subject matching is what detects it.

    Unsigned builds are written as WordMD-Setup-<version>-UNSIGNED.exe so they can
    never be mistaken for a releasable artifact.

.PARAMETER SkipSigning
    Build unsigned (CI validation). Skips signing, Azure preflight, and hashing.

.PARAMETER SkipWebBundle
    Skip 'npm ci' + 'npm run build'. Only safe when web\dist is current.

.PARAMETER SkipPublish
    Skip 'dotnet publish'. Only safe when the publish folder is current.

.EXAMPLE
    .\tools\build-signed-release.ps1
    Full signed release build.

.EXAMPLE
    .\tools\build-signed-release.ps1 -SkipSigning
    What CI runs: proves the chain compiles, produces an -UNSIGNED installer.
#>
[CmdletBinding()]
param(
    [switch] $SkipSigning,
    [switch] $SkipWebBundle,
    [switch] $SkipPublish,
    [string] $SignTool,
    [string] $Dlib,
    [string] $Metadata        = 'C:\Code\Signing\.wordmd-signing\metadata.json',
    [string] $Iscc,
    [string] $AzCli,
    [string] $Timestamp       = 'http://timestamp.acs.microsoft.com',
    # Verification asserts the signature really came from this identity.
    [string] $ExpectedSubject = 'ReboundMan.com LLC'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$startedAt = Get-Date

$repo       = Split-Path $PSScriptRoot -Parent
$version    = (Get-Content (Join-Path $repo 'VERSION') -Raw).Trim()
$tfmDir     = 'net8.0-windows10.0.26100.0'
$publishDir = Join-Path $repo "src\WordMD\bin\Release\$tfmDir\win-x64\publish"
$appExe     = Join-Path $publishDir 'WordMD.exe'
$iss        = Join-Path $repo 'installer\WordMD.iss'
# Must match OutputBaseFilename in WordMD.iss, which suffixes unsigned builds.
$installer  = Join-Path $repo ("dist\WordMD-Setup-$version" + $(if ($SkipSigning) { '-UNSIGNED' } else { '' }) + '.exe')

function Assert-Path([string] $Path, [string] $What) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "$What path not resolved. Pass it explicitly." }
    if (-not (Test-Path $Path)) { throw "$What not found: $Path" }
}

# Tool discovery: prefer PATH, fall back to known install locations, allow an
# explicit override. Hardcoding a single SDK build number breaks on any machine
# with a different Windows SDK installed.
function Resolve-Tool([string] $Explicit, [string] $Command, [string[]] $Candidates, [string] $What) {
    if ($Explicit) { Assert-Path $Explicit $What; return $Explicit }
    # $Command is '' for tools with no PATH entry point (the dlib is a plain .dll,
    # not an executable). Get-Command's -Name parameter is [ValidateNotNullOrEmpty()],
    # so passing '' throws a parameter-binding error that -ErrorAction cannot
    # suppress (validation errors happen before the cmdlet body runs) -- skip the
    # PATH lookup entirely rather than call Get-Command with nothing to look up.
    if ($Command) {
        $onPath = Get-Command $Command -ErrorAction SilentlyContinue
        if ($onPath) { return $onPath.Source }
    }
    $hit = $Candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $hit) { throw "$What not found. Install it or pass the path explicitly." }
    return $hit
}

Write-Host "WordMD release build - version $version$(if ($SkipSigning) { ' (UNSIGNED validation)' })" -ForegroundColor Cyan

# --- Preflight: fail before the slow steps, not after -----------------------
$Iscc = Resolve-Tool $Iscc 'ISCC' @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) 'Inno Setup ISCC.exe'

if (-not $SkipSigning) {
    # Newest SDK bin dir wins, rather than pinning one build number.
    $sdkCandidates = @(
        Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Directory -ErrorAction SilentlyContinue |
            Where-Object Name -match '^10\.' | Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' }
    )
    $SignTool = Resolve-Tool $SignTool 'signtool' $sdkCandidates 'SignTool (x64)'
    $Dlib     = Resolve-Tool $Dlib '' @(
        "$env:LOCALAPPDATA\Microsoft\MicrosoftArtifactSigningClientTools\Azure.CodeSigning.Dlib.dll"
    ) 'Artifact Signing dlib'
    Assert-Path $Metadata 'Signing metadata.json'

    # The temp .cmd below is written as ASCII so its quoting reaches ISCC intact.
    # A non-ASCII path would be mangled there and the dlib would silently fall
    # back to local-store signing, so refuse that case outright.
    foreach ($p in @($SignTool, $Dlib, $Metadata, $Iscc, $iss)) {
        if ($p -match '[^\x20-\x7E]') { throw "Path contains non-ASCII characters, which the ISCC invocation cannot carry safely: $p" }
    }

    $AzCli = Resolve-Tool $AzCli 'az' @(
        "$env:ProgramFiles\Microsoft SDKs\Azure\CLI2\wbin\az.cmd",
        "${env:ProgramFiles(x86)}\Microsoft SDKs\Azure\CLI2\wbin\az.cmd"
    ) 'Azure CLI (winget install -e --id Microsoft.AzureCLI)'

    # The dlib authenticates via DefaultAzureCredential. With no CLI login it
    # falls through to an interactive browser prompt against the wrong tenant,
    # so surface the login state (and which tenant) up front.
    $acct = & $AzCli account show --output json 2>$null
    if (-not $acct) { throw "Not logged in to Azure. Run: `"$AzCli`" login  (then select the tenant holding the signing account)" }
    $acctObj = $acct | ConvertFrom-Json
    Write-Host "  Azure: $($acctObj.user.name) / $($acctObj.name)" -ForegroundColor DarkGray
    Write-Host "  Tenant: $($acctObj.tenantId) - must be the tenant holding the signing account" -ForegroundColor DarkGray
}

# --- Web bundle -------------------------------------------------------------
if (-not $SkipWebBundle) {
    Write-Host 'Building web bundle...' -ForegroundColor Cyan
    Push-Location (Join-Path $repo 'web')
    try {
        & npm ci
        if ($LASTEXITCODE -ne 0) { throw "npm ci failed ($LASTEXITCODE)" }
        & npm run build
        if ($LASTEXITCODE -ne 0) { throw "npm run build failed ($LASTEXITCODE)" }
    } finally { Pop-Location }
}

# --- Publish ----------------------------------------------------------------
# SkipWebBundle=true is always passed: the csproj's BuildWebBundle target would
# otherwise rebuild the bundle a second time on every publish, which also made
# this script's -SkipWebBundle switch a no-op.
if (-not $SkipPublish) {
    Write-Host 'Publishing WordMD...' -ForegroundColor Cyan
    & dotnet publish (Join-Path $repo 'src\WordMD\WordMD.csproj') -c Release -r win-x64 `
        -p:WindowsAppSDKSelfContained=true -p:SelfContained=true -p:PublishTrimmed=false `
        -p:SkipWebBundle=true
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}
Assert-Path $appExe 'Published WordMD.exe'

# --- Sign the app exe, BEFORE packaging ------------------------------------
if (-not $SkipSigning) {
    Write-Host 'Signing WordMD.exe (before packaging)...' -ForegroundColor Cyan
    & $SignTool sign /v /fd SHA256 /tr $Timestamp /td SHA256 /dlib $Dlib /dmdf $Metadata $appExe
    if ($LASTEXITCODE -ne 0) { throw "Signing WordMD.exe failed ($LASTEXITCODE)" }
}

# --- Build the installer ---------------------------------------------------
# Stale-output guard: a previous run's installer at this path would otherwise be
# verified and reported as this run's output.
if (Test-Path $installer) { Remove-Item $installer -Force }

if ($SkipSigning) {
    & $Iscc $iss
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }
} else {
    # Built through a temp .cmd so the embedded quoting reaches ISCC exactly as
    # written; passing a string containing double quotes straight to a native exe
    # is where PowerShell's argument re-quoting bites. $q and $f are Inno tokens,
    # not PowerShell variables, hence the single-quoted template.
    $template = '/S"artifactsigning=$q{0}$q sign /v /fd SHA256 /tr {1} /td SHA256 /dlib $q{2}$q /dmdf $q{3}$q $f"'
    $signArg  = $template -f $SignTool, $Timestamp, $Dlib, $Metadata
    $cmdFile  = Join-Path ([System.IO.Path]::GetTempPath()) "wordmd-iscc-$PID.cmd"
    Set-Content -Path $cmdFile -Value "@echo off`r`n`"$Iscc`" /DSIGN $signArg `"$iss`"`r`nexit /b %ERRORLEVEL%" -Encoding ASCII
    Write-Host 'Building signed installer...' -ForegroundColor Cyan
    try {
        & cmd.exe /c $cmdFile
        if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE). Check its log for the signtool line it built." }
    } finally { Remove-Item $cmdFile -ErrorAction SilentlyContinue }
}
Assert-Path $installer 'Built installer'
if ((Get-Item $installer).LastWriteTime -lt $startedAt) {
    throw "Installer at $installer predates this run - it is stale output, not a fresh build."
}

# --- Verify BOTH binaries, including signer identity -----------------------
if (-not $SkipSigning) {
    Write-Host 'Verifying signatures...' -ForegroundColor Cyan
    foreach ($target in @($appExe, $installer)) {
        $sig = Get-AuthenticodeSignature $target
        if ($sig.Status -ne 'Valid') {
            throw "Signature INVALID on $target - status: $($sig.Status) ($($sig.StatusMessage))"
        }
        $subject = $sig.SignerCertificate.Subject
        if ($subject -notmatch [regex]::Escape($ExpectedSubject)) {
            throw @"
Signature on $target is valid but was made by the WRONG identity.
  expected subject to contain: $ExpectedSubject
  actual subject:              $subject
This is the local-certificate-store fallback: the dlib was misconfigured and
signtool signed with a local cert instead of Azure Artifact Signing. Do not ship.
"@
        }
        if (-not $sig.TimeStamperCertificate) {
            throw "Signature on $target is NOT timestamped. Artifact Signing certs expire in ~3 days, so the signature would stop validating. Do not ship."
        }
        Write-Host "  verified: $(Split-Path $target -Leaf) - $subject" -ForegroundColor Green
    }
}

# --- Report ----------------------------------------------------------------
if ($SkipSigning) {
    Write-Host ''
    Write-Host "Chain OK. Unsigned validation artifact: $installer" -ForegroundColor Yellow
    Write-Host 'This file is NOT releasable. Run without -SkipSigning to produce a signed installer.'
    exit 0
}

$hash = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLower()
$shaFile = "$installer.sha256"
Set-Content -Path $shaFile -Value "$hash  $(Split-Path $installer -Leaf)" -NoNewline

Write-Host ''
Write-Host 'Signed build complete.' -ForegroundColor Green
Write-Host "  installer : $installer"
Write-Host "  sha256    : $hash"
Write-Host ''
# Draft-first ordering: publishing before the asset is attached makes an
# assetless releases/latest the default state for however long the upload takes.
Write-Host 'Release it draft-first, so releases/latest is never assetless:' -ForegroundColor Cyan
Write-Host "  gh release create v$version --draft --title `"WordMD v$version`" --notes-file <notes.md>"
Write-Host "  gh release upload v$version `"$installer`" `"$shaFile`" --clobber"
Write-Host "  gh release edit v$version --draft=false --latest"
