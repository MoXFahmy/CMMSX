# Platform Shield and Automatic Backups (0.29.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Protect CMX from common attacks (edge firewall, internal load balancer, per-address limits, security headers, e-mail
check on sign-up) and keep every byte recoverable (layered backups with locked copies in Ireland, a monthly restore test),
with detection and alarms that reach a person.

**Architecture:** App changes are host-neutral and switched by settings (AWS sets them; Azure and local runs leave them off).
AWS changes are CloudFormation: `cmms-app` (London) grows; two new stacks, `cmms-edge` (us-east-1, WAF) and `cmms-dr`
(eu-west-1, locked copies), are deployed by the same workflow before the app stack; `cmms-github` (bootstrap) gains the
permissions and the deny guard, re-run once by the user.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 / SQL Server, xUnit (SkippableFact, ApiFixture), React 19 + Mantine 8,
Vitest, CloudFormation (cfn-lint), GitHub Actions, AWS WAF, CloudFront VPC origins, AWS Backup, RDS SQL Server Express,
S3 Object Lock and replication, CloudTrail, GuardDuty, CloudWatch, EventBridge, SNS.

**Spec:** `docs/superpowers/specs/2026-11-25-security-backups-0.29.0-design.md`

## Global Constraints

- Work in a worktree: `WT=/Users/mohamedfahmy/Projects/CMMSX/.worktrees/security-backups-0.29.0`, branch `feature/security-backups-0.29.0` (superpowers:using-git-worktrees).
- No IP address is ever written to the database, the activity log or the application log (0.25.0 rule). The limiter keeps addresses in memory only.
- Every AWS-only behaviour sits behind a setting that is empty or off by default; with no settings the app behaves as 0.28.0 except for the security headers and the (generous) limits.
- The API-key limiter (`ApiKeyRules.AddApiKeyLimits`, `Api:RequestsPerMinute`) keeps working unchanged.
- Arabic for every new message, label and e-mail: web `web/src/locales/en.json` + `ar.json`, server `server/FourC.Cmms.Api/Localization/ar.json`.
- Tests first, watched failing. Server tests are `[SkippableFact]` with `Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.")`; organizations from `Loop.OrgAsync(fx)`. Never write test files with shell redirection; use the Write/Edit tools.
- Settings (exact names and defaults):
  - `Abuse:Enabled` true · `Abuse:ViewerAddressHeader` "" (AWS: `CloudFront-Viewer-Address`) · `Abuse:SignInFailures` 20 · `Abuse:SignInWindowMinutes` 15 · `Abuse:SignUpsPerHour` 5 · `Abuse:ResendsPerHour` 10 · `Abuse:ConfirmsPer15Minutes` 20 · `Abuse:AnswersPer15Minutes` 30
  - `Auth:RequireEmailConfirmation` (unset = on when e-mail is available, else off; the AWS stack passes it explicitly)
  - `Backups:S3Arn` "" (AWS: the native-backup bucket ARN) · `Platform:JobsAutoRun` true
- Copy (exact English; Arabic in the locale files):
  - 429 from the app: "Too many attempts from your network. Try again in {0} minutes."
  - WAF block page: "Too many requests from your network. Wait a few minutes and try again."
  - Sign-in while pending: "Confirm your e-mail address to start using CMX."
  - Bad or used link: "This link has expired or was already used."
- AWS values: second region `eu-west-1`; stacks `cmms-edge` (us-east-1) and `cmms-dr` (eu-west-1); WAF rate limits 300 (`/api/auth/*` writes), 100 (`/api/public/*`), 6000 (everything) per 5 minutes per address; `WafMode` `count` | `block`.
- Retention: point-in-time 35 days; daily snapshots 35 days; monthly 365 days; Ireland automated backups 7 days (fallback hourly copy kept 2 days); London files' old versions 90 days; Ireland Object Lock governance 35 days (files) and 90 days (exports); exports expire after 90 days; CloudTrail logs to Glacier Instant Retrieval at 90 days, expire at 365; app logs 90 days.
- Alarm thresholds exactly as the spec's section 8 table.
- Nothing that costs money beyond the spec's cost list: no Bot Control, NAT gateway, Security Hub, AWS Config or Shield Advanced.
- Commands (helper scripts; create them once in the session scratchpad `bin` if missing):
  - `cmx-test "<filter>"`: `docker run --rm --network cmmsx_default -v "$WT":/src -v cmx-nuget:/root/.nuget -w /src -e CMMS_TEST_SQL="Server=sql,1433;User Id=sa;Password=$SA;TrustServerCertificate=True" mcr.microsoft.com/dotnet/sdk:10.0 dotnet test server/FourC.Cmms.slnx --filter "$1"` (`$SA` read from `docker inspect cmmsx-sql-1`, never printed). Run one at a time (the SQL container has crashed under parallel full runs).
  - `cmx-ef <Name>`: same container, `dotnet tool install -g dotnet-ef --version 10.*` then `dotnet ef migrations add <Name> -p server/FourC.Cmms.Core -s server/FourC.Cmms.Api`.
  - `cmx-web`: `docker run --rm -v "$WT":/src -w /src/web node:22 sh -c "npm ci && npx vitest run && npm run build"`.
  - `cmx-lint`: `docker run --rm -v "$WT":/w -w /w python:3.12-slim sh -c "pip install -q cfn-lint && cfn-lint infra/aws/*.yml deploy/aws/bootstrap.yml && python scripts/check-boundaries.py"`.
  - `cmx-ci`: CI's exact server gate: `dotnet build server/FourC.Cmms.slnx -c Release` then `dotnet test server/FourC.Cmms.slnx -c Release --no-build`.

