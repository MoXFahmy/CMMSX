# Editable lists, brands and models, asset groups, yearly numbering (0.21.0)

Roadmap release 0.21.0, split from 0.20.0 at the user's request (0.20.0 shipped cost centres and budgets). The user handed over
the design: "go with your picks". Every choice below is mine unless marked as the user's.

## Intent

Each organization wants CMX to speak its own words, and cleaner asset data. Today:

- Work types, priorities, asset statuses and criticality are fixed enums with the same names for everyone (17 web files, the
  phone, reports and e-mails show them through `t('workType.X')`-style keys on the web and the server's Translator).
- An asset's manufacturer and model, and a part's / measurement point's / number step's unit, are free text, so the same brand
  or unit is spelled several ways.
- An asset has one category and nothing else to group it by.
- Numbers run on forever per kind (WO-00042), per organization or per site prefix.

DimoMaint has reference tables, brands and models, asset groups and a yearly numbering reset; CMX should match them.

## Goals

1. **Editable fixed lists**: rename (English and Arabic), colour, reorder and hide the values of work types, priorities, asset
   statuses and criticality. The meaning of each value stays fixed, so rules, targets, reports and the AI keep working.
2. **Brands and models**: a list of brands, each with its models, picked on assets.
3. **Units**: a list of units, picked for parts, measurement points and number steps.
4. **Asset groups**: named sets of assets anywhere in the tree (an asset in any number of groups), used as list filters, report
   filters and PM targets.
5. **Yearly numbering**: an option to restart numbers each year with the year in the number.

## Non-goals

- Adding new work types, priorities, statuses or criticality levels (meanings stay fixed; the user's roadmap wording).
- Editable work order / request statuses, hold reasons (not in the roadmap).
- Brands on parts (a part's manufacturer stays free text); the manufacturer companies of 0.17.0 stay as they are.
- Pack names of the 0.18.0 supplier catalogue (they are not units).
- Asset groups on the phone app (the phone only shows the names already on jobs).
- Grouping reports *by* asset group (an asset in two groups would be counted twice); groups are filters only.
- Restarting asset numbers yearly.

## Anti-goals

- A renamed or hidden value must never change or lose data on existing records.
- No screen may show the built-in name where the organization has its own (web, phone, printed job card, reports, CSV/xlsx
  exports, e-mails). The import accepts both.
- A PM aimed at a group must never silently generate nothing because the group became empty — it says so, as rounds do today.
- Yearly numbering must never produce a duplicate number, also across the change of year and in bulk imports.

## Approaches considered

**Editable lists.**
- **A. Label overrides on the fixed values (chosen).** Organization settings hold, per list and value: English name, Arabic name,
  colour, order, hidden. The web loads them at sign-in and overrides its translation keys, so every existing `t('workType.X')`
  shows the organization's word without touching each screen; pickers use one helper for order and hidden. The server uses one
  helper for names it writes for people. Meanings stay the enums.
- B. Turn the four enums into tables. Rejected: every rule keyed on a value (SLA targets per priority, PM type, completion
  rules, reports, AI context, imports, 4CSQL migration) would change, for no gain the user asked for (no new values).

**Brands, models and units.**
- **A. Lists with ids, and the record keeps its text copy (chosen).** `Brand`/`BrandModel` tables; an asset stores `BrandId`
  and `ModelId` and keeps its existing `Manufacturer`/`Model` text columns equal to the chosen names (renaming a brand or model
  updates them). Every existing reader (lists, search, exports, AI context, reports) keeps working. Units: a `Unit` list; parts,
  points and steps keep their unit text, chosen from the list (renaming a unit updates them).
- B. Ids only, dropping the text columns. Rejected: touches every reader of `Manufacturer`/`Model`/`UnitOfMeasure` for no
  user-visible gain.

**Asset groups in PMs** (the user picked "go with your picks" on my pick A).
- **A. The PM follows the group (chosen).** A PM targets a group; each generation uses the group's current members. A new asset
  added to the group is included from the next visit.
- B. The group fills the PM's asset list once. Rejected: a new asset is missed until someone edits the PM.

**Yearly numbering.**
- **A. The year goes into the prefix and the sequence (chosen).** With the option on, a new number is
  `prefix + yyyy + "-" + padded`, e.g. `WO-2027-00001`, counted in a sequence of its own per year
  (`WorkOrder:2027`, `WorkOrder:<site>:2027`). The existing collision check (numbers that already start with the prefix) keeps
  working because the year is part of the prefix.
- B. A format string with tokens. Rejected for now: more to explain, more ways to make duplicates; the roadmap example is fixed.

## Design

### 1. Editable lists

- **Settings › Lists** (new tab, *Manage settings*): one table per list (Work types, Priorities, Asset statuses, Criticality),
  one row per value in the organization's order: built-in name (read-only, as a hint), English name, Arabic name, colour
  (Mantine palette colour), hidden, and up/down to reorder. "Reset" puts a row back to the built-in names, colour and place.
- Stored in organization settings: `ListLabels` keyed by list (`workType`, `priority`, `assetStatus`, `criticality`) then by
  the enum name: `{ name?, nameAr?, color?, order?, hidden? }`. Empty name = the built-in name in that language. Saving checks
  names ≤ 50 characters, no two values of one list with the same name in one language, colours from the palette, and at least
  one visible value per list.
- **Hidden**: not offered when creating or editing (unless it is the record's current value); still shown on existing records
  and still offered in list filters (to find old records). Form defaults (Corrective, Medium, Active, Medium criticality) fall
  back to the first visible value when hidden.
- **Order**: the order of values in pickers, filter chips and report rows grouped by these lists. Sorting a list *by* priority
  or criticality still follows the meaning (Urgent above Low).
- **Colour**: priority and criticality badges, asset status badges and work type badges/charts use the organization's colour
  when set, else today's.
- **Where names show**: everywhere a person reads them —
  - Web and phone (one place: at sign-in the app adds the organization's names to its translations in both languages).
  - Printed job card and report print/PDF/CSV (they use the web texts).
  - Server-written texts: report rows and group labels, list exports (xlsx), e-mails and notifications that name a type,
    priority, status or criticality — through one helper `ListLabels.Name(settings, list, value, language)`.
  - Not the AI prompts (they keep the built-in meaning).
- **Import**: cells for these lists accept the built-in English name, the Arabic built-in name and the organization's names in
  both languages (case-insensitive); the template and the "current data" export write the organization's English names.

### 2. Brands and models

- **Settings › Brands and models** (editable with *Edit assets*; seen by everyone who sees assets): a list of brands (name,
  active) each with its models (name, active). Brand names unique per organization; model names unique within their brand
  (case-insensitive, trimmed). In use = can't be deleted, make it inactive.
- **Asset form**: Brand and Model pickers (model filtered to the brand; choosing a new brand clears the model). Inactive ones
  are offered only when already chosen. People with *Edit assets* can add a brand or model from the picker ("Add “X”") without
  leaving the form.
- **Asset**: new `BrandId`, `ModelId` (nullable). The existing `Manufacturer` and `Model` text columns stay and always equal the
  chosen names (renaming a brand or model updates every asset's copy in one update). A model without a brand isn't allowed.
- **Asset list**: brand and model as filters (by id); the existing columns keep showing the text.
- **Upgrade**: the migration creates a brand per distinct trimmed manufacturer text (case-insensitive, first spelling met wins)
  and a model per distinct (brand, model text), and sets every asset's ids. Model text on an asset with no manufacturer can't
  become a model (a model needs a brand): the asset keeps the text with no ids (shown, not filterable until someone picks a
  brand).
- **Import (Assets sheet)**: the existing Manufacturer and Model columns pick by name; an unknown brand or model is added to the
  list (imports and the 4CSQL tool keep working) and the check counts it ("New brands: 3"). A model with no manufacturer is a
  problem: "Choose a brand for model X." New sheet **Brands and models** (brand, model, active) for loading the list itself.

### 3. Units

- **Settings › Units** (*Manage settings*): symbol (e.g. `pcs`, `m`, `L`, `°C`; unique, ≤ 20), name (English), name (Arabic),
  active. Seeded at upgrade from the distinct unit texts on parts, measurement points and number steps (symbol = the text,
  name = the text), plus a starter set for new organizations (pcs, m, L, kg, h, °C, bar, kWh).
- **Part form, measurement point form, number step (checklist and template steps)**: the unit is picked from the list (stored
  as the symbol text, as today). The part's unit stays required.
- **Renaming a symbol** updates every part, point and step that uses it (one update each). In use = can't be deleted.
- **Import**: unit cells must match a symbol in the list — unknown ones are added (same reason as brands) and counted.
- Units are not translated in quantities ("5 pcs" stays) — the Arabic name is for the picker only.

### 4. Asset groups

- **Settings › Asset groups** (*Edit assets*; seen with *View assets*): code (unique), name, optional site (one site's or all
  sites', as teams and cost centres), active, and its members. A site-limited person sees groups of their sites and shared
  ones, and only the members they can see; changing membership never touches members they can't see.
- **Membership**: from the group (add assets with the asset picker, remove), from the asset form (a Groups multi-select), and as
  a bulk action on the asset list ("Add to group" / "Remove from group"). A site group only takes assets of its site.
- **Filters**: "Asset group" on the asset list and the work order list (work orders whose asset is in the group, or rounds that
  cover one); saved views keep it.
- **Report filter** "Asset group" on Maintenance costs, Asset reliability, Downtime log, Failure analysis, Failure tree,
  PM compliance and Asset register summary (by the work order's / stop's / asset's own asset).
- **PM target "Asset group"** (a fourth target beside asset, location and several assets): the PM names a group and works like
  "several assets" in round or group mode, with the group's **current active members at the PM's site** at each generation
  (skip statuses apply per asset as today). Per-asset templates and even spread aren't offered for group targets (asset and
  category checklists still apply). An empty group (or none left after skip statuses) gives the same results as a round with no
  available assets ("No active assets…"), never a silent nothing. The PM page and occurrences list show the members it would
  use today.
- A group in use by a PM can't be deleted (make it inactive; an inactive group's PMs report "The asset group is inactive.").
- **Import**: sheet **Asset groups** (code, name, site code, active) and **Asset group members** (group code, asset number;
  re-importing adds, never removes); the PM schedules sheet gains "Asset group code".

### 5. Yearly numbering

- **Settings › Organization › Numbering**: "Restart numbers each year" (off by default).
- When on, new work orders, requests, purchase orders, purchase requests, work groups and contracts are numbered
  `prefix + yyyy + "-" + number` (`WO-2027-00001`) in a sequence per year (and per site where a site has its own prefix).
  Assets keep their running number. The year is the organization's local year (its time zone setting) at creation.
- Switching it on or off never renumbers existing records; switching on mid-year starts that year at 1 (numbers already taken
  are skipped by the existing check).
- Bulk numbering for imports (`NextNumbersAsync`) follows the same rule.
- The number preview in settings shows the next work order number.

### Permissions

No new permissions: lists, units and numbering with *Manage settings*; brands, models and asset groups with *Edit assets*.

### Arabic

Every new text in en + ar (web locales, server ar.json). Built-in Arabic names of the four lists stay the defaults.

## What's left to the builder

- Exact screen layout of the settings tables; whether brands and models share one screen (a master/detail table is fine).
- How the web applies the overrides (i18next `addResource` at sign-in is the expected way) and the picker helper's name.
- Migration SQL for the brand/model/unit seeding.
- Which e-mails name these values (find them; use the helper).

## Tests that matter most

1. A renamed, recoloured, hidden priority shows the organization's word everywhere (a report row, an export, an e-mail), stays
   on an existing work order, isn't offered for a new one, and the import accepts both names.
2. Upgrade seeding: brands/models/units from existing texts, case and spaces collapsed, ids set.
3. Renaming a brand, model or unit updates every record's text copy.
4. A group PM follows membership: add an asset → next generation includes it; remove → drops; empty → "No active assets".
5. Site-limited people: groups and members they can see; membership changes don't touch hidden members.
6. Yearly numbering: `WO-2027-00001`, restarts on 1 Jan in the organization's time zone, per-site prefixes, imports, no duplicates.
