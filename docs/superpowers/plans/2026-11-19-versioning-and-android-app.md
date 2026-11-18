# Versioning and Android App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Release what exists as version 0.0.0 for the cloud app and a new Android app shell, with one version number and a release script that drops each release's APK and notes into the Drive folder.

**Architecture:** A root `VERSION` file is the only place the number lives: MSBuild, Vite, Gradle and the release script all read it. The Android app is a Capacitor 8 project in `mobile/` that loads the live cloud address and adds native handling for downloads, in-browser files, printing, the back button and being offline. The APK is built and signed inside a Docker container.

**Tech Stack:** ASP.NET Core 10, React 19 + Vite, Capacitor 8.6.2 (Android), Java (MainActivity), Gradle, Docker (JDK 21 + Android SDK + Node 22), bash.

**Spec:** `docs/superpowers/specs/2026-11-19-versioning-and-android-app-design.md`

## Global Constraints

- Current version **0.0.0**; semantic versioning: feature → middle number, fix/small task → last number.
- `VERSION` (repo root) is the single source; nothing else hard-codes the number.
- Android: app name **4C CMMS**, id `com.fourcsystems.cmms`, loads `https://dxvjhwlc6echh.cloudfront.net`, user agent gets `4CCMMS-Android/<version>`.
- `versionCode` = major×1,000,000 + minor×1,000 + patch + 1 (0.0.0 → 1).
- Signing key and its passwords live only in `$DRIVE/_private/android-signing/` — never in git.
- Release outputs: `$DRIVE/Releases/vX.Y.Z/4C-CMMS-X.Y.Z.apk` and `RELEASE-NOTES.md`.
- Brand blue `#1c7ed6`.
- `$DRIVE` is the assigned Drive folder `…/My Drive/01 CMMS/1000 CMX`. Scripts never hard-code its full path, because the path contains the account name: `release.sh` uses `RELEASE_DRIVE_DIR` if set, else the single match of `~/Library/CloudStorage/GoogleDrive-*/My Drive/01 CMMS/1000 CMX`, and stops if there are zero or several matches.

## Commands

- Server tests — `.superpowers/bin/dt '<filter>'` (git-ignored helper; Task 1 changes it to mount the whole repo so `VERSION` is visible).
- Web build — `docker run --rm -v "$PWD":/repo:ro -v cmmsx-npm:/root/.npm node:22-alpine sh -c 'cp -r /repo /w && cd /w/web && rm -rf node_modules && npm ci --no-audit --no-fund >/dev/null && npm run build'`
- Android build — `mobile/build-apk.sh [debug|release]` (Task 3), runs everything inside the `cmms-android-build` image.
- Inspect an APK — `docker run --rm -v "$PWD/mobile/out":/out cmms-android-build sh -c 'aapt2 dump badging /out/<file>.apk | head -5; apksigner verify --print-certs /out/<file>.apk'`

## Review Focus

1. **Signed-in state after closing and reopening the app** — expected: still signed in ("Keep me signed in"). Task 4: cookies flushed when the app pauses.
2. **An attachment download** — expected: the real file saved with the user's session, not the sign-in page as HTML. Task 4: the session cookie is passed to the download manager.
3. **Opening the app with no network** — expected: the offline page with "Try again", not a white screen or a browser error. Task 3 (`server.errorPath`), checked by Task 3's config test.
4. **The page header under the phone's status bar** (Android 15 draws apps edge to edge) — expected: nothing hidden behind the clock and battery icons. Task 4 sets Capacitor's edge-to-edge margin handling, checked by its config test.
5. **Installing 0.0.1 over 0.0.0** — expected: in-place update, still signed in. This needs the same key and a higher `versionCode`. Task 2's release-script tests refuse a non-increasing version; Task 5 backs up the key.

---

### Task 0: Branch

- [ ] **Step 1:** `git switch -c versioning-android` from `main` (`195cc61`); commit the spec and this plan: `git commit -m "Design and plan: versioning and Android app"`.

### Task 1: Version 0.0.0 in the cloud app

