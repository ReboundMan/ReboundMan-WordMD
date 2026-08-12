namespace WordMD;

/// <summary>
/// Outbound links the UI offers, in one place so a URL change is a one-line edit.
/// </summary>
internal static class SupportLinks
{
    public const string GitHub = "https://github.com/ReboundMan/ReboundMan-WordMD";
    public const string ProductPage = "https://reboundman.com/wordmd.html";

    /// <summary>
    /// Stripe Payment Link for the optional tip jar, or empty when not yet
    /// configured. Setup runbook: spec\features\support-wordmd-tip-jar.md.
    ///
    /// This is a public, hosted Stripe URL, not a credential; the house payments
    /// standard is explicit that apps hold no Stripe keys, and this respects it
    /// (WordMD only opens a URL in the browser and never handles payment data).
    ///
    /// While empty, the About dialog omits its support section entirely, so
    /// shipping ahead of the Stripe setup shows no broken affordance.
    /// </summary>
    public const string TipPaymentLink = "https://buy.stripe.com/00waEZbbAcDwf3E6JMasg01";

    public static bool TipEnabled => !string.IsNullOrWhiteSpace(TipPaymentLink);

    /// <summary>
    /// Appends attribution so tips can be told apart by where they came from.
    /// <c>client_reference_id</c> lands on the Stripe Checkout Session and its
    /// completion webhook. Stripe accepts alphanumerics, dashes and underscores
    /// up to 200 characters and silently drops anything else, so the value is
    /// sanitised here rather than trusting the caller.
    /// </summary>
    public static string BuildTipUrl(string source, string version)
    {
        if (!TipEnabled) return string.Empty;
        var reference = Sanitize($"wordmd-{source}-{version}");
        var separator = TipPaymentLink.Contains('?') ? "&" : "?";
        return $"{TipPaymentLink}{separator}client_reference_id={reference}";
    }

    private static string Sanitize(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-');
        }
        // Stripe's cap is 200; nothing here approaches it, but truncate rather
        // than let a future longer source string get the whole value dropped.
        return sb.Length <= 200 ? sb.ToString() : sb.ToString(0, 200);
    }
}
