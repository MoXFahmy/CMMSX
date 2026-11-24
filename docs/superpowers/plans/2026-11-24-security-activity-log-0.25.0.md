# Security and the activity log (0.25.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Password rules, one session per person, anonymising someone who has left, one activity log of every change with its source and change number, and a storage overview.

**Architecture:**
- **The log and History share one store.** The activity log is the existing `AuditEntry` store, widened: every recorded entity (an inventory decides which), plus a source, a change number and a parent record. The record History tab and the new Settings › Activity log read the same rows.
- **New security rules sit in their own settings object.** They live in a `SecuritySettings` object on the tenant settings, saved by its own endpoint, so older settings pages can't reset them.
- **Password rules are enforced in one place.** They run in an Identity password validator plus a password-history table. Expiry is checked at sign-in and in the cookie's per-request validation.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 (SQL Server), ASP.NET Identity cookies, xUnit `SkippableFact` + `ApiFixture`, React 19 + Mantine 8 + TanStack Query, Vitest, the offline phone view in `web/src/tech`.

**Spec:** `docs/superpowers/specs/2026-11-24-security-activity-log-0.25.0-design.md` (read its "My calls" section: it widens the log to every change).

## Global Constraints

- **Defaults change nothing:** minimum length 8, expiry 0 (never), history 0, one session per person off, retention empty (keep everything).
- **Ranges and their messages:**
  - minimum length 8–64: "Enter a minimum length from 8 to 64.";
  - expiry 0 or 30–730 days: "Enter 0 (never) or 30 to 730 days.";
  - history 0–24: "Enter 0 to 24.";
  - retention empty or 12–120 months: "Keep at least 12 months, at most 120, or leave it empty.".
- **Masked value text:** `"••••"`.
- **Sources:** `Web`, `Phone`, `Import`, `Job`.
- **Placeholders:**
  - "Former employee ‹code›" / "موظف سابق ‹code›";
  - "Former user ‹first 6 chars of user id›" / "مستخدم سابق ‹…›";
  - placeholder e-mail `anonymised-‹userId›@invalid`.
- **Permissions:** no new permissions. *Manage settings* covers security, the activity log and storage; *Manage users* covers anonymising.
- **Translations:** every server message goes in `server/FourC.Cmms.Api/Localization/ar.json` (exact or `{0}` templates). Every web label goes in `web/src/locales/en.json` + `ar.json`, with full Arabic plural sets where `count` is used.
- **Older clients keep working:** new inputs are optional, and a field that is left out keeps its value.
- **Migration:** additive only.
- **Release check:** CI's exact Release build (`wt-ci`) gates the release.

## Review Focus

1. **A cached older settings page saves the organization settings:** the security settings must survive. Test `Older_settings_pages_keep_security_settings` (Task 3).
2. **A phone with queued changes is ended by one-session mode or password expiry:** its changes must be kept on the device and sent after signing in again. Vitest `a 401 keeps the outbox` (Task 11).
3. **Anonymising when someone else has the same name:** text-only names must be left unchanged, while id-linked names are still replaced. Test `Same_name_leaves_text_names_alone` (Task 7).
4. **A site-limited administrator opens the activity log:** they must not see entries for records at sites they can't see. Test `Site_limited_admins_see_their_sites` (Task 5).
5. **Retention set while a record's History is open:** only entries older than the cut-off go, and the newest stay. Test `Retention_deletes_only_older_entries` (Task 6).

---

### Task 1: The audit core — source, change number, parent, inventory, masking

**Files:**
- Modify:
  - `server/FourC.Cmms.Core/Auditing/AuditEntry.cs`;
  - `server/FourC.Cmms.Core/Common/Entity.cs` (`ITenantContext`);
  - `server/FourC.Cmms.Core/Data/CmmsDbContext.cs` (save pipeline);
  - `server/FourC.Cmms.Api/Infrastructure/Infrastructure.cs` (`HttpTenantContext`).
- Create: `server/FourC.Cmms.Core/Auditing/AuditScope.cs`.
- Migration `ActivityLog`.
- Test:
  - `server/FourC.Cmms.Tests/AuditScopeTests.cs` (plain);
  - `server/FourC.Cmms.Tests/ActivitySourceTests.cs`.

