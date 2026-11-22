# Sites and access (0.9.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Limit each signed-in person to their sites or branches of the location tree everywhere, and add per-site settings, a site switch with per-site dashboard counters, teams, and per-person working hours.

**Architecture:**
- A middleware works out each request's scope (site ids, branch paths, optionally narrowed by the `X-Cmx-Site` header) and exposes it through `ITenantContext`.
- `CmmsDbContext` adds a named "Site" query filter beside the existing organization filter, so every read is limited in one place. Numbering and uniqueness checks look past it with `AllSites()`.
- Per-site settings live as JSON on `Site`. Teams are a new entity. Working hours are JSON lists on the organization, site and employee.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10.0.14 (named query filters) on SQL Server, xUnit (`SkippableFact` + `ApiFixture`); React 19, Mantine 8, TanStack Query, i18next, Vitest.

**Spec:** `docs/superpowers/specs/2026-11-22-sites-access-0.9.0-design.md`

**Commands:** `B=/private/tmp/claude-501/-Users-mohamedfahmy-Library-CloudStorage-GoogleDrive-engfahmy-gmail-com-My-Drive-01-CMMS-1000-CMX/3981ff7e-d5cc-4bc4-ab6b-ddb873a8b5d0/scratchpad/bin`
- Server tests: `$B/wt-test "FullyQualifiedName~<Class>"`, or `$B/wt-test` for all of them.
- Web: `$B/wt-vitest`, `$B/wt-web`.
- Migration: `$B/wt-ef <Name>`.

All of these point at `.worktrees/sites-access-0.9.0`. The baseline is 391 passed, 2 skipped.

## Global Constraints

- **One migration,** `SitesAndAccess`, added in Task 1 with every schema change of the release. It is additive only, and existing rows mean "no limit".
- **Nothing changes for organizations without limits.** No list means every site; a site setting left empty means the organization's value.
- **Shared catalogues stay shared by all sites:** parts, suppliers, PM task library, trades, failure codes, categories, custom fields.
- **Translations:** every new text goes in `web/src/locales/en.json` and `ar.json`, and every server message goes through `Translator.T` with an entry in `server/FourC.Cmms.Api/Localization/ar.json`. Use `$B/ar-add.py` to append.
- **Records outside the scope answer 404,** never 403, so their existence doesn't leak.
- **Background work stays unlimited.** The PM generator, e-mail sender, due-date backfill, housekeeping and tools have no user, so `SiteIds` is null.
- **Commits** end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Lost branches.** A person whose branches were all deleted must see nothing, never everything. Test: `A_user_whose_branches_are_gone_sees_nothing` (Task 2).
2. **Access changes apply at once.** Changing a person's sites takes effect on their next request, without signing out. Test: `Changing_access_applies_at_once` (Task 1).
3. **Background jobs.** PM generation still creates work for every site while limited users exist. Test: `Pm_generation_is_not_limited_by_any_user` (Task 1).
4. **Hidden-site filters.** A list URL filtered to a hidden site's id (a saved view, a pasted link) answers an empty list, not an error or the other site's rows. Test: `A_list_filtered_to_a_hidden_site_is_empty` (Task 3).
5. **Clashes with hidden records.** A code or number used in a hidden site is refused with a message, not a 500. Test: `Codes_stay_unique_across_sites_the_user_cannot_see` (Task 1).

---

### Task 1: Site limits on the server (scope, filter, users)

**Files:**
- Modify:
  - `server/FourC.Cmms.Core/Common/Entity.cs` (`ITenantContext` members);
  - `server/FourC.Cmms.Core/Identity/Identity.cs` (`AppUser.SiteIds`, `AppUser.LocationIds`);
  - `server/FourC.Cmms.Core/Locations/Locations.cs` (`Site.Settings`);
  - `server/FourC.Cmms.Core/People/People.cs` (`Team`, `Employee.WorkingHours`);
  - `server/FourC.Cmms.Core/Work/Work.cs` (`WorkOrder.TeamId`);
  - `server/FourC.Cmms.Core/Pm/Pm.cs` (`PmSchedule.TeamId`);
  - `server/FourC.Cmms.Core/Notifications/OutboundEmail.cs` (`FromName`);
  - `server/FourC.Cmms.Core/Tenancy/Tenant.cs` (`WorkingHours`);
  - `server/FourC.Cmms.Core/Data/CmmsDbContext.cs`;
  - `server/FourC.Cmms.Api/Infrastructure/Infrastructure.cs` (`HttpTenantContext`);
  - `server/FourC.Cmms.Api/Program.cs`;
  - `server/FourC.Cmms.Api/Features/AdminEndpoints.cs` (users);
  - `server/FourC.Cmms.Api/Features/AuthEndpoints.cs` (`/me`);
  - `server/FourC.Cmms.Api/Features/SupportEndpoints.cs` (attachments, audit);
  - the four `Numbering.NextAsync` callers, plus every code-uniqueness check on a site-owned entity: site code, location code, asset number, store code, PM schedule code.
