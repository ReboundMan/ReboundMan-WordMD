using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WordMD;

internal enum UpdateCheckStatus { UpToDate, Available, Failed }

/// <summary>Everything the UI needs to offer, download, and verify one release.</summary>
internal sealed record UpdateInfo(
    Version Version,
    string Tag,
    string InstallerName,
    string InstallerUrl,
    long InstallerSize,
    string Sha256Url,
    string ReleasePageUrl,
    string Notes);

/// <param name="Error">A fixed, plain-language sentence. Never built from remote text.</param>
/// <param name="Code">A short stable code from a fixed vocabulary, shown to users for support.</param>
/// <param name="Latest">The latest published version when it is known (set for UpToDate and Available).</param>
internal sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Update, string? Error, string? Code, Version? Latest)
{
    public static UpdateCheckResult UpToDate(Version? latest = null) => new(UpdateCheckStatus.UpToDate, null, null, null, latest);
    public static UpdateCheckResult Available(UpdateInfo update) => new(UpdateCheckStatus.Available, update, null, null, update.Version);
    public static UpdateCheckResult Failed(string error, string code) => new(UpdateCheckStatus.Failed, null, error, code, null);
}

/// <summary>
/// Base for every failure the update flow expects. Messages are fixed, plain sentences chosen
/// by this file (never remote text, never a framework exception message, never a local path),
/// and Code comes from a small fixed vocabulary.
/// </summary>
internal abstract class UpdateException : Exception
{
    public string Code { get; }
    protected UpdateException(string message, string code) : base(message) { Code = code; }
}

/// <summary>A downloaded installer failed a safety check. It must never be run.</summary>
internal sealed class UpdateVerificationException : UpdateException
{
    public UpdateVerificationException(string message, string code) : base(message, code) { }
}

/// <summary>The update did not come from where WordMD expects (an address or file name it refuses). Nothing was downloaded.</summary>
internal sealed class UpdateSourceException : UpdateException
{
    public UpdateSourceException(string message, string code) : base(message, code) { }
}

/// <summary>An ordinary network or disk failure while downloading.</summary>
internal sealed class UpdateDownloadException : UpdateException
{
    public UpdateDownloadException(string message, string code) : base(message, code) { }
}

/// <summary>Which signer identity counts as "us". See <see cref="UpdateService.AcceptedSigners"/>.</summary>
internal sealed record SignerPin(string CommonName, string IssuerOrganization, string? RequiredEkuOid);

/// <summary>
/// A downloaded installer that passed every check. Holds a read-only handle that denies
/// writers and deleters, which closes the gap between verification and the moment the
/// installer process is created. It does not protect the file after WordMD exits (the
/// installer re-reads its own file by path while it runs); the signature is what the
/// installer's authenticity rests on, not this handle.
/// </summary>
internal sealed class VerifiedInstaller : IDisposable
{
    private FileStream? _lock;
    private readonly string _dir;

    public string FilePath { get; }

    internal VerifiedInstaller(string filePath, string dir, FileStream lockStream)
    {
        FilePath = filePath;
        _dir = dir;
        _lock = lockStream;
    }

    public void Dispose()
    {
        _lock?.Dispose();
        _lock = null;
    }

    public void DisposeAndDelete()
    {
        Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}

/// <summary>
/// "Check for Updates": finds the latest GitHub release, downloads its installer, and
/// verifies it before anything is run. Deliberately free of any UI types so it can be
/// exercised on its own. See spec\features\check-for-updates.md for the design and
/// threat model.
///
/// Trust model, in order of strength:
///   1. TLS to GitHub (every redirect hop is re-checked against an exact host allowlist).
///   2. The installer's Authenticode signature must be valid AND made by an accepted signer:
///      the exact common name, issued by Microsoft, carrying the account-specific Artifact
///      Signing identity EKU. The build script asserts the same identity as a looser
///      substring match; this is stricter on purpose. This is the only check a compromised
///      GitHub account cannot satisfy.
///   3. The signed ProductName/ProductVersion inside the installer must match the release
///      that announced it, so an older validly signed installer cannot be passed off as a
///      newer release.
///   4. Size and the SHA-256 published next to the installer. These come from the same
///      release as the installer, so they catch truncation and corruption, not a malicious
///      release.
/// Revocation is not consulted (a conscious choice, see the spec).
/// </summary>
internal static class UpdateService
{
    internal const string LatestReleaseApi = "https://api.github.com/repos/ReboundMan/ReboundMan-WordMD/releases/latest";
    private const string AssetUrlPrefix = "https://github.com/ReboundMan/ReboundMan-WordMD/releases/download/";
    private const string ReleasePagePrefix = "https://github.com/ReboundMan/ReboundMan-WordMD/releases";
    internal const string ProductName = "WordMD";