**Files:**
- Create: `VERSION` (`0.0.0` + newline), `CHANGELOG.md`, `server/Directory.Build.props`
- Modify: `server/FourC.Cmms.Api/Program.cs:98-99`, `Dockerfile`, `web/vite.config.ts`, the web env typings (`web/src/vite-env.d.ts` or equivalent), `web/src/pages/AuthPages.tsx`, `web/src/components/AppLayout.tsx`, `web/src/components/utils.ts`, `web/src/locales/en.json`, `.github/workflows/deploy-aws.yml` (Summary step), `.superpowers/bin/dt`
- Test: `server/FourC.Cmms.Tests/HostingTests.cs`

**Interfaces:**
- Produces: `/api/health` → `{ status, version, release }`, where `release` is the `VERSION` text. Web global `__APP_VERSION__: string`. Helper `appVersionLabel(): string` in `web/src/components/utils.ts` returns `"Version 0.0.0"`, plus `" · Android app 0.0.0"` when `navigator.userAgent` contains `4CCMMS-Android/x.y.z`.

- [ ] **Step 1: Write the failing test** `Health_reports_the_release_version` in `HostingTests`: GET `/api/health` → JSON `release` equals the trimmed `VERSION` file, found by walking up from `AppContext.BaseDirectory` until a `VERSION` file exists. Also assert `release` matches `^\d+\.\d+\.\d+$`.
- [ ] **Step 2:** Change `dt` to mount the repository root (`-v "$HOME/Projects/CMMSX":/repo:ro`, then `cp -r /repo/server /src && cp /repo/VERSION /VERSION`). Create `VERSION`. Run `dt 'FullyQualifiedName~Health_reports'` — expected: FAIL (no `release` property).
- [ ] **Step 3:** In `server/Directory.Build.props`, set `<Version>` from `$(MSBuildThisFileDirectory)../VERSION` (trimmed) when the file exists, else `0.0.0-dev`. In `Program.cs`, `release` = `typeof(Program).Assembly`'s `AssemblyInformationalVersionAttribute`, cut at the first `+`. Add `release` to the health response.
- [ ] **Step 4:** `dt ''` — expected: all pass (47 + 1).
- [ ] **Step 5:** Web: in `vite.config.ts`, `define: { __APP_VERSION__: JSON.stringify(<../VERSION, trimmed>) }`. Declare it in the env typings. Add `appVersionLabel()`. Show it as a dimmed line under the sign-in card in `AuthPages.tsx`, and as a `Menu.Label` at the bottom of the user menu in `AppLayout.tsx`. Locale keys: `common.version` = `"Version {{v}}"`, `common.androidApp` = `"Android app {{v}}"`.
- [ ] **Step 6:** `Dockerfile`: `COPY VERSION /VERSION` in both the `web` and `api` stages, before their builds. `deploy-aws.yml` Summary: add `· version $(cat VERSION)`.
- [ ] **Step 7:** `CHANGELOG.md` in Keep-a-Changelog form. A `## [0.0.0] - 2026-11-19` section lists the modules (from README's Modules table) and the latest work: multi-asset PM rounds, step readings and remarks, importer fixes and results, AWS deployment.
- [ ] **Step 8:** Web build (command above) — expected: built, no TypeScript errors. `docker compose up -d --build app`, then `curl -s localhost:8080/api/health` — expected: `"release":"0.0.0"`. The sign-in page shows "Version 0.0.0".
- [ ] **Step 9: Commit** `git commit -m "Version 0.0.0: VERSION file, changelog, version in health and UI"`.

### Task 2: Release script

**Files:**
- Create: `scripts/release.sh`, `scripts/release-lib.sh`, `scripts/test-release.sh`, `docs/VERSIONING.md`
- Modify: `.github/workflows/ci.yml` (shellcheck `scripts/*.sh`, run `scripts/test-release.sh`)

**Interfaces:**
- Produces, in `release-lib.sh` (sourced, pure bash functions):
  - `is_semver <v>` — exit 0 for `X.Y.Z` with non-negative integers and no leading zeros.
  - `version_gt <a> <b>` — exit 0 when a > b.
  - `changelog_has <file> <v>` — exit 0 when the file has `## [<v>]`.
  - `changelog_section <file> <v>` — prints that section without its heading.
  - `find_drive_dir` — prints `$RELEASE_DRIVE_DIR` or the single glob match from Global Constraints; non-zero exit otherwise.
- `release.sh <version> [--no-bump]`, steps: validate → write `VERSION` → commit `Release vX.Y.Z` → tag `vX.Y.Z` → `mobile/build-apk.sh release` → copy outputs to `$(find_drive_dir)/Releases/vX.Y.Z/`. It refuses when the work tree is dirty, the tag exists, the version isn't higher (unless `--no-bump` with version == `VERSION`), or the changelog section is missing.

- [ ] **Step 1: Write failing tests** in `scripts/test-release.sh` (plain bash, prints `ok`/`FAIL`, exits non-zero on any failure):
  - `is_semver`: `0.0.0` ✓, `1.10.3` ✓, `01.0.0` ✗, `1.0` ✗, `1.0.0-beta` ✗.
  - `version_gt`: `0.0.1 0.0.0` ✓, `0.1.0 0.0.9` ✓, `0.10.0 0.9.0` ✓, `0.0.0 0.0.0` ✗, `0.0.1 0.1.0` ✗.
  - `changelog_has`/`changelog_section` on a fixture written to a temp file.
  - `find_drive_dir` with `RELEASE_DRIVE_DIR` pointing at a temp dir prints it.
- [ ] **Step 2:** `bash scripts/test-release.sh` — expected: FAIL (library missing).
- [ ] **Step 3:** Implement `release-lib.sh`, then `release.sh` on top of it.
- [ ] **Step 4:** `bash scripts/test-release.sh` — expected: all `ok`. `shellcheck scripts/*.sh` via `koalaman/shellcheck:stable` — expected: clean. Update `ci.yml` as listed.
- [ ] **Step 5:** `docs/VERSIONING.md`: the numbering rules, what goes in the changelog, release steps (edit `CHANGELOG.md` → `scripts/release.sh X.Y.Z` → push from GitHub Desktop → cloud deploys → share the APK from Drive), where the signing key lives and why it must not be lost.
- [ ] **Step 6: Commit** `git commit -m "Release script and versioning guide"`.

### Task 3: Android app project and APK build

**Files:**
- Create:
  - `mobile/package.json` (`@capacitor/core`, `@capacitor/android`, `@capacitor/cli` 8.6.2; dev `@capacitor/assets` 4.0.0) and `mobile/package-lock.json`
  - `mobile/capacitor.config.ts`
  - `mobile/www/index.html` (redirects to the cloud address) and `mobile/www/offline.html`
  - `mobile/assets/icon-foreground.png` + icon background colour `#1c7ed6` (from `web/public/favicon.svg`)
  - `mobile/Dockerfile` (image `cmms-android-build`: `eclipse-temurin:21-jdk`, Node 22, Android cmdline-tools, the platform and build-tools that `android/variables.gradle` names)
  - `mobile/build-apk.sh`
  - `mobile/android/` (generated by `npx cap add android`, committed)
  - `mobile/.gitignore` (`node_modules`, `out/`, `android/keystore.properties`, build dirs)
  - `mobile/test-config.mjs`
- Modify: `mobile/android/app/build.gradle` (version + signing)

**Interfaces:**
- Consumes: `VERSION` (Task 1).
- Produces: `mobile/build-apk.sh debug|release` → `mobile/out/4C-CMMS-<version>[-debug].apk`. Release signing reads `android/keystore.properties` (`storeFile`, `storePassword`, `keyAlias`, `keyPassword`), which the script writes inside the container from env vars `CMMS_KEYSTORE`, `CMMS_KEYSTORE_PASSWORD`, `CMMS_KEY_ALIAS`, `CMMS_KEY_PASSWORD`; the key file is mounted read-only.

- [ ] **Step 1: Write the failing test** `mobile/test-config.mjs` (run with `node`): loads the resolved config (`npx cap config --json`) and asserts:
  - `appId === 'com.fourcsystems.cmms'` and `appName === '4C CMMS'`;
  - `server.url === 'https://dxvjhwlc6echh.cloudfront.net'`;
  - `server.errorPath === 'offline.html'`;
  - `android.appendUserAgent === '4CCMMS-Android/' + VERSION`.
- [ ] **Step 2:** Run it in `node:22-alpine` — expected: FAIL (no config).
- [ ] **Step 3:** Create the project files. The config reads `../VERSION`. Run `npx cap add android` and `npx @capacitor/assets generate --android` inside the build image. In `build.gradle`, read `../../VERSION` to set `versionName` and `versionCode` (formula in Global Constraints), and add the signing config. The config test passes.
- [ ] **Step 4:** `docker build -t cmms-android-build mobile`, then `mobile/build-apk.sh debug`. Expected: `mobile/out/4C-CMMS-0.0.0-debug.apk`. Badging shows `package: name='com.fourcsystems.cmms' versionCode='1' versionName='0.0.0'` and `application-label:'4C CMMS'`.
- [ ] **Step 5: Commit** `git commit -m "Android app: Capacitor shell for the cloud app, Docker APK build"`.

### Task 4: Native behaviour in the app

**Files:**
- Modify: `mobile/android/app/src/main/java/com/fourcsystems/cmms/MainActivity.java`, `mobile/android/app/src/main/AndroidManifest.xml` (camera permission for the photo picker), `mobile/capacitor.config.ts` (edge-to-edge margins), `mobile/test-config.mjs`
- Create: `mobile/android/app/src/main/java/com/fourcsystems/cmms/AppBridge.java` (JavaScript interface), `mobile/android/app/src/main/assets/cmms-shell.js` (page script)

**Interfaces:**
- `AppBridge` exposed to the page as `window.CmmsAndroid`:
  - `saveFile(String fileName, String mimeType, String base64)` — writes to Downloads via MediaStore (Android 10+) or the app's files dir, then shows a toast with the file name.
  - `print(String jobName)` — `PrintManager.print(jobName, webView.createPrintDocumentAdapter(jobName), null)` on the UI thread.
- `cmms-shell.js`, injected on every page load through Capacitor's `WebViewListener.onPageLoaded`:
  - replaces `window.print` with `CmmsAndroid.print(document.title)`;
  - wraps `HTMLAnchorElement.prototype.click` so a `blob:` link with `download` is read with `fetch` → `FileReader` → `CmmsAndroid.saveFile`;
  - is idempotent (guard flag).

- [ ] **Step 1: Extend the config test:** `android.adjustMarginsForEdgeToEdge === 'force'`, plus `server.allowNavigation` not containing other hosts (they must open in the phone's browser).
- [ ] **Step 2:** Run it — expected: FAIL.
- [ ] **Step 3:** Implement in `MainActivity`:
  - a `DownloadListener` for `http(s)` URLs: `DownloadManager.Request` with a `Cookie` header from `CookieManager`, the user agent, notification on completion, destination Downloads, file name from `URLUtil.guessFileName`;
  - `CookieManager.getInstance().flush()` in `onPause`;
  - the back button: web history back if possible, else `finish()`;
  - register `AppBridge` and the page-script injection.
  `offline.html`: app-styled message "Can't reach 4C CMMS. Check your connection." with a **Try again** button that loads the cloud address.
- [ ] **Step 4:** Config test passes. `mobile/build-apk.sh debug` builds. Badging lists `android.permission.CAMERA` and `INTERNET`.
- [ ] **Step 5: Commit** `git commit -m "Android app: downloads, file exports, printing, back button, offline page"`.

### Task 5: Signing key and release 0.0.0

**Files:**
- Create (outside git):
  - `$DRIVE/_private/android-signing/4c-cmms-release.jks`
  - `…/signing.env` (the four `CMMS_*` variables; passwords random 24 chars)
  - `…/README.md` (what it is, never share, how `release.sh` uses it, losing it means users uninstall to update)
- Create (in git): `.gitignore` entries for `*.jks` and `keystore.properties` if missing

- [ ] **Step 1:** Generate the key with `keytool -genkeypair -alias cmms -keyalg RSA -keysize 4096 -validity 10000 -dname "CN=4C Systems, O=4C Systems LLC"` in the build image. Write it and `signing.env` to the private folder. `release.sh` sources `signing.env` from there; `RELEASE_SIGNING_DIR` overrides the location.
- [ ] **Step 2:** `scripts/release.sh 0.0.0 --no-bump` — expected: tag `v0.0.0`, and `$DRIVE/Releases/v0.0.0/` holds `4C-CMMS-0.0.0.apk` and `RELEASE-NOTES.md` (the 0.0.0 changelog section + install steps).
- [ ] **Step 3: Verify** with `apksigner verify --print-certs` on the Drive copy. Expected: verified, signer `CN=4C Systems`, v2/v3 schemes true, badging `versionName='0.0.0'`.
- [ ] **Step 4:** Merge `versioning-android` into `main` (fast-forward), fast-forward GitHub Desktop's copy, and ask the user to push `main` and the tag. Check that the cloud deploy reports `"release":"0.0.0"`.
