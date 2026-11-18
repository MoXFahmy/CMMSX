# Changelog

All notable changes to 4C CMMS — the cloud app and the Android app share one version number.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); numbering follows the rules in
[docs/VERSIONING.md](docs/VERSIONING.md): a new feature raises the middle number, a fix or small task the last one.

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