    /// <summary>
    /// The signer identities WordMD will accept an installer from. A list, not a single
    /// constant, so an identity rotation can be shipped safely: release a build that
    /// accepts both the old and the new entry while still signed by the old identity, then
    /// switch signing (see spec\DEPLOY.md, "In-app updater contract"). The issuing CA's own
    /// name is deliberately not pinned, because Microsoft rotates those (the shipped
    /// releases already span two); the issuer ORGANIZATION and the account EKU are stable.
    /// </summary>
    internal static readonly SignerPin[] AcceptedSigners =
    {
        new("ReboundMan.com LLC", "Microsoft Corporation", "1.3.6.1.4.1.311.97.84717075.176126319.764670622.315378995"),
    };

    /// <summary>
    /// Exact hosts a request or redirect may touch. Deliberately not a "*.githubusercontent.com"
    /// wildcard, which would also admit hosts that serve arbitrary user uploads. If GitHub moves
    /// its release-asset host this list needs the new name (the flow then fails with the
    /// "redirect-host" code and the releases-page fallback).
    /// </summary>
    private static readonly string[] AllowedHosts =
    {
        "api.github.com",
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
        "github-releases.githubusercontent.com",
    };

    private const long MaxInstallerBytes = 300L * 1024 * 1024;
    private const int MaxApiBytes = 2 * 1024 * 1024;
    private const int MaxShaFileBytes = 4096;
    private const int MaxNotesChars = 1500;
    private const int MaxRedirects = 5;
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DownloadWallClock = TimeSpan.FromMinutes(20);

    private const string ProxyText = "Your network's proxy needs sign-in before WordMD can reach GitHub.";
    private const string TimeoutText = "The connection to GitHub timed out. Check your internet connection and try again.";

    private static readonly Regex InstallerNameRegex =
        new(@"^WordMD-Setup-[0-9]{1,4}\.[0-9]{1,4}\.[0-9]{1,4}\.exe\z", RegexOptions.CultureInvariant);

    /// <summary>Per-user download area. Mutable only so a test harness can point it elsewhere.</summary>
    internal static string UpdatesRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WordMD", "updates");

    private static readonly Lazy<HttpClient> LazyHttp = new(CreateClient);
    private static HttpClient Http => LazyHttp.Value;