**Interfaces:**
- Produces:
  - **`AuditEntry` gains:**
    - `string Source` (default `"Web"`, max 10);
    - `Guid ChangeId`;
    - `string? ParentType` (max 100);
    - `Guid? ParentId`;
    - index (TenantId, Timestamp), index (TenantId, ChangeId), index (TenantId, ParentType, ParentId).
  - **`ITenantContext` gains** default members `string Source => "Web"` and `Guid ChangeId => …`:
    - `FixedTenantContext` gets an optional `source` (default `"Job"`) and a `ChangeId` fixed for its lifetime;
    - `HttpTenantContext.Source` is `Phone` for paths starting `/api/tech`, `Import` for `/api/import`, otherwise `Web`;
    - `HttpTenantContext.ChangeId` is one Guid per HTTP request, kept in `HttpContext.Items`.
  - **`static class AuditScope`** (Core):
    - `IReadOnlySet<Type> Excluded`: StockTransaction, UnitMovement, MeterReading, UserSession, AuditEntry, OutboundEmail, PushMessage, TechAction, AiCall, AiDiagnosisVote, NumberSequence, StockReservation, SavedView, UserListSetting, WorkOrderStatusChange, AssetTransfer, PasswordHistory (Task 3) and Identity infrastructure (`DataProtectionKey`, identity claim/login/token tables);
    - `bool IsRecorded(Type t)`;
    - `(string Type, Guid Id)? ParentOf(object entity)`. Parent map: WorkOrderTask/Labor/Part/Contractor/Cost/Comment/Assignee/Asset → WorkOrder; WorkRequestComment, RequestFeedback → WorkRequest; PurchaseOrderLine, PurchaseOrderApprovalStep, PurchaseOrderSignature → PurchaseOrder; PurchaseRequestComment → PurchaseRequest; ContractPeriod/Amendment/Asset → Contract; StockLevel → Part; StockCountLine → StockCount; WorkOrderGroupComment → WorkOrderGroup; AssetPart → Asset; PmTaskPart (and PmTask step entities) → PmTask; Attachment → (its EntityType, EntityId); RoleListSetting → Role.
  - **`static IReadOnlySet<string> AuditScope.Sensitive`**: PasswordHash, SecurityStamp, ConcurrencyStamp, PushToken, plus any property named `*Token*` or `*Secret*` or ending `Hash`.
  - **Save pipeline:**
    - writes an entry for every tracked entity whose type `IsRecorded`, not only `TenantEntity`;
    - TenantId comes from `ITenantOwned` or the context;
    - EntityId is the entity's `Id`, or for keyless types the parent id;
    - Source, ChangeId and the parent are filled in;
    - masked values are written as `"••••"`;
    - deletes of non-soft-deletable recorded rows write an entry with `Action = "Deleted"`.

- [ ] **Step 1: Write the failing tests**
  - `AuditScopeTests.Every_entity_is_recorded_or_excluded`: for each `CmmsDbContext` model entity type, assert `IsRecorded(t) || Excluded.Contains(t)`. Failing message: "‹Type› is neither recorded nor excluded from the activity log".
  - `AuditScopeTests.Children_name_their_parent`: `ParentOf(new WorkOrderLabor { WorkOrderId = w })` == ("WorkOrder", w); `ParentOf(new Attachment { EntityType = "Asset", EntityId = a, … })` == ("Asset", a).
  - `AuditScopeTests.Secrets_are_masked`: `AuditScope.IsSensitive("PasswordHash")`, `("PushToken")`, `("ApiKeyHash")` are true; `("FullName")` is false.
  - `ActivitySourceTests.Web_phone_and_job_changes_say_where_they_came_from`:
    1. An asset saved through `/api/assets` → its entry has Source `Web` and a non-empty ChangeId.
    2. A phone `labor.add` → the `WorkOrderLabor` entry has Source `Phone` and ParentType `WorkOrder`.
    3. A PM generation run (`/api/pm-schedules/generate`, or the generator with a `FixedTenantContext`) → its work order entries say `Job`.
  - `ActivitySourceTests.One_action_shares_one_change_number`: closing a work order with labour lines writes several entries with one ChangeId.
  - `ActivitySourceTests.Child_lines_are_recorded`:
    1. Adding then deleting a work order cost writes Created and Deleted entries with ParentId = the work order.
    2. `GET /api/audit?entityType=WorkOrder&entityId=` includes them.
- [ ] **Step 2: Run** `wt-test "AuditScopeTests|ActivitySourceTests"`. Expected: fail (members missing).
- [ ] **Step 3: Implement.**
  - Add migration `ActivityLog`. Existing rows get Source `Web` and a ChangeId equal to their Id.
  - `GET /api/audit` also returns child entries (ParentType/ParentId = the record) and each entry's `source`.
