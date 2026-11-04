# Deploying to Azure

The cloud edition runs as one container on **Azure Container Apps**, with **Azure SQL Database** for data and
**Blob Storage** for attachments. GitHub Actions builds and deploys it: once set up, every change pushed to `main`
that passes the tests goes live automatically, and you can also deploy on demand from the Actions tab.

Nothing in the setup uses passwords or keys. GitHub signs in to Azure with a short-lived token that only this
repository's `main` branch can obtain. The app signs in to SQL and Storage with its own managed identity.

## What gets created

All in one resource group (default `rg-cmms-test`):

| Resource | Purpose | Approx. cost / month* |
|---|---|---|
| Container App `cmms` (0.5 vCPU, 1 GiB, 1 replica) | Web app + API, HTTPS address `https://cmms.<…>.azurecontainerapps.io` | $10–40 (less when idle) |
| Azure SQL Database `cmms`, Standard S1 (20 DTU) | All data; 7-day point-in-time restore | ~$30 |
| Container Registry (Basic) | Stores the app images | ~$5 |
| Storage account | Attachments (14-day soft delete) | < $1 |
| Log Analytics workspace | App logs, 30 days | usually < $5 |
| Managed identity | The app's sign-in to SQL, Storage and the registry | free |

\* US list prices; other regions differ slightly. Deleting the resource group stops all charges (see the end of this page).

## Before you start

- An Azure subscription where you are **Owner** (the setup creates a resource group and assigns roles).
- Permission to create **app registrations** in Microsoft Entra ID (allowed for all users by default; if your
  organization restricts it, ask an Entra administrator to run step 1).
- Admin access to this GitHub repository.

## 1. Connect Azure to GitHub (once, about 3 minutes)

1. Sign in to the [Azure portal](https://portal.azure.com) and open **Cloud Shell** (the `>_` icon at the top), choosing **Bash**.
2. Run:
   ```bash
   curl -fsSLO https://raw.githubusercontent.com/MoXFahmy/CMMSX/main/deploy/azure/setup.sh
   ```
   ```bash
   bash setup.sh
   ```
   To choose another region, prefix the second command, e.g. `LOCATION=westeurope bash setup.sh`.
   The default is `uaenorth` (UAE North).

The script ([deploy/azure/setup.sh](../deploy/azure/setup.sh)) registers the Azure services used, creates the resource
group, and creates a sign-in for GitHub Actions that can manage **only that resource group**. It can grant only
the two roles the app itself needs. At the end it prints four values.

## 2. Add the four values to GitHub

In GitHub open **Settings → Secrets and variables → Actions → Variables** and add each value as a **New repository
variable**:

| Name | Value |
|---|---|
| `AZURE_CLIENT_ID` | printed by the script |
| `AZURE_TENANT_ID` | printed by the script |
| `AZURE_SUBSCRIPTION_ID` | printed by the script |
| `AZURE_RESOURCE_GROUP` | `rg-cmms-test` (or the name you chose) |

These are identifiers, not secrets.

## 3. Deploy

Open **Actions → Deploy to Azure → Run workflow**, choose branch `main`, then **Run workflow**. The first run takes about
15 minutes because it creates everything. Later runs take about 5 minutes. When it finishes, the run's summary page shows
the web address.

## 4. First sign-in

1. Open the address and choose **Create an organization**. You become its administrator.
2. To explore with data, use **Load sample data** on the dashboard (or under **Settings → Organization**). It adds three
   sites, about 230 assets and a year of work orders, PM, stock and purchasing history. Do this in a new, empty
   organization so the demo records don't mix with your own.
3. Invite colleagues from **Settings → Users**.

### Close public sign-up

By default anyone who has the address can create their own organization. That is useful while partners and customers
try the system, but once you have your organizations you can switch it off. Add the repository variable
`CMMS_ALLOW_REGISTRATION` = `false`, then run the workflow again. The sign-in page then no longer offers
"Create an organization".

## Settings

Repository variables read by the deployment workflow:

| Variable | Default | Meaning |
|---|---|---|
| `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` | — | Azure sign-in created in step 1. The workflow is skipped while these are missing. |
| `AZURE_RESOURCE_GROUP` | — | Where everything is deployed |
| `CMMS_ALLOW_REGISTRATION` | `true` | Let visitors create new organizations |
| `CMMS_DATABASE_SKU` | `S1` | Database size: `Basic`, `S0`, `S1`, `S2`, `S3`. Changing it causes no data loss and almost no downtime. |

## Day-to-day operations

- **Logs**: Azure portal → the container app `cmms` → **Monitoring → Log stream** (live) or **Logs** (searchable).
- **Health**: `https://<address>/api/health` returns the deployed commit, e.g. `{"status":"ok","version":"4624387…"}`.
- **API reference**: `https://<address>/api/docs`.
- **Database backups**: Azure SQL keeps 7 days of point-in-time restore. Restore from the portal (SQL database → **Restore**).
- **Deleted attachments** can be recovered for 14 days (storage account → container `files` → show deleted blobs).
- **Own domain** (e.g. `cmms.4c.com`): container app → **Custom domains** → add the domain with a free managed certificate.
- **Roll back**: re-run an earlier successful run of *Deploy to Azure* from the Actions tab. Each image is tagged with
  its commit. If a newer version changed the database, restore the database to a point before that deployment too.

## Remove everything

In Cloud Shell:
```bash
az group delete --name rg-cmms-test
```
```bash
az ad app delete --id <AZURE_CLIENT_ID>
```
Then delete the four repository variables. The *Deploy to Azure* workflow then switches itself off again.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Setup: `az ad app create` fails with insufficient privileges | Your organization restricts app registrations; ask an Entra administrator to run step 1. |
| Deploy: *"No matching federated identity record"* (`AADSTS700213`) | The workflow ran from a branch other than `main`, or the repository was renamed. Re-run `setup.sh` with `REPO=owner/name`. |
| Deploy: SQL *"location is not accepting creation of new … servers"* | That region has no SQL capacity for your subscription. Delete the resource group, re-run `setup.sh` with another `LOCATION` (e.g. `westeurope`, `northeurope`, `swedencentral`, `italynorth`), update `AZURE_RESOURCE_GROUP` if you changed it, and deploy again. |
| Deploy: an error about the managed identity or principal not being found | Azure had not yet finished creating the new identity. Re-run the workflow. |
| *"did not report version … within 10 minutes"* | The app failed to start. Open **Log stream** on the container app; the first lines show why (often the database connection). |

## Before going into production

This setup is designed for evaluation and early customers. Before it holds production data for many customers, plan:

- **Private networking**: put the container app in a virtual network and reach SQL and Storage through private endpoints
  instead of the "Allow Azure services" firewall rule.
- **Front door**: Azure Front Door with a web application firewall in front of the app, plus your own domain.
- **Scale-out**: the app runs one replica, because the PM scheduler and start-up migrations assume a single
  instance. Running more replicas first needs a distributed lock for the scheduler.
- **Key protection**: sign-in cookie keys are stored in the database (encrypted at rest by Azure SQL). Wrap them with an
  Azure Key Vault key for defence in depth.
- **Resilience**: zone-redundant database and storage, longer backup retention, and alerts on errors and response time.
- **Separate environments**: a staging resource group that deploys first, with production promoted by approval.
