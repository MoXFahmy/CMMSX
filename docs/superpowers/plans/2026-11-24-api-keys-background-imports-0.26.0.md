# API keys and background imports (0.26.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Service accounts with API keys that act as them under their roles and sites, and every import check/run moved to a background worker with progress, history and an e-mail.

**Architecture:** A service account is an `AppUser` with `IsServiceAccount`; an `ApiKey` row (hash only) belongs to it. A policy scheme forwards requests carrying a key to an `ApiKey` handler that builds the same principal a signed-in person has, so permissions, site access and the activity log work unchanged (source "API"). Imports become `ImportJob` rows plus a stored file; one `ImportWorker` per server claims and runs them one at a time through the existing `ImportRun`, reporting progress and storing the result.

**Tech Stack:** ASP.NET Core 10 minimal APIs, ASP.NET Identity, EF Core 10 / SQL Server, `Microsoft.AspNetCore.RateLimiting`, `Microsoft.AspNetCore.OpenApi` + Scalar; React 19 + Mantine 8 + TanStack Query + i18next; xUnit `SkippableFact` + `ApiFixture`; Vitest.

**Spec:** `docs/superpowers/specs/2026-11-24-api-keys-background-imports-0.26.0-design.md`

## Global Constraints

- Key format: `cmx_` + 40 random letters and digits (base62); stored as SHA-256 hash; the first 8 characters after `cmx_` kept as `Prefix` for the list ("cmx_4f9k…").
- Headers: `Authorization: Bearer cmx_…` or `X-Api-Key: cmx_…`.
- Messages (exact): "The API key isn't valid." (401) · "This key is read-only." (403) · "API keys can't do this." (403) · "You won't see this key again." · "Too many requests with this key. Try again in {0} seconds." (429).
- Rate limit: 120 requests a minute per key (`Api:RequestsPerMinute`, default 120).
- Activity log source for key requests: "API". Import jobs save with source "Import".
- Import limits: 20,000 rows a sheet; 50,000 rows a file in all; 25 MB a file; 30 minutes a run (`Import:MaxMinutes`, default 30).
- Import messages (exact): "Choose a file of up to 25 MB." · "{0} has {1:N0} rows; the limit is 20,000 per sheet. Split it into smaller files." · "This file has {0:N0} rows; the limit is 50,000 in one file. Split it into smaller files." · "This import took too long. Split it into smaller files." · "The server restarted during this import. Nothing was saved. Try again." · "Waiting for another import to finish".
- A job is stale when `Running` and its heartbeat is older than 2 minutes; heartbeats at least every 20 seconds.
- The e-mail goes when a check or import finishes more than 1 minute after it was uploaded, to a starter who isn't a service account.
- Uploaded import files kept 30 days after the job finishes.
- Service account e-mail: `service-‹id›@service.invalid`; e-mail is never sent to an address ending in `.invalid`.
- Permissions: *Manage users* for service accounts and keys; *Manage settings* for imports. No new permissions.
- Every new label and message in English and Arabic (server `Localization/ar.json`, web `locales/en.json` + `ar.json`).

## Review Focus

1. **A deploy runs two servers for a minute:** a queued import must be run by exactly one of them. Test `Two_claims_take_one_job` (Task 5).
2. **A browser that is signed in sends a wrong key:** the request must fail as a bad key, not fall back to the cookie. Test `A_key_header_never_falls_back_to_the_cookie` (Task 2).
3. **A site-limited administrator imports rows for another site:** the background job must apply their site limits. Test `Jobs_run_with_the_starters_sites` (Task 5).
4. **A service account is deleted or deactivated while it has keys:** its keys stop at once. Test `Deactivating_or_deleting_the_account_stops_its_keys` (Task 2).
5. **An import started with a key:** no e-mail goes to the service account, and the job's changes say Import. Test `Key_imports_send_no_email` (Task 6).

---

### Task 1: Service accounts