## Review Focus

1. Fifty people behind one factory address sign in correctly at 07:00 → none is limited (only failures count). Pinned in Task 1.
2. Every organization and person that exists before the upgrade signs in as before (no pending flag, no e-mail asked). Pinned in Task 3.
3. Someone who lost their link signs up again with the same address → they get a fresh link and the same "check your e-mail" answer, not "account exists"; "Send the link again" never reveals whether an address is registered. Pinned in Task 3.
4. A link clicked twice, after 48 hours, or after the organization was purged → the clear "expired or already used" message, never a 500. Pinned in Task 3.
5. On a host without the AWS settings, a forged `CloudFront-Viewer-Address` header doesn't change which address is limited, no export is attempted, and the purge never touches organizations not created by sign-up. Pinned in Tasks 1, 4 and 5.

---

### Task 1: Visitor address and per-address limits

**Files:**
- Create: `server/FourC.Cmms.Api/Infrastructure/VisitorAddress.cs`, `server/FourC.Cmms.Api/Infrastructure/AbuseLimits.cs`
- Modify: `server/FourC.Cmms.Api/Infrastructure/ApiKeyRules.cs` (OnRejected message), `server/FourC.Cmms.Api/Program.cs` (register), `server/FourC.Cmms.Api/Features/AuthEndpoints.cs` (sign-in guard, policies on register), `server/FourC.Cmms.Api/Features/RequestFeedback.cs` (policy on `pub.MapGet/MapPost("/{token}")`), `server/FourC.Cmms.Api/Localization/ar.json`
- Modify: `server/FourC.Cmms.Tests/ApiTests.cs` (`ApiFixture`: `b.UseSetting("Abuse:Enabled", "false")`)
- Test: `server/FourC.Cmms.Tests/AbuseLimitTests.cs` (with `public sealed class AbuseFixture : ApiFixture` setting `Abuse:Enabled=true`, `Abuse:SignInFailures=3`, `Abuse:SignUpsPerHour=2`, `Abuse:ViewerAddressHeader=CloudFront-Viewer-Address`), `server/FourC.Cmms.Tests/VisitorAddressTests.cs` (pure)

**Interfaces:**
- Produces: `static string VisitorAddress.Of(HttpContext http, IConfiguration config)` — header value when `Abuse:ViewerAddressHeader` is set and the header parses (`198.51.100.10:46532` → `198.51.100.10`; IPv6 with port → address without port), else `http.Connection.RemoteIpAddress?.ToString() ?? "unknown"`.
- Produces: `sealed class SignInGuard` (singleton) with `bool IsBlocked(string address, out TimeSpan retryAfter)` and `void Failed(string address)` (sliding count of failures per address in `IMemoryCache` over `Abuse:SignInWindowMinutes`).
- Produces: `static IServiceCollection AbuseLimits.AddAbuseLimits(this IServiceCollection, IConfiguration)`; policy names `AbuseLimits.SignUp = "abuse-signup"`, `Resend = "abuse-resend"`, `Confirm = "abuse-confirm"`, `Answer = "abuse-answer"`; `const string AbuseLimits.TooManyFromNetwork` (copy above). Policies are fixed windows partitioned by `VisitorAddress.Of`, no-limit partitions when `Abuse:Enabled` is false. Task 3 applies `Resend` and `Confirm` to its endpoints.

- [ ] **Step 1: Write the failing tests**

`VisitorAddressTests`: `Header_wins_when_configured("198.51.100.10:46532" → "198.51.100.10")`, `Ipv6_header_drops_port`, `Malformed_header_falls_back_to_connection`, `Header_ignored_when_not_configured` (forged header, setting empty → connection address).
`AbuseLimitTests`:
```csharp
// 3 failures from 203.0.113.7 → 4th attempt (even with the right password) is 429, body title contains "Too many attempts from your network", Retry-After present
Failed_sign_ins_from_one_address_are_limited()
// 30 successful sign-ins from one address → all 200
Successful_sign_ins_are_never_limited()
// failures from 203.0.113.7 don't limit 203.0.113.8
Limits_are_per_address()
// third sign-up within the hour from one address → 429; an API key's requests are unaffected (still limited only by Api:RequestsPerMinute)
Sign_ups_are_limited_and_keys_are_not()
// 429 on the requester answer link after Abuse:AnswersPer15Minutes requests
Answer_links_are_limited()
```
The tests set the address with `c.DefaultRequestHeaders.Add("CloudFront-Viewer-Address", "203.0.113.7:1234")`.

