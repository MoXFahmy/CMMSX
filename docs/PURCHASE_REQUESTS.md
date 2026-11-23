# Purchase requests and approval levels (0.16.0)

Technicians and store keepers ask for what they need with a **purchase request**; one person approves it ("do we need
this?"); buyers turn approved lines into draft **purchase orders**, where **approval levels** decide who signs by
amount ("can we afford it?"). Organizations that set no levels keep approving orders exactly as before.

## Who may do what

| Permission | Can |
| --- | --- |
| *Raise purchase requests* (`purchasing.request`) | Raise, edit (draft or returned), submit, cancel and delete (draft) their own requests; comment and add files. |
| *Approve purchase requests* (`purchasing.approveRequests`) | Approve or return requests at their sites — never their own. |
| *Create purchase orders* (`purchasing.create`) | Turn approved lines into draft orders; close a request whose remaining lines won't be ordered. |
| *Approve purchase orders* (`purchasing.approve`) | Sign orders: by approval levels when they apply, otherwise within their personal limit as before. Restart an order's approval. |

Built-in roles: Technician raises requests; Maintenance Manager and Administrator also approve them. A requester always
sees their own requests; buyers and approvers see every request at their sites.

## Purchase requests

Purchasing › **Requests** › New request (numbered `PR-00001`; a site can have its own prefix).

- **Lines**: a part from the catalogue (with the store it should arrive in) or free text for services and non-stock
  items; quantity; needed-by date; the work order it's for (a work order fills in its asset); an optional estimated price
  in the base currency; a reason.
- **Raise one from**: a work order's planned parts (**Request purchase** when anything is short), Inventory › Short for
  work (select lines), and the Re-order tab (for people who may ask but not order). Several sites make one request each.
- **Statuses**: Draft → Waiting approval → Approved → Partly ordered → Ordered. *Returned* goes back to the requester to
  fix and submit again; *Cancelled* needs a reason and is only possible before any line is ordered; *Closed* when the
  buyer decides the remaining lines won't be ordered.
- **Comments and files** on every request.

## Requests into orders

On an approved request, **Order lines** — or, for lines from several requests at once, Purchasing › Requests › **Lines to
order** — asks for a supplier per line (the part's preferred supplier by default), the
quantity and the price (the estimate, else the part's last purchase price). CMX makes **one draft order per supplier and
site**; each order line keeps its request line, and the order's notes name the requests. Orders made this way are Parts
orders when a part is on them, else Services.

- When a draft order is deleted, a line is removed from it, or the order is cancelled, those request lines are back to
  order and the request's status follows.
- Parts ordered for a work order are issued straight to it when they're received (as before).
- The requester is told when their lines are ordered and when they arrive.

## Order types

Settings › Purchasing › **Order types**: Parts, Services and Capital to start with (an editable list). Each order has a
type — new orders default to Parts — shown and filterable on the order list.

## Approval levels

Settings › Purchasing › **Approval levels** (*Manage settings*).

- A level has a name, a **minimum amount** (the order total including tax and shipping, in the base currency), **up to
  five approvers** with *Approve purchases*, and whether **all** of them or **any one** must sign.
- **Levels add up**: an order needs every level whose minimum it reaches, signed **lowest first**. With levels at 0,
  10,000 and 100,000, a 50,000 order needs the first two; a 200,000 order all three.
- **Sets and types — the most specific levels win**: the site's levels for the order's type, else the site's *all types*
  levels, else the organization's levels for the type, else the organization's *all types* levels. A site that adds only a
  Capital rule keeps the organization's levels for its Parts and Services orders.
- **No level applies** (none set, or the order is below every minimum): one approver within their personal limit, as
  before 0.16.0.
- The settings page warns about approvers who can't sign (no permission, can't see the sites, inactive) and shows what
  an amount would need ("An order of 50,000 needs: Maintenance manager → Plant manager.").

**Signing an order**

- Submitting copies the levels the order needs onto it. Submit is refused when a level has nobody who could sign for the
  order's site (the message names the level).
- Each approver of the current level is e-mailed when it's their turn. The person who submitted the order can't sign it.
- Approving from Draft isn't offered when levels apply — submit first.
- Returning (with a reason) at any level sends the order back to Draft; submitting again starts from the first level, and
  the earlier round stays visible on the order.
- When nobody listed on the waiting level can sign any more (someone left or lost access), the order says so and
  **Restart approval** reads the levels again.
- Two people signing at the same moment are taken one after the other: only one signature counts on an *any one* level.
- The order page shows every level, who signed when and with what comment; the printed order lists the signatures.
  Orders already waiting when 0.16.0 arrived finish under the old rule.

## My approvals

**My approvals** (menu, and a count in the bell): requests and orders waiting for my signature — including orders I may
approve within my limit when no level applies — with who signed before and who's next, and what was sent back to me.
Approve, or return with a reason, right there. Only sites I can see are counted.

## E-mails

Settings › Notifications › purchasing switches (on by default; everyone can opt out personally):

| Switch | Goes to |
| --- | --- |
| Purchase request submitted | The site's purchase-request recipients (Site settings), else the organization's (Settings › Purchasing), else everyone who may approve requests at the site — plus anyone named on the request. |
| Purchase request approved / returned | The requester (with the reason when returned). |
| Purchase request ordered / items arrived | The requester. |
| Order waiting for my signature | The approvers of the level whose turn it is. |

Links open the request or order in CMX; signing in is needed (approving straight from an e-mail isn't possible).

## Not in 0.16.0

Raising requests from the phone app; stand-ins for approvers who are away; cost centres and budgets (0.19.0); import
sheets for requests, order types or levels; supplier catalogue prices and quotes (0.17.0).
