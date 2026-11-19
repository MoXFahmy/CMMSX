# Changelog

All notable changes to 4C CMMS — the cloud app and the Android app share one version number.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); numbering follows the rules in
[docs/VERSIONING.md](docs/VERSIONING.md): a new feature raises the middle number, a fix or small task the last one.

## [0.0.2] - 2026-11-19

Quick wins from the DimoMaint comparison. New rules are **off** until an administrator switches them on in
Settings › Organization, so nothing changes for existing organizations on its own.

### Added
- **PM generation rules:** don't generate PM work for assets that are under repair, inactive or in storage (you
  choose which); move PM work that falls on a non-working day to the next working day while the schedule keeps its
  dates; don't generate new PM work while the previous work order is still open. Skipped occurrences are listed with
  the reason in the PM preview.
- **Completion rules:** require labour, every checklist step ticked, or a failure code on breakdown work before a work
  order can be completed.
- **Work starts automatically:** logging labour or ticking a checklist step on an Open work order moves it to In
  progress, with the reason in its history.
- **Job card:** print any work order as a job card with its checklist, blank rows for labour and parts, completion
  lines and signature lines.
- **Duplicate a work order** with its checklist, assignees and asset (dates, status and bookings are not copied).
- **More work order filters:** location (with sub-locations), asset, assignee, created and due date ranges, and
  source (from PM, from a request, raised directly).
- **Alert bell** in the header: overdue work orders (and how many are yours), requests waiting for triage and parts at
  or below re-order level, each opening the filtered list.
- **Generate now:** create the next PM work order straight from the forecast or the schedule page.

## [0.0.1] - 2026-11-19

### Security
- **Attachments can no longer run script in the app.** An uploaded SVG (or other file a browser treats as a page)
  opened from an attachment ran its script as the signed-in user, in the web app and in the Android app. Only photos
  (PNG, JPEG, GIF, WebP, BMP, AVIF) and PDFs now open in place; every other file, SVG included, is downloaded. All
  attachment responses forbid script (`Content-Security-Policy`), and everything except PDFs shown in place is also
  sandboxed. Files uploaded before this fix are covered too.
- The stored type of an uploaded file now follows its file extension instead of what the browser reported.

### Changed
- **Release tags come from GitHub.** After a release deploys, the deploy workflow tags the commit `vX.Y.Z` itself,
  so a release needs only a push of `main`; `scripts/release.sh` no longer tags and refuses a version already tagged
  on GitHub.

## [0.0.0] - 2026-11-19

The first numbered release: everything built so far.

### Added
- **Platform:** organizations (tenants), users, roles and permissions, audit trail, file attachments, settings and
  terminology.
- **Locations:** sites and an unlimited-depth location tree.
- **Assets:** hierarchy, categories, custom fields, warranty, financials, transfers, photos and documents, bulk edit,
  CSV export.
- **People:** employees and contractors with trades, rates and sites; trades; failure codes.
- **Work requests:** request, triage, approve into a work order or reject; duplicate and warranty checks.
- **Work orders:** full workflow, assignees, task checklists, labour and other costs, completion details, downtime,
  activity feed and files.
- **Preventive maintenance:** PM tasks and schedules on an asset, a location or a **round of several assets** (one
  work order per cycle with the checklist per asset); fixed interval, nth weekday, fixed date and meter-based
  schedules; multiple PM; preview, automatic generation and forecast; meters and readings. Checklist steps record a
  reading and remarks.
- **Inventory:** stores, part groups, parts with store levels, alternates, shelf-life batches; receive, issue, return,
  adjust, transfer and stock take; moving average cost.
- **Purchasing:** suppliers, purchase orders with approval limits, currencies, tax, partial receipts, re-order
  proposal, printable PO.
- **Reports:** 14 reports with KPIs, charts, CSV export and print.
- **4CSQL importer:** locations, assets, people, work history, PM (including multi-asset and section PMs), PM step
  results, meters, inventory and purchasing; verified on 4CSQL 6.108.1.1 with three customer databases.
- **Deployment:** Docker image, GitHub Actions CI, AWS (ECS Fargate, RDS for SQL Server, S3, CloudFront) live in
  eu-west-2, Azure templates prepared.
- **Android app:** an app shell that opens the cloud app, with downloads, file exports, printing, the back button and
  an offline message.
- **Versioning:** this changelog, the `VERSION` file and `scripts/release.sh`.
