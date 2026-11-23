# CMX 0.16.0 — Purchase requests and approval levels

Roadmap release 0.16.0 (DimoMaint review, plan version 5). Status: design for approval.

## Why

Technicians and store keepers can't ask for something to be bought without writing a purchase order themselves, and one
approval limit per person doesn't match how finance signs off large orders: a bigger order collects more signatures, in
order, lowest level first. DimoMaint has purchase requests that turn into orders, signature levels by amount with up to
five signatories, a list of orders to sign, and (Q4 2026) signatures by order type.

## Goals

1. **Purchase requests**: anyone allowed asks for parts or services; one person approves or rejects ("do we need this?").
2. **Requests into orders**: buyers turn approved request lines into draft orders grouped by supplier; every order line
   remembers its request line; the requester hears when it is ordered and when it arrives.
3. **Approval levels on orders** ("can we afford this?"): levels by minimum amount, cumulative, each with up to five named
   approvers and an all-or-any rule; organization-wide, replaceable per site; optionally per order type.
4. **My approvals**: one place listing what waits for my signature; approve, or reject with a comment; e-mail when it's
   my turn; the approval history on the order and on its print.

## Non-goals (0.16.0)

- Raising purchase requests from the phone app (a later release).
- Stand-ins for approvers who are away (the *any one* rule covers most cases).
- Cost centres and budgets (0.19.0) — so "a work order fills in the order's cost centre" waits until then.
- Import sheets for requests, order types or levels.
- Approving from an e-mail link without signing in.
- Supplier catalogue prices, quotes and tenders (0.17.0 contracts and contractors).

## Anti-goals (a failure even if it works)

- **Organizations that set nothing see a change.** With no levels set, ordering works exactly as today.
- **An order waits forever with nobody able to act.** Every waiting order must have someone who can sign, reject or
  restart it, and the screen must say who.
- **Silent no-ops** (a lesson from DimoMaint): a refused action says why; nothing is skipped quietly.
- **Approval rules in two places that disagree**: the same checks apply on every screen, list, e-mail and count.

## Decisions taken with the user

- Purchase requests get their **own one-step approval** before ordering; amount levels apply to **orders only**.
- Levels **add up**: an order needs every level whose minimum it reaches, signed lowest first.
- Everything else: "go with your picks" — the calls below marked *(my call)*.

## Approaches considered

1. **A shared approval engine** that requests, orders and later contracts and budgets all use. Most reusable, but built
   ahead of need; 0.17.0 and 0.19.0 may want something different.
2. **Purchasing's own tables (chosen)**: approval levels and order types as settings; when an order is submitted, a copy of
   the levels and approvers it needs is stored on the order as rows, with signatures as rows. "Waiting for me" is a
   simple indexed query, and changing settings never disturbs orders in flight. A request's single approval is a few
   fields on the request.
3. **The approval copy as one JSON column on the order**: quick to write, slow to query for "waiting for me" and harder to
   keep correct under concurrent signing.

## What CMX has today (and keeps)

- Orders: Draft → Submitted → Approved → (Partly) Received → Closed, or Cancelled. Anyone with *Approve purchases*
  approves when the order's base-currency total (incl. tax and shipping) is within their personal limit (empty = no
  limit); a rejection sends it back to Draft with a reason. Approvers can approve straight from Draft.
