# 0.29.0 — Platform shield and automatic backups

First release of the "secure foundation" wave in the Maximo parity roadmap (artifact P6YWMzRa8oCcQfQdVdSwe7, release card
0.29.0). Company sign-in (SSO) is parked (2026-11-25), so this release takes the next number after 0.28.0. Picked in the
conversation (2026-11-25): **CloudTrail and GuardDuty** (no Security Hub), **keep SQL Server Express in one zone with a size
alarm**, **open sign-up with an e-mail check**, **everything as code** in CloudFormation, and a **CloudFront VPC origin** so only
CloudFront can reach the load balancer.

## Why

The user asked for CMX and its database to be secured against attacks and for automatic app and database backups (2026-11-24).
A review of the live stack (`infra/aws/cmms.yml`) and the code found a sound base and a set of gaps that attackers try first or
that would turn an incident into data loss:

- No web application firewall. Sign-in and sign-up have no limit beyond the per-account lockout (8 failures,
  10 minutes), and sign-up is open to anyone with no e-mail check.
- Any CloudFront distribution in any AWS account can reach the load balancer (the security group allows CloudFront's whole
  address range), and CloudFront talks to it over plain HTTP.
- No browser security headers (CSP, HSTS, frame protection), except `nosniff` on file downloads.
- Backups stay in one region and one account for 7 days, nothing stops them being deleted, and no restore has ever been tested.
- No record of who did what in the AWS account, and nothing watches for stolen keys, malicious addresses or malware in uploads.
- CI doesn't look for vulnerable libraries or leaked secrets.
- SQL Server Express caps each database at 10 GB, and all organizations share one database. Nothing warns before the cap.

Success looks like: a burst of password guesses from one address is stopped at the edge; a direct request to the load balancer
gets nowhere; a bot can create an organization but can't use it; every night's database and every file exist in a second
region, locked against deletion; once a month CMX restores last night's backup by itself and e-mails cworks the result; and when
something is wrong (an attack, a failed backup, the database nearing 10 GB, the site failing) an alarm reaches a person.

## Goals

1. **Edge protection:** an AWS WAF web ACL on the CloudFront distribution with managed rules and per-address rate limits.
2. **No way around the edge:** the load balancer becomes internal; CloudFront reaches it through a VPC origin.
3. **App-level limits** on sign-in, sign-up, e-mail confirmation and the requester answer links, keyed by the visitor's real address, that
   work on any host (AWS, Azure, local), plus browser security headers on every response.
4. **E-mail check on sign-up:** a new organization can't be used until its owner confirms their e-mail address.
5. **Automatic, layered backups** with a second-region copy, a deletion lock, and a monthly automatic restore test.
6. **Detection and alarms:** CloudTrail, GuardDuty and CloudWatch alarms that reach an on-call inbox.
7. **Supply-chain checks in CI** that stop a release with a known critical vulnerability or a leaked secret.
8. **A database size alarm** at 7 GB, well before the 10 GB Express cap.
9. **A runbook** for restoring and for rebuilding in another region.

## Non-goals

- **SSO and SCIM** (parked; SCIM is in 0.30.0 and waits for SSO).
- **Client-facing security controls** (two-step sign-in, allowed networks, session limits, access review, organization export,
  trust page): 0.30.0.
- **Security Hub and AWS Config** (declined for now; revisit for certification).
- **A bigger or Multi-AZ database, or a second app task** (declined: cost). The app stays one Fargate task, the database
  SQL Server Express in one zone.
- **Private subnets for the app task**: they would need a NAT gateway (the app calls Anthropic, Firebase and SES). The task's
  security group already refuses everything except the load balancer.
- **AWS WAF Bot Control** (paid add-on); the free managed rule groups and rate limits cover this release.
- **Acting on malware findings inside the app** (refusing to serve a flagged file). This release alerts on them only.
- **Self-service password reset and a public request portal.** Neither exists today (administrators reset passwords in
  Settings; QR labels need sign-in; ROADMAP lists the portal as "Next"). Both are features of their own; the limits here cover
  the anonymous endpoints that do exist.
- **Storing visitors' IP addresses.** 0.25.0 decided that no address is kept (`AuthEndpoints.Device`, `UserSession`). That stays:
  the visitor-address helper feeds the rate limiter only, which holds addresses in memory for its window and nowhere else.