- [ ] **Step 4: Run** `wt-test "AuditScopeTests|ActivitySourceTests|Audit|History"`. Expected: pass.
- [ ] **Step 5: Commit** `feat(activity): every change recorded with its source, change number and parent`

### Task 2: Bulk operations write entries

**Files:**
- Modify the `ExecuteUpdateAsync`/`ExecuteDeleteAsync` sites on recorded types in:
  - `WorkOrderEndpoints.cs` (labour, tasks and cost line deletes);
  - `BrandEndpoints.cs`;
  - `UnitEndpoints.cs`;
  - `AssetGroupEndpoints.cs`;
  - `CostCentreEndpoints.cs`;
  - `InventoryEndpoints.cs`;
  - `PurchasingEndpoints.cs`;
  - `LocationEndpoints.cs`;
  - `ContractorEndpoints.cs`;
  - `SupportEndpoints.cs`;
  - `AdminEndpoints.cs`.
- Leave alone: sites on excluded types (sessions, outbox, push, tech actions, housekeeping).
- Create: `server/FourC.Cmms.Core/Auditing/AuditLog.cs`.
- Test: `server/FourC.Cmms.Tests/BulkActivityTests.cs`.

**Interfaces:**
- Produces:
  - `static class AuditLog` (Core):
    - `void Summary(CmmsDbContext db, ITenantContext ctx, string entityType, Guid entityId, string action, string summary)`: adds an entry with `Changes = {"Summary": {"to": summary}}`, the context's source and ChangeId; the caller saves.
    - `void Event(CmmsDbContext db, ITenantContext ctx, string entityType, Guid entityId, string action, object? changes = null)`: used by Tasks 3, 4, 7.
  - Deletes of child lines become tracked deletes (load, `RemoveRange`, save), so each line is logged by Task 1.
  - Cascades write one summary on the changed record: unit rename, brand/model merge, asset-group membership sync, cost-centre reassignment, location moves:
    - "Brand merged into ‹name›: ‹n› assets";
    - "Unit renamed to ‹symbol›: ‹n› parts and points";
    - "Cost centre set on ‹n› work orders".

- [ ] **Step 1: Write the failing tests**
  - `BulkActivityTests.Deleting_lines_logs_each_line`: deleting a labour line (`DELETE /api/work-orders/{id}/labor/{laborId}`) writes a Deleted entry for that `WorkOrderLabor` with ParentId = the work order.
  - `BulkActivityTests.Merges_write_a_summary`:
    1. Merging two brands writes one entry on the kept brand.
    2. Its Summary contains "Brand merged" and the count of assets that followed.
  - `BulkActivityTests.Unit_rename_writes_a_summary`: renaming a unit of measure writes a summary on the unit naming how many parts followed.
- [ ] **Step 2: Run** `wt-test "BulkActivityTests"`. Expected: fail.
- [ ] **Step 3: Implement** the per-site changes listed above. Each site that stays a bulk statement gets one `AuditLog.Summary` call, saved in the same request.
- [ ] **Step 4: Run** `wt-test "BulkActivityTests|WorkOrder|Brand|Unit|CostCentre|AssetGroup|FailureCodeModelTests"`. Expected: pass.
- [ ] **Step 5: Commit** `feat(activity): bulk changes write entries`

### Task 3: Security settings and password rules

**Files:**
- Modify:
  - `server/FourC.Cmms.Core/Tenancy/Tenant.cs` (`TenantSettings.Security`);
  - `server/FourC.Cmms.Core/Identity/Identity.cs` (`AppUser.PasswordChangedAt`);
  - `AdminEndpoints.cs` (settings PUT keeps `Security`; user create/edit check passwords);
  - `AuthEndpoints.cs`;
  - `Program.cs` (register validator).
- Create:
  - `server/FourC.Cmms.Core/Identity/PasswordHistory.cs`;
  - `server/FourC.Cmms.Api/Features/SecurityEndpoints.cs`;
  - `server/FourC.Cmms.Api/Infrastructure/PasswordRules.cs`.
- Test: `server/FourC.Cmms.Tests/PasswordRuleTests.cs`.

