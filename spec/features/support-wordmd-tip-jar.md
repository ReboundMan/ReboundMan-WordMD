---
feature: support-wordmd-tip-jar
status: live
agent: claude
drafted: 2026-08-12
wired: 2026-08-12 (JJ created the Stripe product and Payment Link; both code touchpoints updated same day)
fleet-reviewed: 2026-08-12 (Hawk, Sage, Rookie, Lens, Beacon; reviews/wordmd-tip-jar-*.md; all findings applied same day, see "Fleet review fixes" below)
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

## The two code touchpoints — DONE 2026-08-12, revised after fleet review

Payment Link (the real Stripe URL): `https://buy.stripe.com/00waEZbbAcDwf3E6JMasg01`.

1. **`src/WordMD/SupportLinks.cs`** — `TipPaymentLink` is **not** the raw Stripe URL. It points at `https://reboundman.com/wordmd-tip`, a redirect owned by `reboundman.com/Caddyfile` ("WordMD tip jar redirect"), which holds the real Stripe URL and the `client_reference_id=wordmd-app` tag. See "Fleet review fixes" below for why.
2. **`reboundman.com/wordmd.html`** — the `#thanks` section links Stripe directly with `client_reference_id=wordmd-web`, unchanged. A static page redeploys in one commit, so the rotation risk that motivated the app-side redirect does not apply here.

## Attribution

Two independent mechanisms, because one dedicated SKU answers "how much came from WordMD" and the reference answers "from where":

- **The SKU itself.** A dedicated product (`wordmd_tip_once` in `PAYMENTS-JJ.md`) means WordMD tips never mix with other portfolio revenue in reporting.
- **`client_reference_id`: `wordmd-app`** (in-app, via the redirect) **or `wordmd-web`** (site). Both are the exact values `PAYMENTS-JJ.md` section 4 documents — see "Fleet review fixes" for why an earlier version of this file, the app, and the punchlist all disagreed with that on the app side.

Verified against `docs.stripe.com/payment-links/url-parameters` (2026-08-12): `client_reference_id` is supported on Payment Links, lands on the Checkout Session, and is delivered in the `checkout.session.completed` webhook.

**Open, not resolved here:** Sage's fleet review found `client_reference_id` already carries a different, load-bearing meaning elsewhere in the portfolio — `PAYMENTS-JJ.md` uses it as a Firebase uid grant key for `phd_pool_single`. Today nothing consumes either webhook, so nothing breaks yet, but whoever writes the `paysvc` M2 webhook handler needs to dispatch on the session's product/price *before* treating `client_reference_id` as a uid, or a tip session's free-text tag will be looked up as a uid and fail (see `reviews/wordmd-tip-jar-sage.md` for the "regret in 18 months" scenario). Recording the discriminator explicitly in `PAYMENTS-JJ.md` section 4 is a house-standards edit, so it belongs in a `JJProjectStatus` session, not here.

## Standards debt this creates

`PAYMENTS-JJ.md` section 4's catalog has no tip-jar row, and its app-slug list and grant-strategy table describe only `wordmd_pro_lifetime`. A `wordmd_tip` entry should be added there, with grant strategy "none" (nothing is granted; the app is already free). That file lives in `OneDrive\CodeArtifacts-Personal\JJProjectStatus\` and is a house standards doc, so the edit belongs in a session that owns those standards, not here.

## Fleet review fixes (2026-08-12)

Full panel — Hawk, Sage, Rookie, Lens, Beacon — ran against both commits (`e068332`, `2fe8148` in this repo; `a049c2c`, `3fd0e78` in reboundman.com). Findings and what was applied:

