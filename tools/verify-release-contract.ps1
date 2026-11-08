<#
.SYNOPSIS
    Checks that a published GitHub release meets the contract the in-app updater depends on.

.DESCRIPTION
    Help > Check for Updates finds and installs releases with no server of its own, so it only
    works if every release is shaped exactly as spec\DEPLOY.md ("In-app updater contract")
    says. A release that breaks the contract is never unsafe, it is just silently never
    offered, which is easy to miss. This script turns each rule into a hard failure.

    Checked: the tag is strict vMAJOR.MINOR.PATCH and matches VERSION; the release is
    published (not draft, not pre-release) and is the one GitHub marks Latest; it carries
    WordMD-Setup-<ver>.exe and WordMD-Setup-<ver>.exe.sha256 under this repo's download path
    and no -UNSIGNED file; the .sha256 is exactly "<64 lowercase hex>  <installer name>" and
    matches the installer; the installer's Authenticode signature is valid, timestamped, and
    from a signer that UpdateService.AcceptedSigners accepts (name, issuer organization,
    account EKU, read straight out of src\WordMD\UpdateService.cs so the two cannot drift); and
    the installer's own ProductName and ProductVersion match the release.

    Every rule is evaluated and reported; the exit code is 1 if any failed.

    One caveat when rotating the signing identity: this checks against the pins in THIS
    release's source, while already-installed copies trust the pins from THEIR release. Verify
    a rotation release by hand against the previous release's AcceptedSigners as well.

.PARAMETER Tag
    The release tag, for example v2.1.0.

.PARAMETER AllowNotLatest
    Do not fail because the release is not the one marked Latest (for checking an older tag).

.PARAMETER AssetDir
    Test seam: use the installer and .sha256 already in this folder instead of downloading them.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Tag,
    [string] $Repo = 'ReboundMan/ReboundMan-WordMD',
    [string] $RepoRoot,
    [switch] $AllowNotLatest,
    [string] $AssetDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Not a param() default: $PSScriptRoot is empty there under Windows PowerShell 5.1.
if (-not $RepoRoot) { $RepoRoot = Split-Path $PSScriptRoot -Parent }

$failures = New-Object System.Collections.Generic.List[string]

# Text taken from the release (asset names, states, URLs, the .sha256 contents, signature
# messages) is attacker-influencable if a release is ever tampered with. Before it reaches the
# console (where "::" starts a workflow command) or the Markdown step summary, drop control
# characters, break "::", and cap the length; the summary additionally escapes Markdown/HTML.
function Clean([string] $Text) {
    if ($null -eq $Text) { return '' }
    $t = [regex]::Replace($Text, '[\x00-\x1F\x7F]', ' ')
    $t = $t.Replace('::', ': :')
    if ($t.Length -gt 400) { $t = $t.Substring(0, 400) + '...' }
    return $t
}
function ForSummary([string] $Text) {
    $t = Clean $Text
    $t = $t.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
    return [regex]::Replace($t, '([\\`*_\[\]|~])', '\$1')
}
function Fail([string] $Message, [string] $Why = '') {
    $failures.Add($Message)
    Write-Host "  FAIL  $(Clean $Message)" -ForegroundColor Red
    if ($Why) { Write-Host "        $(Clean $Why)" -ForegroundColor DarkYellow }
}
function Pass([string] $Message) { Write-Host "  PASS  $(Clean $Message)" -ForegroundColor Green }

Write-Host "Release contract check for $(Clean $Repo) $(Clean $Tag)" -ForegroundColor Cyan

# --- 1. tag shape and VERSION ----------------------------------------------
$m = [regex]::Match($Tag, '^v([0-9]{1,4})\.([0-9]{1,4})\.([0-9]{1,4})\z')
if (-not $m.Success) {
    Fail "Tag '$Tag' is not strict vMAJOR.MINOR.PATCH." 'The updater refuses any other shape (no -rc suffix, no fourth part), so this release will never be offered.'
    exit 1
}
$ver = "$($m.Groups[1].Value).$($m.Groups[2].Value).$($m.Groups[3].Value)"
Pass "Tag is strict vMAJOR.MINOR.PATCH ($Tag)"

