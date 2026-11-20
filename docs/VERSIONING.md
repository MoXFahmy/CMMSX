# Versions and releases

CMX has **one version number** for the cloud app and the Android app. Every feature, task or bug fix that reaches
users ships as a new version of both.

## Where the number lives

- `VERSION` at the repository root — the only place it is written. Everything else reads it:
  the server (`/api/health` → `"release"`), the web app (sign-in page and user menu), the Android app
  (`versionName`/`versionCode`) and the release script.
- `CHANGELOG.md` — what each version added, changed or fixed, newest first.
- Git tags `vX.Y.Z` mark the commit of each release. GitHub makes them itself after the release deploys (see below).

## Numbering

`MAJOR.MINOR.PATCH`, starting from **0.0.0** (everything built up to 19 November 2026).

| Change | Example | New version |
|---|---|---|
| New feature or module | offline work orders | 0.0.0 → **0.1.0** |
| Bug fix, small task, wording | fix a report total | 0.1.0 → **0.1.1** |
| First release for paying customers | — | → **1.0.0** |

After 1.0.0, a change that breaks existing data, integrations or user workflows raises the first number.

The Android `versionCode` (which phones use to decide what is newer) is derived:
major × 1,000,000 + minor × 1,000 + patch + 1. For 0.0.0 it is 1, for 0.1.0 it is 1001.

## Making a release

1. Finish and merge the work on `main` (tests green).
2. Add the new section at the top of `CHANGELOG.md` and commit it:
   ```markdown
   ## [0.1.0] - 2026-12-01

   ### Added
   - Offline work orders in the Android app.
   ```
3. Run, on the Mac, in the CMMSX folder (Docker Desktop running):
   ```bash
   scripts/release.sh 0.1.0
   ```
   It refuses to run with uncommitted changes, a version that isn't higher, a version already tagged (here or on
   GitHub) or a missing changelog section. It writes `VERSION`, builds and signs the APK in Docker, commits
   "Release v0.1.0", and copies to the Drive folder **01 CMMS › 1000 CMX › Releases › v0.1.0**:
   - `CMX-0.1.0.apk`
   - `RELEASE-NOTES.md` (the changelog section and install steps)
4. Push `main` from GitHub Desktop. GitHub Actions tests the code, deploys the cloud app (about 10 minutes) and then
   tags the release commit `v0.1.0`; `/api/health` reports the new `release`. **Fetch origin** in GitHub Desktop
   shows the tag.
5. Share the APK from the Drive folder with the team.

The very first release used `scripts/release.sh 0.0.0 --no-bump` (release the version already in `VERSION`).

### Why GitHub makes the tags

GitHub Desktop only pushes tags created inside Desktop, so a tag made on the command line never reached GitHub. The
deploy workflow (`.github/workflows/deploy-aws.yml`, job *Tag the release*) runs `scripts/tag-release.sh` after
each successful deploy of `main`:

- `VERSION` not tagged yet: it tags the deployed commit `vX.Y.Z`.
- The same commit deployed again: nothing changes.
- `VERSION` already tagged on an earlier commit: the change is live but is not a release. The run shows the warning
  *This deploy is not a release*; ship it as a new version with `scripts/release.sh`.

If a release's deploy fails, it gets no tag. Fix the problem, or re-run **Deploy to AWS** on `main` from the
Actions tab; the tag follows a successful deploy.

## The Android signing key

Android only installs an update over an existing app when both are signed with the **same key**. The key is in
**1000 CMX › _private › android-signing** (`4c-cmms-release.jks` and `signing.env` with its passwords). It is never in
git and never shared with testers.

- Keep a second copy somewhere safe (e.g. an encrypted USB drive or a password manager).
- If it is lost, a new key can be made, but every phone must **uninstall** the old app before installing the new one.

## The Android app

`mobile/` is a [Capacitor](https://capacitorjs.com) project. Today it is an app shell that opens the live cloud app
(`server.url` in `mobile/capacitor.config.ts`) and adds downloads, file exports, printing, the back button and an
offline message. Build a test APK without releasing:

```bash
mobile/build-apk.sh debug      # → mobile/out/CMX-<version>-debug.apk
```

When the cloud app moves to its own domain, change `server.url` and make a new release.
