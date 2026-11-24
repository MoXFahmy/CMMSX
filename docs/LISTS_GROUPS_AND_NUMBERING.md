# Lists, brands and models, units, asset groups and yearly numbering (0.21.0)

Each organization can make CMX use its own words, and keep its asset data tidy:

- **its own names, colours and order** for work types, priorities, asset statuses and criticality, in English and Arabic;
- **brands and models** picked on assets, and a **units** list for parts, measurement points and template steps;
- **asset groups**: named sets of assets anywhere in the tree, used as filters and as PM targets;
- an option for **numbers that restart each year** (WO-2027-00001).

Organizations that change nothing see CMX as before.

## Who may do what

| Permission | Can |
| --- | --- |
| *Manage settings* | Edit the lists' names, colours, order and hidden values; edit units; switch on yearly numbering. |
| *View assets* | See brands, models and asset groups. |
| *Edit assets* | Add, rename, merge and delete brands and models (also from the asset form); add and change asset groups and their members. |
| *View inventory* | See the units list. |

There are no new permissions.

## Your own words for the fixed lists

**Settings › Lists** shows work types, priorities, asset statuses and criticality. For each value you can set:

- an **English name** and an **Arabic name** (empty = the built-in words), up to 50 characters;
- a **colour** for its badge, from the palette;
- its **place** in pickers and filters (the arrows);
- **hidden**: not offered for new records. Records that already have it keep it, and list filters still offer it so old records
  can be found. A form whose default is hidden starts on the first value shown instead.

**Reset** puts a value back to its built-in name, colour and place. Each list keeps at least one value showing, and a name can't be
the same as another value's name in either language, so a name always means one value.

The **meaning** of each value never changes: response targets per priority, rules, reports and the AI keep working. Your names
show everywhere people read them: screens, the phone (also offline), the job card, reports and their print/PDF and CSV, list
exports and e-mails. The import accepts the built-in names and yours, in English or Arabic.

## Brands and models

**Settings › Brands and models**: a list of brands, each with its models. On the asset form, **Brand** and **Model** are picked from
the list (the models of the chosen brand). People who edit assets can add a missing brand or model straight from the picker
(**Add “…”**). Inactive ones are offered only where they're already chosen.

The asset keeps the names as its manufacturer and model text, so lists, search, exports and reports keep working. Renaming a brand
or model renames it on every asset. **Renaming onto a name already in the list merges the two** after you agree: the assets move to
the one kept. A brand or model in use can't be deleted; make it inactive instead.

The asset list can be filtered by brand and model.

## Units

**Settings › Units**: a symbol (such as `pcs`, `m`, `L`, `°C`), an English name and an Arabic name. Symbols are **case-sensitive**:
`mW` and `MW` are different units. New organizations start with pcs, m, L, kg, h, °C, bar and kWh.

The part form, the measurement point form and number steps on templates pick their unit from the list (the unit stays the symbol's
text). A new symbol is written to every part, point and template step that used the old one; renaming onto another unit's symbol
merges the two. A work order's own checklist keeps the unit it was copied with, so finished jobs keep their history.

## The starting lists (on upgrade)

On its first start, 0.21.0 builds the brand, model and unit lists from what your records already say:

- one brand per manufacturer text — "Grundfos", " grundfos" and "GRUNDFOS" become one brand (the first spelling met names it);
- one model per brand and model text; a model with no manufacturer stays as text until someone picks a brand;
- one unit per unit text on parts, measurement points and template steps (spaces trimmed, case kept).

Assets are linked to their brand and model. Then tidy the lists by renaming onto each other to merge. The same happens after every
import and at the end of a 4CSQL migration run, so new names join the lists by themselves; the import's check shows how many it
would add ("New brands: 3").

## Asset groups

**Settings › Asset groups**: a code, a name, a site (empty = assets of every site) and active. An asset can be in any number of
groups. Members are added:

- from the group (the members button), one asset at a time;
- from the asset form (**Asset groups**);
- from the asset list: select assets, then **Asset group › Add to a group / Remove from a group**.

A site's group only takes that site's assets. Someone limited to some sites sees their sites' groups and the shared ones, only the
members they can see, and never changes members they can't see.

Groups are used as:

- a **filter** on the asset list and the work order list (work on one of its assets, or a round covering one);
- a **report filter** on Maintenance costs, Asset reliability, Downtime log, Failure analysis, Failure tree, PM compliance and the
  Asset register summary;
- a **PM target**: on the PM form choose **Asset group** as what the PM maintains, then round or group mode. **The PM follows the
  group**: each visit covers the group's active members at the PM's site that day, so an asset added later is included from the
  next visit. The form shows the members it would cover today. Per-asset templates and spread aren't available for a group PM.
  If the group has no active members (or is inactive), the PM says so and stays due, as a round does; if every member is in a
  status that skips PM work, the visit is skipped and the PM moves on.

A group used by a PM can't be deleted; make it inactive instead.

## Yearly numbering

**Settings › Organization**, under work order numbering: **Restart numbers each year**. When on, new work orders, requests,
purchase orders, purchase requests, work groups and contracts carry the year and start again at 1 each year:
`WO-2027-00001` (with a site's own prefix: `CAI-2027-00001`). The year is your organization's, in its time zone. Asset numbers keep
running. Switching it on or off never renumbers existing records.

## Import and export

Settings › Import has new sheets, and the "current data" export includes them:

- **Brands and models**: brand, model (empty for the brand itself), active.
- **Asset groups**: code, name, site code, active.
- **Asset group members**: group code, asset number. Importing adds members and never removes any.

The **PM schedules** sheet gains **Asset group code**. Brands, models and units named on the Assets and Parts sheets join the lists.

## Not in this version

- New values in the fixed lists (meanings stay fixed), and editable work order statuses.
- Brands on parts (a part's manufacturer stays text).
- Grouping reports *by* asset group (an asset can be in two groups); groups are filters.
- Asset groups on the phone.