- Create:
  - `server/FourC.Cmms.Core/Data/QueryScope.cs`;
  - `server/FourC.Cmms.Core/People/WorkingHours.cs` (only the `WorkingDay` record here; Task 6 adds the logic);
  - `server/FourC.Cmms.Api/Infrastructure/Access.cs`;
  - migration `SitesAndAccess`.
- Test: `server/FourC.Cmms.Tests/SiteAccessTests.cs`; `server/FourC.Cmms.Tests/AccessScopeTests.cs` (pure).

**Interfaces:**
- Produces, in Core:
  - `ITenantContext.SiteIds : IReadOnlyList<Guid>?` and `ITenantContext.BranchPaths : IReadOnlyList<string>?` are default interface members returning null. Invariant: `BranchPaths != null` ⇒ `SiteIds != null`.
  - `CmmsDbContext.TenantFilter = "Tenant"`, `SiteFilter = "Site"`, `CurrentSiteIds`, `CurrentBranchPaths`.
  - `QueryScope.AllSites<T>(this IQueryable<T>)` = `IgnoreQueryFilters([CmmsDbContext.SiteFilter])`.
- Produces, data model:
  - `AppUser.SiteIds`, `AppUser.LocationIds` (both `List<Guid>`, default `[]`).
  - `SiteSettings` (in `Locations.cs`) with:
    - `AssetNumberPrefix`, `WorkOrderNumberPrefix`, `WorkRequestNumberPrefix`, `PurchaseOrderNumberPrefix` (`string?`);
    - `WorkingHours` (`List<WorkingDay>?`);
    - `DefaultStoreId` (`Guid?`);
    - `RequestRecipientIds` (`List<Guid>`, default `[]`);
    - `EmailSenderName` (`string?`).
  - `Site.Settings : SiteSettings` (JSON).
  - `WorkingDay(DayOfWeek Day, string Start, string End)` with "HH:mm" strings.
  - `Team : TenantEntity` with `Code`, `Name`, `Guid? SiteId`, `Site?`, `string? Color`, `List<Guid> MemberIds`, `bool IsActive`.
  - `WorkOrder.TeamId`, `PmSchedule.TeamId` (`Guid?`, FK restrict); `Employee.WorkingHours : List<WorkingDay>?` (JSON).
  - `TenantSettings.WorkingHours : List<WorkingDay>` (default `[]`); `OutboundEmail.FromName : string?` (max 200).
- Produces, in Api:
  - `UserAccess(IReadOnlyList<Guid>? SiteIds, IReadOnlyList<AccessBranch>? Branches)` and `AccessBranch(Guid SiteId, string Path)`.
  - `AccessScope(IReadOnlyList<Guid>? SiteIds, IReadOnlyList<string>? BranchPaths, Guid? NarrowedTo)`.
  - `static AccessScope Access.Narrow(UserAccess access, Guid? requestedSite)` and `static bool Access.Covers(UserAccess access, Guid siteId, string? locationPath)`.
  - `AccessService.GetAsync(Guid userId, CancellationToken) : Task<UserAccess>` (cached 60 s); `AccessService.Invalidate(Guid userId)`.
  - `app.UseSiteAccess()` (after `UseAuthentication`); `Access.SiteHeader = "X-Cmx-Site"`.
- Produces, endpoints:
  - `UserInput` gains `List<Guid>? SiteIds = null, List<Guid>? LocationIds = null` (null keeps the current value). GET `/users` returns `siteIds` and `locationIds`.
  - `/auth/me` gains `access: { limited: bool, sites: [{ id, code, name }], siteId: Guid? }`. `sites` holds the person's whole scope, ignoring the header; `siteId` is the site the request was narrowed to.