**Files:**
- Modify: `server/FourC.Cmms.Core/Identity/Identity.cs` (`AppUser.IsServiceAccount`), migration `ServiceAccounts` (via `wt-ef`).
- Create: `server/FourC.Cmms.Api/Features/ServiceAccountEndpoints.cs`.
- Modify: `AdminEndpoints.cs` (users list adds `isServiceAccount`; `/users/lookup`, user PUT and `/users/{id}/password` exclude or refuse service accounts), `AuthEndpoints.cs` (login and renew refuse service accounts), `AnonymiseEndpoints.cs` (refuses them), `EmailOutbox.cs` + every user-recipient query (exclude service accounts; never send to `*.invalid`), `Push.cs` (no push), the approver/recipient validations in `ApprovalLevelEndpoints.cs`, `LocationEndpoints.cs`, `PurchaseRequestEndpoints.cs`, `RequestTypeEndpoints.cs`, `WorkRequestEndpoints.cs` (service accounts aren't valid people), `PeopleEndpoints.cs` (an employee can't link to one), `Program.cs`, `Localization/ar.json`.
- Test: `server/FourC.Cmms.Tests/ServiceAccountTests.cs`.

**Interfaces:**
- Produces: `AppUser.IsServiceAccount : bool`; `POST /api/service-accounts` `{ fullName, roleIds, siteIds, locationIds, isActive }` → 201 `{ id }`; `PUT /api/service-accounts/{id}` same body; `DELETE /api/service-accounts/{id}`; `GET /api/users` rows gain `isServiceAccount`. All under `Permissions.UsersManage`, with the same access checks as users (`UserAccessRules`).
- Produces: `IQueryable<AppUser> People(this IQueryable<AppUser>)` in Core (`!IsServiceAccount`), used by every people query above.

- [ ] **Step 1: Write the failing tests** `ServiceAccountTests`:
  - `Create_list_and_edit`: POST "SAP integration" with the Viewer role → 201; `/api/users` shows it with `isServiceAccount = true`; PUT renames it.
  - `Cannot_sign_in_or_get_a_password`: login with its e-mail and any password → 401 "Invalid email or password."; `POST /api/users/{id}/password` → 400 "Service accounts have no password."; `PUT /api/users/{id}` → 400 "Edit service accounts under Service accounts.".
  - `Not_a_person`: `/users/lookup` leaves it out; an approval level naming it as approver is refused; linking an employee to it is refused; anonymise preview → 400 "Service accounts aren't people.".
  - `Never_emailed`: queue any e-mail to its user id (e.g. make it a PO approver by DB and submit a PO) → no `OutboundEmails` row to `*.invalid`.
- [ ] **Step 2: Run** `wt-test "ServiceAccountTests"`. Expected: fail (no endpoint / no column).
- [ ] **Step 3: Implement**, add the migration and the Arabic messages.
- [ ] **Step 4: Run** `wt-test "ServiceAccountTests|User|Anonymise|Approval|PurchaseRequest|RequestType|Email|FailureCodeModelTests|CostCentreCorrectionTests|HostingTests|ChecklistModelTests"`. Expected: pass.
- [ ] **Step 5: Commit** `feat(api): service accounts`

### Task 2: API keys and signing in with a key

**Files:**
- Create: `server/FourC.Cmms.Core/Identity/ApiKey.cs`; `server/FourC.Cmms.Api/Infrastructure/ApiKeyAuth.cs` (handler, scheme names, key maker); `server/FourC.Cmms.Api/Features/ApiKeyEndpoints.cs`.
- Modify: `Program.cs` (policy scheme), `Infrastructure.cs` (`HttpTenantContext.Source` = "API" for key requests), `CmmsDbContext.cs` (DbSet, index on `Prefix`), migration `ApiKeys`, `AuditScope.cs` (ApiKey recorded as a child of "AppUser"; `IsSensitive` adds `*Password*` and `*ApiKey*`), `NameColumns.cs` (ApiKey `CreatedByName`), `ServiceAccountEndpoints.cs` (delete revokes keys).
- Test: `server/FourC.Cmms.Tests/ApiKeyTests.cs`.

**Interfaces:**
- Produces: `ApiKey { Id, TenantId, ServiceAccountId, Name, Prefix, Hash, ReadOnly, ExpiresAt?, CreatedAt, CreatedById?, CreatedByName?, LastUsedAt?, RevokedAt?, RevokedById? }`.
- Produces: `ApiKeyAuth.Scheme = "ApiKey"`, `ApiKeyAuth.Smart = "Smart"`; claims on a key principal: `NameIdentifier` (service account id), `ClaimNames.TenantId`, `ClaimNames.FullName`, `ApiKeyAuth.KeyIdClaim = "cmx_key"`, `ApiKeyAuth.ReadOnlyClaim = "cmx_key_ro"`; `static bool ApiKeyAuth.IsKey(ClaimsPrincipal)`; `static (string Key, string Prefix, string Hash) ApiKeyAuth.Make()`; `static string ApiKeyAuth.HashOf(string key)`.
- Produces: `GET /api/api-keys` (list: `id, name, serviceAccountId, serviceAccountName, prefix, readOnly, createdAt, createdByName, expiresAt, lastUsedAt, status: Active|Expired|Revoked|AccountInactive`), `POST /api/api-keys` `{ serviceAccountId, name, readOnly, expiresAt? }` → 201 `{ id, key, prefix }`, `POST /api/api-keys/{id}/revoke`. *Manage users*.

