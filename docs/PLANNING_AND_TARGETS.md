# Targets and the planning board

## Targets by priority

Settings › Organization › **Targets by priority**: for each priority, *respond within* and *complete within*, in hours or
days. Leave a box empty for no target. Nothing changes until you set a target.

- **New requests** get a response deadline and a completion deadline, counted from when they are raised.
- **New work orders** get a start-by (response) deadline and a due time, counted from when they are created — or, when
  approved from a request, from when the request was raised (even if the priority changes while approving).
- **PM work** keeps the due date of its schedule; it has no response deadline.
- The clock counts **every hour**, including nights and weekends, and does not stop while a job is on hold.
- A **due date or time typed by hand** always wins. A date without a time means "by the end of that day" in the
  organization's time zone (Settings › Organization › Time zone — please check it).
- Changing a job's **priority** recomputes deadlines that came from a target; changing a **target** never moves deadlines
  that are already set.
- A requester's **Needed by** date is shown beside the deadline; approvers can choose it with *Use the requester's date*.
  It becomes the deadline only for priorities with no completion target.

### What counts as a response

- A request: when it is approved or rejected.
- A work order (not from a request): the first time someone acts on it — starts it, puts it on hold, completes it, ticks
  a checklist step or logs hours. For work done offline on the phone, the time the technician did it.

### Late, due soon, done

| Shown | Means |
|---|---|
| due in 3 h | open, on track |
| due in 40 min (orange) | less than a quarter of the target left (or under 2 hours for a deadline typed by hand) |
| 2 h late (red) | open and past the deadline |
| done in time / done late | completed before / after the deadline |

The *Late* filter (work orders) and *Late* tab (requests) list everything past its response or completion deadline. The
dashboard counts late work, work due soon, and work and requests waiting for a response. **Reports › Targets met** shows
how many responses and completions met their targets, per month or per priority.

## The planning board

Maintenance › **Planning**. Week (7 days from today) or month; by technician or by asset; filter by site and trade.

- A job sits on its **planned start**, or its due day when it has no planned start, in the row of each person on it
  (*Nobody yet* when nobody is). Click a card to open the work order.
- **Drag** a card to another day to change its planned start (a warning when that is after its due date), or to another
  person to hand it over (their planned hours go with it). Drop it on *Nobody yet* to take that person off. Each move
  saves at once and can be undone from the message that appears.
- Each person's day shows **planned hours / working hours** (Settings › Organization › Working hours per technician per
  day, 8 by default), in red when over. A person's planned hours on a job count; otherwise the job's estimate is split
  between the people on it.
- **From earlier days**: open work planned for a day that has passed. **No date**: open work with neither a planned start
  nor a due date. Drag one onto a day to plan it.
- **Upcoming PMs** (not generated yet) appear as dashed cards in the rows of the schedule's technicians; *Generate now*
  creates the work order straight away.
- Only people who may edit work orders can drag; everyone who can see work orders can look.
- Since 0.19.0 each day counts time away and extra time, the board warns when work lands on a day someone is away or
  over their hours, and it has a contractor view — see [CALENDARS_AND_TIMESHEET.md](CALENDARS_AND_TIMESHEET.md).
