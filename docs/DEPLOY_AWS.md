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
| Application Load Balancer | Routes CloudFront traffic to the app; accepts traffic only from CloudFront | ~$18 |
| RDS for SQL Server **Express**, db.t3.micro, 20 GB, encrypted | All data; 7 days of point-in-time restore | ~$15–25 |
| Public IPv4 addresses (load balancer, task) | Charged by AWS per address | ~$11 |
| CloudFront distribution | HTTPS address `https://dxxxxxxxx.cloudfront.net` | ~$1 at test volumes |
| S3 bucket | Attachments; deleted or replaced files kept 14 days | < $1 |
| ECR repository, CloudWatch logs (30 days), Secrets Manager secret | Images, logs, database login | ~$2 |
| VPC with 2 public + 2 private subnets, IAM roles | Network and permissions | free |

\* US East list prices; other regions are slightly higher. Roughly **$65–80 a month** in total. New AWS accounts may have
free-tier credits that cover part of this. Deleting the stacks stops the charges (see the end of this page).

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
- the role CloudFormation uses to create the app's resources.

At the end it prints two values.

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

## Day-to-day operations

- **Logs**: CloudWatch → Log groups → `/ecs/cmms-app` (or ECS → Clusters → the cluster whose name starts with `cmms-app` → its service → *Logs*).
- **Health**: `https://<address>/api/health` returns the deployed commit, e.g. `{"status":"ok","version":"8c761a6…"}`.
- **API reference**: `https://<address>/api/docs`.
- **Database backups**: RDS keeps 7 days of point-in-time restore (RDS → Databases → the database whose name starts with
  `cmms-app` → Actions → Restore to point in time). The database login is in Secrets Manager (stack output `DatabaseSecretArn`).
- **Deleted attachments** stay recoverable for 14 days. In S3, open the bucket from the stack output `FilesBucketName`
  and turn on **Show versions**.
- **Own domain** (e.g. `cmms.4c.com`): request a certificate for it in **ACM in us-east-1**, add it to the CloudFront
  distribution as an alternate domain name, and point a DNS CNAME at the `dxxxxxxxx.cloudfront.net` address.
- **Roll back**: re-run an earlier successful run of *Deploy to AWS* from the Actions tab. Each image is tagged with its
  commit. If a newer version changed the database, restore the database to a point before that deployment too.

## Remove everything

1. CloudFormation → delete the stack **`cmms-app`** (10–20 minutes). RDS keeps a final snapshot of the database, and
   the S3 bucket with attachments is kept. Delete both by hand if you don't need them.
2. CloudFormation → delete the stack **`cmms-github`**. This removes the image repository and the GitHub roles.
3. In GitHub, delete the repository variables. The *Deploy to AWS* workflow then switches itself off again.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Setup: `CAPABILITY_NAMED_IAM` or access denied | Run CloudShell as a user with administrator access to the account. |
| Deploy: *"Not authorized to perform sts:AssumeRoleWithWebIdentity"* | The workflow ran from a branch other than `main`, or the repository was renamed, or `AWS_REGION` doesn't match the region where you ran the setup. Re-run `setup.sh` (with `REPO=owner/name` if renamed). |
| Deploy fails with *"The maximum number of VPCs has been reached"* | AWS allows 5 VPCs per region by default. Ask for more in **Service Quotas**, or use another region. |
| The first deployment failed | The run's *Show why the deployment failed* step lists the reason. Fix it, then run the workflow again; it removes the failed attempt by itself before retrying. |
| *"did not report version … within 15 minutes"* | The app failed to start. Look at the newest stream in the CloudWatch log group `/ecs/cmms-app`; the first lines show why. |

## Before going into production

This setup is designed for evaluation and early customers. Before it holds production data for many customers, plan:

- **Database edition**: Express is limited to 10 GB and one instance. Move to SQL Server Standard with Multi-AZ by restoring
  a snapshot into a new instance.
- **Private networking**: run the app in private subnets with a NAT gateway or VPC endpoints, and protect the load
  balancer with a secret header from CloudFront in addition to the CloudFront-only security group.
- **Firewall and domain**: AWS WAF on the CloudFront distribution, plus your own domain and certificate.
- **Database certificate**: add the RDS certificate bundle to the image and validate the server certificate
  (`TrustServerCertificate=False`). Today the connection is encrypted but stays inside the VPC unvalidated.
- **Scale-out**: the app runs one task, because the PM scheduler and start-up migrations assume a single instance.
  Running more tasks first needs a distributed lock for the scheduler.
- **Monitoring**: CloudWatch alarms on 5xx errors, response time and database CPU and storage.
- **Separate environments**: a staging stack that deploys first, with production promoted by approval.