- [ ] **Step 1: Write the failing tests** `ApiKeyTests` (helper `KeyClient(fx, key)` = a client with `Authorization: Bearer key` and no cookie):
  - `Key_acts_as_its_account`: Administrator service account key → `POST /api/assets` 201; the asset's Created entry has `Source == "API"` and the account's name.
  - `Key_stays_within_its_roles_and_sites`: Viewer account limited to site A → `GET /api/assets` lists site A's asset only; `POST /api/assets` 403.
  - `Only_the_hash_is_kept`: the POST answer's `key` starts with `cmx_`, is 44 characters; the stored row's `Hash` equals `HashOf(key)` and no column holds the key; `GET /api/api-keys` has `prefix` but no `key`.
  - `Bad_keys_all_answer_the_same`: wrong key, revoked key, expired key (`ExpiresAt` set in the past by DB), inactive account → each 401 with "The API key isn't valid.".
  - `A_key_header_never_falls_back_to_the_cookie`: the signed-in admin client sending `X-Api-Key: cmx_wrong` → 401.
  - `Deactivating_or_deleting_the_account_stops_its_keys`: deactivate → 401; reactivate → 200; delete → 401 and the key shows Revoked.
  - `Last_used_is_kept`: after a call, the list's `lastUsedAt` is set.
- [ ] **Step 2: Run** `wt-test "ApiKeyTests"`. Expected: fail.
- [ ] **Step 3: Implement.** The policy scheme forwards to `ApiKey` when either key header is present, else to the Identity cookie. The handler finds candidates by `Prefix`, compares hashes in constant time, and checks revoked/expired, account active and service account, organization active; `LastUsedAt` is written with `ExecuteUpdate` at most once a minute. `AuditScopeTests`, `NameColumnTests` stay green.
- [ ] **Step 4: Run** `wt-test "ApiKeyTests|ServiceAccountTests|AuditScopeTests|NameColumnTests|SessionRuleTests|PasswordRuleTests|FailureCodeModelTests|CostCentreCorrectionTests|HostingTests|ChecklistModelTests"`. Expected: pass.
- [ ] **Step 5: Commit** `feat(api): API keys`

### Task 3: Key rules: read only, out-of-bounds areas, rate limit

**Files:**
- Create: `server/FourC.Cmms.Api/Infrastructure/ApiKeyRules.cs`.
- Modify: `Program.cs` (`AddRateLimiter` + `UseRateLimiter` after authentication; `UseApiKeyRules()` after site access), `Localization/ar.json`.
- Test: `server/FourC.Cmms.Tests/ApiKeyRuleTests.cs`.

**Interfaces:**
- Produces: `ApiKeyRules.Blocked(string method, PathString path) : bool` — true for any method under `/api/auth`, `/api/tech`, `/api/api-keys`, `/api/service-accounts`, `/api/settings/security`, `/api/anonymise`; and for non-GET under `/api/users` (including `/users/{id}/password` and `/users/{id}/devices`) and `/api/roles`.

- [ ] **Step 1: Write the failing tests** `ApiKeyRuleTests`:
  - `Read_only_key_reads_but_cannot_write`: GET `/api/assets` 200; POST → 403 "This key is read-only.".
  - `Keys_cannot_manage_access`: an Administrator account's key gets 403 "API keys can't do this." for:
    - `POST /api/users`, `PUT /api/roles/{id}`, `POST /api/users/{id}/password`;
    - `POST /api/api-keys`, `POST /api/service-accounts`;
    - `PUT /api/settings/security`, `POST /api/anonymise`;
    - `GET /api/auth/me`, `GET /api/tech/bundle`.

    And it gets 200 for `GET /api/users` and `GET /api/roles`.
  - `Blocked_paths_plain`: `[Fact]` table of `ApiKeyRules.Blocked` cases (GET /api/users false, POST /api/users true, GET /api/auth/me true, GET /api/assets false).
  - `Rate_limit_answers_429`: 121 GETs with one key in a minute → the last is 429 with a `Retry-After` header and the message; a second key still gets 200.