**Interfaces:**
- Produces:
  - `public sealed class SecuritySettings { int PasswordMinLength = 8; int PasswordExpiryDays = 0; int PasswordHistory = 0; bool OneSessionPerUser = false; int? ActivityRetentionMonths = null; }` on `TenantSettings.Security` (never null; defaults).
  - `GET/PUT /api/settings/security` (settings.manage).
    - The PUT validates the ranges (Global Constraints) and writes an audit event on `Tenant` with each changed key's old and new value.
    - The general `PUT /api/settings` copies the existing `Security` over the input's, so older pages can't change it.
  - `PasswordHistory : Entity, ITenantOwned { Guid UserId; string Hash; DateTimeOffset SetAt; }` keeps at most 24 per user.
  - `PasswordRules` (Api):
    - `Task<IReadOnlyList<string>> ProblemsAsync(CmmsDbContext db, AppUser user, string password, CancellationToken ct)`. It returns "Use at least {0} characters." and "You used this password recently. Choose another one.";
    - `Task RecordAsync(CmmsDbContext db, AppUser user, string newHash, CancellationToken ct)`. It stores the history row, sets `PasswordChangedAt = now` and trims to 24;
    - `bool Expired(AppUser user, SecuritySettings s, DateTimeOffset now)`.
  - The checks run in every place a password is set:
    - register (default rules);
    - admin create user and admin edit user with a password;
    - change password;
    - the renew step (below).
  - **Login:**
    - when the password is correct but expired, or shorter than the current minimum: 403 `{ code: "password-expired" | "password-rules", title }`, with no sign-in;
    - `POST /api/auth/renew-password { email, password, newPassword, rememberMe, technician, endOtherSessions? }` checks the current password and the rules and history, saves, then signs in as login does.
    - Messages: "Your password has expired. Choose a new one." and "Your organization's password rules changed. Choose a new password."
  - **Migration `Security`:**
    - adds `AppUser.PasswordChangedAt` (existing users get the migration time) and the `PasswordHistories` table;
    - `PasswordHistory` joins `AuditScope.Excluded`;
    - `AppUser` and `Role` are recorded (Task 1 inventory), with `PasswordHash`/stamps masked.