$versionFile = Join-Path $RepoRoot 'VERSION'
if (Test-Path $versionFile) {
    $fileVersion = (Get-Content $versionFile -Raw).Trim()
    if ($fileVersion -eq $ver) { Pass "VERSION file matches ($fileVersion)" }
    else { Fail "VERSION file says '$fileVersion' but the tag is '$Tag'." }
} else { Fail "No VERSION file at $versionFile." }

# --- 2. release metadata ---------------------------------------------------
$rel = $null
# gh prints a 404's JSON body to stdout and exits non-zero, so the exit status has to be checked:
# parsing that body as a release would "succeed" and then fail confusingly on missing fields.
$relJson = $null
try { $relJson = & gh api "repos/$Repo/releases/tags/$Tag" 2>$null } catch { }
if ($LASTEXITCODE -eq 0 -and $relJson) { try { $rel = $relJson | ConvertFrom-Json } catch { } }
if (-not $rel -or -not ($rel.PSObject.Properties.Name -contains 'assets')) {
    Fail "Could not read release $Tag from $Repo (does it exist and is it published?)."
    exit 1
}

if ($rel.draft) { Fail 'The release is still a draft.' 'releases/latest never returns a draft, so it is not offered.' } else { Pass 'Release is published (not a draft)' }
if ($rel.prerelease) { Fail 'The release is marked as a pre-release.' 'releases/latest skips pre-releases, so it is not offered.' } else { Pass 'Release is not marked pre-release' }

# --- 3. is it the one marked Latest? (retry up to ~75 s: the flag can lag the publish event) ----
$latestTag = $null
for ($i = 1; $i -le 6; $i++) {
    try { $latestTag = (& gh api "repos/$Repo/releases/latest" --jq '.tag_name') } catch { $latestTag = $null }
    if ($latestTag -ceq $Tag) { break }
    if ($i -lt 6) { Start-Sleep -Seconds 15 }
}
if ($latestTag -ceq $Tag) { Pass "Release is the one GitHub marks Latest" }
elseif (-not $latestTag -and -not $AllowNotLatest) { Fail 'Could not read which release GitHub marks Latest (a GitHub or network problem, not necessarily a bad release).' 'Re-run the check.' }
elseif ($AllowNotLatest) { Write-Host "  SKIP  Latest check (-AllowNotLatest); GitHub's Latest is '$(Clean $latestTag)'" -ForegroundColor DarkGray }
else { Fail "This release is not marked Latest (GitHub's Latest is '$latestTag'; the flag can lag a publish, so re-run once before assuming it is wrong)." 'Everyone on an older version is told they are up to date and never offered this release. Fix: gh release edit <tag> --latest' }

# --- 4. assets -------------------------------------------------------------
$installerName = "WordMD-Setup-$ver.exe"
$shaName = "$installerName.sha256"
$assets = @($rel.assets)
$names = @($assets | ForEach-Object { $_.name })

$unsigned = @($names | Where-Object { $_ -like '*UNSIGNED*' })
if ($unsigned.Count -gt 0) { Fail "An UNSIGNED file is attached: $($unsigned -join ', ')." 'An unsigned build must never be published.' }
else { Pass 'No -UNSIGNED file is attached' }

foreach ($n in @($installerName, $shaName)) {
    $a = $assets | Where-Object { $_.name -ceq $n } | Select-Object -First 1
    if (-not $a) { Fail "Asset '$n' is missing."; continue }
    if ($a.state -ne 'uploaded') { Fail "Asset '$n' is in state '$($a.state)', not 'uploaded'."; continue }
    $expectedUrl = "https://github.com/$Repo/releases/download/$Tag/$n"
    if ($a.browser_download_url -cne $expectedUrl) { Fail "Asset '$n' URL is '$($a.browser_download_url)', expected '$expectedUrl'."; continue }
    if ($n -eq $installerName -and ($a.size -le 0 -or $a.size -gt 300MB)) { Fail "Installer size $($a.size) is outside the updater's accepted range."; continue }
    Pass "Asset present, uploaded, correct download path: $n"
}
$installerAsset = $assets | Where-Object { $_.name -ceq $installerName } | Select-Object -First 1