- [ ] **Step 2: Run** `wt-test "ApiKeyRuleTests"`. Expected: fail.
- [ ] **Step 3: Implement.** The limiter partitions by the key-id claim (no limit for cookie requests), fixed window of one minute.
- [ ] **Step 4: Run** `wt-test "ApiKeyRuleTests|ApiKeyTests"`. Expected: pass.
- [ ] **Step 5: Commit** `feat(api): read-only keys, limits and areas keys can't reach`

### Task 4: API documentation

**Files:**
- Modify: `Program.cs` (`AddOpenApi` document transformer declaring an HTTP bearer scheme "ApiKey" with a global requirement; `MapOpenApi().RequireAuthorization()`; Scalar under `/api/docs` with authorization required and the bearer scheme preferred).
- Create: `docs/API.md`.
- Test: `server/FourC.Cmms.Tests/ApiDocsTests.cs`.

- [ ] **Step 1: Write the failing test** `ApiDocsTests.Docs_need_sign_in_or_a_key`: anonymous `GET /openapi/v1.json` → 401; with a key → 200 and the JSON has `components.securitySchemes.ApiKey.scheme == "bearer"`; the signed-in admin → 200.
- [ ] **Step 2: Run** `wt-test "ApiDocsTests"`. Expected: fail (anonymous gets 200 today).
- [ ] **Step 3: Implement**, and write `docs/API.md`:
  - service accounts and keys;
  - the headers;
  - read only;
  - the rate limit and 429;
  - errors (401/403 messages);
  - paging (`page`, `pageSize`, `PagedResult`);
  - areas keys can't reach;
  - imports by key (202 + job status);
  - examples with `curl`:
    - open work orders (`GET /api/work-orders?status=Open`);
    - a meter reading (`POST /api/meters/{id}/readings`);
    - a work request (`POST /api/work-requests`).
- [ ] **Step 4: Run** `wt-test "ApiDocsTests|HostingTests"`. Expected: pass.
- [ ] **Step 5: Commit** `feat(api): documentation for keys`

### Task 5: Background import jobs

**Files:**
- Create: `server/FourC.Cmms.Core/Imports/ImportJob.cs`; `server/FourC.Cmms.Api/Features/Import/ImportWorker.cs`; `server/FourC.Cmms.Api/Features/Import/ImportJobs.cs` (queue, claim, progress, stale).
- Modify: `ImportEndpoints.cs` (check/run queue a job, 202; `GET /import/jobs`, `GET /import/jobs/{id}`, `GET /import/jobs/{id}/problems`; drop `POST /import/problems`; limits), `ImportRun.cs` (limits, progress callback, `TooBig` text), `CmmsDbContext.cs`, `Common/Entity.cs` (`FixedTenantContext` takes optional `siteIds`/`branchPaths`), `AuditScope.cs` (ImportJob excluded), `NameColumns.cs` (ImportJob `UserName` ↔ `UserId`), `Program.cs` (register worker), migration `ImportJobs`, `ApiFixture` (`Import:RunInBackground=false`), `Localization/ar.json`; existing import tests move to the job flow.
- Test: `server/FourC.Cmms.Tests/ImportJobTests.cs` and the existing import tests (`ImportTests`, `Import*Tests`) through a helper.

**Interfaces:**
- Produces: `ImportJob { Id, TenantId, UserId?, UserName?, FileName, StorageKey?, Save (bool), Status: Queued|Running|Done|Failed, Sheet?, RowsDone, RowsTotal, ResultJson?, Error?, AccessJson?, CreatedAt, StartedAt?, FinishedAt?, HeartbeatAt? }`.
- Produces: `ImportWorker.RunNextAsync(CancellationToken) : Task<bool>` (claims the oldest queued job, runs it, true when one ran); the loop polls every 2 seconds when `Import:RunInBackground` is true (default true).
- Produces: `ImportJobs.ClaimNextAsync(CmmsDbContext root, DateTimeOffset now, CancellationToken) : Task<Guid?>` (`WITH next AS (SELECT TOP(1) … WITH (UPDLOCK, READPAST, ROWLOCK) WHERE Status='Queued' ORDER BY CreatedAt) UPDATE next … OUTPUT inserted.Id`); `ImportJobs.FailStaleAsync(root, now, ct) : Task<int>`.
- Produces: `ImportRun.RunAsync(…, Func<string, int, int, Task>? progress)` — sheet name, rows done, rows in sheet; called at least every 100 rows.
- Produces (HTTP): `POST /api/import/check|run` → 202 `{ id }`; `GET /api/import/jobs/{id}` → `{ id, save, status, fileName, userName, createdAt, startedAt, finishedAt, sheet, rowsDone, rowsTotal, waitingBehind, result, error }`; `GET /api/import/jobs` → the last 100, newest first, without `result`.
- Test helper in `ImportTestsBase` or `Loop`: `ImportAsync(fx, client, bytes, name, save) : Task<JsonElement>` = upload, `RunNextAsync` until the job finishes, return the job.