- [ ] **Step 1: Write the failing tests** in `PasswordRuleTests`:
  - `Defaults_change_nothing`: a new organization accepts an 8-character password and has `security.passwordMinLength = 8`.
  - `Minimum_length_applies_everywhere`: set the minimum to 12.
    1. Admin create user with 10 characters → 400 "Use at least 12 characters.".
    2. Change password with 10 → 400 with the same message.
    3. Register still accepts 8 (a new organization's defaults).
  - `Recent_passwords_are_refused`: set history 3; change A→B→C; changing back to A → "You used this password recently. Choose another one."
  - `Expired_password_must_be_renewed`:
    1. Set expiry 30 and backdate the user's `PasswordChangedAt` by 31 days.
    2. Login → 403 code `password-expired`.
    3. `renew-password` → 200 and signed in (`/api/auth/me` 200).
    4. The audit has a `PasswordRenewed` event.
  - `Raised_minimum_is_met_at_next_sign_in`: a user with an 8-character password; raise the minimum to 10; login → 403 code `password-rules`.
  - `Older_settings_pages_keep_security_settings`: set the minimum to 12, then `PUT /api/settings` with a body from before 0.25.0 (no `security`); `GET /api/settings/security` still shows 12.
  - `Security_settings_are_validated`: minimum 7 → "Enter a minimum length from 8 to 64."; expiry 10 → "Enter 0 (never) or 30 to 730 days."; retention 6 → "Keep at least 12 months, at most 120, or leave it empty."
- [ ] **Step 2: Run** `wt-test "PasswordRuleTests"`. Expected: fail.
- [ ] **Step 3: Implement** as in the Interfaces block. Add the Arabic messages.
- [ ] **Step 4: Run** `wt-test "PasswordRuleTests|Auth|User|Settings|FailureCodeModelTests"`. Expected: pass.
- [ ] **Step 5: Commit** `feat(security): password rules and security settings`

### Task 4: Sessions, expiry on signed-in devices, one session per person, security events

**Files:**
- Modify:
  - `server/FourC.Cmms.Api/Infrastructure/Sessions.cs` (`ValidateAsync`);
  - `AuthEndpoints.cs` (login, logout, change-password);
  - `DeviceEndpoints.cs` (sign out a device);
  - `AdminEndpoints.cs` (user and role writes are recorded by Task 1; nothing else).
- Test: `server/FourC.Cmms.Tests/SessionRuleTests.cs`.

**Interfaces:**
- Produces:
  - **`Sessions.ValidateAsync`:** when the organization's expiry is on and the user's password expired, it rejects with header `X-Password-Expired: 1`. The API answers 401, and the web and phone read that header.
    - The user's `PasswordChangedAt` and the tenant's `Security` are cached with the session state, with the existing cache time.
  - **Login with one session per person on:**
    - when another active, unexpired session exists and `endOtherSessions` isn't true: 409 `{ code: "other-session", device, lastSeenAt }`;
    - with `endOtherSessions: true`: revokes the others (as `Sessions.RevokeAsync`) and signs in.
  - **Security events through `AuditLog.Event` on `AppUser`:**
    - Actions: `SignedIn`, `SignInFailed` (known users only), `LockedOut`, `SignedOut`, `DeviceSignedOut` (with by whom), `PasswordChanged`, `PasswordRenewed`, `OtherSessionsEnded`.
    - Each records the device name, never the password or any token.

- [ ] **Step 1: Write the failing tests** in `SessionRuleTests`:
  - `Second_sign_in_asks_before_ending_the_first`: turn one session on.
    1. Client A logs in.
    2. Client B logs in → 409 code `other-session` with A's device name.
    3. B logs in again with `endOtherSessions: true` → 200.
    4. A's next request → 401 with `X-Session-Revoked: 1`.
  - `Off_by_default_nothing_asks`: two logins → both 200, and both work.
  - `Expiry_reaches_signed_in_devices`: signed in, then expiry turned on and `PasswordChangedAt` backdated (cache cleared) → next request 401 with `X-Password-Expired: 1`.
  - `Sign_ins_and_failures_are_recorded`: a failed login for a known e-mail and a successful one write `SignInFailed` and `SignedIn` entries on that user with the device name; the failure's entry has no password.
- [ ] **Step 2: Run** `wt-test "SessionRuleTests"`. Expected: fail.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `wt-test "SessionRuleTests|Session|Device|Auth|FailureCodeModelTests"`. Expected: pass.
- [ ] **Step 5: Commit** `feat(security): one session per person, expiry on signed-in devices, security events`

### Task 5: Activity log API

**Files:**
- Create: `server/FourC.Cmms.Api/Features/ActivityEndpoints.cs`.
- Modify: `Program.cs`, list export registration (as other lists use `ListExport`).
- Test: `server/FourC.Cmms.Tests/ActivityLogTests.cs`.

**Interfaces:**
- Produces:
  - `GET /api/activity` (settings.manage).
    - **Filters:** `from`, `to`, `userId`, `source`, `entityType`, `action`, `changeId`, `search`, `page`, `pageSize`.
    - **Rows:** `{ id, timestamp, userId, userName, source, changeId, entityType, entityId, parentType, parentId, recordLabel, action, changes (parsed JSON) }`, newest first, paged as other lists.
    - `recordLabel` comes from a resolver: number/code + name for the main record types (WorkOrder number, Asset number, Part number, PO number, request number, user name, role name…), else the type name.
    - `search` matches the label, entity type and change keys.
  - `GET /api/activity/export` through `ListExport` (CSV/Excel, capped).
  - **Site visibility:** a site-limited administrator sees only entries whose record (or parent) is visible to them, plus entries with no site (settings, users, roles, their own sign-ins). The builder maps record types that carry `SiteId` (directly or through their parent).
  - `GET /api/activity/last-changed?entityType=Tenant&keys=…` → for each settings key, the last entry that changed it `{ key, userName, timestamp }`. It's used by the settings pages' "Last changed" lines.

- [ ] **Step 1: Write the failing tests** in `ActivityLogTests`:
  - `Filters_and_same_change`:
    1. Make three changes (one asset edit, one work order close with labour, one settings change).
    2. `source=Web` lists them.
    3. `changeId=<the close's>` lists exactly that action's entries.
    4. `entityType=Asset&search=<asset number>` finds the edit.
    5. Rows carry `recordLabel`.
  - `Masked_values_never_show`: changing a user's password writes an `AppUser` entry whose `PasswordHash` change is `"••••"` both ways.
  - `Site_limited_admins_see_their_sites`:
    1. An administrator role limited to site A.
    2. An asset changed at site B is absent.
    3. Site A's change is present.
    4. A settings change (no site) is present.
  - `Export_works`: `GET /api/activity/export?format=csv` → 200 with a header row.
  - `Last_changed_names_who_and_when`: after changing the time zone, `last-changed?entityType=Tenant&keys=timeZone` gives the admin's name.
- [ ] **Step 2: Run** `wt-test "ActivityLogTests"`. Expected: fail.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `wt-test "ActivityLogTests|ActivitySourceTests"`. Expected: pass.
- [ ] **Step 5: Commit** `feat(activity): the activity log API with filters, export and site limits`

### Task 6: Retention and the organization settings diff

**Files:**
- Modify:
  - `server/FourC.Cmms.Api/Features/Housekeeping.cs` (+ its hosted runner);
  - `AdminEndpoints.cs` (settings PUT writes a settings-diff event);
  - `LocationEndpoints.cs` (site settings are a `Site` change, already recorded).
- Test: `server/FourC.Cmms.Tests/RetentionTests.cs`.

**Interfaces:**
- Produces:
  - `Housekeeping.TrimActivityAsync(CmmsDbContext db, DateTimeOffset now, CancellationToken ct)`: for each tenant with `ActivityRetentionMonths`, it deletes `AuditEntries` older than now − months. It runs with the nightly housekeeping.
  - The settings PUT writes one `Tenant` event `SettingsChanged` whose changes are `{ "settings.<key>": { from, to } }` for every changed top-level settings key, compared as JSON.

- [ ] **Step 1: Write the failing tests**
  - `RetentionTests.Retention_deletes_only_older_entries`: with retention 12, entries backdated 13 months go and entries from 11 months ago stay; another organization's old entries stay.
  - `RetentionTests.Settings_changes_list_each_key`: changing the time zone and the language writes one `SettingsChanged` entry listing both keys with old and new values.
- [ ] **Step 2: Run** `wt-test "RetentionTests"`. Expected: fail.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `wt-test "RetentionTests|Settings|Housekeeping|FailureCodeModelTests"`. Expected: pass.
- [ ] **Step 5: Commit** `feat(activity): retention and settings changes by key`

### Task 7: Anonymise a person who has left

**Files:**
- Create:
  - `server/FourC.Cmms.Api/Features/AnonymiseEndpoints.cs`;
  - `server/FourC.Cmms.Core/People/NameColumns.cs`.
- Modify:
  - `server/FourC.Cmms.Core/People/People.cs` (`Employee.AnonymisedAt`);
  - `Identity.cs` (`AppUser.AnonymisedAt`);
  - migration (in the Task 3 migration if not yet applied, else `Anonymise`).
- Test:
  - `server/FourC.Cmms.Tests/AnonymiseTests.cs`;
  - `server/FourC.Cmms.Tests/NameColumnTests.cs` (plain).

**Interfaces:**
- Produces:
  - **`NameColumns.All`:** `IReadOnlyList<(Type Entity, string Property, string? IdProperty)>`. It lists every string property holding a person's copied name across the model.
    - **With id:** CreatedByName↔CreatedById, UserName↔UserId, ByName↔ByUserId and similar.
    - **Text-only (no id):** RequestedBy, SignedBy, CountedByName, DecidedByName, RequestedByName, ApprovedByName, ChangedByName, UpdatedByName, ByName where no id.
    - `NameColumnTests.Every_name_column_is_listed` scans the model for string properties whose name ends with `ByName`, `UserName`, `RequestedBy`, `SignedBy`, `RequesterName` and fails on any not in the list. Explicit exceptions are allowed: `WorkRequest.RequesterName` and `RequesterEmail` are outside requesters, matched by text only.
  - **`GET /api/anonymise/preview?employeeId=|userId=`** (users.manage) → `{ placeholder, records: n, sameNameAs: [names] | [], blocked: string | null }`. Blocked reasons:
    - "Make the person inactive first.";
    - "Make their sign-in inactive first.";
    - "This person is already anonymised.".
  - **`POST /api/anonymise`** `{ employeeId?, userId?, confirm: "<code or placeholder>" }`:
    - refuses with the blocked reason, or "Type ‹code› to confirm." when `confirm` doesn't match;
    - otherwise, in one transaction:
      1. replaces id-linked name copies;
      2. replaces text-only copies only when `sameNameAs` is empty;
      3. rewrites `AuditEntries`: UserName where UserId matches; all personal values in entries of the person's own Employee/AppUser records become `"••••"`; values elsewhere equal to the name or e-mail become the placeholder;
      4. clears the employee's e-mail, phone, job title and notes and sets its name, and closes the user as the spec says (e-mail and user name placeholder, sessions revoked, push tokens removed);
      5. writes an `Anonymised` event with no old values.

- [ ] **Step 1: Write the failing tests**
  - `AnonymiseTests.Name_leaves_every_copy`:
    1. A user/employee "Karim Saad" logs labour, comments, signs and reads a meter.
    2. Make both inactive and anonymise with confirm "E-012".
    3. Not one listed name column and no audit entry contains "Karim Saad".
    4. Their labour hours and costs are unchanged.
    5. The employee shows "Former employee E-012".
    6. A new user can be created with Karim's old e-mail.
  - `AnonymiseTests.Refused_for_active_people_and_unconfirmed`:
    1. An active employee → "Make the person inactive first.".
    2. An inactive employee with an active sign-in → "Make their sign-in inactive first.".
    3. Wrong confirm text → "Type E-012 to confirm.".
  - `AnonymiseTests.Same_name_leaves_text_names_alone`:
    1. Two people named "Ali Hassan"; anonymise one.
    2. The preview lists the clash.
    3. A request's text-only `RequestedBy = "Ali Hassan"` is unchanged.
    4. The anonymised person's id-linked comments now show the placeholder.
  - `AnonymiseTests.Login_only_people_too`: the registration administrator's second admin (inactive, no employee) anonymised from the user → "Former user ‹6 chars›".
  - `NameColumnTests.Every_name_column_is_listed` (see Interfaces).
- [ ] **Step 2: Run** `wt-test "AnonymiseTests|NameColumnTests"`. Expected: fail.
- [ ] **Step 3: Implement.** Add the Arabic messages, and the placeholders' Arabic form for Arabic organizations (by the organization's default language).
- [ ] **Step 4: Run** `wt-test "AnonymiseTests|NameColumnTests|People|User|FailureCodeModelTests"`. Expected: pass.
- [ ] **Step 5: Commit** `feat(security): anonymise a person who has left`

