# Deploying to AWS

The cloud edition runs as one container on **Amazon ECS (Fargate)**, with **RDS for SQL Server** for data and **S3** for
attachments. **CloudFront** serves the app over HTTPS, with a load balancer in between. GitHub Actions builds and deploys
it: once set up, every change pushed to `main` that passes the tests goes live automatically, and you can also deploy on
demand from the Actions tab.

No AWS access keys are stored anywhere. GitHub signs in with a short-lived token that only this repository's `main`
branch can obtain. The app reads its database password from Secrets Manager and reaches S3 through its own IAM role.

Everything is created in **its own VPC and CloudFormation stacks**. Your existing instances, VPCs and databases are not
touched, and deleting the stacks removes only what they created.

## What gets created

In the region you choose:

| Resource | Purpose | Approx. cost / month* |
|---|---|---|
| ECS Fargate service, 1 task (0.5 vCPU, 1 GB) | Web app + API | ~$18 |
| Application Load Balancer (internal, no public address) | Routes CloudFront traffic to the app; CloudFront reaches it through a VPC origin, nobody else can | ~$18 |
| RDS for SQL Server **Express**, db.t3.micro, 20 GB, encrypted | All data; 35 days of point-in-time restore | ~$15–25 |
| Public IPv4 address (the app task) | Charged by AWS per address | ~$4 |
| CloudFront distribution | HTTPS address `https://dxxxxxxxx.cloudfront.net` | ~$1 at test volumes |
| S3 bucket | Attachments; deleted or replaced files kept 90 days | < $1 |
| ECR repository, CloudWatch logs (90 days), Secrets Manager secret | Images, logs, database login | ~$2 |
| VPC with 2 public + 2 private subnets (the private ones hold the load balancer), IAM roles | Network and permissions | free |

\* US East list prices; other regions are slightly higher. Roughly **$60–75 a month** for the above. New AWS accounts may
have free-tier credits that cover part of this. Deleting the stacks stops the charges (see the end of this page).

From 0.29.0 two more stacks and some account-wide services are added (see *Security and backups* below), about **$30–50 a
month** more: the web firewall (~$12), GuardDuty (~$3–10), snapshots and copies in a second region (~$3–8), the continuous
database copy (~$2–5), file and export replication (~$1–3), CloudTrail and alarms (~$1–4). VPC origins, private subnets, Object
Lock and the e-mail topics cost nothing. The restore test costs well under $1 a month. All in, expect **about $95–130 a month**.

SQL Server Express is free to license and supports databases up to 10 GB, which is plenty for evaluation. For production,
see the checklist at the end.

## Before you start

- Administrator access to the AWS account (the setup creates IAM roles).
- Choose the region, e.g. `me-central-1` (UAE), `me-south-1` (Bahrain), `eu-central-1` (Frankfurt) or `us-east-1`.
- Admin access to this GitHub repository.

## 1. Connect AWS to GitHub (once, about 2 minutes)

1. Sign in to the AWS console and switch to your chosen region (menu at the top right).
2. Open **CloudShell** (the `>_` icon in the top bar).
3. Run:
   ```bash
   curl -fsSLO https://raw.githubusercontent.com/MoXFahmy/CMMSX/main/deploy/aws/setup.sh
   ```
   ```bash
   bash setup.sh
   ```

The script ([deploy/aws/setup.sh](../deploy/aws/setup.sh)) creates one CloudFormation stack, `cmms-github`
([deploy/aws/bootstrap.yml](../deploy/aws/bootstrap.yml)), containing:
- the GitHub sign-in (an OpenID Connect provider, or it reuses yours if the account already has one);
- a role that GitHub Actions may assume only from this repository's `main` branch. That role can push images and
  deploy the `cmms-app` stack, nothing else;
- the `cmms` image repository;
- the role CloudFormation uses to create the app's resources;
- from 0.29.0: a permissions boundary that every role the app creates must carry, and a guard that stops the deploy pipeline
  from deleting backups, the locked copies in Ireland or the CloudTrail logs (see [SECURITY_AND_BACKUPS.md](SECURITY_AND_BACKUPS.md));
