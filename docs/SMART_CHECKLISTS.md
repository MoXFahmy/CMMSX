# Smart checklists and follow-up work (0.13.0)

## Step types

Each step of a work template says what it asks for (open the step on the template page):

| Type | Answer | Fails when |
|---|---|---|
| **OK / Not OK / N/A** (Check) | one of the three | answered Not OK |
| **Number** | a number, with a unit and decimals, or judged against a **measurement point** | the reading reaches an alert or alarm level of its point |
| **Meter reading** | a reading saved on the asset's meter (meter PMs follow, linked meters too) | never |
| **Text** | a short text (up to 1,000 characters) | never |

Any step can also be:

- **Photo required**: the work order can't be completed until a photo is taken on that step.
- **Before starting (safety)**: lock-out, permit, protective equipment. It must be answered OK or N/A (or given a value) before the
  work can go In progress or Completed, and before any other step can be answered — on the web, in bulk, for a group and on the
  phone. A safety step answered Not OK keeps the work from starting until it is OK. Automatic start waits for safety steps too.
  On a round, each asset's safety steps hold up that asset's steps only.

Steps from before 0.13.0 are OK / Not OK / N/A steps; a step that was ticked counts as OK.

The type is set on the template and copied to every work order made from it: PM work (rounds and groups included), a new work order
from a template, asset and category checklists, the phone's *Add a checklist* and *Own template* copies. Changing a template later
doesn't change work already made.

A Meter step reads the asset's meter with the step's unit (or the asset's only meter). If there's no such meter, the person answering
chooses it. A lower reading or a big jump asks for confirmation, as on the meter form.

## Measurement points

PM › **Measurement points** lists what Number steps measure, for example "Supply air temperature", °C, possible values 0–60, alert at
28, alarm at 32. A point has:

- **Kind**: temperature, pressure, current, voltage, level, consumption or other. It also has a **unit** and **decimals**.
- **Lowest / highest possible value**: anything outside is refused as a typo (850 typed for 85.0).
- **Up to five levels**: alarm low, alert low, target, alert high, alarm high. Leave a level empty if it isn't used. Each alert or
  alarm level can name a **work template** to raise work from.
- **Assets** it is measured on: they show the point's chart.

A level counts at its threshold: with an alert at 28, a reading of 28 is an alert. Changing a limit applies to the next reading
everywhere. A point that templates use can't be deleted.

## Follow-up work

A **Not OK** answer, or a reading at an **alert or alarm** level, raises follow-up work in the same save. Each step chooses what it
raises (*If it fails*):

- **Organization default** (Settings › Organization › *A failed step raises*): a request for triage unless you change it.
- **Nothing**.
- **Work order from a template**.
- **Request for triage**: a person approves or rejects it.

When the reading's level has its own template on the measurement point, that wins. If a step's template can't be found, a request is
raised instead, so a failed check never goes unnoticed.

The follow-up:

- is for the step's asset (on a round, that asset; otherwise the work order's);
- is titled, for example, "Belt replacement — from step 2 of WO-00123";
- carries the step, the answer or value with its level, and the remarks;
- links back: its page shows where it came from, with the step's photos.

Each step raises **one** follow-up. Correcting the answer, and even getting it wrong again, doesn't raise another. If the follow-up
was a mistake, cancel it. Readings entered on the asset's *Measurements* tab raise their follow-ups the same way, using the
organization's default.

Answers from the phone raise their follow-ups when the phone's changes reach the office. An answer the server refuses (outside the
possible values, or changed by someone else in the meantime) raises nothing.

## On the work order

The *Tasks* tab:

- lists safety steps first; the other steps are locked until the safety steps are answered;
- gives each step the answer its type asks for, shows a reading's level, and has a photo button;
- offers **All OK**, which answers every remaining plain OK / Not OK / N/A step. It skips safety steps, photo steps and numbers, and
  works for the whole work order or for one asset of a round;
- shows each round asset in green (all answered, nothing failed) or red (a Not OK or an alert/alarm reading).

Settings › Organization › **Require every number, meter and text step answered before completing** sits next to *every step ticked*.

## Trends and the report

- **Asset › Measurements**: a chart of each point's readings with its levels, the latest readings with their work orders and
  follow-ups, and *Add reading* (people who may record meter readings).
- **Reports › Out of limits** (Reliability): the readings at an alert or alarm level in a period, with point, asset, value, level,
  work order and follow-up, and totals of alarms, alerts and follow-ups raised.

## Import

- *PM steps* has new optional columns: Type, Unit, Decimals, Measurement point (code), Photo required, Before starting, Follow-up
  and Follow-up template (code). Blank cells keep what the step with the same number and text has.
- A new *Measurement points* sheet takes code, name, kind, unit, decimals, the possible range, the five levels, the four level
  templates (codes), assets (asset numbers) and active. It adds and updates points by code, with the same checks as the screen.
