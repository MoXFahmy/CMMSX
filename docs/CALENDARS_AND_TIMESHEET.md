# Calendars, availability and the daily timesheet (0.19.0)

Supervisors plan by person, by machine and by contractor; technicians log a day's hours in one place. CMX keeps planning
**by the day** on the existing planning board (work orders keep their planned date, no clock times) and adds:

- **availability**: time away (holiday, sick leave, training, a meeting) and extra time (an extra shift), so each person's
  day shows the hours they really have;
- **contractor visits** on the board, with a date of their own;
- a **daily timesheet** on the web and **My day** on the phone, over the labour already logged on work orders.

Organizations that enter no availability see the board as before: each person's day is their working pattern.

## Who may do what

| Permission | Can |
| --- | --- |
| *View work orders* | See the board, availability and their own timesheet. |
| *Edit work orders* or *Manage people* | Add, change and delete time away and extra time. |
| *Edit work orders* | Drag work and contractor visits on the board; set a visit date. |
| *Execute work orders* | "Assign to me"; record a contractor visit's hours, date and person; log hours on the timesheet. |
| *Edit work orders* or *Log hours for others* | See other people's timesheets and the week. |
| *Manage settings* | Edit availability types. |

## Availability

**Settings › Availability types**: code, name, colour, and whether the type counts as *time away* (reduces the hours
available) or *extra time* (adds hours). Every organization starts with Holiday, Sick leave, Training, Meeting (time away)
and Extra shift (extra time). A type in use can't be deleted; make it inactive instead.

**Maintenance › Availability** lists entries and adds them; the **+** beside a person's name on the board opens it for
that person. An entry has:

- a **type**, and **a person or a team** — a team entry applies to whoever is a member when the board is looked at;
- **from** and **to** dates (one day when *to* is empty), at most 366 days;
- **weekdays** it applies on — none means every day in the range ("every Sunday" for a weekly meeting);
- **hours a day** — empty means the whole working day (time away only; extra time always has hours).

### Hours available

For a person and a day: the hours of their **working pattern** (their own, else their site's, else the organization's)
**minus time away** (a whole-day entry takes all of it; never below 0) **plus extra time**. A holiday on an 8-hour day
leaves 0; a 2-hour meeting leaves 6; a 6-hour extra shift on a Saturday off gives 6. The board, its warnings, the timesheet
and the phone all use this same figure.

## The planning board

- Each person's day shows **planned / available** hours. A day with time away is tinted in the type's colour with its name;
  over the available hours it turns red.
- **Planned hours** count all of the person's open work you can see, whatever the board's filters show, so a site filter
  never makes a full day look empty.
- **Warnings** (the move still happens): dropping work on a day someone is away ("Ann Fitter is away on that day
  (Holiday).") or past their hours ("This takes Ann Fitter to 10 of 8 hours on 3 Dec.").
- **Assign to me** on work nobody is on yet: a technician takes it. If two people press at once, one gets it and the other
  is told "Someone was assigned meanwhile."
- **By asset**: a card from a work template shows its expected stop ("Stop ~4 h") next to the "only while stopped" mark.

### Contractor visits

**By contractor** shows one row per company with visits in the range. A visit is a contractor line on open work; its day
is its **visit date**, else the work order's day. Lines with neither are in **No visit date**.

- Drag a visit to another day of its company to set its visit date (back to the tray clears it). The company itself is
  changed on the work order.
- Click a visit to **record** the hours done, the date and the person. Notes are written by planners, as in 0.17.0.
- The visit date is also on the work order's Contractors tab.

## Timesheet

**Maintenance › Timesheet** shows one person's day (yourself by default):

- the work orders they are on (or their team is on) dated that day, and every work order they logged hours on that day;
- their hours, overtime and notes per order, added, changed and deleted in place — the same entries as each work order's
  Labour tab, so costs never count twice;
- "Add hours on another work order", and the day's total against the hours available.

The **Week** tab (supervisors) shows each person's logged hours against their available hours for seven days: red when
more than available, orange when less. Click a day to open it.

## My day on the phone

**My day** (from My jobs) shows a day of the last 30: my jobs of that day, jobs I logged hours on that have been completed
since (read-only), my hours per job and the day's total against the hours I have. **Hours** adds hours to a job offline,
like the job's own Log hours.

## Not in this release

Clock times and an hourly view, contractor visits spanning several days as one bar, saved board views, leave requests or
timesheet approval, and an availability import sheet.