- **`client_reference_id` value never matched the documented convention (Sage, High; independently confirmed by direct file inspection before trusting the review).** `PAYMENTS-JJ.md:100`, `wordmd.html`'s own HTML comment, and commit `3fd0e78`'s message all already said the app side should send `wordmd-app`. The shipped code instead built `wordmd-about-<version>`, sanitised to `wordmd-about-1-6-0` — a value that appears in *this file* and the punchlist (both wrong, both written by the same session that wrote the code) but nowhere else. Anyone reconciling tips by filtering for the documented value would get a confident false zero forever. Fixed: `SupportLinks` now sends exactly `wordmd-app`, matching the standard.
- **A Payment Link compiled into a signed installer is effectively permanent (Sage, High).** There is no update channel that rewrites a `const string` on machines that already installed WordMD. Fixed via a `reboundman.com/wordmd-tip` redirect (Caddyfile) that owns the real Stripe URL; the app now points at that stable indirection instead. 302, not 301, specifically so it stays repointable rather than getting cached client-side. The website's own direct link was left alone: a static page redeploys in one commit, so the same risk doesn't apply there.
- **No compiler-checked link between the two repos' copies of the link (Rookie).** Resolved as a side effect of the redirect: there is now exactly one place (the Caddyfile) that holds the real Stripe URL for the app-side flow, not two hardcoded copies that could silently drift.
- **`AreDefaultContextMenusEnabled`-class accessibility gaps in the About dialog (Beacon, Medium + Low).** The tip disclaimer used a flat `Opacity = 0.7`, which produces a different contrast ratio against light vs. dark backgrounds and is therefore never reliably theme-safe; fixed with a `SecondaryTextBrush()` helper that reads the correct theme's `TextFillColorSecondaryBrush` from `Application.Current.Resources.ThemeDictionaries` (confirmed via the first-party Windows App SDK API reference; `Application.Current.Resources[key]` alone silently returns the Light value even under Dark theme, since it isn't a live `{ThemeResource}` binding). The disclaimer also wasn't reachable by a screen-reader user who tabs straight to the "Buy me a coffee" button; `DescribedBy` turned out to be get-only from code-behind in WinUI 3 (verified against the Windows App SDK API surface before using it — no `SetDescribedBy` exists), so `AutomationProperties.SetHelpText` carries the same disclaimer text instead.
- **`OpenExternal` had no scheme check (Hawk, informational).** All current callers were already safe (hardcoded constants or the sanitised builder), but a future caller could hand it any scheme. Added an http/https-only check matching the pattern the WebView2 navigation handler already uses elsewhere in this file.
- **Live checkout amount doesn't match the "coffee" framing (Lens, plus direct verification: the live page shows product name "Support WordMD (buy me a frosty beverage)" and a $20.00 default).** Flagged, not fixed, in the same pass — resolved by JJ same day: **"Align all of the app with the Stripe language, 'frosty beverage' and the default is $20 but allows adjustment."** Both surfaces now say "Buy me a frosty beverage" (not "coffee"), and the disclaimer text/`fb-note` on both name the $20 default and that it's adjustable, so nobody clicks through and sees different words on checkout than what sent them there.
- **`client_reference_id` overloaded across the portfolio (Sage, High).** Not fixed here; see the Attribution section above. Belongs in `PAYMENTS-JJ.md`.

## Verified / not verified

- **Verified:** the `client_reference_id` contract against current Stripe docs; the redirect target actually returned by the live Payment Link (checkout confirmed to show "Support WordMD (buy me a frosty beverage)", $20.00 default, via direct browser inspection, not just reading the runbook); `AutomationProperties.SetHelpText`, `ResourceDictionary.ThemeDictionaries`, and the plain `redir <from> <to> [<code>]` Caddyfile grammar all confirmed against first-party Microsoft/Caddy docs before use (an earlier draft of the accessibility fix used `AutomationProperties.SetDescribedBy`, which does not exist in WinUI 3's code-behind API and would have failed CI); the reboundman.com page renders, has no horizontal overflow, resolves real theme tokens in both light and dark, and its feedback form builds a correctly encoded GitHub issue URL.
- **Not verified:** the revised C# has **not been compiled**. This machine has the .NET 8 *runtime* but no SDK; CI (`ci.yml`) compiling on push is the check. The Caddyfile's new `redir` line has **not** run through `caddy validate`; the Dockerfile's own `RUN caddy validate` build step is the gate. Also not yet done: JJ's own $1-test-and-refund exit test (Stripe configuration step 6 above).

## Remaining standards debt

`PAYMENTS-JJ.md`'s catalog already gained a `wordmd_tip_once` row (added by another session the same day this shipped) — so the debt this file originally flagged as open is closed, but its documented app-side attribution value (`wordmd-app`) is what the fleet review caught the shipped code disagreeing with; that mismatch is now fixed, not the standard.

Still open there: the `client_reference_id` overload with `phd_pool_single` (Sage, above) has no written discriminator in `PAYMENTS-JJ.md` section 4. Belongs in a `JJProjectStatus` session.

Separately, `spec/punchlist.md`'s Ideas/Backlog carried a 2026-08-03 nag-screen item premised on "once Stripe integration lands there is something concrete paying removes" — a pay-to-remove-friction model. **Parked by JJ, 2026-08-12**: "I don't think we want to piss off people until we get a real product SKU." Revisit only if `wordmd_pro_lifetime` ships.
