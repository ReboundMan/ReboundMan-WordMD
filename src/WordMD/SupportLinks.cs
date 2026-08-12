namespace WordMD;

/// <summary>
/// Outbound links the UI offers, in one place so a URL change is a one-line edit.
/// </summary>
internal static class SupportLinks
{
    public const string GitHub = "https://github.com/ReboundMan/ReboundMan-WordMD";
    public const string ProductPage = "https://reboundman.com/wordmd.html";

    /// <summary>
    /// Where the tip button sends the user, or empty when not yet configured.
    /// Setup runbook: spec\features\support-wordmd-tip-jar.md.
    ///
    /// Deliberately NOT the raw Stripe Payment Link. A URL compiled into a signed
    /// installer is effectively permanent once shipped (there is no update
    /// channel that rewrites it retroactively on machines that already have
    /// WordMD installed), so this points at a small reboundman.com redirect
    /// (Caddyfile, "WordMD tip jar redirect") that owns the real Stripe URL and
    /// the wordmd-app client_reference_id attribution tag. Repointing the tip
    /// jar later, or retiring it, is then a one-line website change instead of
    /// a new app release. This is a public URL, not a credential; the house
    /// payments standard's "apps get NO Stripe keys" rule is respected either
    /// way (WordMD only ever opens a URL in the browser).
    ///
    /// While empty, the About dialog omits its support section entirely, so
    /// shipping ahead of the Stripe setup shows no broken affordance.
    /// </summary>
    public const string TipPaymentLink = "https://reboundman.com/wordmd-tip";

    public static bool TipEnabled => !string.IsNullOrWhiteSpace(TipPaymentLink);
}
