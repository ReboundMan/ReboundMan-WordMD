# Weekly feedback reports

This folder is populated automatically every Sunday by
[`.github/workflows/weekly-report.yml`](../.github/workflows/weekly-report.yml).

Each `weekly-YYYY-Www.md` file summarizes feedback issues that were
created, updated, or closed in the prior 7 days, grouped by category:

- 🐞 Bugs
- ✨ Feature requests
- 🎨 UX / polish
- ⚡ Performance
- 📝 Docs / install
- ❓ Other

The workflow also posts a matching GitHub Discussion (if Discussions are
enabled on the repo) so people can comment on the week without cluttering
individual issues.

## How items get categorized

When you submit feedback via **Help → Send Feedback** in WordMD, the app
prefills a GitHub issue with `feedback` plus the category label you
picked (`bug`, `feature`, etc.). The workflow groups by those labels.

Issues that have `feedback` but no category label show up under
**Needs a category** in the report — relabel them on GitHub to move
them into the right section next week.

## Triggering a report manually

> ⚠️ **The GitHub Actions workflow is currently disabled** because the
> hosted runner can't be allocated for this account's private repo
> (jobs were failing in seconds with no steps executing, generating a
> "run failed" email each time). Use the **local fallback** below
> instead. To re-enable later (e.g. if Actions ever starts working
> here): `gh workflow enable "Weekly feedback report"`.

From the repo **Actions** tab pick **Weekly feedback report**, then click
**Run workflow**. Useful if you want to see what the current week looks
like without waiting until Sunday.

### Local fallback

If the GitHub Actions runner can't run for your account (e.g. org policy
blocks GitHub-hosted runners on private repos), generate the same report
locally with:

```powershell
pwsh ./tools/weekly-feedback-report.ps1
# Or with a different window:
pwsh ./tools/weekly-feedback-report.ps1 -Days 14
# Also post the report as a Discussion (default category: Announcements):
pwsh ./tools/weekly-feedback-report.ps1 -PostDiscussion
pwsh ./tools/weekly-feedback-report.ps1 -PostDiscussion -DiscussionCategory General
```

This writes `reports/weekly-YYYY-Www.md`. Commit and push manually.