- [ ] **Step 1: Write the failing tests.** Fixture: organization with sites A and B, each with a location, an asset, a request, a work order, a PM schedule, a store and a PO. A "Maintenance Manager" user is limited to A.
  - `A_user_with_no_sites_sees_every_site`: the admin's `/api/sites` has 2 rows.
  - `A_site_limited_user_sees_only_their_sites_records`: the limited user's lists of sites, locations, assets, work-requests, work-orders, pm/schedules, stores and purchase-orders each hold only A's rows.
  - `Another_sites_record_answers_not_found`: these answer 404 for B's records:
    - GET asset, work order and PO by id;
    - GET `/api/attachments?entityType=Asset&entityId=<B asset>`;
    - GET `/api/audit?entityType=WorkOrder&entityId=<B wo>`;
    - GET `/api/attachments/<id>/content` for a file on B's asset.
  - `Reports_and_exports_count_only_visible_sites`: for the limited user, dashboard `totals.sites == 1` and `work.open` counts A only. The work-order export has A's rows only, and the alert bell's late-work count counts A only.
  - `Child_rows_follow_their_parent`: labour logged on B's work order is missing from the limited user's labour report or cost rollup, and B's store stock is missing from the part's stock list.
  - `A_limited_user_cannot_create_in_another_site`: POST work order with B's site answers 400 with a `siteId` error.
  - `Codes_stay_unique_across_sites_the_user_cannot_see`: the limited user creates an asset whose number is B's asset number. Expect 400, message "This asset number is already used in a site you can't see.", not 500.
  - `A_limited_admin_can_only_give_access_inside_their_own`: an Administrator limited to A tries three saves:
    - saving a user with `siteIds: []` → 400 `siteIds` "Choose at least one of your sites.";
    - saving with `[B]` → 400;
    - saving an unrelated field on a user who already has `[B]` → 200.
  - `Changing_access_applies_at_once`: the admin moves the limited user from A to B; the user's next `/api/assets` shows B's asset.
  - `Pm_generation_is_not_limited_by_any_user`: with the limited user existing, `PmGenerator` (run as the existing tests do) creates work for B's schedule too.
  - `Me_returns_the_access`: the limited user's `/auth/me` has `access.limited == true` and `access.sites` = [A].
  - `AccessScopeTests` (no database):
    - `Narrow_keeps_everything_without_a_header`;
    - `Narrow_to_a_site_in_scope`;
    - `Narrow_ignores_a_site_outside_scope`;
    - `Narrow_with_branches_keeps_only_that_sites_branches`;
    - `Covers_checks_site_and_branch_path`.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~SiteAccessTests|FullyQualifiedName~AccessScopeTests"`. Expected: compile errors, then failures.
- [ ] **Step 3: Implement the model, the migration (`$B/wt-ef SitesAndAccess`) and the filters.**
  - Name the existing tenant filter "Tenant".
  - Add named "Site" filters:
    - `Site`: `CurrentSiteIds == null || CurrentSiteIds.Contains(s.Id)`.
    - `Location`: site rule, plus `CurrentBranchPaths == null || CurrentBranchPaths.Any(p => l.Path.StartsWith(p))`.
    - `Asset`, `WorkRequest`, `WorkOrder`, `PmSchedule`: site rule, plus `CurrentBranchPaths == null || (e.LocationId != null && CurrentBranchPaths.Any(p => e.Location!.Path.StartsWith(p)))`.
    - `Store` and `PurchaseOrder`: site rule only.
    - `Team`: `CurrentSiteIds == null || t.SiteId == null || CurrentSiteIds.Contains(t.SiteId.Value)`.
    - Children: `CurrentSiteIds == null || <Parent>s.Any(p => p.Id == x.<Parent>Id)`, on:
      - work-order children: `WorkOrderLabor`, `WorkOrderPart`, `WorkOrderCost`, `WorkOrderComment`, `WorkOrderStatusChange`, `WorkOrderTask`, `WorkOrderAssignee`, `WorkOrderAsset`;
      - store children: `StockLevel`, `StockTransaction` (`StoreId`);
      - asset children: `Meter` (asset; skip if meters can exist without an asset, and note it), `MeterReading` (via meter), `DowntimeEvent`, `AssetPart`, `AssetTransfer`;
      - schedule children: `PmScheduleTask`, `PmScheduleAsset`;
      - `PurchaseOrderLine`.
  - Check the SQL for one site query and one child query with `ToQueryString()` in a scratch test, and confirm parameter collections translate. If EF expands `Any` over a parameter list badly, use `EF.Parameter(...)`. Record the result as a ledger note.
