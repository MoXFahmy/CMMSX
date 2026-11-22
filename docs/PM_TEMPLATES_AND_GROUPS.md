# Work templates, triggers and group work (0.12.0)

## Work templates

*PM tasks* are now **work templates** (PM › Work templates). Besides the code, name, steps, parts, trade, estimated hours
and safety notes, a template has:

- **Work order title** (empty = the template's name), **work type** (Preventive by default) and **priority** (empty = the
  PM's priority).
- **Recipients**: people told about every work order made from it (see *Recipients* below).
- **Expected stop (hours)**: shown on the work order, so planners know how long the asset will be down.
- **Documents**: manuals, drawings, procedures. Work orders made from the template show them under *Template*; they are
  not copied, so a newer document reaches every work order. Technicians open them from the job on the phone when online.
- **Used by**: the PMs, asset checklists and open work orders that use it.

**New work order from a template:** on the work order form, pick a *Template*: title, description, type, priority, trade,
hours and steps are filled in; on save the template's parts and recipients are added too.

**Make a copy for this PM:** on a PM that shares a template with others, *Own template* copies it (code
`<template>-<PM>`), so later edits to the shared one no longer reach this PM.

## More triggers: calendars and extra meters

- **PM calendars** (PM › Calendars): named sets of dates many PMs share — *every N days/weeks/months/years from a date*
  or *chosen dates* (up to 500), e.g. a hotel's low-season weeks.
- A PM can use up to **10 calendars**. The new PM type **Calendar** takes its dates only from its calendars; other PMs
  can add calendars on top of their own rule. Calendars need the **Scheduled** basis.
- A single-asset PM can add up to **5 extra meters**, each with its own interval.
- **Whichever comes first** generates the work. Then every meter trigger restarts from its current reading, the PM's
  own time rule restarts from that day, and calendar dates stay as they are.
- A PM whose triggers give no future date shows *No next date* instead of generating.

## Recipients and the PM's own rules

- **Recipients** on a template and on a PM (up to 20 each) are added to each work order's recipients. In Settings ›
  Notifications the column **Work recipients** decides which e-mails reach them (on by default for *PM work generated*,
  *completed* and *cancelled*). Site access still applies: people who can't see the site get nothing.
- **Own rules**: a PM can skip other asset statuses, use other non-working days, or wait (or not) for its previous work
  order, instead of the organization's settings. *Use the organization's rules* puts them back.

## Several assets: round or group, templates per asset, spread

- **Round** (as before): one work order with a checklist per asset.
- **Group**: one work order **per asset**, gathered in a **work group** (`GRP-00001`). Choose how the plan reaches them:
  *Copy* (each gets the template's hours and parts) or *Split* (divided between them). The mode can't change once the PM
  has generated work.
- **A template per asset**: on a round or group, any asset can have its own template; empty = the PM's. Saving warns
  when an asset would get no checklist steps.
- **Spread over N parts** (fixed interval, Scheduled basis, no calendars or extra meters): each period is split into N
  dates, each covering the next block of assets — 12 assets, yearly, 12 parts = one asset a month.

## Occurrences

*Occurrences* on a PM lists the next 24 dates (meter PMs: the next readings with an estimated date), with the template
each would use and the work already made. **Generate this one now** makes that date's work early; when normal generation
reaches it, it is skipped as *Already generated* and the PM moves on.

## Work groups

Work › **Work groups** lists the groups with their progress. A group's page shows its work orders (asset, assignees,
steps done, hours, cost) and totals. Actions apply to every work order, or only to the ticked ones:

| Action | What happens |
|---|---|
| **Assign** | adds the people to each work order; finished work and work with hours logged stay as they are and are listed |
| **Write report** | action taken, cause, prevention and failure code written to unfinished work orders; what a work order already has is kept |
| **Change status** | each work order follows its normal rules (hold reason, cancel reason, closing permission); those that refuse say why |
| **Due date** | passed to every unfinished work order; a group's work order can't have its own due date |
| **Comments** | shown on every work order of the group, on the web and on the phone |

The group's status comes from its work orders (all open = Open, all done = Completed, and so on). Its work orders appear
everywhere like any other (lists have a *Group* column and filter); the group itself is not a work order and is not
counted in reports.

## Checklists on an asset or category

On an asset's *Maintenance plan* tab (**Checklists**), or on a category (Settings › Asset categories, edit), attach work
templates. Every **new** work order on that asset — from the form, a request, a PM, the phone or a group — gets their
steps after its own; a template it already uses isn't added twice. Assets need *edit assets*; categories need *manage
settings*.

## Meters that follow another meter

A meter can **follow** another meter with the same unit (PM › Meters, *Follows meter*): every increase recorded on the
source is added to the follower as its own reading (*From <code>*), and the follower's PMs run. Lower readings (a
reset) and readings older than the latest are not passed on. Chains work (vehicle → engine → pump); loops are refused.

## Import

The *PM tasks* sheet is now **Work templates** (the old name still imports) with optional columns Title, Work type,
Priority and Expected stop hours. *PM schedules* has an optional **Multi-asset mode** column (Round / Group).

## Upgrading

Nothing changes for existing PMs: no calendars, extra meters, recipients or own rules, so they generate on the same dates
with the same work orders. Existing work orders are not touched.