### Task 8: Storage overview API

**Files:**
- Create: `server/FourC.Cmms.Api/Features/StorageEndpoints.cs`.
- Test: `server/FourC.Cmms.Tests/StorageTests.cs`.

**Interfaces:**
- Produces: `GET /api/storage` (settings.manage). It returns:
  - `{ totalBytes, files }`;
  - `byKind: [{ kind: "Images"|"Videos"|"Documents"|"Signatures"|"Other", bytes, files }]`;
  - `byRecord: [{ entityType, bytes, files }]`;
  - `deletedBytes` (files whose record is soft-deleted, when known).

  `GET /api/storage/files?search=&page=` → the largest files first: `{ id, fileName, contentType, size, entityType, entityId, recordLabel, uploadedBy, uploadedAt }`. It reuses the Task 5 label resolver.

- [ ] **Step 1: Write the failing test** `StorageTests.Totals_match_the_files`:
  1. Upload two images and one PDF to an asset, and a signature to a work order.
  2. The totals equal the sum of sizes.
  3. `byKind` has Images=2, Documents=1, Signatures=1.
  4. `files?search=<pdf name>` finds the PDF with the asset's label.
- [ ] **Step 2: Run** `wt-test "StorageTests"`. Expected: fail.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `wt-test "StorageTests"`. Expected: pass.
- [ ] **Step 5: Commit** `feat(admin): storage overview`