- [ ] **Step 4: Implement `Access.cs` and wire it.**
  - `AccessService.GetAsync` reads the user with no filters.
  - With `LocationIds` set, branches = those locations (not deleted) with their site and `Path`, and `SiteIds` = their distinct sites. An empty result stays an empty list, which means nothing is visible.
  - The middleware stores `Access.Narrow(access, header)` in `HttpContext.Items["cmx.scope"]`. `HttpTenantContext` exposes it.
  - Users PUT/POST validate the new lists: sites and locations must exist, and a limited editor stays inside their own scope. They then call `AccessService.Invalidate`.
  - Use `.AllSites()` in numbering `exists`/`existingWithPrefix` and in the code-uniqueness checks, with the new "used in a site you can't see" message when the clash isn't visible.
  - Attachments and audit: check the entity is visible (a switch like the one in POST `/attachments`) and answer 404 when it isn't.
- [ ] **Step 5: Run** `$B/wt-test "FullyQualifiedName~SiteAccessTests|FullyQualifiedName~AccessScopeTests"`, then `$B/wt-test`. Expected: all pass (391 earlier tests still green).
- [ ] **Step 6: Commit** "Sites: limit people to their sites everywhere (scope, filter, users)".

### Task 2: Limits by branch of the location tree

**Files:**
- Modify: the asset, work-request, work-order and PM schedule input validation (`AssetEndpoints.cs`, `WorkRequestEndpoints.cs`, `WorkOrderEndpoints.cs`, `PmEndpoints.cs`), and the phone's `request.create` in `TechActions.cs`.
- Test: `server/FourC.Cmms.Tests/BranchAccessTests.cs`.

**Interfaces:** consumes Task 1's `ITenantContext.BranchPaths`, `UserAccess`, and the `LocationIds` field on users.

- [ ] **Step 1: Write the failing tests.** Fixture: site A has building B1 (floor F1, room R1) and building B2. Each place has an asset and a work order; there is one site-level work order with no location. The user is limited to `locationIds: [B1]`.
  - `A_branch_limited_user_sees_only_their_branches`: locations = B1, F1, R1; assets and work orders are those under B1; sites = [A].
  - `New_locations_under_a_branch_are_included`: the admin adds room R2 under F1, and the user's locations include R2.
  - `Site_level_work_is_hidden_from_branch_limited_users`: the site-level work order answers 404.
  - `Branch_limited_users_must_choose_a_location`: POST work order with no `locationId` → 400, `locationId` "Choose a location in your part of the site." With R1 → 200; with B2 → 400.
  - `A_user_whose_branches_are_gone_sees_nothing`: the admin deletes B1 (after moving or deleting its children the way the app requires). The user's sites, locations and work orders are all empty.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~BranchAccessTests"`. Expected: the validation and gone-branch tests fail.
- [ ] **Step 3: Implement** a shared `Access.RequireLocation(ITenantContext ctx, Guid? locationId, Validator v)`, which adds the `locationId` error when `ctx.BranchPaths != null && locationId == null`. Call it from the four validators and from `request.create`.
- [ ] **Step 4: Run** the class, then `$B/wt-test`. Expected: all pass.
- [ ] **Step 5: Commit** "Sites: limit people to branches of the location tree".

