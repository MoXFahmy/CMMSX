# Versioning and the Android app — design

Status: draft for approval · 2026-11-19

## Why

The cloud app is live and colleagues are starting to test it. From now on every feature, task or bug fix should ship
as a numbered release, the same number for the cloud app and a new Android app, so testers and customers can say
exactly what they are running. What exists today is **version 0.0.0**. The Android app is wanted now as an APK the
team can install on their phones.

## Decisions (from the user)

- Current state = **0.0.0**; every later feature/task/fix gets a new version, released for cloud **and** mobile together.
- Android app of type **A — an app shell around the live cloud app** (chosen over a bundled web app or a separate
  native app). Offline use comes later, when the app moves to bundling the web screens (roadmap module 11).
- Release outputs go to the assigned Drive folder `01 CMMS/1000 CMX`.

## Goals

1. One version number, stored once in the repository, shown in the cloud app, in `/api/health` and in the Android app.
2. A release procedure (and a script) that bumps the version, records the changes, tags git, builds the APK and puts
   the release outputs in the Drive folder.
3. An installable, signed Android APK for 0.0.0 that opens the live cloud app full-screen with its own icon, and in
   which signing in, photos/attachments, downloads, CSV exports and printing work.

## Non-goals (for 0.0.0)

Offline work, barcode/QR scanning, push notifications, Google Play publishing, iOS, building the APK in GitHub Actions.

## Anti-goals

- The app and the cloud showing different version numbers for the same release.
- An app that silently does nothing when a user taps Download, Export or Print.
- Losing the signing key: phones could then only take a new version after uninstalling the old one.

## Design

### Version number (my calls)

- `VERSION` file at the repository root containing `0.0.0` — the single source.
- Semantic versioning while below 1.0: **new feature → middle number** (0.1.0), **bug fix or small task → last number**
  (0.0.1). 1.0.0 is declared when the product is ready for its first paying customer.
- `CHANGELOG.md` at the root: one section per version, newest first (Added / Changed / Fixed). The 0.0.0 section
  summarises everything built so far.
- Git tag `v0.0.0` on the release commit; GitHub shows tags as releases.
- Cloud: `/api/health` keeps `version` (the commit, used by the deploy check) and adds `release: "0.0.0"`. The web
  app shows "Version 0.0.0" on the sign-in page and in the user menu. The Docker build copies `VERSION` in.
- Android: `versionName` = `VERSION`; `versionCode` = major×1,000,000 + minor×1,000 + patch + 1 (0.0.0 → 1; must rise
  with every release). The app adds `4CCMMS-Android/0.0.0` to its user agent, so the web app can show
  "Android app 0.0.0" next to the cloud version.

### Android app (my calls)

- **Capacitor** project in `mobile/` (Android only for now), configured to load the live address
  `https://dxvjhwlc6echh.cloudfront.net` (one setting; changes when the app moves to its own domain). Capacitor
  because it is also the route to the later offline app: the same project then bundles the web screens and adds camera,
  barcode and storage plugins. (A plain Kotlin web view or a Trusted Web Activity were considered: simpler today, but
  a rewrite when offline work starts.)
- App name **4C CMMS**, id `com.fourcsystems.cmms`, icon from the web logo (blue `#1c7ed6`), status bar in the same blue.
- Native behaviour the web view lacks, added in the app's `MainActivity`:
  - **Downloads** (attachments, `/api/...` links): handed to Android's download manager with the sign-in cookie.
  - **In-browser files** (the stock-take CSV is a `blob:` link): passed to the app and saved to Downloads.
  - **Printing** (`window.print()` on purchase orders, reports, stock sheets): opens Android's print dialog (printer or
    "Save as PDF").
  - **Links** to other sites open in the phone's browser; links within the app stay in the app.
  - **Photos/attachments**: the file picker offers camera and gallery.
  - **Back button** goes back in the app; at the home screen it closes the app.
  - **No connection / server unreachable**: a short message with "Try again", instead of a browser error page.
- Signing: one release key, created once, kept **outside git** in a private folder
  `1000 CMX/_private/android-signing/` (key file + password + a README), never shared with testers.

### Build and release (my calls)

- No Android tools on the Mac: the APK is built in a Docker container (JDK + Android SDK + Node), like the server.
- `scripts/release.sh <version>` (run on the Mac):
  1. checks the new version is higher and that `CHANGELOG.md` has its section;
  2. writes `VERSION`, commits "Release vX.Y.Z", tags `vX.Y.Z`;
  3. builds the signed APK;
  4. copies the outputs to `1000 CMX/Releases/vX.Y.Z/`: `4C-CMMS-X.Y.Z.apk`, `RELEASE-NOTES.md` (that version's
     changelog section, plus install instructions).
  Pushing (GitHub Desktop) then deploys the cloud app as today. For 0.0.0 the script runs without a bump.
- `docs/VERSIONING.md` describes the rules and the release steps.
- The existing `1000 CMX/Mobile App/CWorks.apk` (the legacy 4CSQL mobile app) is left untouched.

### Installing (for the team)

Share the APK from the Drive folder; on the phone allow "Install unknown apps" for Drive/Files once, open the APK,
install. Google Play Protect may warn about an app from outside the Play Store. Updates install over the previous
version as long as the same signing key is used.

## Left to the builder

Capacitor version, exact Docker image, icon/splash generation tool, wording of the offline page and release notes.