- **Changing which organizations may send e-mail** (`Mail:AllowedOrganizations` stays as it is).
- **Certification work** (ISO 27001, SOC 2) and external penetration testing: operations track, not this release.

## Anti-goals

- **Locking out real users.** Factories and hotels put many people behind one internet address; a shift starting at 07:00 can
  bring 50 sign-ins from one address in minutes. Limits must count *failures* per address for sign-in, not successes, and be
  generous for normal traffic.
- **Breaking uploads or imports.** WAF's body-size and XSS rules must not block attachments, Excel imports or long texts.
- **A backup that can't be restored**, or a lock that can't be undone by mistake in the first months.
- **Silent failures.** A failed backup, copy or restore test always reaches a person.
- **Downtime during the switch to the internal load balancer** beyond a few minutes, and none for the database.
- **Surprise cost.** The release adds roughly $25–45 a month; nothing in it should cost more without being in this document.

## Constraints

- AWS account 514336067871, app region **eu-west-2** (London). The app stack is `cmms-app`; the setup stack `cmms-github`
  (`deploy/aws/bootstrap.yml`, run by the user in CloudShell with `deploy/aws/setup.sh`) holds the GitHub OIDC deploy role, the
  ECR repository (scan on push already on) and the CloudFormation execution role (PowerUserAccess plus IAM under `/cmms/`).
- The deploy role may only manage the stack named by `AppStackName` in its own region. New stacks in other regions need a
  one-time change to `bootstrap.yml` and a re-run of `setup.sh` by the user.
- A CloudFront web ACL must live in **us-east-1**. The ACM certificate for `cmx.cworks.com.eg` is already there.
- Every green CI build of `main` deploys automatically (`.github/workflows/deploy-aws.yml`).
- One Fargate task (`DesiredCount: 1`): the PM scheduler and start-up migrations assume a single instance.
- RDS SQL Server **Express**, db.t3.micro, 20 GB storage, encrypted, private, `MultiAZ: false`, `BackupRetentionPeriod: 7`,
  `DeletionPolicy: Snapshot`. Files in a private, versioned, encrypted S3 bucket with `DeletionPolicy: Retain`.
- E-mail goes through Amazon SES for `cworks.com.eg`. SES production access was requested on 2026-11-19 and was "under review"
  at last check; in the sandbox SES only delivers to verified addresses. **The sign-up e-mail check needs production access.**