- [ ] **Step 1: Write the failing tests** `ImportJobTests`:
  - `Check_and_import_run_as_jobs`: check of an Assets sheet → 202 `{ id }`, job Queued with `waitingBehind = 0`; `RunNextAsync` → Done with the same `result` shape as before (sheets, counts, problems), nothing saved; import → assets saved; the history lists both, newest first.
  - `Progress_is_reported`: a progress callback test at `ImportRun` level sees the sheet name and rising counts ending at the sheet's rows.
  - `Two_claims_take_one_job`: one queued job, two `ClaimNextAsync` on two contexts at once → exactly one id.
  - `Oldest_first_and_one_at_a_time`: two queued jobs → the first `RunNextAsync` runs the older; the newer shows `waitingBehind = 1` before it.
  - `Stale_jobs_fail_and_save_nothing`: a Running job with `HeartbeatAt` 3 minutes old → `FailStaleAsync` makes it Failed with "The server restarted during this import. Nothing was saved. Try again.".
  - `Jobs_run_with_the_starters_sites`: a site-limited administrator imports an asset at another site → the row is a problem and no asset is created.
  - `Limits_are_checked_at_upload`: a 25 MB + 1 byte file → 400 "Choose a file of up to 25 MB."; a CSV with 20,001 rows → 400 with the per-sheet message.
  - `Problems_workbook_from_a_finished_check`: a check with one bad row → `GET /api/import/jobs/{id}/problems` returns an `.xlsx`.
  - `Twenty_thousand_rows_import`: a 20,000-row Symptoms sheet imports in one job.
- [ ] **Step 2: Run** `wt-test "ImportJobTests"`. Expected: fail.
- [ ] **Step 3: Implement.**
  - The upload validates size, readability and row limits, stores the file at `‹tenant›/imports/‹jobId›/‹fileName›`, saves the job with the starter's sites and branches in `AccessJson`, and answers 202.
  - The worker runs each job with a `FixedTenantContext(tenant, user, userName, "Import", siteIds, branchPaths)`, with a fresh change number per job. It writes progress and the heartbeat with `ExecuteUpdate` at most every 2 seconds, and stores `ResultJson` on finish.
  - Errors end as Failed with the message. Before each claim, the worker calls `FailStaleAsync`.
  - Move the existing import tests onto the helper.
- [ ] **Step 4: Run** `wt-test "Import|FailureCodeModelTests|CostCentreCorrectionTests|HostingTests|ChecklistModelTests"`. Expected: pass.
- [ ] **Step 5: Commit** `feat(import): checks and imports run in the background`

### Task 6: Numbers outside the import, the e-mail, and keeping files

**Files:**
- Modify: `SheetImporters.cs` (asset numbers from `ImportContext.TakeNumbersAsync`), `ImportRun.cs` / `ImportContext` (`TakeNumbersAsync(string name, int count, CancellationToken) : Task<long>`: a check peeks without reserving; an import reserves through a separate context that commits at once), `CmmsDbContext.cs` (`PeekNextNumberAsync(string name, CancellationToken) : Task<long>`), `ImportWorker.cs` (e-mail; file clean-up), `EmailOutbox.cs` (`ImportFinishedAsync(CmmsDbContext db, ImportJob job, CancellationToken ct)`), `Housekeeping.cs` (`TrimImportFilesAsync(CmmsDbContext db, IFileStorage storage, DateTimeOffset now, CancellationToken ct) : Task<int>`), `Localization/ar.json`.
- Test: `server/FourC.Cmms.Tests/ImportJobExtrasTests.cs`.

