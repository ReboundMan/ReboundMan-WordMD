---
feature: support-wordmd-tip-jar
status: live
agent: claude
drafted: 2026-08-12
wired: 2026-08-12 (JJ created the Stripe product and Payment Link; both code touchpoints updated same day)
source: JJ direct ask, 2026-08-12 ("similar to BMAC, something in the about screen that if you like the product as much as I do, feel free to show me your thanks")
size: S
priority: H
---

# WordMD tip jar (Stripe)

An optional thank-you payment, offered in the About dialog and on the product page. WordMD stays free.

## Decision (JJ, 2026-08-12)

Asked whether this replaces the planned paid license, JJ chose: **tip jar only for now; the $29.99 Pro license stays a separate future decision.** So `wordmd_pro_lifetime` remains in the `PAYMENTS-JJ.md` catalog untouched, and this ships as an additional, unrelated SKU.

The positioning that follows from that: WordMD is free and stays free, and the tip is an optional thanks. Both surfaces say so explicitly, because a free product with a payment button reads as nagware unless the copy is unambiguous.

## Why a Stripe Payment Link and not `paysvc`

`PAYMENTS-JJ.md` section 3.8 is explicit: *"Keys: one restricted key for paysvc only... Apps get NO Stripe keys."* A hosted Payment Link satisfies that by construction. WordMD opens a URL in the user's default browser and never sees payment data, holds no key, and gains no network dependency (it still has no `HttpClient` anywhere; see spec 10070). The page is static HTML doing the same thing.

This also means the tip jar does **not** wait on `paysvc`, which is October work in the standard. Nothing here blocks on it, and nothing here builds toward it either; when the Pro license lands, that flows through `paysvc` as designed.

## Word choice: "tip", not "donation"

Deliberate. ReboundMan.com, LLC is a for-profit Wyoming LLC, not a 501(c)(3). Calling this a donation implies tax-deductibility that does not exist and invites a chargeback argument. Both surfaces say "tip jar" / "buy me a coffee" / "say thanks" and never "donate". Keep it that way.

## Stripe configuration (JJ's steps; not automatable from here)

Creating products and links requires dashboard access with financial credentials, so this is a manual runbook, not a script. Dashboard labels drift; the intent per step matters more than the exact wording.

1. **Product.** Dashboard → Product catalog → Add product.
   - Name `WordMD tip jar` (customer-visible on the checkout page and receipt).
   - One-time, not recurring.
   - For the amount, enable the customer-chooses option (Stripe labels this along the lines of "let customers pay what they want"): preset **$5**, minimum **$1**, no maximum. If that option is not offered for this product type, fall back to three fixed prices ($3 / $5 / $10) and one Payment Link each, then point the buttons at the $5 one.
2. **Statement descriptor.** Confirm the dynamic suffix resolves to `RBNDMN* WORDMD`, per `PAYMENTS-JJ.md` section 3.5. A tip charge nobody recognises on a statement is a chargeback waiting to happen.
3. **Payment Link.** Dashboard → Payment Links → Create, selecting that product.
   - Collect email: **on** (needed for the receipt).
   - Promotion codes: off.
   - After completion: redirect to `https://reboundman.com/wordmd.html`. Redirect specifically, not the default confirmation page, because UTM parameters are only reflected back on the redirect behaviour.
4. **Tax.** Stripe Tax monitoring is ON per `PAYMENTS-JJ.md` decision 11.4. Confirm how a voluntary payment against a free product should be treated before the first real charge; a tip is not obviously a taxable sale of goods, and getting this wrong is easier to fix now than after a hundred of them.
5. **Copy the link URL** and put it in the two places below.
6. **Test with a real $1 charge, then refund it.** This is the same exit test `PAYMENTS-JJ.md` milestone M0 already defines. Confirm the receipt arrives, the statement descriptor reads as expected, and the `client_reference_id` shows on the Checkout Session.

## The two code touchpoints — DONE 2026-08-12

Payment Link: `https://buy.stripe.com/00waEZbbAcDwf3E6JMasg01`.

1. **`src/WordMD/SupportLinks.cs`** — `TipPaymentLink` set to the link above. `TipEnabled` now evaluates true, so the About dialog's support section is live.
2. **`reboundman.com/wordmd.html`** — the `#thanks` section is uncommented; JJ had already wired it directly with the same link (confirmed matching before the C# side was touched).

## Attribution

Two independent mechanisms, because one dedicated SKU answers "how much came from WordMD" and the reference answers "from where":

- **The SKU itself.** A dedicated product means WordMD tips never mix with other portfolio revenue in reporting.
- **`client_reference_id`.** The app appends `?client_reference_id=wordmd-about-<version>` (built by `SupportLinks.BuildTipUrl`, which sanitises to Stripe's allowed alphanumerics/dashes/underscores and 200-char cap; invalid values are silently dropped, so sanitising beats hoping). The page uses `wordmd-web`. That distinguishes in-app tips from website tips, and tells you which app version prompted one.

Verified against `docs.stripe.com/payment-links/url-parameters` (2026-08-12): `client_reference_id` is supported on Payment Links, lands on the Checkout Session, and is delivered in the `checkout.session.completed` webhook.

## Standards debt this creates

`PAYMENTS-JJ.md` section 4's catalog has no tip-jar row, and its app-slug list and grant-strategy table describe only `wordmd_pro_lifetime`. A `wordmd_tip` entry should be added there, with grant strategy "none" (nothing is granted; the app is already free). That file lives in `OneDrive\CodeArtifacts-Personal\JJProjectStatus\` and is a house standards doc, so the edit belongs in a session that owns those standards, not here.

## Verified / not verified

- **Verified:** the `client_reference_id` contract against current Stripe docs; the reboundman.com page renders, has no horizontal overflow, resolves real theme tokens in both light and dark, and its feedback form builds a correctly encoded, correctly labelled GitHub issue URL (including the empty-title guard and the over-length fallback). The live `#thanks` section on reboundman.com/wordmd.html confirmed pointing at the same Payment Link before the C# side was edited.
- **Not verified:** the C# About-dialog change (`TipPaymentLink` now non-empty) has **not been compiled**. This machine has the .NET 8 *runtime* but no SDK, so `dotnet build` cannot run; CI (`ci.yml`) compiling on push after this commit is the check. Also not yet done: JJ's own $1-test-and-refund exit test (step 6 above) — worth doing once the CI-compiled build is installed, not blocking this commit.

## Remaining standards debt (unchanged)

`PAYMENTS-JJ.md`'s catalog still has no `wordmd_tip` row (see above); this is unaffected by wiring the link and remains a separate edit in a `JJProjectStatus` session.

Separately, `spec/punchlist.md`'s Ideas/Backlog still carries a 2026-08-03 nag-screen item premised on "once Stripe integration lands there is something concrete paying removes" — a pay-to-remove-friction model. That is not what shipped: WordMD stays free with an optional tip, no friction to remove. That backlog item is now stale relative to the shipped design and worth JJ's call on whether to drop it, not silently edited here.
