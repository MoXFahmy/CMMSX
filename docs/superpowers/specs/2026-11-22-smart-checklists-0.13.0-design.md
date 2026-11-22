# CMX 0.13.0 — Smart checklists and follow-up work (design)

Status: approved by the user ("go ahead", 22 Nov 2026) with option A for measurement points and the executor's other calls.
Roadmap source: CMX release roadmap artifact, release 0.13.0.

## Why

A checklist step today is a tick, a free-text reading (100 characters) and a remark. A failed check or a high
temperature is noticed only if someone reads the work order afterwards. DimoMaint turns such answers into corrective
work automatically, but its follow-ups arrive minutes late from a background job (or not at all). CMX should make each
step say what kind of answer it wants, judge the answer against shared limits, and raise the follow-up **in the same
save**, on the web and from the phone.

## Goals

1. **Step types** on work templates and work orders: **Check** (OK / Not OK / Not applicable), **Number** (unit,
   decimals, optionally a measurement point), **Meter reading** (records a reading on the asset's meter), **Text**.
   Any step can be **photo required** and/or **before starting** (a safety step). Existing steps become Check steps and
   keep their tick, reading and remarks.
2. **Measurement points** (option A): a shared list (PM › Measurement points) — code, name, kind (temperature,
   pressure, current, voltage, level, consumption, other), unit, decimals, a valid range that refuses impossible values,
   and up to five levels (alarm high, alert high, target, alert low, alarm low). Each alert/alarm level can name its own
   work template. A point can list the assets it is measured on. Readings are kept per point and asset.
3. **Follow-up work**: a Not OK answer, or a reading at an alert/alarm level, creates follow-up work in the same save —
   a work order from a template, or a request for triage — set per step, with an organization default. The follow-up
   links back to its work order and step, and carries the answer, value, remarks and the step's photos.
4. **Safety steps before starting**: steps marked "before starting" must be answered (OK / N/A / a value) before the
   work order can go In progress or Completed, and before any other step of the same work (or, on a round, the same
   asset) can be answered — on the web, in bulk, on groups and on the phone.
5. **All OK** answers every remaining plain Check step at once (not safety steps, not photo-required ones); on rounds,
   per asset too. Round progress per asset shows green (all answered, nothing failed) or red (a Not OK or an
   alert/alarm reading).
6. **Completion rules**: a new organization option "Completing needs every number, meter and text step answered", next
   to "every step ticked"; a photo-required step always needs a photo before completion.
7. **Trends**: on the asset, a chart per measurement point over time with its levels, the recent readings, and
   *Add reading*; a new **Out of limits** report across assets.
8. **Phone**: the job's checklist answers every step type offline, enforces safety steps, offers All OK, takes a photo
   on a step, shows the level of a reading as it is typed, and picks the meter when a step needs one. Follow-ups are
   raised when the phone's changes arrive.
9. **Import**: the *Work template steps* sheet gets the step columns; a new *Measurement points* sheet.
10. Arabic for every new text, as always.

## Non-goals (0.13.0)

- Readings import; readings entered on the phone outside a checklist step; charts on the phone.
- Conditional steps (show a step only when another is Not OK), signatures per step, planned labour per step.
- Typed steps added by hand on a work order (an ad-hoc step stays a Check step).
- Changing steps already on work orders when a template or a point changes (new work only, as today) — except that a
  point's limits are read when a reading is entered, so a changed limit applies to the next reading everywhere.

## Anti-goals

- A follow-up that appears late, twice, or for an answer that was corrected before saving.
- A limit typed in many places (that is option B, rejected).
- Blocking a technician offline: the phone must be able to answer, photograph and sync later.
- Existing organizations noticing anything before they use the new step types (except that Not OK is a new answer).

## Constraints (house rules, unchanged)

- One migration, `SmartChecklists`; only new tables and nullable/defaulted columns; existing rows keep every value.
- 0.9.0 site access everywhere (readings filtered by site; points are organization-wide like templates and calendars).
- Code names stay (`PmTask`, `PmTaskStep`, `WorkOrderTask`); screens say *Work template*, *step*, *measurement point*.
- No new permission keys: points with `pm.view` / `pm.manage`; readings with `meters.record`; answering steps with
  `workorders.execute`; report with `workorders.view`.
- Times in UTC, shown in the site's zone; no form defaults to a time after now.

## Approaches considered

- **A. Measurement points as their own list (chosen).** A template step picks a point; one template works on many
  assets, limits change in one place, readings make a history per asset for trends.
- **B. Limits typed on each template step.** Quicker, but limits get copied into every template and there is no shared
  history to chart. Rejected.
- **C. Measurement points as a kind of meter.** Meters already have readings and threshold PM triggers, but a meter
  belongs to one asset (a template can't name it) and counters must not go down. Rejected; meter-reading steps still
  write to meters.

## Design

### Step model

- `StepType { Check = 0, Number = 1, Meter = 2, Text = 3 }` (Check = 0 so existing rows are Check steps).
- `StepAnswer { Ok = 1, NotOk = 2, NotApplicable = 3 }`.
- `FollowUpKind { Default = 0, None = 1, WorkOrder = 2, Request = 3 }`.
- `MeasureLevel { Normal = 0, AlertHigh = 1, AlarmHigh = 2, AlertLow = 3, AlarmLow = 4 }`.
- **Template step** (`PmTaskStep`) gains: `Type`, `Unit` (≤ 20), `Decimals` (0–4, null = 2), `MeasurementPointId`,
  `PhotoRequired`, `BeforeStart`, `FollowUp`, `FollowUpTemplateId`. A Number step with a point takes the point's unit and
  decimals. A Meter step's `Unit` (optional) picks which of the asset's meters it reads.
- **Work order step** (`WorkOrderTask`) gains the same definition fields (copied from the template step, so later
  template edits don't change existing work) plus `MeterId` (resolved at creation: the step's asset — its own asset on a
  round, else the work order's — active meter with that unit, or its only active meter; else null and the answer picks
  it), and the answer: `Answer`, `Value` (decimal), `TextValue` (≤ 1000), `Level`, `FollowUpWorkOrderId`,
  `FollowUpRequestId`.
- `IsDone` keeps meaning "answered": Check → `Answer` set; Number/Meter → `Value` set; Text → `TextValue` set. An old
  client's `isDone: true` on a Check step means OK; `isDone: false` clears the answer (and value). The existing
  `Reading` and `Remarks` stay on every type.
- **Every place that copies template steps copies the definition**: PM generation (single, round with per-asset
  templates, group children, spread parts), a new work order from a template (the form sends step texts: each text
  equal to an unused template step's description takes that step's definition, in order; other texts are Check
  steps), asset and category checklists, the phone's *Add a checklist*, *Own template* copies, and the import.

### Measurement points and readings

- `MeasurementPoint` (organization-wide, soft-deleted, row version): `Code` (unique, ≤ 50), `Name` (≤ 200), `Kind`
  (`MeasurementKind { Temperature = 1, Pressure, Current, Voltage, Level, Consumption, Other }`), `Unit` (≤ 20),
  `Decimals` (0–4), `ValidMin?`, `ValidMax?`, `AlarmLow?`, `AlertLow?`, `Target?`, `AlertHigh?`, `AlarmHigh?`,
  `AlarmLowTemplateId?`, `AlertLowTemplateId?`, `AlertHighTemplateId?`, `AlarmHighTemplateId?`, `IsActive`; assets in
  `MeasurementPointAsset (PointId, AssetId)`.
- Validation: valid min < max; set levels in order alarm low ≤ alert low ≤ target ≤ alert high ≤ alarm high, and inside
  the valid range; a level template must exist; deleting a point that steps use is refused (in use by N templates).
- `MeasurementReading` (site-filtered): `PointId`, `AssetId?`, `SiteId`, `Value`, `ReadAt`, `Level`, `WorkOrderId?`,
  `WorkOrderTaskId?`, `Note`, `ById`, `ByName`, `FollowUpWorkOrderId?`, `FollowUpRequestId?`.
- **Level** (pure function): value ≥ alarm high → AlarmHigh; ≥ alert high → AlertHigh; ≤ alarm low → AlarmLow;
  ≤ alert low → AlertLow; else Normal. A value outside the valid range is refused ("Enter a value from {min} to {max}
  {unit}."). Values are rounded to the point's decimals.
- A Number step with a point writes one reading per answer change (changing the value replaces that step's reading,
  so the history has one reading per step). Readings can also be entered on the asset page (`meters.record`), with the
  same levels and follow-ups.

### Follow-up work

- Raised when a Check step is answered Not OK, or a reading (step or direct) is at an alert/alarm level.
- **Which**: effective kind = the step's `FollowUp`, or the organization's default when it is Default (direct readings
  use the default). None → nothing. If the point has a template for the reading's level → a work order from that
  template. Else WorkOrder → a work order from the step's template, else the organization's default template; Request
  → a request for triage.
- Organization settings: `FollowUpDefault` (default **Request**: Not OK is a new answer, so nothing changes for anyone
  until they use it; a request is the safest default because a person decides), `FollowUpTemplateId`,
  `RequireReadingSteps` (default off). Saving WorkOrder as the default needs a template; a template step set to
  WorkOrder needs a template.
- **Once per step**: a step that already has a follow-up raises no other (even if the level changes); correcting the
  answer back to OK keeps it (people cancel it if it was a mistake). A direct reading raises its own.
- **What**: the work order goes through the normal creation (numbering, targets, e-mails, asset checklists, typed
  template steps) — asset = the step's asset (round) or the work order's; site and location from the work order; type
  and priority from the template (priority falls back to the source's); title "{template title or name} — from step
  {n} of {WO number}"; description: the step, the answer or value with its level, the remarks. A request gets title
  "{step} — from step {n} of {WO number}", the same description, the site, location and asset, and the answering person
  as requester. Both record `SourceWorkOrderId` and `SourceTaskId`; the follow-up's page shows "From step {n} of
  WO-…" with the answer and the step's photos (not copied).
- Created in the same transaction as the answer (web PUT, phone change, direct reading).

### Safety steps

- `BeforeStart` steps must be answered OK or N/A (Check) or have a value before: moving the work order to In progress
  or Completed (every path: page, bulk, group status, phone, request reopen excluded since it moves to Open), and
  answering any other step. On a round, a step of asset X needs the safety steps of X and those without an asset;
  In progress needs only the ones without an asset.
- A safety step answered Not OK blocks the same moves ("A safety step isn't OK: {step}.").
- Automatic start (labour or a step on an Open work order) doesn't happen while safety steps are unanswered (no error).
- Messages: "Answer the safety steps first: {steps}."

### All OK, photos, completion

- `POST /api/work-orders/{id}/tasks/all-ok { assetId? }`: answers OK every unanswered Check step that isn't a safety
  step or photo-required, of the work order or of one asset; safety rules apply. The phone does the same locally, as
  one change per step.
- Step photos: `Attachment.WorkOrderTaskId` (nullable). The web upload and the phone's `/tech/files` take an optional
  step id; the photo is still the work order's file and also shows on its step.
- Completing needs a photo on every photo-required step (always), and, with `RequireReadingSteps`, every Number, Meter
  and Text step answered. Messages name the steps.

### Meter-reading steps

- Answering records the value through the existing `RecordReadingAsync` (same checks, propagation and meter PMs),
  stamped with the work order; the same confirmation as the meter form for a lower value or a big jump (the web asks
  and re-sends with confirm; the phone sends confirm, as its reading change does). Without a resolved meter the answer
  names one of the asset's meters.

### Screens

- **PM › Measurement points** tab: list (code, name, kind, unit, levels, assets, readings) and an editor with the five
  levels, a template per alert/alarm level, and assets.
- **Work template page**: each step row gets type, unit / point, decimals, photo required, before starting, follow-up
  (default / none / work order + template / request).
- **Work order › Tasks**: safety steps first with a lock note; answer controls per type (OK / Not OK / N/A buttons,
  number with unit and the level badge, meter picker, text); photo button per step; *All OK*; round groups per asset
  with green/red; follow-up links; on a follow-up, the source banner.
- **Asset › Measurements** tab: a chart per point (readings over time, level lines), recent readings with work orders
  and follow-ups, *Add reading*.
- **Settings › Organization**: follow-up default and template; "every number, meter and text step answered".
- **Reports › Out of limits** (Reliability): readings at alert/alarm levels in a period, by site, with point, asset,
  value, level, work order and follow-up; totals by level.
- **Phone job**: as goal 8.

### Import

- *Work template steps*: optional columns Type, Unit, Decimals, Measurement point (code), Photo required, Before
  starting, Follow-up, Follow-up template (code); unknown codes are refused per row.
- *Measurement points*: code, name, kind, unit, decimals, valid min/max, the five levels, the four level template codes,
  assets (asset numbers), active; add and update by code.

## Left to the builder

- Exact names of endpoints beyond those above, index choices, page layout within the existing Mantine patterns, chart
  details, and how the phone stores the points it needs (bundle).