### Task 3: Site switch and per-site counters

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/AdminEndpoints.cs` (dashboard).
- Test: `server/FourC.Cmms.Tests/SiteSwitchTests.cs`.

**Interfaces:**
- Consumes: `Access.SiteHeader`.
- Produces: dashboard `bySiteWork: [{ siteId, name, open, late, completedLast30, requestsWaiting }]`. It is present only when more than one site is visible after narrowing, and otherwise an empty list.

- [ ] **Step 1: Write the failing tests.**
  - `The_site_header_narrows_to_one_site`: the admin with header A sees only A's assets, and `/auth/me` `access.siteId == A` while `access.sites` still lists A and B.
  - `A_site_header_outside_the_scope_is_ignored`: a user limited to A sends header B and sees A's rows, with `access.siteId == null`.
  - `A_list_filtered_to_a_hidden_site_is_empty`: the A-limited user's GET `/api/work-orders?siteId=<B>` answers 200 with `total == 0`.
  - `Dashboard_counts_work_per_site`: the admin's dashboard has two `bySiteWork` rows with the expected open, late, completed and waiting counts. With header A it is empty.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~SiteSwitchTests"`. Expected: the dashboard test fails (the others may already pass from Task 1; keep them as guards).
- [ ] **Step 3: Implement** `bySiteWork` with grouped counts. Use the same rules as the existing cards: `SlaQueries.LateWork`, `CompletedAt >= now-30d`, requests with `New` status.
- [ ] **Step 4: Run** the class, then `$B/wt-test`. Expected: all pass.
- [ ] **Step 5: Commit** "Sites: site switch header and per-site dashboard counters".

### Task 4: Per-site settings

**Files:**
- Modify:
  - `server/FourC.Cmms.Api/Features/LocationEndpoints.cs` (`SiteInput.Settings`, validation, GET returns settings);
  - `server/FourC.Cmms.Core/Services/Numbering.cs`;
  - the four numbering callers and the asset numbering;
  - `server/FourC.Cmms.Api/Features/Planning.cs` (`DefaultStoreAsync`);
  - `server/FourC.Cmms.Api/Features/EmailOutbox.cs`;
  - `server/FourC.Cmms.Api/Features/EmailSenderService.cs`.
- Test: `server/FourC.Cmms.Tests/SiteSettingsTests.cs`.

**Interfaces:**
- Produces, numbering:
  - `enum NumberKind { Asset, WorkOrder, WorkRequest, PurchaseOrder }`.
  - `Numbering.For(TenantSettings org, Site site, NumberKind kind) : (string Sequence, string Prefix)`. A site prefix gives sequence `"<OrgSequence>:<siteId:N>"`; org sequences stay "WorkOrder", "WorkRequest", "PurchaseOrder" and the asset one.
- Produces, e-mail:
  - `EmailOutbox.QueueAsync(..., Guid? siteId = null, string? locationPath = null)`. It drops recipients with a user account when `Access.Covers` is false for them, and sets `FromName` from the site's `EmailSenderName`.
  - `EmailSenderService` uses `row.FromName ?? orgName` in `EmailText.SenderName`.

- [ ] **Step 1: Write the failing tests.**
  - `A_site_with_its_own_prefix_numbers_in_its_own_sequence`: site A has work-order prefix "A-WO-" and B has none. Expect "A-WO-00001", "A-WO-00002" for A and "WO-00001" for B. The same check runs for requests and POs.
  - `Planned_parts_start_from_the_sites_default_store`: A has stores S1 and S2, and the default is S2. A part with stock only in S1, planned on A's work order, gets S2.
  - `New_requests_go_to_the_sites_recipients`: A's recipients are [manager1]. A request at A e-mails manager1 only, and a request at B e-mails everyone with requests.manage who can see B.
  - `E_mails_skip_people_who_cannot_see_the_site`: a manager limited to B gets nothing for A's new request.
  - `E_mails_about_a_site_use_its_sender_name`: A's sender name is "Plant A". The sent message's from-name is "Plant A via CMX".
  - `Site_settings_are_checked`: each of these answers 400 with the field named:
    - a default store from another site;
    - a recipient that isn't an active user;
    - a prefix over 20 characters;
    - working hours with end before start.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~SiteSettingsTests"`. Expected: failures.
- [ ] **Step 3: Implement.**
  - `SiteInput` gains `SiteSettings? Settings = null` (null keeps the current value).
  - `DefaultStoreAsync` returns the site's active default store first.
  - `RequestSubmittedAsync` uses the site's recipients when there are any.
  - Every `QueueAsync` call about a site record passes its site id and location path: work orders, requests, POs and parts received.
- [ ] **Step 4: Run** the class, then `$B/wt-test`. Expected: all pass (`EmailTests` and `SenderNameTests` unchanged).
- [ ] **Step 5: Commit** "Sites: per-site numbering, default store, request recipients and sender name".