- from 0.29.0: a bucket for the stacks' templates, and a CloudTrail trail (`cmms-trail`, every region, into a locked bucket)
  unless the account already has a multi-region trail that is logging, which is then used instead.

The same script can be run again at any time (it is the one-time re-run described under *Security and backups* below). At the
end it prints two values.

## 2. Add the two values to GitHub

In GitHub open **Settings → Secrets and variables → Actions → Variables** and add each value as a **New repository
variable**:

| Name | Value |
|---|---|
| `AWS_REGION` | the region, e.g. `me-central-1` |
| `AWS_DEPLOY_ROLE_ARN` | `arn:aws:iam::<account>:role/cmms-github-deploy-<region>`, printed by the script |

These are identifiers, not secrets.

## 3. Deploy

Open **Actions → Deploy to AWS → Run workflow**, choose branch `main`, then **Run workflow**.

- It needs the repository variable `CMMS_ALERT_EMAIL` (the address that gets the alarms); without it the run stops at the first
  step and says so.
- The **first run takes 30–40 minutes**, most of it AWS creating the SQL Server database and the CloudFront distribution.
- Later runs take 5–10 minutes.
- When it finishes, the run's summary page shows the web address (`https://dxxxxxxxx.cloudfront.net`). It is also the
  `Url` output of the `cmms-app` stack in the CloudFormation console.

## 4. First sign-in

1. Open the address and choose **Create an organization**. You become its administrator.
2. To explore with data, use **Load sample data** on the dashboard (or under **Settings → Organization**). It adds
   three sites, about 230 assets and a year of work orders, PM, stock and purchasing history. Do this in a new, empty
   organization so the demo records don't mix with your own. On the smallest database it can take up to a minute.
3. Invite colleagues from **Settings → Users**.

### Close public sign-up

By default anyone who has the address can create their own organization. That is useful while partners and customers
try the system, but once you have your organizations you can switch it off. Add the repository variable
`CMMS_ALLOW_REGISTRATION` = `false`, then run the workflow again.

## Settings

Repository variables read by the deployment workflow:

| Variable | Default | Meaning |
|---|---|---|
| `AWS_REGION`, `AWS_DEPLOY_ROLE_ARN` | — | From step 1. The workflow is skipped while these are missing. |
| `CMMS_ALLOW_REGISTRATION` | `true` | Let visitors create new organizations |
| `CMMS_DB_INSTANCE_CLASS` | `db.t3.micro` | Database size, e.g. `db.t3.small` for more memory. Changing it restarts the database (a few minutes); data is kept. |
| `CMMS_MAIL_DOMAIN` | empty | Domain notification e-mails are sent from, e.g. `example.com` (see *Sending e-mail* below) |
| `CMMS_MAIL_FROM_ADDRESS` | empty | Address they are sent from, on that domain, e.g. `notifications@example.com` |
| `CMMS_MAIL_ALLOWED_ORGS` | empty | Organizations allowed to send e-mail while sign-up is open: short names, comma-separated (`*` = all) |
| `CMMS_DOMAIN` | empty | The app's own web address, e.g. `cmms.example.com` (see *Own domain* below) |
| `CMMS_DOMAIN_CERTIFICATE_ARN` | empty | ARN of the ACM certificate for `CMMS_DOMAIN`, requested in **us-east-1** |
| `CMMS_PUSH_SECRET_ARN` | empty | ARN of the Secrets Manager secret holding the Firebase key, for push notifications to the Android app (see *Push notifications* below) |
| `CMMS_ALERT_EMAIL` | none, **required** | Address that receives alarms, backup failures, restore test results and GuardDuty findings (see *Security and backups* below) |
| `CMMS_WAF_MODE` | `count` | `count` = the web firewall only records what it would block; `block` = it blocks. Move to `block` after a day or two |
| `CMMS_REQUIRE_EMAIL_CONFIRMATION` | `false` | New organizations must confirm their owner's e-mail address before use. Set `true` only once SES has production access |
| `CMMS_CREATE_GUARDDUTY_LONDON` | `true` | Create the GuardDuty detector in the app's region. `false` where the account already has one |
| `CMMS_CREATE_GUARDDUTY_US_EAST` | `true` | Create the GuardDuty detector in N. Virginia. `false` where the account already has one |
| `CMMS_DB_MAJOR_ENGINE_VERSION` | `16.00` | Major version of the live SQL Server engine; must match the database (see below) |
| `CMMS_IRELAND_DB_COPY` | `replication` | How the database reaches Ireland: `replication` (RDS cross-Region automated backups) or `hourly-snapshots` (the fallback, hourly copy kept 7 days) |