# --- 5. get the files ------------------------------------------------------
$work = $AssetDir
if (-not $work) {
    $work = Join-Path ([System.IO.Path]::GetTempPath()) ("wordmd-contract-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    try { & gh release download $Tag --repo $Repo --dir $work --pattern $installerName --pattern $shaName | Out-Null } catch { }
}
$installer = Join-Path $work $installerName
$shaFile = Join-Path $work $shaName
if (-not (Test-Path $installer) -or -not (Test-Path $shaFile)) {
    Fail 'Could not obtain the installer and its .sha256 to inspect.'
    Write-Host ''; Write-Host "$($failures.Count) check(s) failed." -ForegroundColor Red; exit 1
}

# --- 6. the .sha256 file ---------------------------------------------------
$shaBytes = [System.IO.File]::ReadAllBytes($shaFile)
$shaText = [System.Text.Encoding]::UTF8.GetString($shaBytes)
$hasBom = $shaBytes.Length -ge 3 -and $shaBytes[0] -eq 0xEF -and $shaBytes[1] -eq 0xBB -and $shaBytes[2] -eq 0xBF
$nonAscii = @($shaBytes | Where-Object { $_ -gt 127 }).Count -gt 0
$shaMatch = [regex]::Match($shaText, "^([0-9a-f]{64})  $([regex]::Escape($installerName))\r?\n?$")
if ($hasBom -or $nonAscii) { Fail 'The .sha256 is not plain ASCII (a BOM or UTF-16 text).' 'Encoding drift here makes the updater reject the release as failing a safety check.' }
elseif (-not $shaMatch.Success) { Fail "The .sha256 is not exactly '<64 lowercase hex>  $installerName'." "Found: $($shaText.Trim())" }
else {
    Pass 'The .sha256 has the exact format the release script writes'
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash.ToLowerInvariant()
    if ($actual -eq $shaMatch.Groups[1].Value) { Pass 'The .sha256 matches the installer' }
    else { Fail 'The installer does not match its .sha256.' "published $($shaMatch.Groups[1].Value), actual $actual" }
}
if ($installerAsset -and (Get-Item -LiteralPath $installer).Length -ne $installerAsset.size) {
    Fail "The installer is $((Get-Item -LiteralPath $installer).Length) bytes but the API reports $($installerAsset.size)."
}

# --- 7. signature and signer pins ------------------------------------------
$sig = Get-AuthenticodeSignature -LiteralPath $installer
if ($sig.Status -ne 'Valid') { Fail "Installer signature is '$($sig.Status)'." "$($sig.StatusMessage)" }
else {
    Pass 'Installer signature is valid'
    if (-not $sig.TimeStamperCertificate) { Fail 'The signature is not timestamped.' 'Artifact Signing certificates last about three days; without a timestamp the signature stops validating.' }
    else { Pass 'The signature is timestamped' }

    # Pins come straight from the app's own source so the two cannot disagree.
    $src = Join-Path $RepoRoot 'src\WordMD\UpdateService.cs'
    $pins = @()
    if (Test-Path $src) {
        # Comments are removed first so a commented-out pin cannot be read as live, and the
        # block is anchored on the declaration itself (not the first mention of the name, which
        # is a doc comment).
        $text = [regex]::Replace((Get-Content $src -Raw), '/\*[\s\S]*?\*/|//[^\r\n]*', '')
        $decl = [regex]::Match($text, 'SignerPin\[\]\s+AcceptedSigners\s*=\s*\{(?<body>[\s\S]*?)\}\s*;')
        if ($decl.Success) {
            foreach ($pm in [regex]::Matches($decl.Groups['body'].Value,'new\("([^"]+)",\s*"([^"]+)",\s*(?:"([^"]+)"|null)\)')) {
                $pins += [pscustomobject]@{ Name = $pm.Groups[1].Value; Issuer = $pm.Groups[2].Value; Eku = $(if ($pm.Groups[3].Success) { $pm.Groups[3].Value } else { $null }) }
            }
        }
    }
    if ($pins.Count -eq 0) {
        Fail 'Could not read AcceptedSigners out of src\WordMD\UpdateService.cs.' 'The signer cannot be checked against the app''s pins. Update this script if the code changed shape.'
    } else {
        $cert = $sig.SignerCertificate
        $cn = $cert.GetNameInfo('SimpleName', $false)
        $issuerOrg = [regex]::Match($cert.Issuer, '(?:^|, )O=("[^"]+"|[^,]+)').Groups[1].Value.Trim('"')
        $ekus = @($cert.EnhancedKeyUsageList | ForEach-Object { [string]$_.ObjectId })
        $ok = $pins | Where-Object { $_.Name -ceq $cn -and $_.Issuer -ceq $issuerOrg -and (-not $_.Eku -or $ekus -contains $_.Eku) } | Select-Object -First 1
        if ($ok) { Pass "Signer matches an accepted pin (CN '$cn', issuer organization '$issuerOrg')" }
        else { Fail "Signer is not accepted by the app (CN '$cn', issuer organization '$issuerOrg')." 'Installed copies will refuse this update.' }
    }
}

# --- 8. the signed ProductName / ProductVersion ----------------------------
$vi = (Get-Item -LiteralPath $installer).VersionInfo
if ($vi.ProductName.Trim() -eq 'WordMD' -and $vi.ProductVersion.Trim() -eq $ver) { Pass "Installer's signed ProductName/ProductVersion match ('WordMD', '$ver')" }
else { Fail "Installer says ProductName '$($vi.ProductName.Trim())', ProductVersion '$($vi.ProductVersion.Trim())'; expected 'WordMD', '$ver'." 'The updater refuses an installer whose own version does not match the release that announced it.' }

# --- 9. the app and installer still agree on /RELAUNCH ----------------------
# The updater passes /RELAUNCH=1 and installer\WordMD.iss reads {param:RELAUNCH}. Renaming either
# side would make silent updates quietly stop reopening the app (the next launch just reads
# "Completed"), so the pair is checked as text.
$srcPath = Join-Path $RepoRoot 'src\WordMD\UpdateService.cs'
$issPath = Join-Path $RepoRoot 'installer\WordMD.iss'
$srcText = if (Test-Path $srcPath) { Get-Content $srcPath -Raw } else { '' }
$issText = if (Test-Path $issPath) { Get-Content $issPath -Raw } else { '' }
if ($srcText -match '/RELAUNCH=1' -and $issText -match '\{param:RELAUNCH\|' -and $issText -match 'Check:\s*RelaunchRequested') {
    Pass 'The updater and the installer agree on /RELAUNCH'
} else {
    Fail 'The updater and the installer no longer agree on /RELAUNCH.' 'UpdateService.cs must pass /RELAUNCH=1 and installer\WordMD.iss must read {param:RELAUNCH|...} in a [Run] entry with Check: RelaunchRequested; otherwise silent updates stop reopening the app.'
}

# --- result ----------------------------------------------------------------
Write-Host ''
if ($env:GITHUB_STEP_SUMMARY) {
    $lines = @("## Release contract: $(ForSummary $Tag)", '')
    if ($failures.Count -eq 0) { $lines += 'All checks passed.' } else { $lines += $failures | ForEach-Object { "- FAIL: $(ForSummary $_)" } }
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $lines
}
if ($failures.Count -gt 0) {
    Write-Host "$($failures.Count) check(s) failed. Fix the release; the in-app updater depends on these." -ForegroundColor Red
    exit 1
}
Write-Host 'All release-contract checks passed.' -ForegroundColor Green
exit 0