### Task 5: Teams

**Files:**
- Create: `server/FourC.Cmms.Api/Features/TeamEndpoints.cs`.
- Modify:
  - `Program.cs` (map);
  - `WorkOrderEndpoints.cs` (input, detail, list filter and column, export);
  - `PmEndpoints.cs` (input);
  - `PmGenerator.cs` (inherit);
  - `PlanningBoardEndpoints.cs` (team filter);
  - `TechEndpoints.cs` (bundle);
  - `EmailOutbox.cs` (team members);
  - `ReportEndpoints.cs` (wherever `tradeId` filters, add `teamId`).
- Test: `server/FourC.Cmms.Tests/TeamTests.cs`.

**Interfaces:**
- Produces, `/api/teams`:
  - GET `?siteId=&active=` returns `[{ id, code, name, siteId, siteName, color, memberIds, isActive }]`. With `siteId`, it returns that site's teams plus those with no site.
  - POST and PUT take `TeamInput(string Code, string Name, Guid? SiteId, string? Color, List<Guid> MemberIds, bool IsActive = true, string? RowVersion = null)`. DELETE soft-deletes and refuses a team on open work or schedules.
  - Permissions: people.view to read, people.manage to write.
- Produces, work orders and PM:
  - `WorkOrderInput.TeamId`, `PmScheduleInput.TeamId` (`Guid?`).
  - WO list filter `teamId` and row `teamName`; board `teamId` query parameter.

- [ ] **Step 1: Write the failing tests.**
  - `Teams_can_be_created_with_members`: create, then list; code unique (400).
  - `A_work_order_can_be_given_to_a_team_and_filtered`: the WO list with `teamId` returns it, and the detail shows `team.name`.
  - `A_team_of_another_site_is_refused`: a team of site B on a work order at A → 400 `teamId`.
  - `PM_work_inherits_the_schedules_team`: a generated WO has the schedule's `TeamId`.
  - `Team_members_get_the_assigned_email`: a work order given to a team with two members (with e-mail) queues two "assigned" e-mails.
  - `A_technicians_bundle_includes_their_teams_work`: a member's `/api/tech/bundle` has the team's open work order, with nobody assigned.
  - `The_planning_board_filters_by_team`: with `teamId`, people = the members and jobs = the team's work.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~TeamTests"`. Expected: failures.
- [ ] **Step 3: Implement.**
  - Validation: a team must be active, and its site null or equal to the work's site. Members must be active employees.
  - The bundle query becomes: `Assignees.Any(me) || (TeamId != null && myTeamIds.Contains(TeamId))`.
  - E-mails: on create, and on update when `TeamId` changes, call `AssigneesAddedAsync` with the team's members who are not already assignees.
- [ ] **Step 4: Run** the class, then `$B/wt-test`. Expected: all pass.
- [ ] **Step 5: Commit** "Teams: give work and PM to a team; filters, board, phone and e-mails".

### Task 6: Working hours

**Files:**
- Modify:
  - `server/FourC.Cmms.Core/People/WorkingHours.cs`;
  - `AdminEndpoints.cs` (settings validation);
  - `PeopleEndpoints.cs` (`EmployeeInput.WorkingHours`);
  - `PlanningBoardEndpoints.cs`.
- Test:
  - `server/FourC.Cmms.Tests/WorkingHoursTests.cs` (pure);
  - `server/FourC.Cmms.Tests/PlanningBoardTests.cs` (new cases).

**Interfaces:**
- Produces, in `WorkingHours`:
  - `decimal HoursOn(IReadOnlyList<WorkingDay> pattern, DayOfWeek day)`.
  - `IReadOnlyList<WorkingDay> Organization(TenantSettings s)`. An empty `s.WorkingHours` means every day, from 08:00 for `DailyCapacityHours`.
  - `IReadOnlyList<WorkingDay> ForEmployee(List<WorkingDay>? own, IReadOnlyList<Guid> siteIds, IReadOnlyDictionary<Guid, List<WorkingDay>?> siteHours, TenantSettings org)`.
  - `string? Problem(IReadOnlyList<WorkingDay>? pattern)` returns "Each day can appear once." or "The end must be after the start." or "Use times like 08:00.", else null.