## Security and backups (0.29.0)

Release 0.29.0 adds a web firewall, an internal load balancer, locked backup copies in a second region, CloudTrail, GuardDuty and
alarms. What they do, what each alarm means and how to restore are in [SECURITY_AND_BACKUPS.md](SECURITY_AND_BACKUPS.md). This
section is what you do to turn it on.

### Three stacks and a bucket

| Stack | Region | Holds |
|---|---|---|
| `cmms-github` | the app's region | GitHub sign-in, image repository, the CloudFormation role, the permissions boundary and backup guard, the template bucket (`cmms-templates-<account>-<region>`), the CloudTrail trail and its bucket. Created by you with `setup.sh`. |
| `cmms-edge` (new) | `us-east-1` (N. Virginia) | The web firewall (CloudFront's firewall must live here), its alarm and e-mail topic, the GuardDuty detector for that region |
| `cmms-dr` (new) | `eu-west-1` (Ireland) | The locked backup vault, its encryption key and the two Object Lock buckets for file and database-export copies |
| `cmms-app` | the app's region | The app, database, files, backup plan, alarms, GuardDuty, and the new load balancer and CloudFront connection |

The workflow deploys `cmms-dr`, then `cmms-edge`, then `cmms-app`, and passes the ids between them. The app's template is larger
than CloudFormation takes in one piece, so the workflow uploads it to the template bucket first.

### Upgrading an existing deployment to 0.29.0 (once)

1. **Pick a quiet hour.** The deploy replaces the public load balancer with an internal one and points CloudFront at it. Expect
   **about 5 to 10 minutes of errors** (503) while the app registers behind the new load balancer. Nothing happens to the database.
2. **Re-run `setup.sh`** in CloudShell, in the app's region, exactly as in step 1 above. It is safe to run again and changes only
   what 0.29.0 needs: it lets GitHub deploy the two new stacks, adds the boundary, the guard, the template bucket and the
   trail, and prints what it added. **Until you do this, the first deploy after merging stops at once with "Re-run
   deploy/aws/setup.sh in CloudShell"; nothing is changed, run it again afterwards.** If setup finds an existing multi-region
   CloudTrail trail that is logging, it uses that one and does not create another.
3. **Set the repository variables** (Settings, Secrets and variables, Actions, Variables):
   - `CMMS_ALERT_EMAIL`: required.
   - Check GuardDuty in the AWS console in both the app's region and N. Virginia. If a detector already exists in one of them
     (an account can have only one per region), set `CMMS_CREATE_GUARDDUTY_LONDON` or `CMMS_CREATE_GUARDDUTY_US_EAST` to
     `false` for that one, and switch on S3 protection and ECS Fargate runtime monitoring on the existing detector by hand.
   - Check `CMMS_DB_MAJOR_ENGINE_VERSION` against the live database: RDS console, *Databases*, the database whose name starts
     with `cmms-app`, *Configuration*, *Engine version*, e.g. `16.00.4175.1.v1`. The variable is the first two parts,
     `16.00` (SQL Server 2022, the default). If yours is `15.00`, set it, or the deploy fails because the native-backup option
     group does not match.
   - Leave `CMMS_WAF_MODE` on `count` and `CMMS_REQUIRE_EMAIL_CONFIRMATION` on `false` for now.
4. **Run the workflow** (Actions, Deploy to AWS, Run workflow). The run prints each stack's changes before applying them. The
   first run takes about 40 minutes: the VPC origin alone can take 15.
   - If it fails because RDS refuses cross-Region automated backups for this database edition, set `CMMS_IRELAND_DB_COPY` to
     `hourly-snapshots` and run again.
   - If the run ends "stuck in UPDATE_ROLLBACK_FAILED", follow *When a deploy fails* in
     [SECURITY_AND_BACKUPS.md](SECURITY_AND_BACKUPS.md#7-when-a-deploy-fails).
5. **Confirm the two subscription e-mails.** AWS sends a "Subscription Confirmation" e-mail to `CMMS_ALERT_EMAIL` for the London
   topic (`cmms-alerts`) and another for the N. Virginia topic (`cmms-edge-alerts`). Click *Confirm subscription* in each.
   Nothing reaches you until you do.
6. **Do the one-time steps** in [SECURITY_AND_BACKUPS.md](SECURITY_AND_BACKUPS.md#8-one-time-steps-after-the-first-deploy):
   copy the existing files to Ireland (S3 Batch Replication), check the firewall's metric names, watch the app task's memory.

### Move the web firewall to block

The firewall starts in `count`: it records what each rule would block and blocks nothing. After a day or two, in the N. Virginia
console open *WAF & Shield*, *Web ACLs*, `cmms-edge-webacl`, and look at the sampled requests and the counted requests per rule.
If real people (your own sign-ins, uploads, imports) were caught, tell a developer before going on. If only obvious junk was,
set `CMMS_WAF_MODE` to `block` and run the workflow again; no code changes. If blocking later catches real users, set it back to
`count` and run again.

### Turn on the e-mail check for new organizations

With `CMMS_REQUIRE_EMAIL_CONFIRMATION` on, a new organization's owner gets a link by e-mail and cannot use CMX until they click
it; organizations that never confirm are deleted after 7 days. This needs working e-mail to **strangers**, which the Amazon SES
sandbox does not allow. First check that SES has production access, in the app's region:

```bash
aws sesv2 get-account --region <region> --query ProductionAccessEnabled
```

`true` means go ahead (see *Sending e-mail* below if it is `false`). Then set `CMMS_REQUIRE_EMAIL_CONFIRMATION` to `true` and run
the workflow. Test with a new sign-up using an address you own. With it on while SES is still in the sandbox, people could
sign up, never get the e-mail, and lose their organization after 7 days: leave it off until production access is granted.

## Sending e-mail

CMX e-mails requesters, technicians, approvers and suppliers through **Amazon SES**. The app's role is allowed to
send, so no password is stored. Mail comes from your own domain; AWS has to know the domain is yours, and new AWS
accounts may only send to addresses verified one by one (the SES *sandbox*) until you ask for normal sending.

1. In GitHub → **Settings → Secrets and variables → Actions → Variables**, add `CMMS_MAIL_DOMAIN` (e.g. `example.com`)
   and `CMMS_MAIL_FROM_ADDRESS` (e.g. `notifications@example.com`).
2. Deploy: push `main`, or re-run **Deploy to AWS** from the Actions tab.
3. CloudFormation → stack **`cmms-app`** → **Outputs**: `MailDkimRecord1`, `MailDkimRecord2` and `MailDkimRecord3`
   each read `<name> CNAME <value>`. Add the three CNAME records at your DNS host.
4. Wait until **SES → Identities** shows the domain as *Verified* (usually minutes, up to 72 hours).
5. **SES → Account dashboard → Request production access**: mail type *Transactional*, your web address, and a line such
   as "maintenance notifications to our own staff and suppliers; no marketing". AWS usually answers within a day. Until
   then, verify a few test addresses under **SES → Identities → Create identity → Email address** and send only to
   those.
6. **Decide which organizations may send.** While sign-up is open (`CMMS_ALLOW_REGISTRATION` is `true`), anyone could
   create an organization and send mail from your domain, so e-mail works only for organizations listed in
   `CMMS_MAIL_ALLOWED_ORGS` (their short names, shown on **Settings → Notifications** when not yet allowed), then
   deploy again. With sign-up closed, every organization may send and the variable can stay empty. `*` allows every
   organization; use it only with sign-up closed.
7. In CMX, each organization's administrator opens **Settings → Notifications**, sends a test e-mail and switches
   e-mail on. Every organization starts with e-mail **off**.

The **E-mail log** on that page shows each message and, for failures, the reason (SES's message, e.g. an address not
verified while in the sandbox). Failed messages are retried after 1, 5, 30 and 120 minutes.

## Push notifications

From 0.11.0 the Android app can show a notification when a job is assigned to the technician or their request is
approved; tapping it opens the job. It needs a Firebase project (free) and the 0.11.0 app or later. Releases build the
app with the project's app file (`google-services.json`); the server needs the project's **service account key** to
send.

1. In the Firebase console → **Project settings → Service accounts → Generate new private key**. A key file
   (`.json`) downloads. Keep it private: it lets anyone send notifications as your project.
2. AWS console, in the app's region → **Secrets Manager → Store a new secret** → *Other type of secret* → **Plaintext**
   tab: paste the whole contents of the key file. Name it, e.g. `cmms/push-key`, and store it.
3. Open the secret and copy its **Secret ARN** (the full one, ending in six random characters).
4. In GitHub → **Settings → Secrets and variables → Actions → Variables**, add `CMMS_PUSH_SECRET_ARN` with that ARN.
5. Deploy: push `main`, or re-run **Deploy to AWS** from the Actions tab.

The app reads the key from the secret when it starts; it never appears in GitHub, the logs or the stack's settings. To
change the key, store a new value in the same secret and restart the service (ECS → cluster → service → *Update* →
*Force new deployment*). With the variable empty, nothing is sent and nothing waits to be sent. The phone asks once for
permission to show notifications; a phone that refused gets none.

Uploads from the phone (videos and documents) can be up to 50 MB. CloudFront and the load balancer accept that; if you
put another proxy in front of the app, let it pass request bodies of 52 MB.

## Day-to-day operations

- **Logs** (kept 90 days): CloudWatch → Log groups → `/ecs/cmms-app` (or ECS → Clusters → the cluster whose name starts with `cmms-app` → its service → *Logs*).
- **Health**: `https://<address>/api/health` returns the deployed commit, e.g. `{"status":"ok","version":"8c761a6…"}`.
- **API reference**: `https://<address>/api/docs`.
- **Database backups**: RDS keeps 35 days of point-in-time restore (RDS → Databases → the database whose name starts with
  `cmms-app` → Actions → Restore to point in time), daily and monthly snapshots are also copied to Ireland, and a weekly `.bak`
  export is kept 90 days. Every way to restore is in [SECURITY_AND_BACKUPS.md](SECURITY_AND_BACKUPS.md#4-restore). The database
  login is in Secrets Manager (stack output `DatabaseSecretArn`).
- **Alarms** arrive at `CMMS_ALERT_EMAIL`; what each one means and what to do is in
  [SECURITY_AND_BACKUPS.md](SECURITY_AND_BACKUPS.md#3-alarms-and-e-mails).
- **Deleted attachments** stay recoverable for 90 days (and a copy of every file is kept in Ireland). In S3, open the bucket
  from the stack output `FilesBucketName` and turn on **Show versions**.
- **Own domain** (e.g. `cmms.example.com`):
  1. In **ACM, region us-east-1** (CloudFront only uses certificates from there), request a public certificate for the
     name with DNS validation, add the validation CNAME it shows at your DNS host, and wait until it is *Issued*.
  2. At your DNS host, add a CNAME for the name pointing at the `dxxxxxxxx.cloudfront.net` address (stack output
     `DistributionDomain`).
  3. Set the repository variables `CMMS_DOMAIN` and `CMMS_DOMAIN_CERTIFICATE_ARN`, then deploy. E-mail links and new QR
     labels use the new address; the `dxxxxxxxx.cloudfront.net` address keeps working, so old labels still open.
  4. The Android app opens a fixed address (`mobile/capacitor.config.ts`, `mobile/www/*.html`): change it and release a
     new version. Sign-ins and offline data on a phone belong to one address, so technicians send their waiting changes
     before updating and sign in once more after.
- **Roll back**: re-run an earlier successful run of *Deploy to AWS* from the Actions tab. Each image is tagged with its
  commit. If a newer version changed the database, restore the database to a point before that deployment too.

## Remove everything

1. If GuardDuty runtime monitoring of the app task is on, turn it off first (GuardDuty console, *Runtime monitoring*, ECS Fargate
   agent management). GuardDuty adds a network endpoint to the VPC, and it would block the VPC from being deleted.
2. CloudFormation → delete the stack **`cmms-app`** (10–20 minutes). RDS keeps a final snapshot of the database, and
   the S3 buckets with attachments and database exports, the backup vault with its snapshots, and the native-backup option group
   are kept on purpose. Delete them by hand if you don't need them.
3. CloudFormation (N. Virginia) → delete **`cmms-edge`**. CloudFormation (Ireland) → delete **`cmms-dr`** only if you
   no longer want the locked copies: it keeps its vault, key and buckets anyway, and the locks mean only a console administrator
   can remove them (see *Lifting the lock* in [SECURITY_AND_BACKUPS.md](SECURITY_AND_BACKUPS.md#6-lifting-the-lock)).
4. CloudFormation → delete the stack **`cmms-github`**. This removes the image repository and the GitHub roles. The CloudTrail
   bucket `cmms-trail-<account>` stays (it holds the logs); running `setup.sh` again later in the account needs it removed or
   needs a trail created by hand (see *When a deploy fails* in [SECURITY_AND_BACKUPS.md](SECURITY_AND_BACKUPS.md#7-when-a-deploy-fails)).
5. In GitHub, delete the repository variables. The *Deploy to AWS* workflow then switches itself off again.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Setup: `CAPABILITY_NAMED_IAM` or access denied | Run CloudShell as a user with administrator access to the account. |
| Deploy: *"Not authorized to perform sts:AssumeRoleWithWebIdentity"* | The workflow ran from a branch other than `main`, or the repository was renamed, or `AWS_REGION` doesn't match the region where you ran the setup. GitHub now puts the owner's and repository's numeric ids in its token (`repo:owner@123/name@456:ref:refs/heads/main`); `setup.sh` looks them up on api.github.com, so a stack created by an older `setup.sh`, or for a private repository it could not look up, accepts only the name-only form. Re-run the current `setup.sh` (with `REPO=owner/name` if renamed). CloudTrail → Event history → `AssumeRoleWithWebIdentity` shows the subject GitHub sent. |
| Deploy fails with *"The maximum number of VPCs has been reached"* | AWS allows 5 VPCs per region by default. Ask for more in **Service Quotas**, or use another region. |
| The first deployment failed | The run's *Show why the deployment failed* step lists the reason. Fix it, then run the workflow again; it removes the failed attempt by itself before retrying. |
| Deploy stops at once with *"Re-run deploy/aws/setup.sh in CloudShell"* | After 0.29.0 the setup has to be run once more (*Upgrading to 0.29.0* above). Run it, then re-run the workflow. |
| Deploy stops with *"Set the repository variable CMMS_ALERT_EMAIL"* | Add the variable (the address that gets alarms), then re-run. |
| A stack is *stuck in UPDATE_ROLLBACK_FAILED* | See *When a deploy fails* in [SECURITY_AND_BACKUPS.md](SECURITY_AND_BACKUPS.md#7-when-a-deploy-fails). |
| *"did not report version … within 15 minutes"* | The app failed to start. Look at the newest stream in the CloudWatch log group `/ecs/cmms-app`; the first lines show why. |

## Before going into production

This setup is designed for evaluation and early customers. Before it holds production data for many customers, plan:

- **Database edition**: Express is limited to 10 GB and one instance. Move to SQL Server Standard with Multi-AZ by restoring
  a snapshot into a new instance.
- **Private networking**: from 0.29.0 the load balancer is internal and only CloudFront's VPC origin reaches it. The app task
  itself still runs in a public subnet (it calls Anthropic, Firebase and SES); moving it to private subnets needs a NAT gateway.
- **Firewall and domain**: from 0.29.0 AWS WAF sits on the CloudFront distribution (start in `count`, then `block`). Add your
  own domain and certificate if you have not.
- **Database certificate**: add the RDS certificate bundle to the image and validate the server certificate
  (`TrustServerCertificate=False`). Today the connection is encrypted but stays inside the VPC unvalidated.
- **Scale-out**: the app runs one task, because the PM scheduler and start-up migrations assume a single instance.
  Running more tasks first needs a distributed lock for the scheduler.
- **Monitoring**: from 0.29.0 alarms cover 5xx errors, app down, database CPU, storage and size, failed sign-ins, backups and
  restore tests. Response time is not alarmed.
- **Separate environments**: a staging stack that deploys first, with production promoted by approval.
