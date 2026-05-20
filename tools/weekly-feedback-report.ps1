<#
.SYNOPSIS
  Generates the weekly WordMD feedback report locally. Use this when the
  GitHub Actions workflow can't run (e.g. org policy on private repos).

.DESCRIPTION
  Requires gh CLI (https://cli.github.com) to be installed and logged in.
  Queries the WordMD repo for issues labeled 'feedback' that were created,
  updated, or closed in the last <Days> days, groups them by category
  label, and writes reports/weekly-YYYY-Www.md. Commit and push the file
  manually after reviewing.

.PARAMETER Days
  Lookback window in days. Default 7.

.PARAMETER Repo
  GitHub repo in owner/name form. Default ReboundMan/ReboundMan-WordMD.

.EXAMPLE
  pwsh ./tools/weekly-feedback-report.ps1
  pwsh ./tools/weekly-feedback-report.ps1 -Days 14
#>
param(
  [int]$Days = 7,
  [string]$Repo = "ReboundMan/ReboundMan-WordMD",
  [switch]$PostDiscussion,
  [string]$DiscussionCategory = "Announcements"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
  Write-Error "gh CLI not found. Install from https://cli.github.com"
  exit 1
}

$now   = (Get-Date).ToUniversalTime()
$since = $now.AddDays(-$Days).ToString("yyyy-MM-ddTHH:mm:ssZ")
$year  = $now.ToString("yyyy")
$cal   = [System.Globalization.ISOWeek]::GetWeekOfYear($now)
$week  = ("{0:D2}" -f $cal)
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root "reports"
New-Item -ItemType Directory -Force $outDir | Out-Null
$out = Join-Path $outDir "weekly-$year-W$week.md"

$cats = [ordered]@{
  bug         = "🐞 Bugs"
  feature     = "✨ Feature requests"
  ux          = "🎨 UX / polish"
  performance = "⚡ Performance"
  docs        = "📝 Docs / install"
  other       = "❓ Other"
}

function Fetch-Issues($label, $state) {
  $j = gh issue list --repo $Repo --state $state --label $label --search "updated:>=$since" --limit 200 `
    --json number,title,url,state,createdAt,updatedAt,closedAt,author 2>$null
  if (-not $j) { return @() }
  return $j | ConvertFrom-Json
}

$totalAll  = (gh issue list --repo $Repo --state all --label feedback --search "updated:>=$since" --limit 500 --json number,state | ConvertFrom-Json)
$openCount = ($totalAll | Where-Object { $_.state -eq "OPEN" }).Count
$closedCount = ($totalAll | Where-Object { $_.state -eq "CLOSED" }).Count
$totalCount = $totalAll.Count

$sb = [System.Text.StringBuilder]::new()
$null = $sb.AppendLine("# WordMD weekly feedback — $year week $week")
$null = $sb.AppendLine()
$null = $sb.AppendLine("_Window: last $Days days (issues updated since ``$since``)._")
$null = $sb.AppendLine()
$null = $sb.AppendLine("_Generated locally by tools/weekly-feedback-report.ps1._")
$null = $sb.AppendLine()
$null = $sb.AppendLine("**Totals this week:** $totalCount touched · $openCount open · $closedCount closed")
$null = $sb.AppendLine()

foreach ($cat in $cats.Keys) {
  $title = $cats[$cat]
  $open = @(Fetch-Issues $cat "open")
  $closed = @(Fetch-Issues $cat "closed")
  if ($open.Count -eq 0 -and $closed.Count -eq 0) { continue }
  $null = $sb.AppendLine("## $title")
  $null = $sb.AppendLine()
  $null = $sb.AppendLine("_$($open.Count) open · $($closed.Count) closed this week_")
  $null = $sb.AppendLine()
  if ($open.Count -gt 0) {
    $null = $sb.AppendLine("### Open")
    foreach ($i in $open) {
      $updated = $i.updatedAt.Substring(0,10)
      $line = "- [#{0}]({1}) {2} — _by @{3}, updated {4}_" -f $i.number, $i.url, $i.title, $i.author.login, $updated
      $null = $sb.AppendLine($line)
    }
    $null = $sb.AppendLine()
  }
  if ($closed.Count -gt 0) {
    $null = $sb.AppendLine("### Closed")
    foreach ($i in $closed) {
      $closedOn = $i.closedAt.Substring(0,10)
      $line = "- ~~[#{0}]({1}) {2}~~ — _closed {3}_" -f $i.number, $i.url, $i.title, $closedOn
      $null = $sb.AppendLine($line)
    }
    $null = $sb.AppendLine()
  }
}

# Uncategorized feedback
$uncatJson = gh issue list --repo $Repo --state all --label feedback `
  --search "updated:>=$since -label:bug -label:feature -label:ux -label:performance -label:docs -label:other" `
  --limit 100 --json number,title,url,state,createdAt 2>$null
$uncat = if ($uncatJson) { $uncatJson | ConvertFrom-Json } else { @() }
if ($uncat.Count -gt 0) {
  $null = $sb.AppendLine("## 🏷️ Needs a category")
  $null = $sb.AppendLine()
  foreach ($i in $uncat) {
    $created = $i.createdAt.Substring(0,10)
    $line = "- [#{0}]({1}) {2} — _{3}, created {4}_" -f $i.number, $i.url, $i.title, $i.state.ToLower(), $created
    $null = $sb.AppendLine($line)
  }
}

$sb.ToString() | Out-File -FilePath $out -Encoding utf8
Write-Host "Wrote $out" -ForegroundColor Green
Write-Host ""
Write-Host "Preview:"
Get-Content $out | Select-Object -First 30

if ($PostDiscussion) {
  Write-Host ""
  Write-Host "Posting Discussion in '$DiscussionCategory'..." -ForegroundColor Cyan
  $owner, $name = $Repo.Split('/')
  $repoQuery = 'query($o:String!,$n:String!){repository(owner:$o,name:$n){id discussionCategories(first:20){nodes{id name}}}}'
  $repoInfo = gh api graphql -f query=$repoQuery -F o=$owner -F n=$name | ConvertFrom-Json
  $repoId = $repoInfo.data.repository.id
  $cat = $repoInfo.data.repository.discussionCategories.nodes | Where-Object { $_.name -eq $DiscussionCategory } | Select-Object -First 1
  if (-not $cat) {
    Write-Warning "Category '$DiscussionCategory' not found. Available: $($repoInfo.data.repository.discussionCategories.nodes.name -join ', ')"
  } else {
    $title = "WordMD weekly feedback — $year week $week"
    $body  = (Get-Content $out -Raw)
    $mut = 'mutation($r:ID!,$c:ID!,$t:String!,$b:String!){createDiscussion(input:{repositoryId:$r,categoryId:$c,title:$t,body:$b}){discussion{url}}}'
    $resp = gh api graphql -f query=$mut -F r=$repoId -F c=$cat.id -F t=$title -F b=$body 2>&1
    try {
      $url = ($resp | ConvertFrom-Json).data.createDiscussion.discussion.url
      Write-Host "Posted: $url" -ForegroundColor Green
    } catch {
      Write-Warning "Discussion post failed: $resp"
    }
  }
}