- Produces, planning board: `BoardPerson` gains `decimal[] Hours` (7 values, index = `DayOfWeek`). `CapacityHours` stays for old clients.

- [ ] **Step 1: Write the failing tests.**
  - `HoursOn_counts_the_days_span`: Mon 08:00–16:30 gives 8.5, and Fri absent gives 0.
  - `Organization_defaults_from_the_old_daily_capacity`: `DailyCapacityHours = 6` gives 6 on every day.
  - `A_person_without_hours_uses_their_only_sites_hours`: one site with hours uses them. Two sites, or none, use the organization's.
  - `Problem_rejects_bad_patterns`: the three messages above.
  - `The_board_uses_each_persons_working_hours` (`PlanningBoardTests`): a person working Sun–Thu 8 h has `hours[5] == 0` (Friday) and `hours[0] == 8`.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~WorkingHoursTests|FullyQualifiedName~PlanningBoardTests"`. Expected: failures.
- [ ] **Step 3: Implement.** Settings, site and employee validation all use `WorkingHours.Problem`.
- [ ] **Step 4: Run** the classes, then `$B/wt-test`. Expected: all pass.
- [ ] **Step 5: Commit** "Working hours per organization, site and person; board capacity per person".

### Task 7: The import respects the scope

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/Import/SheetImporters.cs`, `ImportRun.cs` (wherever existing records are matched by code).
- Test: `server/FourC.Cmms.Tests/ImportAccessTests.cs`.

**Interfaces:** consumes `AllSites()`.

- [ ] **Step 1: Write the failing tests.**
  - `A_limited_user_cannot_import_into_another_site`: an assets sheet row for site B is a problem row ("Unknown site …", as for any unknown code). Nothing is saved for it.
  - `Importing_a_code_used_in_a_hidden_site_is_a_problem_row`: a location code or asset number that exists in hidden site B, re-used in a row for A. Expect a problem row ("… is used in a site you can't see."), not a 500 and not an update of B's record.
  - `Current_data_holds_only_visible_sites`: the "current data" workbook for the limited user has A's assets only.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~ImportAccessTests"`. Expected: the hidden-code test fails (500 or duplicate).
- [ ] **Step 3: Implement** a check of keyed codes against `AllSites()` for the sheets whose keys are unique across the organization (assets, PM schedules, stores, and locations per site), reporting the hidden clash per row.
- [ ] **Step 4: Run** the class plus `FullyQualifiedName~Import`, then `$B/wt-test`. Expected: all pass, `ImportSpeedTests` within limits.
- [ ] **Step 5: Commit** "Import: limited people import only into their sites".

### Task 8: Web — access, switch and group view

**Files:**
- Modify:
  - `web/src/api/client.ts` (header);
  - `web/src/api/types.ts`;
  - `web/src/auth/AuthContext.tsx` (access in `me`);
  - `web/src/components/AppLayout.tsx` (switch);
  - `web/src/pages/settings/SettingsPage.tsx` (user form);
  - `web/src/pages/DashboardPage.tsx` (Sites card);
  - locales.
- Create: `web/src/components/siteSwitch.ts` (+ `siteSwitch.test.ts`); `web/src/components/SiteSwitch.tsx`.

**Interfaces:**
- Produces in `siteSwitch.ts`:
  - `const SITE_KEY = 'cmx.site'`;
  - `getSite(): string | null`;
  - `setSite(id: string | null): void` (try/catch around storage);
  - `siteHeader(): Record<string, string>`.
- `client.ts` adds `X-Cmx-Site` when `getSite()` is set.

- [ ] **Step 1: Write the failing test** `siteSwitch.test.ts` with three cases:
  - `setSite` then `getSite` round-trips;
  - `setSite(null)` clears;
  - `siteHeader()` is `{}` when unset and `{ 'X-Cmx-Site': id }` when set.