- The Android app is a shell around the live site (`server.url` = `https://cmx.cworks.com.eg/tech/`); security headers and CSP
  apply to it too. It uses the camera (QR, photos) and the microphone (dictation) inside that window.
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` and `ForwardedHeaders__ProtoHeaderName=CloudFront-Forwarded-Proto` are set. With the
  default forward limit, `X-Forwarded-For` gives the CloudFront edge's address, not the visitor's.
- `infra/azure/main.bicep` and `deploy-azure.yml` exist; app-level changes must keep working there (no AWS-only code paths
  without a setting that switches them off).
- Existing rate limiter: `ApiKeyRules.AddRateLimiter` sets a global partitioned limiter for API keys only ("people" partition
  is unlimited). The new limits must not disturb API keys.

## Approaches

**How the AWS side gets built** (picked: A).

- **A. Everything as code (picked).** Small CloudFormation stacks, all deployed by the existing GitHub workflow: the app stack in
  London gains the backup plan, alarms, CloudTrail, GuardDuty and the VPC origin; an edge stack in us-east-1 holds the web ACL;
  a recovery stack in a second region holds the locked backup vault and the file copies. The user re-runs `setup.sh` once.
  Reviewed in git, repeatable, and the whole system can be rebuilt in another region from the same files, which the backup plan
  relies on.
- **B. Switch the AWS parts on by hand in the console** following a checklist, with only the app changes in code. Faster to start,
  but nothing records the setup, it drifts, and a rebuild means redoing it by hand. Rejected.

**How only CloudFront reaches the load balancer** (picked: VPC origin).

- **CloudFront VPC origin (picked).** The load balancer becomes internal (no public address) in two new private subnets;
  CloudFront reaches it over AWS's network through an `AWS::CloudFront::VpcOrigin`. No extra cost, no certificate or DNS work,
  and bypassing the edge becomes impossible rather than just guarded.
- **Secret header plus HTTPS to the load balancer.** CloudFront adds a secret header the load balancer checks; needs an ACM
  certificate in eu-west-2 for an origin name and a DNS record in cPanel. Rejected: more moving parts, and the load balancer stays
  on the internet.

**Database** (picked: keep Express). Web edition (no 10 GB cap, ~$60–120 a month) and Standard Multi-AZ (~$1,000+ a month) were
declined for now; a daily size check alarms at 7 GB so there is time to move (a restore into a new instance, minutes of
downtime).

**Detection** (picked: CloudTrail and GuardDuty). Security Hub with AWS Config (~$5–25 a month) declined for now.

**Sign-up** (picked: open with an e-mail check). Invitation codes only was declined because it removes self-service trials.

## Design

### 1. Stacks and regions

| Stack | Region | Holds | Deployed |
|---|---|---|---|
| `cmms-github` | eu-west-2 | OIDC deploy role, ECR, CloudFormation execution role (existing) | By the user, `setup.sh` |
| `cmms-edge` (new) | us-east-1 | WAF web ACL, its alarm and SNS topic, GuardDuty detector for us-east-1 | GitHub workflow, before the app stack |
| `cmms-dr` (new) | eu-west-1 (Ireland) | Locked AWS Backup vault, file-copy bucket, native-backup copy bucket | By the user, `setup.sh` (decision R17; the workflow only reads it) |
| `cmms-app` | eu-west-2 | App, database, files, plus everything below | GitHub workflow (existing) |

*Second region: eu-west-1 (my call: closest and cheapest EU region to London; both stay in the UK/EU).*

`bootstrap.yml` changes: the deploy role may also manage `cmms-edge` in us-east-1 and `cmms-dr` in eu-west-1 (CloudFormation
actions on those stack ARNs, PassRole of the existing execution role, which is global). The user re-runs `setup.sh` once; the
script prints what changed. `setup.sh` deploys `cmms-dr` (decision R17: the pipeline never changes the recovery stack); the workflow deploys `cmms-edge`, reads both stacks' outputs (vault ARN, bucket ARNs, web
ACL ARN) and passes them to `cmms-app` as parameters. A missing output (first run before setup is re-run) fails the deploy with
a message that says to re-run `setup.sh`.

### 2. Edge: WAF web ACL (cmms-edge)

Scope `CLOUDFRONT`, default action Allow, attached through `WebACLId` on the distribution. Rules, in order:

| Rule | Action | Notes |
|---|---|---|
| `AWSManagedRulesAmazonIpReputationList` | Block | Known malicious addresses |
| `AWSManagedRulesKnownBadInputsRuleSet` | Block | Log4j-style and other known exploit patterns |
| `AWSManagedRulesSQLiRuleSet` | Block | SQL injection |
| `AWSManagedRulesCommonRuleSet` | Block, with `SizeRestrictions_BODY` and `CrossSiteScripting_BODY` set to Count | Body rules would block uploads, imports and long texts; the app validates bodies itself |
| `AWSManagedRulesAnonymousIpList` | Count | VPNs and hosting ranges; real users use VPNs. Watched, not blocked |
| Rate: sign-in, renew password, sign-up, e-mail confirmation and resend (`/api/auth/*` writes) | Block above 300 requests per 5 minutes per address | Generous for shared factory addresses; the app limits failures more tightly (section 4) |
| Rate: requester answer links (`/api/public/*`) | Block above 100 per 5 minutes per address | The only other anonymous write |
| Rate: everything | Block above 6,000 per 5 minutes per address | Floods only |

*Limits are my call and can be tuned from the stack parameters.* Blocked requests get a short plain page (custom response body)
in English and Arabic: "Too many requests from your network. Wait a few minutes and try again."

**Count first, then block** (my call). A stack parameter `WafMode` (`count` or `block`, from a GitHub variable `CMMS_WAF_MODE`)
sets every Block rule above to Count when `count`. The release deploys in `count`; after a day or two of sampled requests show no
real users caught, the user sets the variable to `block` and re-runs the deploy workflow. No code change between the two.

WAF logging: sampled requests and CloudWatch metrics only (full WAF logs cost extra and aren't needed yet).

### 3. Origin: internal load balancer through a VPC origin

- Two new private subnets (no internet route; subnets cost nothing). The load balancer moves to them with `Scheme: internal`.
- `AWS::CloudFront::VpcOrigin` points at the internal load balancer (HTTP on port 80 inside AWS's network); the distribution's
  origin uses `VpcOriginConfig`.
- The load balancer's security group allows port 80 only from CloudFront's VPC-origin source (the managed prefix list, as today,
  or the service security group CloudFront creates for VPC origins, whichever AWS documents for VPC origins at build time).
- The app task stays in the public subnets with a public IP for outbound calls; its security group still only admits the load
  balancer.
- **Cutover without long downtime:** changing `Scheme` replaces the load balancer. The builder creates the internal load
  balancer, target group and listener as new resources, points the distribution and the ECS service at them in the same deploy,
  and removes the old ones in the same or the next deploy. Expected impact: a short gap while the ECS service registers with the
  new target group. Tried first on a temporary test stack (section 11).

### 4. App: limits, headers, real address

**Visitor address.** The edge stack's origin request policy forwards `CloudFront-Viewer-Address`. The app reads the visitor's
address from it when present (AWS), else from the connection (Azure, local). One helper, used **only by the limiter**; the address
is never written to the database, the activity log or the application log (the 0.25.0 rule stays).

**Limits** (ASP.NET rate limiting, a second policy beside the API-key limiter, partitioned by visitor address):

| Endpoint | Limit per address | Counts |
|---|---|---|
| Sign-in, renew password | 20 per 15 minutes | Failed attempts only (a successful sign-in doesn't count) |
| Sign-up | 5 per hour | Every attempt |
| Resend confirmation | 10 per hour | Every attempt |
| Confirm e-mail link | 20 per 15 minutes | Every attempt |
| Requester answer links (`/api/public/feedback/{token}`) | 30 per 15 minutes | Every attempt |

A refused request returns 429 with a translated message ("Too many attempts from your network. Try again in N minutes.") and a
`Retry-After` header; the sign-in, sign-up, confirmation and requester-answer pages show it. The per-account lockout stays as it
is. *Numbers are my call.*

**Security headers** on every response, from a middleware (works on every host):

- `Strict-Transport-Security: max-age=31536000` (only when the request arrived over HTTPS).
- `Content-Security-Policy`: `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:
  blob:; font-src 'self' data:; connect-src 'self'; media-src 'self' blob:; worker-src 'self'; frame-ancestors 'none';
  object-src 'none'; base-uri 'self'; form-action 'self'`. The builder checks every page (web, technician view, print previews,
  labels, the Android app, the requester answer page) in a browser with the policy enforced and widens it only for real needs, writing
  each exception down in the code.
- `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`,
  `Cross-Origin-Opener-Policy: same-origin`, `Permissions-Policy: camera=(self), microphone=(self), geolocation=(self)`.
- File downloads keep their current headers (the 0.0.1 rule that uploads can't run as pages stays).

### 5. Sign-up e-mail check

- A setting `Auth:RequireEmailConfirmation` (default: on when e-mail is configured, off otherwise). The AWS stack turns it on
  through a parameter.
- Sign-up creates the organization and its owner as today, with the owner's `EmailConfirmed = false`, and sends a confirmation
  e-mail (English or Arabic, the sign-up language) with a single-use link valid 48 hours. This system e-mail is sent whatever
  `Mail:AllowedOrganizations` says, because it goes only to the address that signed up.
- Until confirmed, signing in shows "Confirm your e-mail address to start using CMX" with a "Send the link again" button (limited,
  section 4). Nothing else in the organization is reachable.
- The link confirms the address and signs the person in.
- People an administrator adds, people imported, existing people and service accounts count as confirmed (the migration marks
  every existing account confirmed). Only self sign-up waits.
- Housekeeping deletes organizations whose owner never confirmed within 7 days, with everything in them (they can only hold what
  sign-up created). Each deletion is logged.
- **Prerequisite:** SES production access. The release checklist checks the SES account status; if it is still in the sandbox,
  the stack parameter stays off and the release notes say so.

### 6. Automatic backups

Targets (picked 2026-11-25: "minutes, a few dollars more"): lose at most 5 minutes of database changes in London and at most
about 15 minutes if the whole London region is lost (RPO); back within 2 hours in London, 8 hours from Ireland (RTO).

| Layer | How | Kept | Where |
|---|---|---|---|
| Point-in-time restore | RDS `BackupRetentionPeriod` 7 → **35** | 35 days | London |
| Continuous copy to Ireland | RDS cross-Region automated backup replication to eu-west-1 (encrypted with an RDS key in Ireland), so point-in-time restore works in Ireland too | 7 days | Ireland |
| Daily snapshots | AWS Backup plan, daily rule, start 01:00 UTC | 35 days | London vault, copied to the Ireland vault |
| Monthly snapshots | Same plan, monthly rule (1st of the month) | 12 months | London vault, copied to the Ireland vault |
| Weekly portable export | Native SQL Server backup (`msdb.dbo.rds_backup_database`) of the CMX database to a versioned backups bucket, Sundays 02:00 UTC, started by the app | 90 days | London bucket, replicated to Ireland |
| Files | S3 versioning (exists) + cross-region replication to Ireland; old versions expire after 90 days | 90 days of old versions | London and Ireland |
| Infrastructure | CloudFormation templates and workflow in git | Forever | GitHub |

- **If RDS doesn't support cross-Region automated backups for SQL Server Express** (the builder checks the RDS documentation and
  tries it first), the fallback is an hourly AWS Backup rule with an immediate copy to Ireland, kept 2 days: about 1–2 hours of
  changes at risk instead of 15 minutes. The builder records which one shipped in the runbook and the changelog.
- **Deletion lock:** AWS Backup Vault Lock on the Ireland vault in **governance mode** (my call): copies can't be deleted or
  shortened by the deploy pipeline or anything it creates (next bullet), but the user's own console administrator (the root user
  or an IAM administrator outside the pipeline) can still lift it if a setting is wrong. Moving to compliance mode (nobody can
  delete, irreversible) is a later decision for the user after a quarter of clean runs. The London vault gets a vault access
  policy that denies deleting recovery points to everyone except the root user.
- **Backups survive a compromised pipeline** (my call: GitHub is reachable from the internet, so the pipeline is treated as
  untrusted for backups). `bootstrap.yml` adds explicit denies to the CloudFormation execution role, in every region, for:
  deleting recovery points, shortening their lifecycle, deleting or changing vault lock settings, deleting vaults or their access
  policies, deleting or stopping the replicated automated backups in Ireland, bypassing S3 Object Lock, deleting object versions or changing lifecycle, replication or Object Lock settings on the
  Ireland buckets, and deleting or stopping the CloudTrail trail or its logs. Every role the execution role creates under `/cmms/`
  must carry a permissions boundary with the same denies (enforced by a condition on `iam:CreateRole` and
  `iam:PutRolePermissionsBoundary`), so the pipeline can't create a role that escapes them. Creating these resources the first time
  stays allowed. The exact action list is the builder's, checked against the AWS service authorization reference.
- **Object Lock on the Ireland buckets** (my call): the file-copy and export-copy buckets are created with S3 Object Lock in
  governance mode and a default retention of 35 days (file copies) and 90 days (export copies), so replicated versions can't be
  deleted early either. Replication gets the permissions Object Lock needs.
- **Native export:** RDS option group with `SQLSERVER_BACKUP_RESTORE` and an IAM role (under `/cmms/`) allowed to write only to the
  backups bucket. The app's background service starts the export weekly when `Backups:S3Arn` is set (it isn't on Azure or locally),
  polls `msdb.dbo.rds_task_status`, and writes a log line `BackupExport status=SUCCESS|ERROR …`. Express can run native backups.
- **Monthly restore test:** an AWS Backup restore testing plan picks the latest daily recovery point once a month, restores it into
  a temporary db.t3.micro in the private subnets (a dedicated security group with no inbound rules), and deletes it after 4 hours.
  An EventBridge rule sends the restore job's result (completed or failed, how long it took) to the alarm topic. *"Checked" in this
  release means the restore completed and the instance came up; a check of the data inside is a later step.*
- **Region rebuild:** documented, not automated: restore the Ireland copy to the latest restorable time (or the newest snapshot
  copy), deploy the stacks to the new region with the same workflow, point DNS. Rehearsed by hand once a quarter (runbook,
  section 10).

### 7. Detection: CloudTrail and GuardDuty

- **CloudTrail:** one multi-region trail of management events, global service events included, log file validation on, into a
  private encrypted bucket in London; logs move to cheaper storage after 90 days and expire after 365 days. Before creating it,
  the builder checks whether the account already has a trail; a second trail of the same events would cost $2 per 100,000 events.
- **GuardDuty:** detectors in eu-west-2 (app stack) and us-east-1 (edge stack), with S3 protection, runtime monitoring for ECS
  Fargate (GuardDuty's managed agent), and Malware Protection for S3 on the files bucket (tag objects; alert only). Findings of
  severity 4 or higher go to the alarm topic through EventBridge. If a detector already exists in a region (account-level), the
  stack must not try to create a second one; the builder checks first and uses a parameter to skip creation.
- GuardDuty's database protection doesn't cover SQL Server; that's expected.

### 8. Alarms and the on-call inbox

- An SNS topic `cmms-alerts` in London (and one in us-east-1 for the WAF alarm), each with an e-mail subscription to a new stack
  parameter `AlertEmail`, fed from a GitHub variable `CMMS_ALERT_EMAIL`. The recipient must confirm the subscription e-mail once.
- Alarms (London unless noted):

| Alarm | Fires when |
|---|---|
| Site errors | Load balancer 5xx above 10 in 5 minutes |
| App down | Healthy targets below 1 for 3 minutes |
| Database busy | RDS CPU above 85% for 15 minutes |
| Database disk | RDS free storage below 3 GB |
| Database size | The app's daily size measurement at or above 7 GB (log metric filter on `DatabaseSize mb=…`) |
| Sign-in attack | More than 50 failed sign-ins in 5 minutes (log metric filter on the app's failed sign-in log line) |
| Native export failed | A `BackupExport status=ERROR` line, or no `SUCCESS` line in 8 days |
| Backup, copy or restore test failed | AWS Backup job state FAILED/ABORTED/EXPIRED through EventBridge |
| GuardDuty finding | Severity 4 or higher through EventBridge |
| WAF blocking a lot (us-east-1) | Blocked requests above 1,000 in 5 minutes |

- App log retention 30 → **90 days**.
- **Database size check:** the app's background service measures the data file size daily (`sys.database_files`) and logs
  `DatabaseSize mb=…`. SQL Server only; skipped on other providers.

### 9. CI supply-chain checks

In `ci.yml`, a security job that blocks the build on findings rated high or critical with a fix available:

- `dotnet list package --vulnerable --include-transitive` for the server solution.
- `npm audit --omit=dev --audit-level=high` for `web` and `mobile`.
- Secret scanning of the repository with gitleaks (pinned version).
- In `deploy-aws.yml`, an image scan of the built container with Trivy (pinned) before the push; critical findings with a fix stop
  the deploy. ECR's scan on push stays on.
- Existing findings found on the first run are fixed in this release where an update exists; anything that can't be fixed yet goes
  in a reviewed allowlist file with a reason and an expiry date.
- CodeQL is left out: it needs GitHub Advanced Security on a private repository.

### 10. Documentation

- New `docs/SECURITY_AND_BACKUPS.md`: what protects CMX, what each alarm means and what to do, how to restore a point in time, a
  snapshot, a single file, and a native export, how to rebuild in Ireland, how to lift the vault lock, and the quarterly rehearsal.
- `docs/DEPLOY_AWS.md`: the new stacks, the one-time `setup.sh` re-run, `CMMS_ALERT_EMAIL`, confirming the SNS e-mail, checking
  SES production access, monthly cost.
- `CHANGELOG.md` entry; Arabic translations for every new message.

### 11. Testing

- Server tests (written first): visitor address helper (header present, absent, malformed); limiter counts failures only for
  sign-in and doesn't touch API keys; 429 body and `Retry-After`; security headers present on API, pages and files, HSTS only over
  HTTPS; sign-up leaves the owner unconfirmed, sign-in refused until confirmed, link confirms once, expired link refused, resend
  limited; admin-added and imported people are confirmed; unconfirmed organizations older than 7 days removed with their data,
  confirmed ones never; native export and size check skipped without settings.
- Web tests: sign-in, sign-up, confirmation and requester-answer pages show the 429 message; unconfirmed screen and resend button.
- Template checks: `cfn-lint` on all templates in CI, and a check that every role the templates create under `/cmms/` carries the
  permissions boundary.
- **No separate test stack** (my call). The deploy role only trusts `main` and only manages named stacks; a branch-deployable
  test role with the PowerUser execution role would widen the attack surface this release is closing, and a second copy would
  clash with the account-wide pieces (one GuardDuty detector per region, the trail, the locked Ireland vault). Instead:
  - The workflow shows each stack's change set in its log before applying it, and CloudFormation rolls back on failure.
  - The load-balancer cutover is deployed at a quiet hour (the user picks it), with the rollback steps written in the runbook.
  - WAF starts in `count` mode (section 2) and the blocking checks run after the switch to `block`.
  - The restore test plan is run once on demand in production (it only creates and removes a temporary instance).
  - Alarms are tested with `set-alarm-state` and a GuardDuty sample finding.
- Browser pass with CSP enforced over every page listed in section 4, in English and Arabic, and on the Android app.

## Costs (eu-west-2 estimates per month)

WAF ~$12 (web ACL, rules, requests); CloudTrail ~$0–2; GuardDuty ~$3–10; AWS Backup snapshots and Ireland copies ~$3–8 at today's
database size; continuous backup copy to Ireland ~$2–5; S3 replication and the backups bucket ~$1–3; alarms ~$1–2; monthly restore
test well under $1. **About $30–50 in total.** VPC origins, private subnets, Object Lock and SNS e-mail cost nothing.

## Done when

- In `block` mode, WAF blocks a test SQL-injection request and a sign-in burst from one address; an attachment upload and an
  Excel import pass.
- The deploy pipeline's role can't delete a recovery point, lift the vault lock or delete a locked file copy (tried and refused).
- A request sent straight to the load balancer can't connect.
- securityheaders.com grades the site A or better, and every page works with the CSP enforced, including the Android app.
- A new sign-up can't use CMX until its e-mail link is clicked; unconfirmed organizations disappear after 7 days.
- The first daily snapshot appears in the Ireland vault, locked; the first weekly export lands in both buckets.
- Ireland shows the replicated automated backups with a latest restorable time within 15 minutes of now (or, on the fallback,
  an hourly copy no older than 2 hours).
- The restore test completes on demand and its result e-mail arrives.
- A GuardDuty sample finding and a test alarm reach the on-call inbox.
- CI fails on a deliberately added vulnerable package and on a fake secret, then passes once they're removed.
- Live after release: both addresses report 0.29.0, and the SES, SNS and setup prerequisites are confirmed.

## Left to the builder

- Exact CloudFormation resource names, parameter names beyond those named here, and how the workflow passes outputs between stacks.
- The VPC-origin security group detail (prefix list or the CloudFront service security group) as AWS documents it at build time.
- Whether the old load balancer goes in the same deploy or the next, as long as the cutover gap stays short.
- Final CSP exceptions found in the browser pass, each commented in code.
- Which background service hosts the daily size check, the weekly export and the unconfirmed-organization cleanup (an existing one
  such as the housekeeping or notifier loop, or a new one), keeping the single-instance assumption.

## Prerequisites the user handles

1. Re-run `deploy/aws/setup.sh` in CloudShell once after the `bootstrap.yml` change is on `main`.
2. Set the GitHub variables `CMMS_ALERT_EMAIL` and `CMMS_WAF_MODE` (`count` at first), and confirm the SNS subscription e-mails
   (London and us-east-1). After a day or two in count mode, set `CMMS_WAF_MODE` to `block` and re-run the deploy workflow.
5. Pick a quiet hour for the deploy that moves the load balancer.
3. Check SES production access in the SES console (needed for the sign-up e-mail check).
4. Check in the console whether CloudTrail and GuardDuty are already on at account level (the stack adapts).