### Task 9: Web — sign-in flows and Settings › Security

**Files:**
- Create:
  - `web/src/pages/settings/SecuritySettings.tsx`;
  - `web/src/pages/auth/signIn.ts` + `signIn.test.ts`.
- Modify:
  - `web/src/pages/AuthPages.tsx` (expired / rules / other-session steps);
  - `web/src/api/client.ts` (401 with `X-Password-Expired` → renew page);
  - `web/src/pages/settings/SettingsPage.tsx` (tabs);
  - `web/src/components/AppLayout.tsx` (change password shows rule messages);
  - `web/src/api/types.ts`;
  - locales.

**Interfaces:**
- Produces:
  - `export function signInStep(status: number, body: unknown): 'done' | 'renew' | 'other-session' | 'error'` in `signIn.ts`. It maps 200, 403 `password-expired`/`password-rules`, 409 `other-session`, and anything else.
  - `export function securityProblems(s: SecuritySettings): Partial<Record<keyof SecuritySettings, string>>`: the same ranges as the server, returning i18n keys.

- [ ] **Step 1: Write the failing tests** in `signIn.test.ts`:
  - `signInStep maps each answer`: (200,{}) → done; (403,{code:'password-expired'}) → renew; (403,{code:'password-rules'}) → renew; (409,{code:'other-session'}) → other-session; (401,{}) → error.
  - `securityProblems checks the ranges`: minimum 7 and expiry 10 give problems; the defaults give none.
- [ ] **Step 2: Run** `wt-vitest`. Expected: fail.
- [ ] **Step 3: Implement:**
  - **Sign-in page:** a new-password step for "renew", which posts `/auth/renew-password`. "You're signed in on ‹device› (last used ‹time›). Sign in here and end that session?" with "Sign in here" (`endOtherSessions: true`) and "Cancel".
  - **Expired session:** a 401 with `X-Password-Expired` sends the person to the sign-in page with the renew step and a "Your password has expired" notice, then back to the page.
  - **Settings › Security** (settings.manage): minimum length, expiry, history, one session per person, retention with the note "Older changes also leave each record's History tab.", and "Last changed by ‹name›, ‹when›" from `last-changed`.
