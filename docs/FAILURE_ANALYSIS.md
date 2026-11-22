# Failure codes and maintenance analysis (0.14.0)

## Symptom, cause and remedy

Three lists in Settings: **Symptoms** (what people saw), **Causes** (what was wrong — the failure codes of earlier
versions, with all their history) and **Remedies** (what was done). Each has a code, a name and *Active*. A code that a
request or work order uses can't be deleted; mark it inactive instead. Inactive codes aren't offered any more, but a
record keeps the one it already has.

Where they are recorded:

- **Symptom**: on a new request (web, phone), copied to its work order when the request is approved (also when a request
  type approves it automatically); on a work order's form and on the phone's *New work order*.
- **Cause and remedy**: when completing a work order (web, a work group's report, the phone).
- All three show on the work order page, the phone job and the printed job card, as columns and filters on the work
  order list (*Not recorded* finds work without one) and in its export. The symptom also shows on the request.

*Require a cause on breakdown work* (Settings › Organization) keeps working as *require a failure code* did. Symptom and
remedy are never required.

**Smart ordering.** Once a symptom is chosen, the causes most often recorded with it (on work completed in the last two
years, on the sites you can see) come first under *Most used together*; once a cause is chosen, its most used remedies do.
Everything else follows by code. The phone does the same offline. A code picked on the phone while offline is accepted
even if someone made it inactive in the meantime.

## Reports

- **Failure tree** (Reliability): work completed in the period that has a symptom, a cause or a remedy, plus breakdowns
  without any code — symptom, then its causes, then each cause's remedies, with work orders, labour hours, downtime and
  cost at each level. Each line opens the work order list showing exactly those work orders.
- **Responsiveness** (Work): each request and work order whose response or completion deadline falls in the period, with
  the target, when it was answered or completed, the time taken and *Met*, *Missed* or *Pending*. Group by priority or
  trade for averages and the share met.
- **PM annual plan** (Work): one row per PM, a column per month of the chosen year. Past months show what happened (done
  on time, late, missed, cancelled); this month and later ones show what is still planned. A group's work orders for one
  visit count once.
- **Asset work history** (Assets): every work order raised on one asset in the period (with its components if asked),
  with its codes, labour hours, cost and downtime.
- **PM compliance** can be grouped by asset category, and **Maintenance costs** shows labour hours and can be grouped by
  technician (their labour only).

## Asset costs by year

An asset category can have a **downtime cost per hour**; an asset without its own uses its category's. The asset's
**Costs** tab shows, per year (the last five years and this one, plus older years with costs): labour, parts, other
costs, downtime hours (the asset's stops measured over its running time), downtime cost and the total, with a chart.
*With its components* adds the assets below it. The Reliability report uses the same cost per hour.

## Logo and PDF

Settings › Organization › **Logo**: PNG, JPEG or WebP up to 1 MB. It appears on printed reports and job cards.

Every report has **Print or save as PDF**: a landscape page with the logo, the report's period and filters, who printed
it and when, the totals, the chart and the whole table (headings repeat on each page). Choose *Save as PDF* in the print
dialog to keep a file.

## Import

The import has **Symptoms**, **Causes** and **Remedies** sheets (code, name, active; added and updated by code), and the
**Categories** sheet has *Downtime cost per hour*.
