# Security and backups: runbook

This page is for the owner of CMX and whoever is on call. It says what protects CMX on AWS, what each alarm e-mail means and
what to do about it, and how to get data back. It describes release 0.29.0 and later. How the stacks are deployed is in
[DEPLOY_AWS.md](DEPLOY_AWS.md).

You do not need to be a cloud specialist to follow it. Commands are for **AWS CloudShell** (the `>_` icon in the AWS console
top bar), which is already signed in. In the commands, `<account>` is your 12-digit AWS account number and `<region>` is the
region the app runs in (London, `eu-west-2`, for the live site). To avoid typing the account number, run this first in CloudShell:

```bash
ACCOUNT=$(aws sts get-caller-identity --query Account --output text)
```

**Find it fast**

| I got or I see | Go to |
|---|---|
| An alarm e-mail | [Alarms and e-mails](#3-alarms-and-e-mails) |
| Someone deleted or damaged data | [Restore](#4-restore) |
| The deploy failed or is stuck | [When a deploy fails](#7-when-a-deploy-fails) |
| London is down for good | [Rebuild in Ireland](#5-rebuild-in-ireland) |
| I need to delete a locked backup | [Lifting the lock](#6-lifting-the-lock) |
| First deploy of 0.29.0 | [After the first deploy](#8-one-time-steps-after-the-first-deploy) |
| Every three months | [The quarterly rehearsal](#9-the-quarterly-rehearsal) |

## 1. What protects CMX

| Protection | What it does |
|---|---|
| **Web firewall** (AWS WAF, stack `cmms-edge`, N. Virginia) | In front of CloudFront. Blocks known bad addresses, SQL injection and known exploit patterns, and stops floods: more than 300 sign-in, sign-up or e-mail-link requests in 5 minutes from one address, more than 100 requester-answer-link requests, or more than 6,000 requests of any kind. A blocked visitor sees a short page in English and Arabic ("Too many requests from your network"). The managed body-size and cross-site-scripting body rules only count, so uploads, Excel imports and long texts are not blocked. VPN and hosting addresses are counted, not blocked. |
| **Count mode, then block mode** | The firewall starts in `count` (it records what it would block and blocks nothing). After a day or two with no real users caught, set `CMMS_WAF_MODE` to `block` ([DEPLOY_AWS.md](DEPLOY_AWS.md#move-the-web-firewall-to-block)). |
| **Internal load balancer** | The load balancer has no public address. Only CloudFront reaches it, through a CloudFront VPC origin, so nobody can go around the firewall. The app task's security group only admits the load balancer. |
| **Limits in the app** (per visitor address, in memory only) | 20 wrong passwords in 15 minutes (successful sign-ins do not count), 5 sign-ups an hour, 10 e-mail-link resends an hour, 20 link confirmations in 15 minutes, 30 requester answer-link tries in 15 minutes. A refused request gets "Too many attempts from your network. Try again in N minutes." and a `Retry-After` header. The per-account lockout still applies. API keys keep their own limit. |
| **No addresses stored** | The visitor's address (from CloudFront's `CloudFront-Viewer-Address` header) feeds the limits only, in memory. It is never written to the database, the activity log or the application log. For that reason an alarm e-mail never says which address attacked; the firewall's sampled requests do (see [sign-in attack](#3-alarms-and-e-mails)). |
| **Browser security headers** | Every response carries a content security policy, HSTS (over HTTPS), frame protection, `nosniff`, a referrer policy and a permissions policy. Downloads of uploaded files keep their stricter policy, so an upload can never run as a page. The policy is in `server/FourC.Cmms.Api/Infrastructure/SecurityHeaders.cs`; each exception is commented there. If a page suddenly looks broken, the browser's console shows which source the policy refused. |
| **E-mail check on sign-up** | When switched on (`CMMS_REQUIRE_EMAIL_CONFIRMATION`), a new organization's owner cannot sign in until they click the link in the confirmation e-mail (valid 48 hours). The e-mail is sent whatever the e-mail allow-list says, because it goes only to the address that signed up. Organizations never confirmed within **7 days** are deleted with everything in them, once a day; each deletion is logged with the organization's id only. People an administrator adds, imported people, existing organizations and service accounts are never asked. |
| **Deploy-pipeline guard** | GitHub is on the internet, so the pipeline is treated as untrusted for backups. The role CloudFormation deploys with is explicitly denied: deleting recovery points or shortening their life, deleting the Ireland vault or its lock, deleting the Ireland database backup copies, deleting or disabling the Ireland encryption key, bypassing Object Lock, deleting Ireland file versions, opening the Ireland buckets with a policy, stopping or changing CloudTrail, and writing to the trail bucket. |
| **Permissions boundary** | Every role the pipeline creates under the `/cmms/` path must carry the policy `cmms-boundary-<region>` (the pipeline cannot create a role without it). The boundary repeats the same denies, so the pipeline cannot create a role that escapes the guard. A role inside the boundary also cannot change IAM or pass a role outside `/cmms/`. |
| **CloudTrail** | One multi-region trail, `cmms-trail`, records who did what in the AWS account into the bucket `cmms-trail-<account>` (log validation on; Object Lock governance 90 days; moved to cheaper storage at 90 days; deleted at 365 days). It is created by `setup.sh` in the `cmms-github` stack, outside the pipeline's reach, unless the account already had a multi-region trail. |
| **GuardDuty** | Detectors in London (with S3 protection, runtime monitoring of the app task, and malware scanning of every new upload in the files bucket) and in N. Virginia (basic). Findings of severity 4 or higher are e-mailed to the alarm address. Malware findings only alert; the app still serves the file. GuardDuty does not cover SQL Server databases. |
| **CI security checks** | Every build runs `scripts/security-check.sh`: NuGet and npm packages with a known high or critical flaw (npm only when a fix exists), and leaked secrets (gitleaks). A finding stops the build. A reviewed exception goes in `.github/security-allowlist.txt` with a reason and an expiry date at most 90 days away; the file is empty now. `cfn-lint` and a check that every `/cmms/` role carries the boundary also run in CI. |
| **Image scan** | Before each deploy, Trivy scans the built container; a critical flaw that has a fix stops the deploy. ECR's scan on push stays on. |

## 2. Backups

### Targets

| | London (database or region healthy) | London region lost |
|---|---|---|
| Most data you can lose (RPO) | 5 minutes | about 15 minutes |
| Time to be back (RTO) | 2 hours | 8 hours, from Ireland |

These are targets, not measured results. The [quarterly rehearsal](#9-the-quarterly-rehearsal) is how you find out. The 15-minute
figure holds for the default Ireland method below; with the fallback it is 1 to 2 hours.

### The layers

| Layer | How | Kept | Where |
|---|---|---|---|
| Point-in-time restore | RDS automated backups, to the minute | 35 days | London |
| Continuous copy of the database | RDS cross-Region automated backups, encrypted with the Ireland key. Point-in-time restore also works in Ireland | 7 days | Ireland |
| Daily snapshot | AWS Backup, 01:00 UTC | 35 days | London vault `cmms-app-vault`, copied to Ireland vault `cmms-dr-vault` |
| Monthly snapshot | AWS Backup, 1st of the month, 01:00 UTC | 12 months | both vaults |
| Weekly export | Native SQL Server backup (`.bak`) written by RDS every Sunday from 02:00 UTC, started by the app. File `native/cmms-<date>.bak` | 90 days | bucket `cmms-backups-<account>-eu-west-2` (London), replicated to `cmms-dr-backups-<account>` (Ireland) |
| Files (attachments) | S3 versioning plus replication of every new file and version to Ireland. Old versions expire after 90 days | 90 days of old versions | files bucket (London), `cmms-dr-files-<account>` (Ireland) |
| Infrastructure | Templates and workflow in git | forever | GitHub |

**Which Ireland database method is configured.** By default (`CMMS_IRELAND_DB_COPY` = `replication`) the database reaches
Ireland by RDS cross-Region automated backups, which gives point-in-time restore in Ireland and about 15 minutes of exposure.
If RDS refuses this for the database's edition, set `CMMS_IRELAND_DB_COPY` to `hourly-snapshots` and re-run the deploy. That
switches on a fallback: an hourly AWS Backup snapshot copied at once to the Ireland vault and **kept 7 days there** (2 days in
London). The exposure becomes 1 to 2 hours, and there is no point-in-time restore in Ireland. The daily and monthly snapshots,
the weekly export and the file copies are the same either way.

**Deletions are not copied.** If a file is deleted in London, it stays current in Ireland. Replication copies new files and new
versions only.

### Locks

- **Ireland vault** (`cmms-dr-vault`): AWS Backup Vault Lock in **governance mode**, retention between 7 and 400 days. The
  pipeline and anything it creates cannot delete or shorten the copies. A console administrator (the root user, or an IAM
  administrator outside the pipeline) can still lift the lock ([section 6](#6-lifting-the-lock)). Whether to move to compliance
  mode (nobody can delete, irreversible) is a decision for later, after a quarter of clean rehearsals.
- **London vault** (`cmms-app-vault`): an access policy lets only the root user delete a recovery point. Snapshots still expire on
  their own schedule.
- **Ireland buckets**: S3 Object Lock, governance mode, default retention 35 days (files) and 90 days (exports). They have no
  bucket policy on purpose, and the pipeline may not add one.
- **Ireland key** (`alias/cmms-dr`): encrypts the Ireland vault and the replicated database backups. Never disable it and never
  schedule its deletion: the copies cannot be read without it.

## 3. Alarms and e-mails

All alarms go to the address in `CMMS_ALERT_EMAIL`. Two SNS topics send them: `cmms-alerts` (London) and `cmms-edge-alerts`
(N. Virginia, only for the firewall alarm and N. Virginia GuardDuty findings). Each recipient confirms each subscription once,
by the link in the e-mail AWS sends. Until then nothing arrives, so check this first when no alarm has ever reached anyone
(`aws sns list-subscriptions-by-topic --region <region> --topic-arn <arn>` shows `PendingConfirmation` for an unconfirmed one).

Alarms from CloudWatch say the alarm name and why. Backup, restore test and GuardDuty e-mails come through EventBridge and are
raw JSON: look for `state`, `status`, `severity`, `type` and `title`. Alarm names start with the app stack's name (`cmms-app`).

### Alarms

| Alarm | Fires when | What to do |
|---|---|---|
| `cmms-app-site-errors` | More than 10 server errors (5xx) in 5 minutes | Open the site. Look in CloudWatch, log group `/ecs/cmms-app`, for errors. If it began right after a deploy, re-run the previous good deploy (Actions, Deploy to AWS), see [rollback](#7-when-a-deploy-fails). |
| `cmms-app-app-down` | No healthy app task behind the load balancer for 3 minutes (no data counts as down) | ECS, the cluster whose name starts with `cmms-app`, the service, *Tasks* and *Events*: see why the task stopped. `https://<address>/api/health` should answer. Often a bad deploy or a database that is down. The service restarts tasks by itself. |
| `cmms-app-database-busy` | Database CPU above 85% for 15 minutes | Look for a heavy import or report. If it keeps happening, a bigger class (`CMMS_DB_INSTANCE_CLASS`, e.g. `db.t3.small`; a few minutes' restart). |
| `cmms-app-database-disk` | Free database storage below 3 GB (of 20 GB) | Check the size alarm below. More storage is a change to `AllocatedStorage` in `infra/aws/cmms.yml`; in an emergency raise it in the RDS console (Modify) and put the same value in the template so the next deploy does not fight it. |
| `cmms-app-database-size` | The daily size measurement reached 7 GB (7,168 MB) | The free SQL Server Express edition stops at **10 GB**, and then writes fail. You have time, not much. Plan the move to a bigger edition: restore a snapshot into a new instance (minutes of downtime). Attachments are in S3, not in the database, so growth is data, history and logs. |
| `cmms-app-database-size-not-measured` | **"Database size not measured":** no daily size line (`DatabaseSize mb=`) in the app log for 2 days | The 7 GB alarm is blind until this is fixed. The app measures once a day. Check the app is running, then the log for `Platform job failed: Measuring the database`. Platform jobs are off if the setting `Platform:JobsAutoRun` is false. |
| `cmms-app-sign-in-attack` | More than 50 failed sign-ins in 5 minutes | Someone is guessing passwords (or a shift of people forgot a changed password; wrong passwords, locked accounts and unknown e-mails all count). The per-address limit already turns an address away after 20 wrong tries in 15 minutes. To see the addresses: N. Virginia console, *WAF & Shield*, web ACL `cmms-edge-webacl`, *Sampled requests*. If it is a real attack and the firewall is still in `count`, switch to `block`. |
| `cmms-app-native-export-error` | An export reported `BackupExport status=ERROR` | Sundays only. Read the log line in `/ecs/cmms-app` for the reason. The app tries again every hour that Sunday until it works (or gives up after 3 hours of waiting on one try). Common causes: the option group or its role was changed, the bucket is missing. Fix, then wait for the next try, or restart the service to try at once (an export is due on a Sunday from 02:00 UTC until one has succeeded). On any other day the next Sunday tries again. |
| `cmms-app-native-export-missing` | No successful export in the last calendar week (Monday to Sunday, UTC); fires **the Monday after a missed Sunday** | The week was missed, so the newest `.bak` is more than a week old. Fix the cause as above. The alarm stays on until a week that contains a successful export has ended, so it can still show on the next Monday after you fixed it. The other layers (snapshots, point-in-time) were not affected. |
| `cmms-waf-blocking` (N. Virginia, topic `cmms-edge-alerts`) | More than 1,000 requests blocked in 5 minutes | An attack, or a rule catching real users. Check *Sampled requests* on the web ACL. If real users are caught, go back to `count` ([DEPLOY_AWS.md](DEPLOY_AWS.md#move-the-web-firewall-to-block)). In `count` mode nothing is blocked, so this alarm cannot fire; look at the counted requests instead. |

### E-mails that are not alarms

| E-mail | Meaning | What to do |
|---|---|---|
| **AWS Backup job failed** (`Backup Job State Change` or `Copy Job State Change`, state `FAILED`, `ABORTED` or `EXPIRED`) | A snapshot, or its copy to Ireland, did not complete | AWS Backup console, *Jobs* (London for a backup, Ireland for the receiving side of a copy): read the status message. The next daily job tries again; start an on-demand backup from the vault if you cannot wait. If copies to Ireland keep failing, check the Ireland key is enabled and the vault exists. |
| **Restore test result** (`Restore Job State Change`, `COMPLETED`, `FAILED` or `ABORTED`, with times) | The monthly automatic restore test. On the 2nd of each month at 04:00 UTC AWS Backup restores the latest snapshot into a temporary small instance and deletes it after 4 hours | `COMPLETED` is the good news. `FAILED` or `ABORTED`: AWS Backup, *Restore jobs*, read the message, and treat it as a failed backup until proven otherwise. No e-mail by the 3rd is itself a problem. "Checked" means the restore completed and the instance came up; the data inside is not compared. |
| **GuardDuty finding** (severity 4 or higher; London on `cmms-alerts`, N. Virginia on `cmms-edge-alerts`) | GuardDuty saw something unusual: an upload with malware, odd behaviour in the app task, use of stolen keys, a bad address | Open GuardDuty in the region named in the e-mail and read the finding. A malware finding on an upload: find the file (the finding names the object), remove it from CMX, tell the organization. A finding that suggests stolen AWS keys or an unknown login: change the credentials involved at once and look at CloudTrail (*Event history*, or the logs in `cmms-trail-<account>`). |

### A gap to know about

If CloudFront cannot reach the load balancer (a broken VPC origin), the app and its load balancer stay healthy, so none of the
alarms above fires. After any deploy, open the site yourself; the deploy workflow's last step does it for you and fails the run
if the version does not answer.

## 4. Restore

Before you restore, decide what you need: one file, a few records, or the whole database. For a few records, restore into a
**new** instance and copy what you need; replacing the live database loses everything entered since the restore point.

Find the live database and the values a restore needs:

```bash
aws rds describe-db-instances --region <region> \
  --query "DBInstances[?starts_with(DBInstanceIdentifier,'cmms-app')].[DBInstanceIdentifier,DBInstanceStatus,DBInstanceClass]" --output table
aws rds describe-db-instances --region <region> --db-instance-identifier <identifier> \
  --query "DBInstances[0].[DBSubnetGroup.DBSubnetGroupName,VpcSecurityGroups[0].VpcSecurityGroupId,OptionMemberships[0].OptionGroupName]" --output text
```

The three values from the second command are the subnet group, security group and option group that every restore below needs,
so the new instance is private and sits where the app can reach it. Every restore creates a **new** instance; none overwrites
the live one.

### 4.1 A point in time in London

Console: RDS, *Databases*, the database whose name starts with `cmms-app`, *Actions*, *Restore to point in time*. Choose the
time (or *Latest restorable time*), a new name such as `cmms-restore-1`, the subnet group and security group from above, and
*Publicly accessible: No*. Or:

```bash
aws rds restore-db-instance-to-point-in-time --region <region> \
  --source-db-instance-identifier <identifier> --target-db-instance-identifier cmms-restore-1 \
  --restore-time 2026-12-01T09:30:00Z \
  --db-subnet-group-name <subnet-group> --vpc-security-group-ids <security-group> \
  --option-group-name <option-group> --no-publicly-accessible --no-multi-az \
  --copy-tags-to-snapshot --auto-minor-version-upgrade
```

(Use `--use-latest-restorable-time` instead of `--restore-time` for the newest point.) The time is UTC. Restoring takes
roughly 20 to 60 minutes. The flags match the database in `infra/aws/cmms.yml` (private, one zone, tags copied to snapshots,
minor upgrades on, the native-backup option group). A restore keeps the source's storage encryption key, instance class and
master password, but **not** its backup retention or the copy to Ireland; section 4.5 sets those.

### 4.2 A point in time in Ireland

For when the London database or region is gone. The replicated automated backups are in Ireland:

```bash
aws rds describe-db-instance-automated-backups --region eu-west-1 \
  --query "DBInstanceAutomatedBackups[].[DBInstanceIdentifier,Status,RestoreWindow.EarliestTime,RestoreWindow.LatestTime,DBInstanceAutomatedBackupsArn]" --output table
```

`LatestTime` should be within about 15 minutes of now. Console: RDS in Ireland, *Automated backups*, the *Replicated* tab, the
backup, *Actions*, *Restore to point in time*. Or:

```bash
aws rds restore-db-instance-to-point-in-time --region eu-west-1 \
  --source-db-instance-automated-backups-arn <DBInstanceAutomatedBackupsArn> \
  --target-db-instance-identifier cmms-restore-ie --use-latest-restorable-time \
  --db-subnet-group-name <subnet-group> --vpc-security-group-ids <security-group> \
  --option-group-name <option-group> --no-publicly-accessible --no-multi-az \
  --copy-tags-to-snapshot --auto-minor-version-upgrade
```

Use the subnet group, security group and option group of the network the app will run in. Option groups are per region: the
London one is not there, so a restored instance has no native-backup option until you give it the one the stack in that region
made (see [section 5](#5-rebuild-in-ireland)); without it the weekly export fails. This method exists only when
`CMMS_IRELAND_DB_COPY` is `replication`.

### 4.3 A snapshot from the London vault

Console: AWS Backup (London), *Vaults*, `cmms-app-vault`, choose a recovery point (daily ones are kept 35 days, monthly ones 12
months), *Restore*. Give a new instance name, the subnet group, security group and option group from above, and *Publicly
accessible: No*. For the role choose *Default role* (AWS Backup makes it) or the role whose name starts with
`cmms-app-BackupRole`. The same snapshots also show in RDS under *Snapshots*, *Backup service*.

### 4.4 A copy from the Ireland vault

Same as above, in Ireland: AWS Backup (Ireland), *Vaults*, `cmms-dr-vault`, a recovery point, *Restore*. Choose the default
subnet group (or the new stack's) and a security group with no inbound access you do not need. The copy is encrypted with the
Ireland key, which the restore uses by itself. Daily and monthly snapshots are there; on the hourly fallback so are hourly
copies for 7 days.

### 4.5 Put a restored database in service

Any restored instance is a new instance with its own address. The simplest way to give it to the app is to swap names, so the
address the app already uses points at it. The app must be stopped while you do this.

A restore into the same stack (4.1, 4.3, or 4.4 in the app's own region) keeps the database login the stack's secret already
holds, so no password step is needed. A restore into a **different** stack (a rebuild) does not: see [section 5](#5-rebuild-in-ireland),
step 6.

1. Stop the app (no writes while you swap). Take the cluster and service names from the `cmms-app` stack's outputs
   `ClusterName` and `ServiceName`:
   ```bash
   aws ecs update-service --region <region> --cluster <ClusterName> --service <ServiceName> --desired-count 0
   ```
2. Rename the live database out of the way, then the restored one into its place (each takes a few minutes; wait for
   *available* between them):
   ```bash
   aws rds modify-db-instance --region <region> --db-instance-identifier <identifier> --new-db-instance-identifier <identifier>-old --apply-immediately
   aws rds modify-db-instance --region <region> --db-instance-identifier cmms-restore-1 --new-db-instance-identifier <identifier> --apply-immediately
   ```
3. Start the app again:
   ```bash
   aws ecs update-service --region <region> --cluster <ClusterName> --service <ServiceName> --desired-count 1
   ```
4. Check `https://<address>/api/health`, sign in, and look at the newest records.
5. **Put the backup settings back.** A restore starts with RDS's default backup retention and no copy to Ireland, and running
   **Deploy to AWS** does **not** fix that: CloudFormation only changes what the template or its parameters changed, and it does
   not notice that a setting on the database differs. Set them yourself (wait for the instance to be *available* first):
   ```bash
   aws rds modify-db-instance --region <region> --db-instance-identifier <identifier> --backup-retention-period 35 --apply-immediately
   aws rds describe-db-instances --region <region> --db-instance-identifier <identifier> \
     --query "DBInstances[0].[BackupRetentionPeriod,DBInstanceArn,OptionMemberships[0].OptionGroupName,DBSubnetGroup.DBSubnetGroupName]" --output text
   ```
   The first number should read `35`; the option group must be the stack's (it carries the native backup option). Then, only if
   `CMMS_IRELAND_DB_COPY` is `replication`, start the copy to Ireland again. The key is the `cmms-dr` stack's output `DrKeyArn`:
   ```bash
   KEY=$(aws cloudformation describe-stacks --region eu-west-1 --stack-name cmms-dr --query "Stacks[0].Outputs[?OutputKey=='DrKeyArn'].OutputValue" --output text)
   aws rds start-db-instance-automated-backups-replication --region eu-west-1 \
     --source-db-instance-arn <DBInstanceArn from above> --kms-key-id "$KEY" --backup-retention-period 7
   aws rds describe-db-instance-automated-backups --region eu-west-1 \
     --query "DBInstanceAutomatedBackups[].[DBInstanceIdentifier,Status,RestoreWindow.LatestTime]" --output table
   ```
   The new instance should appear in Ireland (a few minutes, then a recent `LatestTime`). On the hourly fallback there is
   nothing to start. The table above lists every setting the template gives the database; the template sets no deletion
   protection and no log exports, so there is nothing else to restore.
6. Keep `<identifier>-old` for a few days, then delete it in the RDS console. Do not delete it before you are sure.

### 4.6 A single file

Files are in the versioned bucket named by the `cmms-app` stack's output `FilesBucketName` (call it `$B`). Ask a developer for
the file's key if you only know the record.

```bash
B=<FilesBucketName>
aws s3api list-object-versions --region <region> --bucket "$B" --prefix "<key>" \
  --query "{Versions:Versions[].[Key,VersionId,LastModified,IsLatest],DeleteMarkers:DeleteMarkers[].[Key,VersionId,LastModified]}"
```

- **A file that was deleted** shows a delete marker as the latest version. Remove the marker and the file is back:
  ```bash
  aws s3api delete-object --region <region> --bucket "$B" --key "<key>" --version-id "<marker VersionId>"
  ```
- **A file that was replaced or damaged:** copy the good version over the current one:
  ```bash
  aws s3api copy-object --region <region> --bucket "$B" --key "<key>" --copy-source "$B/<key>?versionId=<VersionId>"
  ```
  (Old versions are kept 90 days.)
- **From the Ireland copy** (London bucket lost, or the versions are gone):
  ```bash
  aws s3api list-object-versions --region eu-west-1 --bucket "cmms-dr-files-$ACCOUNT" --prefix "<key>"
  aws s3api get-object --region eu-west-1 --bucket "cmms-dr-files-$ACCOUNT" --key "<key>" --version-id "<VersionId>" restored-file
  aws s3 cp restored-file "s3://$B/<key>"
  ```
  The Ireland copy still holds files deleted in London, because deletions are not copied.

### 4.7 A native `.bak` export, into any SQL Server

The newest export is in the London bucket `cmms-backups-<account>-eu-west-2` and in `cmms-dr-backups-<account>` (Ireland), as
`native/cmms-<date>.bak` (the Sunday's date):

```bash
aws s3 ls "s3://cmms-backups-$ACCOUNT-eu-west-2/native/"
aws s3 cp "s3://cmms-backups-$ACCOUNT-eu-west-2/native/cmms-<date>.bak" .
aws s3 ls "s3://cmms-dr-backups-$ACCOUNT/native/" --region eu-west-1
```

Then, on any SQL Server of the same or a newer version than the one in AWS (2022 by default), restore it as usual
(`RESTORE DATABASE cmms FROM DISK = '<path>' WITH MOVE ...`, or the SQL Server Management Studio dialog). The export is a full
copy of the `cmms` database as it was on that Sunday; use it when you want your data outside AWS, in another cloud, or to look
at something without touching the live instance.

To restore it into an RDS SQL Server instance that has the native-backup option (the stack's option group does), a database
administrator runs `exec msdb.dbo.rds_restore_database @restore_db_name='cmms_recovered', @s3_arn_to_restore_from='arn:aws:s3:::cmms-backups-<account>-eu-west-2/native/cmms-<date>.bak'`
and follows it with `exec msdb.dbo.rds_task_status`. The restore target must not already exist, and the Express edition's 10 GB
cap still applies.

## 5. Rebuild in Ireland

For when the London region is lost or unusable for a long time. This is documented, not automated: do it by hand, and rehearse it
([section 9](#9-the-quarterly-rehearsal)). It needs a console administrator, because it uses the root or administrator access the
pipeline does not have.

1. **Make sure the copies are fine.** In Ireland: `cmms-dr-vault` holds recent recovery points; `aws rds describe-db-instance-automated-backups --region eu-west-1`
   (section 4.2) shows a recent `LatestTime` when you use `replication`; the files bucket `cmms-dr-files-<account>` has the files.
   Nothing here depends on London.
2. **Set up the new region.** In CloudShell **in the new region**: `bash setup.sh` (`REGION=eu-west-1 bash setup.sh` also works).
   It creates the `cmms-github` stack there (role `cmms-github-deploy-<region>`, boundary `cmms-boundary-<region>`, template
   bucket). Set the GitHub variables `AWS_REGION` and `AWS_DEPLOY_ROLE_ARN` to the new region's values (the script prints them).
3. **Choose the trail.** `setup.sh` finds the old region's `cmms-trail` and does not create another (it sets `CreateTrail` to
   `false`). The default decision is to **keep the old region's `cmms-github` stack**: its trail keeps the account's history and
   its locked bucket. If the old region is gone for good and its trail no longer logs, a console administrator creates a
   multi-region trail in the new region by hand (a new bucket name: `cmms-trail-<account>` is taken by the old one and the
   template cannot reuse it), and the old logs stay in the old bucket until they expire. The pipeline cannot do either.
4. **Variables for a rebuild into Ireland itself.** RDS cannot copy automated backups into the region they are already in, so set
   `CMMS_IRELAND_DB_COPY` to `hourly-snapshots` for that deploy. (Rebuilding in a third region keeps `replication`.) Check
   `CMMS_DB_MAJOR_ENGINE_VERSION` against the restored engine version.
5. **Deploy.** Run **Deploy to AWS**. It deploys `cmms-dr` and `cmms-edge` again (no changes) and creates `cmms-app` in the new
   region with an **empty** database. The first run takes 30 to 40 minutes.
   - The new region gets its own weekly-export bucket, `cmms-backups-<account>-<region>`; the London one is kept.
6. **Bring the data.** Stop the new app (section 4.5, step 1). Restore the Ireland copy as a new instance **inside the new stack's
   network** (section 4.2 for the point in time, or 4.4 for a snapshot), using the subnet group, security group and option group
   of the empty instance the stack made (the commands in [section 4](#4-restore) show them). Then swap names (4.5, steps 2 and 3
   only: leave the app stopped, skip the start).
   **Reset the database password before starting the app.** The new stack made its own random password for the login
   `cmmsadmin` and keeps it in its secret (stack output `DatabaseSecretArn`); the app reads it from there. The restored copy still
   has the **old** stack's password, so the app could not sign in to it, and a deploy would not change that. Set the restored
   instance's password to the new secret's value (the secret stays the one place the password lives). The commands keep it out
   of the screen and of the shell history:
   ```bash
   PW=$(aws secretsmanager get-secret-value --region <new region> --secret-id <DatabaseSecretArn> --query SecretString --output text \
     | python3 -c 'import sys,json; print(json.load(sys.stdin)["password"])')
   aws rds modify-db-instance --region <new region> --db-instance-identifier <identifier> --master-user-password "$PW" --apply-immediately
   unset PW
   aws rds wait db-instance-available --region <new region> --db-instance-identifier <identifier>
   ```
   This takes a few minutes. Then start the app with a fresh deployment, so its tasks pick the connection up again:
   ```bash
   aws ecs update-service --region <new region> --cluster <ClusterName> --service <ServiceName> --desired-count 1 --force-new-deployment
   ```
   Put the backup settings back (4.5, step 5; in the new region), then copy the files into the new bucket:
   ```bash
   aws s3 sync "s3://cmms-dr-files-$ACCOUNT" "s3://<new FilesBucketName>" --region eu-west-1
   ```
7. **Point the name at it.** Take the new distribution's address from the `cmms-app` stack output `DistributionDomain`. If you use
   your own domain (`CMMS_DOMAIN`), change its CNAME at your DNS host to that address (the certificate is the same one, in
   N. Virginia). The old `dxxxxxxxx.cloudfront.net` address stops working with the old stack, so the Android app and printed QR
   labels that use it need the domain; technicians send waiting changes before and sign in again after.
8. **Check** `https://<address>/api/health`, sign in, open the newest records and a few attachments. Run the CMX alarms test
   (section 9, step 1) so you know alarms reach the inbox from the new region, and confirm the two subscription e-mails again if
   `CMMS_ALERT_EMAIL` changed.

## 6. Lifting the lock

Sometimes a lock has to come off: a wrong retention was set, or a copy must be deleted by law. Only a **console administrator**
can do it: the root user, or an IAM administrator who is not the deploy pipeline. The pipeline cannot, by design, and nobody
should add that ability to it.

- **Ireland vault lock.** AWS Backup (Ireland), *Vaults*, `cmms-dr-vault`, delete the vault lock configuration (or
  `aws backup delete-backup-vault-lock-configuration --region eu-west-1 --backup-vault-name cmms-dr-vault`). Governance mode
  allows it. Afterwards a recovery point can be deleted from the vault the usual way. Put the lock back by deploying again after
  fixing the template (a fix to `infra/aws/dr.yml` that changes the lock is itself a deliberate step; the retention is fixed from
  the first deploy, so do not edit it casually).
- **A London recovery point.** Only the root user can delete one (the vault's access policy). Sign in as root.
- **Object Lock on the Ireland buckets** (`cmms-dr-files-<account>`, `cmms-dr-backups-<account>`). Delete a locked version with
  the bypass flag:
  ```bash
  aws s3api delete-object --region eu-west-1 --bucket cmms-dr-files-$ACCOUNT --key "<key>" --version-id "<VersionId>" --bypass-governance-retention
  ```
  The default retention can be changed with `aws s3api put-object-lock-configuration`. Object Lock stays on the bucket; only
  retention moves.
- **The trail bucket** `cmms-trail-<account>` works the same way (governance retention, bypass flag).

Write down what you lifted and why. Compliance mode, which nobody can lift, is not used.

## 7. When a deploy fails

Every stack is deployed through a change set that is printed in the run's log before it is applied, and CloudFormation rolls a
failed update back by itself. The run's log lists the failed events and their reasons.

**A stack stuck in `UPDATE_ROLLBACK_FAILED` (`cmms-app`, `cmms-edge` or `cmms-dr`).** Rolling back sometimes needs to remove the
permissions boundary from a role that had none before 0.29.0 (the app's two task roles). The guard refuses that, so the rollback
cannot finish. The run says so and prints the exact stack and resources. Two ways out, the same thing:

1. **Actions, Deploy to AWS, Run workflow**, branch `main`: set `continue_rollback_stack` to the stack (`cmms-app`,
   `cmms-edge` or `cmms-dr`) and `resources_to_skip` to the logical resource ids printed in the failed run, comma-separated
   (for the roles: `TaskRole,TaskExecutionRole`). It runs *continue update rollback* and deploys nothing.
2. Or in CloudShell, the command the failed run printed:
   ```bash
   aws cloudformation continue-update-rollback --region <region> --stack-name cmms-app \
     --role-arn arn:aws:iam::<account>:role/cmms-cfn-exec-<region> --resources-to-skip TaskRole TaskExecutionRole
   ```
   (`cmms-edge` is in `us-east-1`, `cmms-dr` in `eu-west-1`; the execution role is the one of the app's region.)

The skipped role simply keeps its boundary. When the stack reads `UPDATE_ROLLBACK_COMPLETE`, fix the cause the log named and
run **Deploy to AWS** again with `continue_rollback_stack` back on `none`.

**The load balancer switch.** 0.29.0 replaces the public load balancer with an internal one and points CloudFront at it through
a VPC origin, in one deploy. Expect **5 to 10 minutes of errors** while the app registers behind the new load balancer; pick a
quiet hour. If the deploy fails, CloudFormation rolls back by itself and the public load balancer is back; that needs nothing
from you. If the deploy succeeds but the site misbehaves, do not re-run an old 0.28.x commit: its template would take the
permissions boundary off the app's roles, which the guard refuses, and leave the stack stuck. Deploy the last good 0.29.x commit
instead, or have a developer fix forward (to go back to a public load balancer the template has to change, keeping the boundary).
There is no do-it-yourself revert to 0.28.x: once 0.29.0 has been applied, the boundary on the app's roles cannot be removed by the
pipeline, and an older template tries to remove it.

**A failed first deployment of `cmms-edge`** (ROLLBACK_COMPLETE) is removed and started again by the workflow. **`cmms-dr`** is
never removed automatically, because it holds the locked copies: it stops with a message. Its key, vault and buckets are kept on
purpose, so after a failed first create they stay behind and a retry fails with "already exists". They are empty then; a console
administrator deletes the leftovers and the stack in the console and re-runs. **`cmms-app`** in ROLLBACK_COMPLETE after a failed
first deployment is removed and started again by the workflow; its fixed-name backup bucket, vault and option group go with it
because they were new in that deploy.

**An unused option group.** After a rolled-back deploy or a change of `CMMS_DB_MAJOR_ENGINE_VERSION`, an RDS option group (name
starts with `cmms-app-optiongroup`) can be left over. It is harmless and costs nothing. To remove it: RDS, *Option groups*,
select it, *Delete*, or `aws rds delete-option-group --region <region> --option-group-name <name>`. RDS refuses while an
instance or a snapshot still uses it; then leave it.

**The setup stack (`cmms-github`) failed on its first run** and shows `ROLLBACK_COMPLETE`: delete the stack in CloudFormation and
run `setup.sh` again. If CloudTrail had already delivered logs into `cmms-trail-<account>` before the failure, that bucket is
not empty and is locked, so it stays behind and the retry fails on its name. A console administrator removes it (delete the object
versions with `--bypass-governance-retention`, then delete the bucket), or creates a multi-region trail by hand, which makes
`setup.sh` skip creating its own.

**Teardown, then `setup.sh` again in the same region.** The trail bucket `cmms-trail-<account>` is kept when the stack is deleted
(it holds the logs). A second `setup.sh` then fails on that name. Either a console administrator removes the old bucket as above,
or creates the trail by hand and lets `setup.sh` find it.

**`CreateTrail` is `false`** when the account already had a multi-region trail that was logging. CMX then uses that trail and the
guard still stops the pipeline from changing **any** trail in the account (stop, update, delete, event selectors). It does **not**
protect the **bucket** that trail writes to: that bucket belongs to whoever set it up, and CMX's guard only covers
`cmms-trail-<account>`. Check that bucket's protection yourself (versioning, Object Lock, who can delete in it).

## 8. One-time steps after the first deploy

1. **Copy the existing files to Ireland.** Replication copies only files uploaded after it was switched on. Files already in the
   bucket need one **S3 Batch Replication** job, run once by a console administrator: S3, the files bucket (output `FilesBucketName`),
   *Management*, *Replication rules*, the rule `to-ireland`, then the option to replicate existing objects (create the batch
   job; let the console make its role). It copies every current object once. Check that the count in `cmms-dr-files-<account>`
   matches afterwards.
2. **Confirm the WAF metric names.** The `cmms-waf-blocking` alarm only fires if the firewall's metric has exactly the dimensions
   it looks for. Run:
   ```bash
   aws cloudwatch list-metrics --region us-east-1 --namespace AWS/WAFV2 --dimensions Name=WebACL,Value=cmms-edge-webacl
   ```
   It should list `WebACL` and `Rule` only. If a `Region` dimension appears, the alarm would never fire: tell a developer.
3. **Watch the app task's memory.** GuardDuty's runtime agent starts inside the app's task, which has 512 CPU units and 1,024 MB.
   After the first day, open CloudWatch, *ECS*, the service's `MemoryUtilization`. Steady use under about 80% is fine. If it
   is near the top, ask for more memory in `infra/aws/cmms.yml` (`Memory: "2048"`).
4. **See the first copies arrive.** After the first 01:00 UTC: a recovery point in `cmms-dr-vault` (Ireland); after the first
   Sunday 02:00 UTC: `native/cmms-<date>.bak` in both export buckets; with `replication`, the Ireland automated backup with a
   `LatestTime` within about 15 minutes (section 4.2), or on the fallback an hourly copy no older than 2 hours.
5. **Test the inbox once.** Section 9, step 1.

## 9. The quarterly rehearsal

Every three months, with the date and result written in the table below. It takes about two hours. The point is to find out
that a restore works before the day you need it.

1. **Alarms reach a person.** Set one alarm into the alarm state on purpose and check the e-mail arrives, in both regions:
   ```bash
   aws cloudwatch set-alarm-state --region <region> --alarm-name cmms-app-site-errors --state-value ALARM --state-reason "rehearsal"
   aws cloudwatch set-alarm-state --region us-east-1 --alarm-name cmms-waf-blocking --state-value ALARM --state-reason "rehearsal"
   ```
   Both alarms return to normal by themselves on the next check. Also check `aws sns list-subscriptions-by-topic --region <region> --topic-arn <arn>` for each topic (`us-east-1` for `cmms-edge-alerts`)
   shows no `PendingConfirmation`.
2. **The Ireland vault is current.** The newest recovery point in `cmms-dr-vault` is less than a day and a half old and the
   vault is locked:
   ```bash
   aws backup list-recovery-points-by-backup-vault --region eu-west-1 --backup-vault-name cmms-dr-vault \
     --query "sort_by(RecoveryPoints,&CreationDate)[-1].[CreationDate,Status]"
   aws backup describe-backup-vault --region eu-west-1 --backup-vault-name cmms-dr-vault --query "[Locked,MinRetentionDays,MaxRetentionDays]"
   ```
3. **Restore from Ireland.** Restore the newest Ireland recovery point (or the replicated backup) into a temporary instance
   (section 4.4 or 4.2). Time it from the start: this is your real RTO. When it shows *available*, delete the temporary instance
   in the RDS console (it is yours; the live database is not involved). Do the same for a London point-in-time restore
   (section 4.1) at least once a year.
4. **A file, both ways.** Delete a test file in the app, undelete it from the London bucket (4.6). Restore one file from the
   Ireland bucket.
5. **The export.** Download the latest `.bak` from London and from Ireland (4.7); check the sizes are similar and not zero.
   Once a year restore one into a SQL Server and open it.
6. **The restore test e-mail.** Find the last three monthly restore test e-mails; all `COMPLETED`.
7. **The firewall.** Look at *Sampled requests* and the counts per rule. In `block` mode check nothing legitimate is caught.
8. **GuardDuty and CloudTrail.** Open any findings. Check that the trail is logging and that files from this week are in
   `cmms-trail-<account>` (or in your own trail's bucket if `CreateTrail` is `false`).
9. **Size and cost.** Read the last value of the database size measurement against the 10 GB cap and the month's AWS bill
   against the $30 to $50 this release adds.
10. **Rebuild in Ireland, on paper once a year.** Read section 5 against the current templates and fix this page where it has
    drifted.
11. **The lock decision.** After a quarter of clean runs, decide whether to stay in governance mode or move to compliance mode.
    Compliance mode cannot be undone; write the decision down either way.

| Date | Who | Restore from Ireland took | Problems | Fixed |
|---|---|---|---|---|
| | | | | |

## 10. What you do, in order

The checklist behind a release of 0.29.0. Detail is in [DEPLOY_AWS.md](DEPLOY_AWS.md).

1. Pick a **quiet hour** for the deploy (5 to 10 minutes of errors while the load balancer switches).
2. **Re-run `setup.sh`** in CloudShell, in the app's region (London). The first deploy after merging stops with "Re-run
   deploy/aws/setup.sh in CloudShell" until you do.
3. In GitHub, set **`CMMS_ALERT_EMAIL`** (required). Check whether GuardDuty is already on in London and N. Virginia (set
   `CMMS_CREATE_GUARDDUTY_LONDON` or `CMMS_CREATE_GUARDDUTY_US_EAST` to `false` where a detector already exists), and
   `CMMS_DB_MAJOR_ENGINE_VERSION` against the live database. Leave the firewall on `count` and e-mail confirmation off.
4. **Run the workflow.** Then confirm **both** subscription e-mails (London and N. Virginia).
5. Do the [one-time steps](#8-one-time-steps-after-the-first-deploy) (Batch Replication, metric check, memory).
6. After a day or two, if no real users were caught, set **`CMMS_WAF_MODE` to `block`** and re-run.
7. Once **SES has production access**, set `CMMS_REQUIRE_EMAIL_CONFIRMATION` to `true` and re-run.
8. Put the first rehearsal on the calendar, three months out.
