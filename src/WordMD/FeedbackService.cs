using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace WordMD;

/// <summary>
/// Captures user-submitted feedback. Every submission is appended to a local
/// JSONL log (one record per line) and a prefilled GitHub issue URL is opened
/// in the user's default browser for the user to review and submit.
/// </summary>
public sealed class FeedbackService
{
    public const string RepoOwner = "ReboundMan";
    public const string RepoName = "ReboundMan-WordMD";

    public static string LogDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WordMD", "feedback");

    public string LogPath => Path.Combine(LogDir, $"feedback-{DateTime.UtcNow:yyyyMMdd}.jsonl");

    public sealed class Submission
    {
        public string Category { get; set; } = "other"; // bug / feature / ux / performance / docs / other
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IncludeDiagnostics { get; set; } = true;
        public Dictionary<string, string> Diagnostics { get; set; } = new();
    }

    /// <summary>Write the submission to the local JSONL log.</summary>
    public void AppendLocal(Submission s)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var record = new JsonObject
            {
                ["timestamp"]   = DateTime.UtcNow.ToString("o"),
                ["category"]    = s.Category,
                ["title"]       = s.Title,
                ["description"] = s.Description,
            };
            if (s.IncludeDiagnostics && s.Diagnostics.Count > 0)
            {
                var diag = new JsonObject();
                foreach (var kv in s.Diagnostics) diag[kv.Key] = kv.Value;
                record["diagnostics"] = diag;
            }
            else
            {
                record["diagnostics"] = null;
            }
            File.AppendAllText(LogPath, record.ToJsonString() + Environment.NewLine);
        }
        catch { /* never throw from feedback */ }
    }

    /// <summary>Build a GitHub "new issue" URL with title, body, and labels prefilled.</summary>
    public string BuildGitHubIssueUrl(Submission s)
    {
        var body = new StringBuilder();
        body.AppendLine(s.Description);
        body.AppendLine();
        if (s.IncludeDiagnostics && s.Diagnostics.Count > 0)
        {
            body.AppendLine("---");
            body.AppendLine("**Diagnostics**");
            body.AppendLine();
            body.AppendLine("| Key | Value |");
            body.AppendLine("|---|---|");
            foreach (var kv in s.Diagnostics)
                body.AppendLine($"| {kv.Key} | {kv.Value} |");
        }

        var labels = "feedback," + s.Category;
        var url = $"https://github.com/{RepoOwner}/{RepoName}/issues/new" +
                  $"?title={WebUtility.UrlEncode(s.Title)}" +
                  $"&body={WebUtility.UrlEncode(body.ToString())}" +
                  $"&labels={WebUtility.UrlEncode(labels)}";
        return url;
    }
}