- [ ] **Step 1: Write the failing tests** `ImportJobExtrasTests`:
  - `A_check_takes_no_numbers`: auto-numbered assets; a check with 3 unnumbered assets leaves the "Asset" sequence's `NextValue` unchanged and shows the numbers the import would take; the import then takes them.
  - `Numbers_are_kept_even_if_the_import_rolls_back`: `TakeNumbersAsync` inside an import transaction that rolls back → `NextValue` still advanced.
  - `Long_runs_email_the_starter`: a job uploaded 2 minutes ago (CreatedAt backdated before `RunNextAsync`) → one e-mail to the starter with the file name and result; a job uploaded just now → none.
  - `Key_imports_send_no_email`: a job started by a service account and backdated → no e-mail; its asset's Created entry has `Source == "Import"`.
  - `Old_files_are_removed`: a job finished 31 days ago → `TrimImportFilesAsync` removes its file and clears `StorageKey`; `/problems` then answers 404 "This file is no longer kept."; a job finished 29 days ago keeps its file.
- [ ] **Step 2: Run** `wt-test "ImportJobExtrasTests"`. Expected: fail.
- [ ] **Step 3: Implement.** File clean-up runs in the worker loop once an hour.
- [ ] **Step 4: Run** `wt-test "Import|Email|FailureCodeModelTests|CostCentreCorrectionTests|HostingTests|ChecklistModelTests"`. Expected: pass.
- [ ] **Step 5: Commit** `feat(import): numbers kept outside the import, e-mail when done, old files removed`

### Task 7: Web — imports, service accounts and API keys

**Files:**
- Modify: `web/src/pages/settings/ImportSettings.tsx` (upload → job; progress bar; result; history table; problems workbook by job), `web/src/pages/settings/importView.ts` (+ test: `jobLabel(job, t)` "Checking Assets… 1,240 of 3,000 rows" / "Waiting for another import to finish"), `SettingsPage.tsx` (Users: "New service account", badge, actions; tab "API keys"), `web/src/api/types.ts`, locales.
- Create: `web/src/pages/settings/ApiKeysSettings.tsx`, `web/src/pages/settings/ServiceAccountModal.tsx`, `web/src/pages/settings/apiKeys.ts` + `apiKeys.test.ts` (`keyStatusColor(status)`, `keyLabel(prefix)` → "cmx_4f9k…").

- [ ] **Step 1: Write the failing tests** (vitest):
  - `importView.test.ts` › `jobLabel`: queued with `waitingBehind > 0` → "Waiting for another import to finish"; running → "Checking Assets… 1,240 of 3,000 rows" (import: "Importing …").
  - `apiKeys.test.ts` › `keyLabel` and `keyStatusColor` (Active teal, Expired/Revoked gray, AccountInactive yellow).
- [ ] **Step 2: Run** `wt-vitest`. Expected: fail.
- [ ] **Step 3: Implement:**
  - **Import page:**
    - polls the job every 1.5 seconds while it is Queued or Running;
    - the result renders exactly as before;
    - the history (last 100) opens a job's result;
    - the limits text reads "Up to 20,000 rows a sheet, 50,000 in all, 25 MB."
  - **API keys tab** (Manage users):
    - the list;
    - "New key", which shows the key once with a copy button and "You won't see this key again.";
    - Revoke, with a confirmation.
  - **Users:**
    - service accounts show a badge;
    - their row has Edit (the service account dialog), API keys and Delete.
- [ ] **Step 4: Run** `wt-vitest`, then `wt-web`. Expected: pass.
- [ ] **Step 5: Commit** `feat(web): background imports, service accounts and API keys`

### Task 8: Guides and verification

**Files:**
- Modify: `docs/API.md` (final pass), the import section of the user guides that mention the 45-second / 5,000-row / 10 MB limits (`grep -rn "45 seconds\|5,000\|10 MB" docs`), `docs/SECURITY_AND_ACTIVITY.md` (source API now exists).

- [ ] **Step 1: Update the guides.**
- [ ] **Step 2: Measure memory:**
  - run a server container limited to 1 GB (`docker run --memory 1g …` on the test SQL network);
  - import a 20,000-row Assets sheet through the API with a key;
  - record the peak memory from `docker stats`.

  If the peak is over 800 MB, lower the file total in `ImportRun` and the Global Constraints message, and record a ruling.
- [ ] **Step 3: Run** `wt-ci`, `wt-vitest`, `wt-web`. Expected: all pass.
- [ ] **Step 4: Commit** `docs: API keys and background imports`