    private static HttpClient CreateClient()
    {
        // No automatic redirects: GetAsync follows them by hand so every hop is checked.
        // No automatic decompression: installer bytes must be exactly what was sent.
        // No cookies: nothing here is a session. Windows credentials go only to a proxy that
        // asks for them (enterprise NTLM/Kerberos proxies), never to GitHub.
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        };
        var client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "WordMD/" + UserAgentVersion());
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static string UserAgentVersion()
    {
        try
        {
            var info = System.Reflection.CustomAttributeExtensions
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(UpdateService).Assembly)
                ?.InformationalVersion ?? "0.0.0";
            var plus = info.IndexOf('+');
            if (plus >= 0) info = info.Substring(0, plus);
            return Regex.IsMatch(info, @"^[0-9A-Za-z.\-]{1,32}\z", RegexOptions.CultureInvariant) ? info : "0.0.0";
        }
        catch { return "0.0.0"; }
    }

    // ---------------------------------------------------------------- version parsing

    /// <summary>
    /// Strict MAJOR.MINOR.PATCH with an optional leading "v". Anything else (a prerelease
    /// suffix, a fourth part, junk) is refused rather than guessed at.
    /// </summary>
    internal static bool TryParseReleaseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.Length > 0 && (t[0] == 'v' || t[0] == 'V')) t = t.Substring(1);
        var m = Regex.Match(t, @"^([0-9]{1,4})\.([0-9]{1,4})\.([0-9]{1,4})\z", RegexOptions.CultureInvariant);
        if (!m.Success) return false;
        version = new Version(
            int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }

    private static string Dotted(Version v) => $"{v.Major}.{v.Minor}.{v.Build}";

    // ---------------------------------------------------------------- installed copy

    private const string InstallerUninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{A6E3D6F8-2B5E-4B10-9C40-7C3F3B5E1D70}_is1";

    /// <summary>
    /// Where the Inno installer says WordMD is installed (per-user first, then per-machine),
    /// or null if no installation is registered. The AppId in the key name must match
    /// installer\WordMD.iss; it also has to stay constant for in-place upgrades to work at all.
    /// </summary>
    internal static string? ReadInstallLocation()
    {
        var places = new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
        };
        foreach (var (hive, view) in places)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(InstallerUninstallKey);
                if (key?.GetValue("InstallLocation") is string s && !string.IsNullOrWhiteSpace(s)) return s;
            }
            catch { }
        }
        return null;
    }

    /// <summary>True when the running folder is the registered install folder.</summary>
    internal static bool PathsMatch(string runningDirectory, string? installLocation)
    {
        if (string.IsNullOrWhiteSpace(installLocation)) return false;
        try
        {
            static string Norm(string p) => Path.GetFullPath(p).TrimEnd('\\', '/');
            return string.Equals(Norm(runningDirectory), Norm(installLocation), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // ---------------------------------------------------------------- the check

    /// <summary>
    /// Asks GitHub for the latest release. Never throws except OperationCanceledException
    /// when the caller cancels; every other failure comes back as a Failed result carrying a
    /// fixed plain-language message and a short code.
    /// </summary>
    public static async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ApiTimeout);
            using var resp = await GetAsync(new Uri(LatestReleaseApi), "application/vnd.github+json", cts.Token).ConfigureAwait(false);

            var status = (int)resp.StatusCode;
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return UpdateCheckResult.Failed("GitHub has no published release for WordMD yet.", "http-404");
            if (status == 429 || (resp.StatusCode == HttpStatusCode.Forbidden && RateLimitExhausted(resp)))
                return UpdateCheckResult.Failed("GitHub is limiting requests from this network right now. Try again in a little while.", "rate-limit");
            if (resp.StatusCode == HttpStatusCode.Forbidden)
                return UpdateCheckResult.Failed("GitHub refused the request. A proxy or firewall on your network may be blocking it.", "http-403");
            if (resp.StatusCode == HttpStatusCode.ProxyAuthenticationRequired)
                return UpdateCheckResult.Failed(ProxyText, "proxy-407");
            if (!resp.IsSuccessStatusCode)
                return UpdateCheckResult.Failed("GitHub reported a problem. Try again later.", $"http-{status}");

            var json = await ReadLimitedStringAsync(resp, MaxApiBytes, cts.Token).ConfigureAwait(false);
            return ParseRelease(json, current);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return UpdateCheckResult.Failed(TimeoutText, "timeout");
        }
        catch (UpdateException ex)
        {
            return UpdateCheckResult.Failed(ex.Message, ex.Code);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex}");
            var (message, code) = MapNetworkError(ex);
            return UpdateCheckResult.Failed(message, code);
        }
    }

    private static bool RateLimitExhausted(HttpResponseMessage resp) =>
        resp.Headers.TryGetValues("X-RateLimit-Remaining", out var v) && v.FirstOrDefault() == "0";

    /// <summary>Turns a framework exception into one of a few fixed, plain sentences.</summary>
    private static (string Message, string Code) MapNetworkError(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is HttpRequestException h
                && (h.StatusCode == HttpStatusCode.ProxyAuthenticationRequired || h.Message.Contains("407", StringComparison.Ordinal)))
                return (ProxyText, "proxy-407");
            if (e is AuthenticationException)
                return ("A secure connection to GitHub couldn't be established. A proxy, firewall, or captive portal on your network may be interfering.", "tls");
        }
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is HttpRequestException or System.Net.Sockets.SocketException or IOException)
                return ("WordMD couldn't connect to GitHub. Check your internet connection and try again.", "network");
        }
        return ("Something went wrong while talking to GitHub.", "unexpected");
    }

    /// <summary>Turns the releases/latest JSON into a result. Separate so it can be tested with canned input.</summary>
    internal static UpdateCheckResult ParseRelease(string json, Version current)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
                return UpdateCheckResult.Failed("GitHub sent a response WordMD couldn't read.", "bad-response");

            // releases/latest never returns these, but never offer one if it somehow did.
            if (GetBool(root["draft"]) || GetBool(root["prerelease"])) return UpdateCheckResult.UpToDate();

            var tag = GetString(root["tag_name"])?.Trim();
            if (!TryParseReleaseVersion(tag, out var latest))
                return UpdateCheckResult.Failed("The latest release isn't tagged with a version number WordMD understands.", "bad-tag");

            // Equal or older (for example a rollback that marked an older release as latest)
            // is simply "up to date"; the updater never moves anyone backwards.
            if (latest <= current) return UpdateCheckResult.UpToDate(latest);

            var wantedName = $"WordMD-Setup-{Dotted(latest)}.exe";
            var wantedShaName = wantedName + ".sha256";
            var assets = root["assets"] as JsonArray;
            var installerUrl = FindAssetUrl(assets, wantedName, out var size);
            var shaUrl = FindAssetUrl(assets, wantedShaName, out _);
            if (installerUrl == null || shaUrl == null)
                return UpdateCheckResult.Failed($"WordMD v{Dotted(latest)} was announced, but its installer isn't attached to the release yet. Try again shortly.", "no-assets");

            // The tag is escaped exactly as GitHub builds the download path, so a release
            // whose asset URLs point anywhere other than this repo's release downloads is refused.
            var expectedPrefix = AssetUrlPrefix + Uri.EscapeDataString(tag!) + "/";
            if (installerUrl != expectedPrefix + wantedName || shaUrl != expectedPrefix + wantedShaName)
                return UpdateCheckResult.Failed("The release's download address isn't where WordMD expects, so it wasn't offered. Use the releases page instead.", "asset-path");
            if (size <= 0 || size > MaxInstallerBytes)
                return UpdateCheckResult.Failed("The release's installer has an unexpected size, so WordMD won't download it.", "bad-size");

            // Built from the validated tag, never taken from the release's own html_url.
            var pageUrl = $"{ReleasePagePrefix}/tag/{Uri.EscapeDataString(tag!)}";
            var notes = SanitizeNotes(GetString(root["body"]) ?? "");
            return UpdateCheckResult.Available(new UpdateInfo(latest, tag!, wantedName, installerUrl, size, shaUrl, pageUrl, notes));
        }
        catch (Exception ex)
        {
            // Includes JsonException and the ArgumentException a duplicate key raises, whose
            // message would embed attacker-chosen text.
            Debug.WriteLine($"Release JSON rejected: {ex.GetType().Name}");
            return UpdateCheckResult.Failed("GitHub sent a response WordMD couldn't read.", "bad-response");
        }
    }

    private static string? FindAssetUrl(JsonArray? assets, string name, out long size)
    {
        size = 0;
        if (assets == null) return null;
        foreach (var node in assets)
        {
            if (node is not JsonObject a) continue;
            if (!string.Equals(GetString(a["name"]), name, StringComparison.Ordinal)) continue;
            if (!string.Equals(GetString(a["state"]), "uploaded", StringComparison.Ordinal)) continue;
            size = GetLong(a["size"]);
            return GetString(a["browser_download_url"]);
        }
        return null;
    }

    private static bool GetBool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    private static string? GetString(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    private static long GetLong(JsonNode? n) => n is JsonValue v && v.TryGetValue<long>(out var l) ? l : 0;

    /// <summary>
    /// Release notes come from a remote source, so: drop control characters (except newline
    /// and tab) and the bidirectional override/isolate characters that can visually reorder
    /// text, then cut to a fixed length on a code-point boundary.
    /// </summary>
    internal static string SanitizeNotes(string text)
    {
        var sb = new StringBuilder(Math.Min(text.Length, MaxNotesChars + 16));
        foreach (var ch in text.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (ch == '\n' || ch == '\t') { sb.Append(ch); continue; }
            if (char.IsControl(ch)) continue;
            if ((ch >= '‪' && ch <= '‮') || (ch >= '⁦' && ch <= '⁩')) continue;
            sb.Append(ch);
            if (sb.Length > MaxNotesChars + 8) break;
        }
        var s = sb.ToString().Trim();
        if (s.Length <= MaxNotesChars) return s;
        var cut = MaxNotesChars;
        if (char.IsHighSurrogate(s[cut - 1])) cut--;
        return s.Substring(0, cut).TrimEnd() + "...";
    }

    // ---------------------------------------------------------------- download + verify

    /// <summary>
    /// Downloads the installer for <paramref name="update"/> and verifies it. Returns only
    /// an installer that passed every check; anything else throws and leaves nothing behind.
    /// Throws OperationCanceledException only when the caller cancels; every other failure is
    /// an <see cref="UpdateException"/> with a fixed message and code.
    /// </summary>
    public static async Task<VerifiedInstaller> DownloadAndVerifyAsync(
        UpdateInfo update, IProgress<double>? progress, CancellationToken ct)
    {
        // Re-validate here rather than trusting the caller: this is the function that
        // decides what gets written to disk and later executed.
        if (!InstallerNameRegex.IsMatch(update.InstallerName))
            throw new UpdateSourceException("The installer's file name isn't one WordMD recognizes, so nothing was downloaded.", "file-name");
        if (!update.InstallerUrl.StartsWith(AssetUrlPrefix, StringComparison.Ordinal)
            || !update.Sha256Url.StartsWith(AssetUrlPrefix, StringComparison.Ordinal))
            throw new UpdateSourceException("The installer's address isn't on WordMD's GitHub releases, so nothing was downloaded.", "asset-path");
        if (update.InstallerSize <= 0 || update.InstallerSize > MaxInstallerBytes)
            throw new UpdateSourceException("The release reports an unexpected installer size, so nothing was downloaded.", "bad-size");

        // A fresh folder per attempt: a leftover or still-running earlier installer can't
        // collide with this download, and a half-written file is never at a reusable path.
        var dir = Path.Combine(UpdatesRoot, Guid.NewGuid().ToString("N"));
        try
        {
            var expectedHash = await FetchExpectedHashAsync(update, ct).ConfigureAwait(false);

            try { Directory.CreateDirectory(dir); }
            catch (Exception) { throw new UpdateDownloadException("WordMD couldn't save the download to disk. Check free space and permissions.", "disk"); }
            var path = Path.Combine(dir, update.InstallerName);

            using (var wall = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                wall.CancelAfter(DownloadWallClock);
                await DownloadFileAsync(new Uri(update.InstallerUrl), path, update.InstallerSize, progress, wall.Token).ConfigureAwait(false);
            }

            // Reopen read-only, denying writers and deleters, and verify THAT handle's bytes.
            var lockStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                if (lockStream.Length != update.InstallerSize)
                    throw new UpdateVerificationException("The downloaded installer isn't the size GitHub reported.", "size");

                var actual = Convert.ToHexString(await SHA256.HashDataAsync(lockStream, ct).ConfigureAwait(false)).ToLowerInvariant();
                if (!string.Equals(actual, expectedHash, StringComparison.Ordinal))
                    throw new UpdateVerificationException("The downloaded installer doesn't match the checksum published with the release.", "checksum");

                VerifyAuthenticode(path);
                VerifyInstallerIdentity(path, update.Version);
                return new VerifiedInstaller(path, dir, lockStream);
            }
            catch
            {
                lockStream.Dispose();
                throw;
            }
        }
        catch (UpdateException)
        {
            TryDeleteDirectory(dir);
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TryDeleteDirectory(dir);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(dir);
            throw new UpdateDownloadException(TimeoutText, "timeout");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update download failed: {ex}");
            TryDeleteDirectory(dir);
            var (message, code) = MapNetworkError(ex);
            throw new UpdateDownloadException(message, code);
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }

    private static async Task<string> FetchExpectedHashAsync(UpdateInfo update, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ApiTimeout);
        using var resp = await GetAsync(new Uri(update.Sha256Url), "application/octet-stream", cts.Token).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new UpdateDownloadException("GitHub couldn't provide the release's checksum file right now. Try again later.", $"http-{(int)resp.StatusCode}");
        var text = await ReadLimitedStringAsync(resp, MaxShaFileBytes, cts.Token).ConfigureAwait(false);
        var hash = ParseSha256File(text, update.InstallerName);
        return hash ?? throw new UpdateVerificationException("The checksum published with the release is missing or malformed.", "checksum-file");
    }

    /// <summary>
    /// Reads the "&lt;64 hex&gt;  &lt;file name&gt;" line the release script writes (also tolerates the
    /// "*name" binary-mode marker and a bare hash). Returns null unless the hash is well formed
    /// and, when a name is present, it names this installer.
    /// </summary>
    internal static string? ParseSha256File(string text, string installerName)
    {
        var line = text.TrimStart('﻿')
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);
        if (line == null) return null;
        var m = Regex.Match(line, @"^([0-9a-fA-F]{64})(?:[ \t]+\*?(.+))?\z", RegexOptions.CultureInvariant);
        if (!m.Success) return null;
        if (m.Groups[2].Success
            && !string.Equals(m.Groups[2].Value.Trim(), installerName, StringComparison.OrdinalIgnoreCase))
            return null;
        return m.Groups[1].Value.ToLowerInvariant();
    }

    private static async Task DownloadFileAsync(Uri uri, string path, long expectedSize, IProgress<double>? progress, CancellationToken ct)
    {
        using var resp = await GetAsync(uri, "application/octet-stream", ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new UpdateDownloadException("GitHub couldn't provide the installer right now. Try again later.", $"http-{(int)resp.StatusCode}");
        var declared = resp.Content.Headers.ContentLength;
        if (declared.HasValue && declared.Value != expectedSize)
            throw new UpdateVerificationException("The installer GitHub is serving isn't the size the release reported.", "size");

        await using var source = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        FileStream target;
        try { target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true); }
        catch (Exception) { throw new UpdateDownloadException("WordMD couldn't save the download to disk. Check free space and permissions.", "disk"); }

        await using (target)
        {
            var buffer = new byte[81920];
            long total = 0;
            var clock = Stopwatch.StartNew();
            long lastReportMs = -1000;
            while (true)
            {
                int read;
                using (var stall = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    stall.CancelAfter(StallTimeout);
                    try { read = await source.ReadAsync(buffer.AsMemory(), stall.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new UpdateDownloadException("The download stalled and was stopped.", "stalled");
                    }
                }
                if (read == 0) break;
                total += read;
                if (total > expectedSize)
                    throw new UpdateVerificationException("The installer is larger than the release reported, so it was stopped.", "size");
                try { await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false); }
                catch (IOException ex)
                {
                    var diskFull = (ex.HResult & 0xFFFF) is 112 or 39;
                    throw new UpdateDownloadException(
                        diskFull ? "There isn't enough free disk space to download the update." : "WordMD couldn't save the download to disk. Check free space and permissions.",
                        diskFull ? "disk-full" : "disk");
                }
                if (progress != null && clock.ElapsedMilliseconds - lastReportMs >= 100)
                {
                    lastReportMs = clock.ElapsedMilliseconds;
                    progress.Report(total * 100.0 / expectedSize);
                }
            }
            try { await target.FlushAsync(ct).ConfigureAwait(false); }
            catch (IOException) { throw new UpdateDownloadException("WordMD couldn't save the download to disk. Check free space and permissions.", "disk"); }
            progress?.Report(100);
        }
    }

    /// <summary>Removes earlier attempts' leftovers (best effort, never throws).</summary>
    internal static void CleanStaleDownloads()
    {
        try
        {
            if (!Directory.Exists(UpdatesRoot)) return;
            foreach (var d in Directory.EnumerateDirectories(UpdatesRoot))
            {
                try
                {
                    if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(d) > TimeSpan.FromHours(12))
                        Directory.Delete(d, recursive: true);
                }
                catch { }
            }
        }
        catch { }
    }

    // ---------------------------------------------------------------- HTTP with checked redirects

    internal static bool IsAllowedHost(Uri uri)
    {
        if (!uri.IsAbsoluteUri) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        if (!uri.IsDefaultPort) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
        return Array.IndexOf(AllowedHosts, uri.IdnHost.ToLowerInvariant()) >= 0;
    }

    private static async Task<HttpResponseMessage> GetAsync(Uri uri, string accept, CancellationToken ct)
    {
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!IsAllowedHost(uri))
                throw new UpdateSourceException("WordMD stopped because GitHub redirected the request somewhere it doesn't recognize. Use the releases page instead.", "redirect-host");

            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.TryAddWithoutValidation("Accept", accept);

            // A header timeout, so a server that accepts the connection and then says nothing
            // cannot hold the dialog open. The timer ends with this block; body reads are
            // governed separately by the stall timeout and the caller's token.
            HttpResponseMessage resp;
            using (var headers = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                headers.CancelAfter(HeaderTimeout);
                resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, headers.Token).ConfigureAwait(false);
            }

            var code = (int)resp.StatusCode;
            if (code is 301 or 302 or 303 or 307 or 308)
            {
                var location = resp.Headers.Location;
                resp.Dispose();
                if (location == null) throw new UpdateSourceException("GitHub sent an incomplete redirect, so WordMD stopped.", "redirect");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            return resp;
        }
        throw new UpdateSourceException("GitHub redirected the request too many times, so WordMD stopped.", "redirects");
    }

    private static async Task<string> ReadLimitedStringAsync(HttpResponseMessage resp, int maxBytes, CancellationToken ct)
    {
        var declared = resp.Content.Headers.ContentLength;
        if (declared.HasValue && declared.Value > maxBytes)
            throw new UpdateDownloadException("GitHub sent a response WordMD couldn't use.", "bad-response");
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0) break;
            if (ms.Length + read > maxBytes)
                throw new UpdateDownloadException("GitHub sent a response WordMD couldn't use.", "bad-response");
            ms.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    // ---------------------------------------------------------------- Authenticode

    /// <summary>
    /// Throws unless the file carries a valid Authenticode signature made by one of
    /// <see cref="AcceptedSigners"/>. WinVerifyTrust honors the signature timestamp, which is
    /// what keeps a short-lived signing certificate valid long after it expired.
    /// </summary>
    internal static void VerifyAuthenticode(string path)
    {
        var rc = WinVerifyTrustFile(path);
        if (rc != 0)
            throw new UpdateVerificationException("The installer's digital signature couldn't be confirmed.", $"signature-0x{rc:X8}");

        X509Certificate2 cert;
        try
        {
            // The certificate of the signature WinVerifyTrust just validated (the first one),
            // taken from the signer info rather than from the unauthenticated certificate bag.
            cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
        }
        catch (Exception)
        {
            throw new UpdateVerificationException("The installer's signer couldn't be read.", "signer-unreadable");
        }
        using (cert)
        {
            if (!AcceptedSigners.Any(pin => SignerMatches(cert, pin)))
                throw new UpdateVerificationException("The installer isn't signed by ReboundMan.com LLC.", "signer");
        }
    }

    internal static bool SignerMatches(X509Certificate2 cert, SignerPin pin)
    {
        if (!string.Equals(cert.GetNameInfo(X509NameType.SimpleName, false), pin.CommonName, StringComparison.Ordinal))
            return false;
        if (!string.Equals(GetOrganization(cert.IssuerName), pin.IssuerOrganization, StringComparison.Ordinal))
            return false;
        if (pin.RequiredEkuOid != null)
        {
            var found = false;
            foreach (var ext in cert.Extensions)
            {
                if (ext is not X509EnhancedKeyUsageExtension eku) continue;
                foreach (var oid in eku.EnhancedKeyUsages)
                    if (string.Equals(oid.Value, pin.RequiredEkuOid, StringComparison.Ordinal)) found = true;
            }
            if (!found) return false;
        }
        return true;
    }

    private static string? GetOrganization(X500DistinguishedName dn)
    {
        try
        {
            foreach (var rdn in dn.EnumerateRelativeDistinguishedNames())
                if (rdn.GetSingleElementType().Value == "2.5.4.10") return rdn.GetSingleElementValue();
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>
    /// After the signature is confirmed, the installer's own ProductName and ProductVersion
    /// (part of the signed bytes, written by Inno from the app name and version) must match the
    /// release that announced it. This is what stops a validly signed OLDER installer from being
    /// passed off as a newer release under a new tag.
    /// </summary>
    internal static void VerifyInstallerIdentity(string path, Version expected)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        if (!string.Equals(info.ProductName?.Trim(), ProductName, StringComparison.Ordinal))
            throw new UpdateVerificationException("The installer isn't a WordMD installer.", "product");
        if (!TryParseReleaseVersion(info.ProductVersion?.Trim(), out var actual) || actual != expected)
            throw new UpdateVerificationException("The installer isn't the version the release announced.", "version");
    }

    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_REVOCATION_CHECK_NONE = 0x10;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

    private static int WinVerifyTrustFile(string path)
    {
        var pathPtr = Marshal.StringToHGlobalUni(path);
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = pathPtr,
            }, filePtr, false);

            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = filePtr,
                dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = WTD_REVOCATION_CHECK_NONE,
            };
            var action = WintrustActionGenericVerifyV2;
            var rc = WinVerifyTrust(InvalidHandleValue, ref action, ref data);

            // Always release the state WinVerifyTrust allocated for the verify call.
            data.dwStateAction = WTD_STATEACTION_CLOSE;
            WinVerifyTrust(InvalidHandleValue, ref action, ref data);
            return rc;
        }
        finally
        {
            Marshal.FreeHGlobal(pathPtr);
            Marshal.FreeHGlobal(filePtr);
        }
    }
}
