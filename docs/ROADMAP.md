# Roadmap

Built in the order the business depends on it. Each module ships with API, UI, permissions, tests and its part of the 4CSQL importer.

| # | Module | 4CSQL source | Highlights |
|---|---|---|---|
| ✅ 1 | Platform | employees_accessibility, keyvalue, relable, tblFeatures | Organizations, users, roles/permissions, audit, files, terminology |
| ✅ 2 | Locations | site, department, location, Area/DetailedArea | Unlimited-depth tree, moves, notes to technician |
| ✅ 3 | Assets | assets, assetcategories, sibling, assetstransfer | Components, custom fields, warranty, transfers, photos, export |
| ✅ 4 | People & trades | employees, worktrade, EmployeeWorkTrades, Course/EmployeeTrainings | Technicians vs. users, trades, labour rates, skills/certifications |
| ✅ 5 | Work requests | worequest, workrequeststatus | Triage, approve → work order, duplicate/warranty check. Next: public request portal (QR code per asset/location), email notifications |
| ✅ 6 | Work orders | workorders, workorderlabor, direct, issue, WOStatusAudit, jobtask, failurecause | Status workflow, labour, other costs, tasks, failure codes, completion details, activity. Next: parts (with Inventory), print/PDF, customer rating |
| 7 | Preventive maintenance | pmschedule, pmmeters, pmchecklist, readings, pmgenerate | Time-based (5 legacy schedule types) + meter-based PM, checklists, auto-generation, forecast |
| 8 | Inventory | stockcode, stockcode_levels, substore, receive/issue/return/adjustment | Multi-store stock, min/max, reservations for WOs, stock take, barcode labels |
| 9 | Purchasing | purchase_req_master/details, suppliers, currency_rate | Requisition → approval → PO → GRN, purchase proposals from min levels |
| 10 | Reports & KPIs | reportdb (82 reports) | MTBF, MTTR, PM compliance, backlog, downtime, cost by trade/location/cost center |
| 11 | Mobile & offline | — | Installable technician app, offline WOs, barcode/QR scan, photos, signatures |
| 12 | Integrations | SPOC, Dynamics GP | REST API keys & webhooks, alarm → work order, ERP export |
| 13 | Languages | ObjectCaptions (12 languages) | Arabic (RTL) first, then others from the existing caption tables |

Workflows are checked against the *4CSQL User's Guide* (251 pages) so that existing customers find the same concepts:
work request → generate work order, notes to technician (asset + location), warranty/contract alert, assignment to employees
or contractors, labour with overtime, direct issues with PO reference, closing with actual dates, cause / action / prevention,
failure code and downtime, re-opening, and full status-change tracking.
