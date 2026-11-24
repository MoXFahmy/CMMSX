# The CMX API (0.26.0)

Other systems can read and change CMX data with an **API key**: an ERP reading work orders and costs or creating purchase orders,
a BI tool reading history, a sensor gateway posting meter readings or raising requests. Every screen in CMX uses the same API, so
anything the app can do, an integration can do, within the access you give it.

The full list of endpoints, with their fields, is at **`/api/docs`** on your CMX address (open it signed in, or call
`/openapi/v1.json` with a key).

## Service accounts and keys

A key never acts as a person. It acts as a **service account**: an account for a system, not a person, with its own roles, sites
and branches, like a user.

1. **Settings › Users › New service account** (Manage users): name it after the system ("SAP integration"), and give it the
   roles and sites the integration needs. The Viewer role reads; give more only if it must change data.
2. **Settings › API keys › New key**: choose the service account, name the key ("Power BI nightly"), tick **Read only** if it
   only reads, and set an expiry date if you want one.
3. **Copy the key now.** It looks like `cmx_` and 40 letters and digits, and it's shown once: CMX keeps only a fingerprint of it
   and can't show it again. Store it where your integration keeps secrets.

The key list shows each key's first characters, who made it, when it was last used, and whether it is active, expired, revoked or
stopped by its account. **Revoke** ends a key at once. Deactivating or deleting a service account stops all its keys.

## Calling the API

Send the key with every request, either way:

```
Authorization: Bearer cmx_…
X-Api-Key: cmx_…
```

Requests and answers are JSON. Dates are ISO 8601 (`2026-11-24T08:30:00Z`); ids are GUIDs.

- **Lists are paged:** `?page=1&pageSize=50` (most lists allow up to 200), answering `{ "items": [...], "total": 312, "page": 1, "pageSize": 50 }`.
- **The key sees what its account may see:** its roles decide what it may read and change, and its sites and branches limit the
  records it sees, exactly as for a person.
- **Changes show who made them:** the activity log and each record's History tab name the service account, with the source
  **API**.

### What a key can't do

Whatever its roles, a key can't:
- sign in or out, or change passwords;
- create, change or deactivate users, service accounts or roles (it may read the users and roles lists if its roles allow);
- make or revoke keys;
- change security settings or anonymise anyone;
- use the technician app's sync.

These answer 403 "API keys can't do this.". A read-only key answers 403 "This key is read-only." to anything but reading.

### Limits and errors

| Answer | Meaning |
| --- | --- |
| 400 | The request is wrong; `errors` lists each field's problem. |
| 401 "The API key isn't valid." | The key is missing, wrong, revoked or expired, or its account or organization is inactive. |
| 403 | The account's roles don't allow it, or keys can't do it. |
| 404 | No such record, or outside the account's sites. |
| 429 "Too many requests with this key." | More than 120 requests in a minute with one key. Wait the seconds in the `Retry-After` header. |

## Imports by key

Imports run in the background (see Settings › Import). `POST /api/import/check` or `POST /api/import/run` with the workbook as
`file` (multipart) answers 202 with `{ "id": … }`. Then `GET /api/import/jobs/{id}` until `status` is `Done` or `Failed`; `result`
holds the counts and problems, `GET /api/import/jobs/{id}/problems` the problem rows. Up to 20,000 rows a sheet, 50,000 in all,
25 MB. Their changes show the source **Import**.

## Examples

Open work orders:

```bash
curl -H "Authorization: Bearer $CMX_KEY" "https://cmx.example.com/api/work-orders?status=Open&pageSize=100"
```

A meter reading (a sensor gateway):

```bash
curl -X POST -H "Authorization: Bearer $CMX_KEY" -H "Content-Type: application/json" \
  -d '{"value": 12450, "readAt": "2026-11-24T06:00:00Z", "note": "From the BMS"}' \
  "https://cmx.example.com/api/meters/{meterId}/readings"
```

A work request (an alarm raises it):

```bash
curl -X POST -H "Authorization: Bearer $CMX_KEY" -H "Content-Type: application/json" \
  -d '{"title": "Chiller 2 high pressure alarm", "siteId": "{siteId}", "locationId": "{locationId}", "priority": "High", "type": "Corrective"}' \
  "https://cmx.example.com/api/work-requests"
```