- [ ] **Step 4: Run** `wt-vitest`, then `wt-web`. Expected: pass.
- [ ] **Step 5: Commit** `feat(web): password rules, one-session prompt and security settings`

### Task 10: Web — activity log, storage, anonymise, History source

**Files:**
- Create:
  - `web/src/pages/settings/ActivityLog.tsx`;
  - `web/src/pages/settings/StorageSettings.tsx`;
  - `web/src/pages/settings/activity.ts` + `activity.test.ts`;
  - `web/src/components/AnonymiseModal.tsx`.
- Modify:
  - `SettingsPage.tsx` (tabs "Activity log", "Storage");
  - `web/src/components/AuditTimeline.tsx` (source badge, child entries' parent line);
  - the employee page and the Users settings (an "Anonymise" action on inactive people);
  - `web/src/components/list/apiQueries.ts` (`activity` list);
  - `types.ts`;
  - locales.

**Interfaces:**
- Produces:
  - `export function changeSummary(changes: Record<string, { from?: unknown; to?: unknown }> | null, t): string`: up to three "field: old → new" pairs, then "+n more"; masked values show as "••••".
  - `export function fileKind(contentType: string, isSignature: boolean): 'Images' | 'Videos' | 'Documents' | 'Signatures' | 'Other'`: matches the server's grouping.

- [ ] **Step 1: Write the failing tests** in `activity.test.ts`:
  - `changeSummary lists the first three changes`: 5 keys → 3 pairs + "+2 more"; a masked value stays "••••".
  - `fileKind groups like the server`: image/png → Images; application/pdf → Documents; signature → Signatures; video/mp4 → Videos.
- [ ] **Step 2: Run** `wt-vitest`. Expected: fail.
- [ ] **Step 3: Implement:**
  - **The activity log page:** list toolkit with filters (dates, person, source, record type, action, search), expandable rows, "Same change" and export.
  - **The storage page:** totals, by kind, by record, and the largest files with search.
  - **Anonymise:** the preview in a modal (the count, the clash notice, "Names typed inside descriptions and comments aren't changed.", and the typed confirmation), on inactive employees and inactive users.
  - **AuditTimeline:** a source badge, with children shown under their parent record.
- [ ] **Step 4: Run** `wt-vitest`, then `wt-web`. Expected: pass.
- [ ] **Step 5: Commit** `feat(web): activity log, storage overview and anonymise`

### Task 11: Phone — renew and one-session on the phone, queued changes kept

**Files:**
- Modify:
  - `web/src/tech/api.ts` (`SignedOut` carries `passwordExpired`);
  - `web/src/tech/useTech.tsx`;
  - the phone's sign-in screen (the file `useTech`/`TechApp` uses for sign-in);
  - `web/src/tech/outbox.test.ts` (or `api.test.ts`);
  - locales.

**Interfaces:**
- Consumes: Task 4 headers/codes and the Task 9 `signInStep`.
- Produces: `SignedOut(revoked: boolean, passwordExpired = false)`.
  - The phone shows "Your password has expired. Choose a new one." with the renew fields, and "You were signed out because you signed in on another device" for `X-Session-Revoked`.
  - Queued changes stay in the outbox and are sent after the next successful sign-in.

- [ ] **Step 1: Write the failing tests:**
  - `api.test.ts`: a 401 with `X-Password-Expired: 1` throws `SignedOut` with `passwordExpired = true`.
  - `outbox.test.ts`: `a 401 keeps the outbox`. A send that hits `SignedOut` leaves every queued change in place, with its state not `failed`.
- [ ] **Step 2: Run** `wt-vitest`. Expected: fail.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `wt-vitest`, then `wt-web`. Expected: pass.
- [ ] **Step 5: Commit** `feat(tech): renew an expired password and one-session prompt on the phone`

### Task 12: Guide and full verification

**Files:**
- Create: `docs/SECURITY_AND_ACTIVITY.md` (style of `docs/SERIAL_PARTS.md`).

- [ ] **Step 1: Write the guide.** Who may do what, password rules, one session per person, anonymising, the activity log (sources, change numbers, masking, retention), storage, and what doesn't change by default.
- [ ] **Step 2: Run** `wt-ci`, `wt-vitest` and `wt-web`. Expected: all pass.
- [ ] **Step 3: Commit** `docs: security and activity log guide`