- The Re-order tab creates draft orders grouped by supplier and site (`/purchase-orders/from-proposal`).
- Short for work lists planned parts their store can't cover; a work order's planned parts show "Short".
- An order line can name a work order: a stock part received on it is **issued straight to that work order**; a non-stock
  line becomes a cost on it. 0.16.0 reuses this as is — no new tick box *(my call; replaces the playback's tick)*.
- Per-site settings (0.9.0) store overrides as JSON on the site; site access limits every query (named `Site` filter).
- E-mails: purchasing events are on/off switches with fixed recipients (not the 0.10.0 grid); personal opt-outs apply.

## Design

### Permissions

New, in the Purchasing group:

| Code | Name | Built-in roles |
| --- | --- | --- |
| `purchasing.request` | Raise purchase requests | Administrator, Maintenance Manager, Technician |
| `purchasing.approveRequests` | Approve purchase requests | Administrator, Maintenance Manager |

The migration adds them to existing organizations' built-in roles of those names. `purchasing.approve` keeps its name;
its description becomes "Approve purchase orders (by approval levels, or up to the user's limit)".

Who sees requests: the requester always sees their own; anyone with `purchasing.view`, `purchasing.create` or
`purchasing.approveRequests` sees every request at their sites. Site access applies to requests like orders.

### Purchase requests

**Record** `PurchaseRequest` (site-owned, numbered `PR-00001`; a site's own prefix like the other numbers):

- Site, requested by (user, name), needed-by date (default for lines), reason/notes, extra recipients (users).
- Status: **Draft → Submitted → Approved → Partly ordered → Ordered**, or **Rejected** (back to editable, with the
  reason) or **Cancelled** (with a reason). **Closed** when the buyer decides the remaining lines won't be ordered.
- Decision: decided at, by (user, name), rejection reason. Submitted at, cancelled at.

**Line** `PurchaseRequestLine`: line number; a part **or** free-text description (services, non-stock); unit; quantity
(> 0); needed-by date; a work order or an asset (optional; a work order implies its asset); the store it should arrive in
(parts; default the site's default store); estimated unit price (optional, base currency); reason (optional).

**Rules**

- Editable in Draft and Rejected; submit needs at least one line, every part line a store, every quantity > 0.
- Approve / reject: a user with `purchasing.approveRequests` and access to the site, **not the requester** *(my call)*.
  Reject needs a reason. A rejected request returns to the requester to fix and submit again (status Rejected, editable;
  Submit moves it to Submitted).
- Cancel: the requester or anyone who may approve requests, while no line is ordered; needs a reason.
- Approved requests can't be edited. Lines are ordered whole: one live order line per request line *(my call)* — the buyer
  may still change the quantity on the draft order.
- Status follows the lines: some ordered → Partly ordered; all ordered → Ordered. When an order line made from a request
  line is deleted (draft deleted) or its order cancelled, the request line becomes "to order" again and the status is
  recomputed. **Close** (buyer) on Approved / Partly ordered: the rest won't be ordered → Closed.
- Comments thread (like request comments) and files (attachments with entity type `PurchaseRequest`).

**Where requests are raised**

- Purchasing › **Requests** tab › New request.
- A work order's planned parts: **Request purchase** when any line is short → a new request filled with the short lines
  (part, short quantity, store, the work order, needed-by = the work order's due date).
- Inventory › Short for work: select lines → **Request purchase** → one request per site.
- Purchasing › Re-order: users who may raise requests but not create orders get **Request** instead of *Create orders*
  → one request per site with the selected lines (estimated price = the proposal's price).

**Notifications** (new purchasing switches, on by default; e-mail plus the alert bell's count):

- Submitted → the site's purchase-request recipients (new per-site and organization setting, like request recipients),
  else everyone with `purchasing.approveRequests` at the site; plus the request's extra recipients.
- Approved / rejected → the requester.
- Ordered (each time lines are put on orders) → the requester, listing the order numbers.
- Received (lines from the request received) → the requester.

### Requests into orders

- Purchasing › Requests: approved lines show **Order**. The buyer (`purchasing.create`) picks lines, a supplier per line
  (default the part's preferred supplier; free-text lines must choose), quantity and price (default the estimated price,
  else the part's last purchase price, else its average cost).
- CMX creates **draft orders grouped by supplier and site**, as the Re-order tab does. Each order line gets the request
  line's part or description, unit, store, work order and a link `PurchaseRequestLineId`; the order's *Requested by*
  lists the requesters; its notes list the request numbers.
- The order page shows each line's request number; the request page shows each line's order number, ordered and received
  quantities.

### Order types

- Editable list (Settings › Purchasing › Order types), code + name + active, starting with **Parts**, **Services**,
  **Capital** for new and existing organizations.
- Each order has a type; new orders default to Parts *(my call)*; existing orders become Parts. Shown and filterable on
  the order list, on the order and on its print.

### Approval levels

**Settings › Purchasing › Approval levels** (`settings.manage`):

- A **set** is the organization's, or a site's own. A site with its own set uses only its set; otherwise the
  organization's applies.
- A **level** in a set: name (e.g. "Maintenance manager"), **minimum amount** (base currency, ≥ 0), order type (one type
  or *all types*), **1–5 approvers** (users who have `purchasing.approve` and access to the site or, for the organization's
  set, to the sites it applies to — saving warns about any who don't), and the rule **all must sign** or **any one**.
- Minimums are unique per set and type. The settings page explains the levels with an example sentence ("an order of
  50,000 needs: Maintenance manager, then Plant manager").

**Which levels an order needs** (computed when it's submitted):

1. The set: the order's site's own set if it has one, else the organization's.
2. Within the set: the levels for the order's type if any exist for that type, else the *all types* levels.
3. Required: every level whose minimum ≤ the order's base-currency total, in ascending minimum.
4. **None required** (no set, or the order is below every minimum) → **today's rule**: one approver within their
  personal limit. So small orders below the first minimum still use personal limits.

**Signing**

- On submit with levels: the required levels are copied to the order as approval steps (level name, minimum, rule,
  approvers) for this **round**; the order is Submitted and step 1 is waiting. Approving straight from Draft isn't
  offered when levels apply.
- Approve: only someone listed on the **current** step, who still has `purchasing.approve` and access to the site, has not
  signed this step, and **did not submit the order** *(my call)*. Their signature is recorded. The step is done when any
  one signed (*any*) or all listed signed (*all*); then the next step waits and its approvers are e-mailed. The last step
  makes the order **Approved** (approved at / by = the last signer). Personal limits don't apply to such orders.
- Reject: anyone on the current step, with a comment → the order returns to Draft; signatures stay as history. Submitting
  again starts a new round from step 1 with the levels as they are then.
- **Restart approval** (`purchasing.approve`, order Submitted with steps): recomputes the steps from the current settings
  and starts a new round — for when an approver left or settings changed. The order page says who can sign now and, when
  nobody on the current step still can, says so and offers Restart to those allowed.
- Concurrency: signing happens in a transaction that locks the order row; two people signing an *any* step at once → one
  signature counts, the other sees "already signed off".
- Orders already Submitted when 0.16.0 deploys have no steps and finish under today's rule.

**Order page**: an approval panel — each step with its name, minimum, rule, who is listed, who signed (with time and
comment) and which step is waiting; earlier rounds folded underneath. **Print**: the last round's signatures (level,
name, date, comment) replace the single "Approved by" line; under today's rule the single line stays.

### My approvals

- A page **My approvals** (menu under Materials, for anyone who may approve requests or orders) with two lists:
  - **Requests** waiting: Submitted, at my sites, not mine, and I may approve requests.
  - **Orders** waiting: Submitted with steps where I'm listed on the current step and haven't signed; and, under
    today's rule, Submitted orders I may approve within my limit *(my call)*.
  - Columns: number, site, requester/creator, amount (orders), needed-by (requests), the level, who signed before, who's
    next, waiting since. Rejected items I raised show their reason in a third list, **Sent back to me**.
- Approve, or reject with a comment, one item at a time *(my call; no bulk approve for money)*.
- The alert bell gains **Waiting for my approval** (requests + orders), linking to the page.
- **E-mail when it's my turn** (new switch "Order waiting for approval"): to the current step's approvers on submit and
  when a step completes. Links open the order or request in CMX; signing in is required *(my call)*. Under today's rule
  the existing "Purchase order submitted" e-mail stays as is.

### Screens touched

- Purchasing: new **Requests** tab (list with filters status, site, mine, waiting for me; export like other lists),
  request form and request page; order form (type), order page (approval panel, request numbers on lines), order print.
- Purchasing menu entry visible to `purchasing.request` holders too (they see only the Requests tab and Re-order).
- Work order planned parts (Request purchase); Inventory › Short for work (Request purchase).
- Settings: new **Purchasing** tab (order types, approval levels, purchase-request recipients); site settings gain
  purchase-request recipients and the PR number prefix; Organization settings gain the PR prefix; Notifications gain the
  new purchasing switches.
- Alert bell; new My approvals page.
- Arabic for every new text, server messages through the translator, plurals in full.

### Data (one migration, `PurchaseRequests`)

- New: `PurchaseRequests`, `PurchaseRequestLines`, `PurchaseRequestComments`, `OrderTypes`, `ApprovalLevels`
  (set = nullable SiteId, OrderTypeId nullable, Name, MinAmount, Rule, ApproverIds JSON), `PurchaseOrderApprovalSteps`
  (order, round, step, name, minimum, rule, approver ids, status, done at), `PurchaseOrderSignatures` (order, round,
  step, user, name, approved, comment, at).
- Changed: `PurchaseOrders` + `OrderTypeId`, `ApprovalRound`; `PurchaseOrderLines` + `PurchaseRequestLineId`;
  `SiteSettings` + `PurchaseRequestRecipientIds`, `PurchaseRequestNumberPrefix`; tenant settings + the same two plus a
  default-prefix `PR-`; `NumberKind.PurchaseRequest`.
- Data: seed the three order types for every existing organization and set existing orders to Parts; add the two
  permissions to existing built-in roles.

### Builder's questions, settled *(my calls)*

- **Nobody able to sign at submit**: if a required level has nobody listed who can sign orders for the order's site
  today, submit is refused naming the level ("Level 'Plant manager' has nobody who can sign orders for Cairo Plant. Ask
  an administrator to fix the approval levels."). An approver who loses access later is the Restart case.
- **Submitter rule only with levels**: under today's rule the person who submitted may still approve within their limit,
  as now; nothing changes for organizations without levels.
- **Order type missing**: an order's type is optional in the data; the form preselects the PARTS type when it exists and
  is active. An order without a type uses the *all types* levels.
- **Work order on a request line** must be at the request's site (as on order lines); a work order fills in its asset.
- **Needed-by dates** may be any date; lists sort by them and show late ones in red.
- **Delete**: a Draft request can be deleted by its requester; anything later is cancelled instead.
- **Prices**: estimated prices are in the base currency. When orders are made from requests for a supplier in another
  currency, the price is carried over unconverted, exactly as the Re-order tab does today, for the buyer to adjust.
- **Requests list**: filters and export like the other lists; no saved views or column chooser this release.
- **Request number on orders**: the order's notes say "From PR-00012, PR-00015"; each line shows its own request number.

## Review focus (most likely to bite)

1. An approver who lost the permission, site access or account while listed on a waiting step — the order must show it
   and Restart must fix it; nobody unlisted may sign.
2. Two signatures at once on an *any* step, or the last signature racing a reject.
3. Totals in a foreign currency against base-currency minimums; an order exactly at a minimum (≥ counts).
4. A request line whose order is cancelled or whose draft is deleted must come back as "to order" and the request status
   must follow — including Partly ordered ↔ Ordered.
5. Site access: requests, My approvals, the bell count and e-mails only for sites the person may see; a site's own set
   fully replaces the organization's.

## Left to the builder

Endpoint shapes, component structure, query details, test layout, exact wording beyond what's above (plain English and
Arabic, same tone as 0.15.0).
