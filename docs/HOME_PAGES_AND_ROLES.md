# Home pages and roles (0.22.0)

Each role can open CMX on what it needs, and everyone can keep an eye on the numbers they care about:

- **a home page per role**: an administrator picks and orders the role's cards. Everyone with that role sees the same layout;
- **counters**: anyone can pin their saved views to the home page, each showing how many records match right now;
- **shortcut tiles**: coloured tiles that open CMX pages, set per role;
- **a tidier menu per role**: menu items a role doesn't need can be hidden;
- **default columns per role** on the lists, and a **Duplicate** button for roles.

A role with no home page keeps today's dashboard, so nothing changes until an administrator sets one up.

## Who may do what

| Permission | Can |
| --- | --- |
| *Manage users* | Set up roles: permissions, home page, menu, default columns; duplicate roles. |
| Anyone signed in | Pin their own saved views to their home page; reset their own columns on a list. |

There are no new permissions. A card only shows to people whose permissions let them see what it shows.

## A role's home page

**Settings › Roles**, open a role, **Home page** tab.

- **Add card** offers the cards in four groups:
  - *Dashboard pieces*: work counters, work by site, urgent work, asset counters, assets by status, by category and by site,
    warranties ending soon, recently added assets (the same pieces as today's dashboard);
  - *Work*: my work, unassigned work (open work with no person assigned, even if a team is set), late work;
  - *Assets and stock*: assets down now, parts below their reorder level;
  - *Tools*: waiting for my approval, report a problem, the location tree, shortcut tiles.
- Each card can be on the page once. Move cards up and down, or remove them.
- **Start from today's dashboard** adds the nine dashboard pieces in today's order. The home page puts wide cards on their own
  row and the others two by two, so the result looks close to today's dashboard, not identical.
- A card the role's permissions don't cover is marked: *People with only this role won't see this card: it needs …*.

The list cards (my work, unassigned, late, assets down, low stock) show the list's total and its first rows. Their number is
always the same as the list's total, and **View all** opens that list.

The **location tree** card lets people pick a site or location and then report a problem, create a work order or add an asset
there, or open it, each button showing only to people allowed to do it.

### Shortcut tiles

The **Shortcut tiles** card holds up to 12 tiles. Each has a label (and an Arabic label, shown to people using CMX in Arabic), a
colour and a link. Pick a page from the list or type any CMX address starting with `/`, such as `/work-orders?view=mine`.
Links to other websites aren't allowed.

## People with several roles

- They see the cards of all their roles, roles in name order, without repeats. Shortcut tiles are combined; a tile whose link
  is already shown is left out.
- A menu item is hidden only when **every** one of their roles that can open it hides it.
- For default columns, the first of their roles (by name) that has a default for that list counts.

## Hiding menu items

**Menu** tab: untick the items people with this role don't need. Home and Settings can't be hidden.

Hiding a menu item only tidies the menu. People can still open the page from a link if their permissions allow it. To stop
access, change the role's permissions.

## Counters

On any list with saved views (work orders, requests, PM schedules, parts, purchase orders, assets), open the view and choose
**Show on home page** in the views menu. It appears under **My counters** at the top of the home page, above the role's cards or
today's dashboard.

- Each tile shows the list, the count and the view's name, and opens the list with that view. Counts refresh every minute.
- A tile's menu moves it earlier or later, or removes it from the home page.
- Up to 24 views can be pinned.
- A tile for a list the person can no longer open says *No access*.

## Default columns for a role

On a list, set up the columns as the role should see them, then in the column menu choose **Use these columns for a role…**
and pick the role. People with that role see those columns on that list until they pick their own: changing columns, or
applying one of their saved views, counts as picking their own.

If some people with that role have already changed their columns on that list, the window says how many and offers to reset
theirs too (off unless ticked).

**Reset columns** in the column menu removes a person's own choice, so their role's default (or the list's own) applies again.
The role window's **Columns** tab lists the lists the role has defaults on, each with **Clear**.

## Duplicate role

The copy button in the roles table creates *‹name› (copy)* with the same permissions, home page, hidden menu items and default
columns, and nobody in it. A copy of Administrator is an ordinary role with every permission.

## What stays the same

- Permissions decide what people can see and do, as before.
- The phone (technician view) keeps its own start screen.