- [ ] **Step 2: Run** `$B/wt-vitest`. Expected: fails (module missing).
- [ ] **Step 3: Implement.**
  - **Switch:** a Mantine `Select` in the header, shown when `me.access.sites.length > 1`. The options are "All sites" and each site (with the organization's site terminology). On change, `setSite`, then `qc.invalidateQueries()` and `refresh()`. If the stored site isn't in `me.access.sites`, clear it.
  - **User form:**
    - a `MultiSelect` "Sites" (`settings.userSites`, hint "Leave empty for every site.");
    - a location `MultiSelect` "Limit to parts of the tree" (`settings.userBranches`, hint "Only these places and everything under them.");
    - a column on the users table: "All sites" or the site names.
  - **Dashboard:** a Sites card when `bySiteWork.length > 0`. It is a table with each number linking to `/work-orders?siteId=…&status=open`, `…&due=late`, `…&status=completed`, `/work-requests?siteId=…&status=New`. Use the query names the lists already read; check `WorkOrdersPage` and `WorkRequestsPage`.
- [ ] **Step 4: Run** `$B/wt-vitest` and `$B/wt-web`. Expected: all pass, build clean.
- [ ] **Step 5: Commit** "Web: site access on users, site switch, per-site dashboard".

### Task 9: Web — site settings, teams, working hours

**Files:**
- Modify:
  - `web/src/pages/locations/LocationForms.tsx` (site settings section);
  - `web/src/pages/people/PeoplePage.tsx` (Teams tab, employee hours);
  - `web/src/pages/settings/SettingsPage.tsx` (organization hours replace "Hours per day");
  - `web/src/pages/work/WorkOrderFormPage.tsx`, `WorkOrdersPage.tsx`, `WorkOrderDetailPage.tsx` (team);
  - `web/src/pages/pm/PmScheduleFormPage.tsx` (team);
  - `web/src/pages/planning/PlanningPage.tsx` and `board.ts` (team filter, per-person capacity);
  - types and locales.
- Create: `web/src/components/WorkingHoursEditor.tsx`, `web/src/components/workingHours.ts` (+ `workingHours.test.ts`).

**Interfaces:**
- Produces in `workingHours.ts`:
  - `type WorkingDay = { day: number; start: string; end: string }`;
  - `hoursOn(pattern: WorkingDay[], day: number): number`;
  - `problem(pattern: WorkingDay[]): string | null`, which returns i18n keys `hours.oncePerDay`, `hours.endAfterStart` or `hours.timeFormat`.
- Planning board: `board.ts` `load`/capacity uses `person.hours[dayOfWeek]` when present, else `capacityHours`.

- [ ] **Step 1: Write the failing tests.**
  - `workingHours.test.ts`: `hoursOn` (08:00–16:30 = 8.5, a missing day = 0) and `problem` (each key).
  - `board.test.ts`: a person with `hours[5] = 0` is over capacity on a Friday with any planned hours.
- [ ] **Step 2: Run** `$B/wt-vitest`. Expected: fails.
- [ ] **Step 3: Implement.**
  - **Site form:** a collapsible "Site settings" section with:
    - the four prefixes;
    - default store (that site's stores);
    - request recipients (users);
    - sender name;
    - the working hours editor (with a "Use the organization's hours" switch).
  - **Teams tab:** a table plus a modal with code, name, site, colour and members (`EmployeePicker` multi).
  - **Work orders and PM:** a team `Select` filtered by site; the work order list gets a team filter chip and column; the work order detail shows the team.
  - **Planning page:** a team `Select` next to trade.
- [ ] **Step 4: Run** `$B/wt-vitest` and `$B/wt-web`. Expected: all pass, build clean.
- [ ] **Step 5: Commit** "Web: site settings, teams and working hours".

### Task 10: Phone label, docs and changelog

**Files:**
- Modify:
  - `server/FourC.Cmms.Api/Features/TechEndpoints.cs` (bundle `teamName`);
  - `web/src/tech/JobPage.tsx` (shows "Team: …" when set);
  - `CHANGELOG.md` (0.9.0 section);
  - `README.md` (feature list).
- Create: `docs/SITES_AND_ACCESS.md` (how to limit people, branches, the switch, site settings, teams, working hours).
- Test: `TechTests` gets `The_bundle_names_the_team`.

- [ ] **Step 1: Write the failing test** `The_bundle_names_the_team`.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~TechTests"`. Expected: fails.
- [ ] **Step 3: Implement**, and write the docs and changelog. Leave VERSION alone: `release.sh` bumps it.
- [ ] **Step 4: Run** `$B/wt-test`, `$B/wt-vitest`, `$B/wt-web`. Expected: all green.
- [ ] **Step 5: Commit** "Docs for 0.9.0; team name on the phone".