- [ ] **Step 2: Run them and watch them fail** — `cmx-test "FullyQualifiedName~AbuseLimitTests|FullyQualifiedName~VisitorAddressTests"`; expected: compile errors / failures.
- [ ] **Step 3: Implement** `VisitorAddress`, `SignInGuard`, `AddAbuseLimits` (via `services.Configure<RateLimiterOptions>`, so the existing global key limiter stays), the guard in `/auth/login` and `/auth/renew-password` (check before the password; `Failed` on a wrong password; locked accounts count as failures), `.RequireRateLimiting(...)` on register and both public answer endpoints, and `OnRejected` choosing `TooMany` for key requests and `TooManyFromNetwork` (minutes = ceil(seconds/60)) otherwise. Add `log.LogWarning("SignInFailed")` (no address, no e-mail) where a failure is recorded, for the CloudWatch metric in Task 9. Arabic for the new copy.
- [ ] **Step 4: Run them and watch them pass**, then `cmx-test "FullyQualifiedName~ApiKey"` to confirm keys are untouched.
- [ ] **Step 5: Commit** — `git commit -m "feat: per-address limits on sign-in, sign-up and answer links"`

### Task 2: Security headers

**Files:**
- Create: `server/FourC.Cmms.Api/Infrastructure/SecurityHeaders.cs`
- Modify: `server/FourC.Cmms.Api/Program.cs` (`app.UseSecurityHeaders()` right after `UseExceptionHandler()`, before static files)
- Test: `server/FourC.Cmms.Tests/SecurityHeaderTests.cs`

**Interfaces:**
- Produces: `static WebApplication SecurityHeaders.UseSecurityHeaders(this WebApplication app)`; `const string SecurityHeaders.ContentSecurityPolicy` = the spec's section 4 policy verbatim.

- [ ] **Step 1: Write the failing tests** — `Api_page_and_file_responses_carry_the_headers` (GET `/api/health`, `/`, an attachment download each have `Content-Security-Policy` == the constant, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Cross-Origin-Opener-Policy: same-origin`, `Permissions-Policy: camera=(self), microphone=(self), geolocation=(self)`); `Hsts_only_over_https` (request with `CloudFront-Forwarded-Proto: https` has `Strict-Transport-Security: max-age=31536000`; plain request has none); `Attachment_download_keeps_its_own_headers` (existing nosniff and content type unchanged).
- [ ] **Step 2: Run and watch fail** — `cmx-test "FullyQualifiedName~SecurityHeaderTests"`.
- [ ] **Step 3: Implement** the middleware (`OnStarting` so every response gets them; don't overwrite a header an endpoint already set).
- [ ] **Step 4: Run and watch pass.**
- [ ] **Step 5: Browser pass with the policy enforced** (local `docker compose up --build`): every web page, the technician view `/tech/`, print previews, labels, the requester answer page, English and Arabic. Widen the policy only for a real break, each exception commented in `SecurityHeaders.cs` and its test updated. Check the Android app against the deployed branch build later (Task 13).
- [ ] **Step 6: Commit** — `git commit -m "feat: browser security headers on every response"`

### Task 3: Sign-up e-mail check

**Files:**
- Modify: `server/FourC.Cmms.Core/Tenancy/Tenant.cs` (`DateTimeOffset? SignUpPendingSince`, `string? SignUpTokenHash` max 64, `DateTimeOffset? SignUpTokenExpires`), `server/FourC.Cmms.Core/Data/CmmsDbContext.cs`, migration `SignUpConfirmation` (generated by `cmx-ef SignUpConfirmation`; no data change: existing rows stay null)
- Create: `server/FourC.Cmms.Api/Features/SignUpConfirmation.cs`
- Modify: `server/FourC.Cmms.Api/Features/AuthEndpoints.cs` (register, login, renew-password, two new endpoints), `server/FourC.Cmms.Api/Features/EmailOutbox.cs` (`QueueAsync(..., bool system = false)`: when true, skip `AllowedFor` and `EmailEnabled`, keep `Available` and address checks; new `SignUpConfirmationAsync`), `server/FourC.Cmms.Api/Features/EmailTemplates.cs` (`ConfirmSignUp`), `Localization/ar.json`
- Modify: `server/FourC.Cmms.Tests/ApiTests.cs` (`ApiFixture`: `b.UseSetting("Auth:RequireEmailConfirmation", "false")`)
- Test: `server/FourC.Cmms.Tests/SignUpConfirmationTests.cs` (fixture `ConfirmFixture : ApiFixture` with `Auth:RequireEmailConfirmation=true`)

**Interfaces:**
- Consumes: `AbuseLimits.Resend`, `AbuseLimits.Confirm` (Task 1).
- Produces: `static class SignUpConfirmation` with `TimeSpan LinkLife = 48h`, `string NewToken()` (32 random bytes, URL-safe base64), `string Hash(string token)` (SHA-256 hex), `bool Required(IConfiguration config, EmailOutbox mail)`.
- Produces: `POST /api/auth/register` → `{ ok: true, confirm: true }` and **no sign-in** when required (else as today); `POST /api/auth/confirm-email { token }` → signs the owner in, `{ ok: true }`, or 400 with the bad-link copy; `POST /api/auth/resend-confirmation { email }` → always `{ ok: true }`; sign-in/renew of a person in a pending organization → 403 `{ title: <pending copy>, code: "email-unconfirmed" }` (same shape as `RenewFirst`).
- Produces: `Task EmailOutbox.SignUpConfirmationAsync(CmmsDbContext db, Tenant tenant, AppUser owner, string token, CancellationToken ct)` (link `{App:PublicUrl}/confirm-email?token=…`, language = the owner's), `static EmailContent EmailTemplates.ConfirmSignUp(string org, string link)`.

- [ ] **Step 1: Write the failing tests**
```csharp
Sign_up_waits_for_the_email_link()          // register → confirm:true; /api/auth/me is 401; one queued e-mail to the address containing "/confirm-email?token="
Sign_in_before_confirming_is_refused()      // login with right password → 403, code "email-unconfirmed"
Link_confirms_once_and_signs_in()           // POST confirm-email with token → 200, /api/auth/me 200; same token again → 400 "This link has expired or was already used."
Expired_link_is_refused()                   // set SignUpTokenExpires to now-1m in the DB → 400, organization still pending
Signing_up_again_sends_a_new_link()         // second register with the same address while pending → { ok, confirm:true }, a new token works, the old one doesn't
Resend_never_reveals_an_account()           // resend for an unknown address and for a confirmed one → 200 { ok:true }, no e-mail queued
Existing_and_admin_added_people_need_no_link() // OrgAsync (confirmation off) then switch on via ConfirmFixture DB update: admin-added user signs in fine; tenants with SignUpPendingSince null are never asked
Confirmation_mail_ignores_the_org_allow_list()  // Email:AllowedOrganizations = "CMX" → confirmation still queued
```
- [ ] **Step 2: Run and watch fail** — `cmx-test "FullyQualifiedName~SignUpConfirmationTests"`.
Implementation note: the pending state lives on the **tenant** (only self sign-up sets it), not on `AspNetUsers.EmailConfirmed`.
That gives the spec's behaviour (existing, admin-added and imported people never asked; only the sign-up owner waits) without a
data migration of users or changes to every place that creates a person.

- [ ] **Step 3: Implement** the entity fields and migration, `SignUpConfirmation`, the register branch (create as today, then set `SignUpPendingSince=now`, hash, expiry; queue the e-mail in the same save; for a pending address, refresh token and resend instead of "already exists"), the login/renew gate (after the password check, before `CompleteSignInAsync`), the two endpoints with their rate-limit policies, the `system` flag, the template (English and Arabic).
- [ ] **Step 4: Run and watch pass**, then `cmx-test "FullyQualifiedName~ApiTests|FullyQualifiedName~Loop"` to confirm the fixture change keeps sign-up-based tests green.
- [ ] **Step 5: Commit** — `git commit -m "feat: new organizations confirm their e-mail before use"`

### Task 4: Purge unconfirmed sign-ups and the platform jobs service

**Files:**
- Create: `server/FourC.Cmms.Api/Features/PendingSignUps.cs`, `server/FourC.Cmms.Api/Features/PlatformJobs.cs`
- Modify: `server/FourC.Cmms.Api/Program.cs` (register `PlatformJobs` as a hosted service), `ApiFixture` (`Platform:JobsAutoRun=false`)
- Test: `server/FourC.Cmms.Tests/PendingSignUpTests.cs`

**Interfaces:**
- Produces: `static TimeSpan PendingSignUps.Keep = 7 days`; `static Task<int> PendingSignUps.PurgeAsync(CmmsDbContext db, DateTimeOffset now, CancellationToken ct)` — deletes, in one transaction, every row of every tenant-owned table for tenants with `SignUpPendingSince < now - Keep`, then the tenant; writes one log line `PendingSignUpRemoved` per tenant (no names, no addresses); returns the count.
- Produces: `sealed class PlatformJobs(IServiceScopeFactory scopes, IConfiguration config, ILogger<PlatformJobs> log) : BackgroundService` — returns at once when `Platform:JobsAutoRun` is false; otherwise once a day runs `PendingSignUps.PurgeAsync` and `DatabaseSize.MeasureAsync` (Task 5), and on Sundays after 02:00 UTC `NativeBackup.RunAsync` (Task 5). Exposes `Task RunDailyAsync(DateTimeOffset now, CancellationToken ct)` for tests and
  `static bool PlatformJobs.ExportDue(DateTimeOffset nowUtc, DateOnly? lastExport)` (true on a Sunday at or after 02:00 UTC when
  `lastExport` isn't that Sunday).

Deletion order: collect the EF model's entity types that have a `TenantId` property (plus `AppUser`), order them so dependents go first (topological sort over the model's foreign keys), and `ExecuteDeleteAsync` each with `IgnoreQueryFilters()` and `TenantId == id`. This is the only place that ignores filters, and it is keyed by the one tenant id.

- [ ] **Step 1: Write the failing tests** — `Unconfirmed_sign_ups_older_than_7_days_are_removed` (pending since now-8d → no row with that TenantId left in any tenant table; e-mail free to sign up again; its old link now returns 400 "This link has expired or was already used."); `Recent_and_confirmed_organizations_stay` (pending since now-6d, confirmed, and an organization created by `TenantProvisioning` directly all untouched, row counts unchanged); `Removal_logs_no_personal_data` (captured log contains `PendingSignUpRemoved` and neither the name nor the address).
- [ ] **Step 2: Run and watch fail** — `cmx-test "FullyQualifiedName~PendingSignUpTests"`.
- [ ] **Step 3: Implement** `PendingSignUps` and the `PlatformJobs` loop (start delay 120 s, then hourly ticks deciding daily/weekly work by UTC date; single instance as today).
- [ ] **Step 4: Run and watch pass.**
- [ ] **Step 5: Commit** — `git commit -m "feat: remove sign-ups never confirmed after 7 days"`

### Task 5: Database size and weekly native export

**Files:**
- Create: `server/FourC.Cmms.Api/Features/DatabaseSize.cs`, `server/FourC.Cmms.Api/Features/NativeBackup.cs`
- Modify: `server/FourC.Cmms.Api/Features/PlatformJobs.cs` (call both)
- Test: `server/FourC.Cmms.Tests/PlatformJobTests.cs`, `server/FourC.Cmms.Tests/NativeBackupTests.cs` (pure)

**Interfaces:**
- Produces: `static Task<long?> DatabaseSize.MeasureAsync(CmmsDbContext db, ILogger log, CancellationToken ct)` — data files only (`SELECT SUM(CAST(size AS bigint)) * 8 / 1024 FROM sys.database_files WHERE type = 0`), logs `DatabaseSize mb={mb}`; returns null (and logs nothing) on a non-SQL Server provider.
- Produces: `static string NativeBackup.ObjectArn(string bucketArn, string database, DateOnly date)` → `{bucketArn}/native/{database}-{yyyy-MM-dd}.bak`; `static Task NativeBackup.RunAsync(CmmsDbContext db, IConfiguration config, ILogger log, DateOnly date, CancellationToken ct)` — does nothing when `Backups:S3Arn` is empty; else `exec msdb.dbo.rds_backup_database @source_db_name=<current db>, @s3_arn_to_backup_to=<ObjectArn>, @overwrite_s3_backup_file=1, @type='FULL'`, polls `msdb.dbo.rds_task_status @task_id=…` every 60 s up to 3 hours, logs `BackupExport status=SUCCESS file=<key>` or `BackupExport status=ERROR <task info>` (no secrets).

- [ ] **Step 1: Write the failing tests** — `ObjectArn_builds_the_key` (`arn:aws:s3:::b`, `Cmms`, 2026-11-29 → `arn:aws:s3:::b/native/Cmms-2026-11-29.bak`); `Size_is_measured_and_logged` (on the test database: returns > 0 and a `DatabaseSize mb=` line); `Export_is_skipped_without_a_bucket` (empty setting → no SQL sent, no log line); `Export_due_only_on_sunday_after_two` (`PlatformJobs.ExportDue`: Saturday 23:00 → false; Sunday 01:59 → false; Sunday 02:00 with no last export → true; Sunday 05:00 with last export that Sunday → false).
- [ ] **Step 2: Run and watch fail** — `cmx-test "FullyQualifiedName~PlatformJobTests|FullyQualifiedName~NativeBackupTests"`.
- [ ] **Step 3: Implement** both and wire them into `PlatformJobs`.
- [ ] **Step 4: Run and watch pass.**
- [ ] **Step 5: Commit** — `git commit -m "feat: daily database size check and weekly native export"`

### Task 6: Web screens for the e-mail check and the limits

**Files:**
- Modify: `web/src/pages/AuthPages.tsx` (`RegisterPage` "check your e-mail" state; `LoginPage` pending state with "Send the link again"; new `ConfirmEmailPage`), `web/src/App.tsx` (route `/confirm-email`, public), `web/src/pages/work/RequestFeedbackPage.tsx` (show a 429 title), `web/src/locales/en.json`, `web/src/locales/ar.json`
- Test: `web/src/pages/AuthPages.test.tsx` (new), `web/src/pages/work/RequestFeedbackPage.test.tsx` (new or existing)

**Interfaces:**
- Consumes: the Task 3 endpoints and response shapes; 429 bodies from Task 1.
- Locale keys: `auth.checkEmailTitle`, `auth.checkEmailText` ("We sent a link to {{email}}. Open it within 48 hours to start using CMX."), `auth.unconfirmed`, `auth.resendLink`, `auth.linkSent`, `auth.confirming`, `auth.linkInvalid`.

- [ ] **Step 1: Write the failing tests** — register with `{ confirm: true }` shows `auth.checkEmailTitle` and the address, and doesn't navigate; login 403 `email-unconfirmed` shows `auth.unconfirmed` and a resend button that posts `/auth/resend-confirmation` then shows `auth.linkSent`; `/confirm-email?token=x` posts the token and navigates home on 200, shows `auth.linkInvalid` on 400; a 429 on login and on the answer page shows the server's title text.
- [ ] **Step 2: Run and watch fail** — `cmx-web`.
- [ ] **Step 3: Implement** the screens and locale strings (English and Arabic, `locales.test.ts` keeps both files in step).
- [ ] **Step 4: Run and watch pass** — `cmx-web` (vitest and build).
- [ ] **Step 5: Commit** — `git commit -m "feat: web screens for e-mail confirmation and too-many-attempts"`

### Task 7: Bootstrap: new stacks allowed, the deny guard and the permissions boundary

**Files:**
- Modify: `deploy/aws/bootstrap.yml`, `deploy/aws/setup.sh` (print what this version adds; unchanged usage)
- Create: `scripts/check-boundaries.py`
- Modify: `.github/workflows/ci.yml` (`infra` job runs `python scripts/check-boundaries.py`)

**Interfaces:**
- Produces (bootstrap outputs and names): managed policy `cmms-boundary` at path `/cmms/` (ARN output `BoundaryPolicyArn`); the deploy role may run the existing CloudFormation actions on `stack/cmms-edge/*` in `us-east-1` and `stack/cmms-dr/*` in `eu-west-1` (plus their change sets) and pass the execution role there; the execution role gets an inline policy `backup-guard` of explicit denies; its `app-roles` policy requires `iam:PermissionsBoundary` = `BoundaryPolicyArn` on `iam:CreateRole`, `iam:PutRolePermissionsBoundary`, `iam:PutRolePolicy`, `iam:AttachRolePolicy`, and denies `iam:DeleteRolePermissionsBoundary`.
- Deny list (both `backup-guard` and `cmms-boundary`), destructive actions only so that first creation still works:
  `backup:DeleteRecoveryPoint`, `backup:UpdateRecoveryPointLifecycle`, `backup:DeleteBackupVault`,
  `backup:DeleteBackupVaultAccessPolicy`, `backup:DeleteBackupVaultLockConfiguration`, `rds:DeleteDBInstanceAutomatedBackup`,
  `rds:StopDBInstanceAutomatedBackupsReplication`, `cloudtrail:DeleteTrail`, `cloudtrail:StopLogging`; and on the Ireland buckets
  and the CloudTrail bucket: `s3:BypassGovernanceRetention`, `s3:DeleteObject`, `s3:DeleteObjectVersion`, `s3:DeleteBucket`.
  Configuration writes (`Put…LockConfiguration`, `PutLifecycleConfiguration`, vault access policy) stay allowed because creation
  needs them; they can't touch what is already stored: Object Lock retention and the vault's recovery points outlive any later
  setting, and deleting them is denied. The London automated backups stay changeable by templates; they are the first line, and
  the locked Ireland copies are the last. The builder checks each action name against the AWS service authorization reference.
- `scripts/check-boundaries.py <templates…>`: parses each template with cfn-lint's decoder; fails if any `AWS::IAM::Role` with `Path: /cmms/` lacks `PermissionsBoundary`.

- [ ] **Step 1: Write the failing check** — run `cmx-lint` with a role in `infra/aws/cmms.yml` lacking a boundary → `check-boundaries.py` exits 1 naming it (it fails today for every existing role).
- [ ] **Step 2: Implement** the bootstrap changes and the script.
- [ ] **Step 3: Verify** — `cmx-lint` passes for `bootstrap.yml`; `check-boundaries.py` still fails for `cmms.yml` until Task 9 adds boundaries (expected; note it in the commit message).
- [ ] **Step 4: Commit** — `git commit -m "infra: bootstrap allows edge and DR stacks, guards backups, requires a boundary"`

### Task 8: The recovery stack (Ireland) and the edge stack (US East)

**Files:**
- Create: `infra/aws/dr.yml`, `infra/aws/edge.yml`

**Interfaces:**
- `dr.yml` (deployed as `cmms-dr` in eu-west-1). Parameters: `SourceAccountId`. Resources: KMS key `DrKey` (rotation on; key policy lets RDS and AWS Backup in this account use it); `DrVault` (AWS Backup vault encrypted with `DrKey`, `LockConfiguration` governance: `MinRetentionDays: 7`, `MaxRetentionDays: 400`); `FilesCopyBucket` and `BackupsCopyBucket` (versioned, SSE-S3, public access blocked, HTTPS-only policy, `ObjectLockEnabled: true` with default retention GOVERNANCE 35 and 90 days; `BackupsCopyBucket` lifecycle expires objects at 90 days). Outputs: `DrVaultArn`, `DrKeyArn`, `FilesCopyBucketArn`, `BackupsCopyBucketArn`.
- `edge.yml` (deployed as `cmms-edge` in us-east-1). Parameters: `WafMode` (`count`|`block`, default `count`), `AlertEmail`, `CreateGuardDutyDetector` (`true`|`false`, default `true`). Resources: `WebAcl` (scope CLOUDFRONT, rules and order exactly as spec section 2, rate rules scoped by URI path `starts with /api/auth/` and method POST, `/api/public/`, and all; every Block rule switches to Count when `WafMode=count`; custom 429 response body with the WAF copy in English and Arabic), `AlertTopic` + e-mail subscription, alarm `WafBlocking` (`BlockedRequests` > 1000 in 5 minutes), `Detector` (condition `CreateGuardDutyDetector`). Outputs: `WebAclArn`.

- [ ] **Step 1: Write both templates.**
- [ ] **Step 2: Verify** — `cmx-lint` passes for both; `check-boundaries.py` passes for both (no `/cmms/` roles without a boundary).
- [ ] **Step 3: Commit** — `git commit -m "infra: recovery stack in Ireland and WAF edge stack in US East"`

### Task 9: The app stack: internal load balancer, backups, detection and alarms

**Files:**
- Modify: `infra/aws/cmms.yml`

**Interfaces:**
- Consumes: `BoundaryPolicyArn` (Task 7), `DrVaultArn`, `DrKeyArn`, `FilesCopyBucketArn`, `BackupsCopyBucketArn`, `WebAclArn` (Task 8) as new parameters of the same names; new parameters `AlertEmail`, `RequireEmailConfirmation` (`true`|`false`, default `false`), `CreateTrail` and `CreateGuardDutyDetector` (`true`|`false`, default `true`).
- Network and edge: `LoadBalancer` replaced by a new resource `InternalLoadBalancer` (`Scheme: internal`, subnets `PrivateSubnet1/2`), `InternalTargetGroup`, `InternalListener`; `VpcOrigin` (`AWS::CloudFront::VpcOrigin`, HTTP 80 to the internal load balancer); the distribution's origin uses `VpcOriginConfig`; `WebACLId: !Ref WebAclArn`; `OriginRequestPolicy` headers add `CloudFront-Viewer-Address`; the load balancer security group admits port 80 from CloudFront's VPC-origin source as AWS documents it; `Service.LoadBalancers` point at the new target group. The old load balancer, target group and listener are removed in the same deploy.
- App environment: `Abuse__ViewerAddressHeader=CloudFront-Viewer-Address`, `Auth__RequireEmailConfirmation=!Ref RequireEmailConfirmation`, `Backups__S3Arn=!GetAtt BackupsBucket.Arn`. Log group retention 90.
- Backups: `Database.BackupRetentionPeriod: 35`; `AutomaticBackupReplicationRegion: eu-west-1`, `AutomaticBackupReplicationKmsKeyId: !Ref DrKeyArn`, `AutomaticBackupReplicationRetentionPeriod: 7` (if cfn-lint or the first deploy rejects them for `sqlserver-ex`, use the fallback: an hourly rule in `BackupPlan` copying to `DrVaultArn`, kept 2 days, and record it in the changelog); `OptionGroup` (`SQLSERVER_BACKUP_RESTORE` with `NativeBackupRole`); `BackupsBucket` (versioned, encrypted, lifecycle 90 days, replication to `BackupsCopyBucketArn` with `ReplicationRole`); `FilesBucket` noncurrent expiry 14 → 90 days and replication to `FilesCopyBucketArn`; `BackupVault` (London) with an access policy denying `backup:DeleteRecoveryPoint` to everyone but the root user; `BackupPlan` (daily 01:00 UTC keep 35 days, monthly on the 1st keep 365 days, each copying to `DrVaultArn` with the same retention), `BackupSelection` (the database; `BackupRole`); `RestoreTestingPlan` (monthly, latest daily recovery point) and `RestoreTestingSelection` (db.t3.micro, `PrivateSubnet1/2`, a security group with no inbound rules, delete after 4 hours).
- Detection: `TrailBucket` + `Trail` (condition `CreateTrail`; multi-region, global events, log file validation, lifecycle per Global Constraints); `Detector` (condition `CreateGuardDutyDetector`; S3 logs, ECS Fargate runtime monitoring with agent management) and `MalwareProtectionPlan` for `FilesBucket` (tagging only; `MalwareRole`).
- Alarms: `AlertTopic` + e-mail subscription; the alarms in spec section 8 (London ones); metric filters on the log group for `DatabaseSize mb=` (value), `SignInFailed` (count), `BackupExport status=ERROR` (count) and `BackupExport status=SUCCESS` (count, alarm when missing for 8 days); EventBridge rules for AWS Backup backup and copy job states FAILED, ABORTED, EXPIRED, every restore job state change of the restore testing plan (COMPLETED and FAILED, so each monthly result is e-mailed with its duration), and GuardDuty findings with severity ≥ 4, targeting `AlertTopic` (topic policy allows EventBridge).
- Every role under `/cmms/` (existing task and execution roles and the new `NativeBackupRole`, `ReplicationRole`, `BackupRole`, `MalwareRole`) has `PermissionsBoundary: !Ref BoundaryPolicyArn`.

- [ ] **Step 1: Make the changes in three commits** (network and edge; backups; detection and alarms), running `cmx-lint` after each.
- [ ] **Step 2: Verify** — `cmx-lint` passes, including `check-boundaries.py` for all templates.
- [ ] **Step 3: Commit** each part — `infra: internal load balancer behind a CloudFront VPC origin and WAF`, `infra: layered backups with copies in Ireland and a monthly restore test`, `infra: CloudTrail, GuardDuty and alarms to an on-call inbox`.

### Task 10: The deploy workflow

**Files:**
- Modify: `.github/workflows/deploy-aws.yml`

**Interfaces:**
- Consumes: Tasks 7–9 templates and outputs. New repository variables: `CMMS_ALERT_EMAIL`, `CMMS_WAF_MODE` (default `count`), `CMMS_REQUIRE_EMAIL_CONFIRMATION` (default `false`), `CMMS_CREATE_TRAIL` and `CMMS_CREATE_GUARDDUTY` (default `true`).

- [ ] **Step 1: Implement**, in order:
  - build the image with `load: true`, scan it with Trivy (pinned image tag), stop on CRITICAL with a fix available, then push;
  - deploy `cmms-dr` to eu-west-1, then `cmms-edge` to us-east-1, with `aws cloudformation deploy --role-arn <execution role> --no-fail-on-empty-changeset`, printing each change set (`--no-execute-changeset`, `describe-change-set`, then `execute-change-set` and wait);
  - read their outputs and the bootstrap's `BoundaryPolicyArn`; if any is missing, fail with "Re-run deploy/aws/setup.sh in CloudShell (0.29.0 adds two stacks), then re-run this workflow.";
  - deploy `cmms-app` as today with the new parameters, printing its change set the same way.
- [ ] **Step 2: Verify** — `actionlint` (pinned, via docker) passes on the workflow; `cmx-lint` still passes.
- [ ] **Step 3: Commit** — `git commit -m "ci: deploy recovery and edge stacks first, scan the image, show change sets"`

### Task 11: Supply-chain checks in CI

**Files:**
- Create: `scripts/security-check.sh`, `.github/security-allowlist.txt` (format per line: `<id>|<reason>|<expires YYYY-MM-DD>`, comments with `#`)
- Modify: `.github/workflows/ci.yml` (new job `security`)
- Modify: any package found vulnerable with a fix available (server `*.csproj`, `web/package*.json`, `mobile/package*.json`)

**Interfaces:**
- `scripts/security-check.sh`: runs `dotnet list server/FourC.Cmms.slnx package --vulnerable --include-transitive --format json`, `npm audit --omit=dev --audit-level=high --json` in `web` and `mobile`, and gitleaks (pinned `zricethezav/gitleaks` image, `detect --source . --no-git` on the checkout plus `git` mode on the pushed range); exits 1 for any high or critical finding with a fix, or any secret, whose id isn't in the allowlist with a future expiry; prints each finding with its id.

- [ ] **Step 1: Write the failing check** — add a known-vulnerable package version to `web/package.json` on a scratch commit and a fake AWS key in a scratch file; run the script locally (docker images) → exits 1 naming both. Revert the scratch commit.
- [ ] **Step 2: Run on the real tree**; update packages with fixes; add any unfixable finding to the allowlist with a reason and an expiry within 90 days.
- [ ] **Step 3: Verify** — script exits 0; `cmx-ci` and `cmx-web` still pass after the package updates.
- [ ] **Step 4: Commit** — `git commit -m "ci: block releases with known vulnerable packages or leaked secrets"`

### Task 12: Runbook, deploy notes and changelog

**Files:**
- Create: `docs/SECURITY_AND_BACKUPS.md`
- Modify: `docs/DEPLOY_AWS.md`, `CHANGELOG.md` (0.29.0 entry: what changes for people — sign-up confirmation, too-many-attempts messages — and for operators; which Ireland backup method shipped)

- [ ] **Step 1: Write** the runbook per spec section 10: what protects CMX; each alarm and what to do; restore a point in time (London and Ireland), a snapshot, a single file (S3 version), a native export; rebuild in Ireland; lift the governance lock (console admin only); the quarterly rehearsal; rollback of the load-balancer cutover (previous commit's template redeploys the public load balancer; CloudFormation rollback on failure).
- [ ] **Step 2: Update DEPLOY_AWS.md**: the two new stacks, the one-time `setup.sh` re-run, the five variables, confirming SNS e-mails, checking SES production access before setting `CMMS_REQUIRE_EMAIL_CONFIRMATION=true`, moving `CMMS_WAF_MODE` to `block`, monthly cost (about $30–50).
- [ ] **Step 3: Commit** — `git commit -m "docs: security and backups runbook, deploy notes, changelog"`

### Task 13: Final gate, release and live checks

- [ ] **Step 1: Full gates** — `cmx-ci` (CI's exact server commands), `cmx-web`, `cmx-lint`, `scripts/security-check.sh`; all pass.
- [ ] **Step 2: Whole-branch review** (fresh reviewer on the most capable model) against the spec; fix Important findings test-first; list minors.
- [ ] **Step 3: Merge and release** — merge to `main`, `scripts/release.sh 0.29.0` (ask the user not to commit in GitHub Desktop meanwhile); the user pushes.
- [ ] **Step 4: Expected first deploy failure and setup** — the deploy stops with the re-run-setup message; the user re-runs `setup.sh` in CloudShell (at a quiet hour), sets the variables, and re-runs the workflow.
- [ ] **Step 5: Live checks (spec "Done when")** with the user:
  - both addresses report 0.29.0; a direct request to the old load balancer address fails; securityheaders.com grade A or better;
  - the Android app signs in, scans, dictates and uploads with the CSP on;
  - after a day or two in `count`, sampled requests show no real users caught → `CMMS_WAF_MODE=block`, re-run; then an injection string and a sign-in burst are blocked while an upload and an Excel import pass;
  - the first daily snapshot is in the Ireland vault (locked); the first Sunday export is in both buckets; Ireland shows replicated automated backups within 15 minutes (or the hourly fallback within 2 hours);
  - the restore test run on demand completes and its e-mail arrives; `set-alarm-state` on one alarm and a GuardDuty sample finding both reach the inbox;
  - the deploy role is refused when trying to delete a recovery point and a locked file copy;
  - SES production access confirmed → `CMMS_REQUIRE_EMAIL_CONFIRMATION=true`, re-run; a test sign-up gets its e-mail and can't sign in before clicking it.
- [ ] **Step 6: Tag and record** — tag `v0.29.0`, update memory and the roadmap page (0.29.0 live).
