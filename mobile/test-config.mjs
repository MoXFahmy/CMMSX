// Checks the Android app's Capacitor settings against docs/superpowers/specs/2026-11-19-versioning-and-android-app-design.md.
// Run in mobile/: node test-config.mjs   (needs `npm ci` first)
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import assert from 'node:assert/strict';

const version = readFileSync(new URL('../VERSION', import.meta.url), 'utf8').trim();
const printed = JSON.parse(execFileSync('npx', ['cap', 'config', '--json'], { cwd: new URL('.', import.meta.url), encoding: 'utf8' }));
const cfg = printed.app?.extConfig ?? printed;

const checks = [
  ['app id', () => assert.equal(cfg.appId, 'com.fourcsystems.cmms')],
  ['app name', () => assert.equal(cfg.appName, '4C CMMS')],
  ['opens the cloud app', () => assert.equal(cfg.server?.url, 'https://dxvjhwlc6echh.cloudfront.net')],
  ['offline page', () => assert.equal(cfg.server?.errorPath, 'offline.html')],
  ['version in the user agent', () => assert.equal(cfg.android?.appendUserAgent, `4CCMMS-Android/${version}`)],
  // Android 15+ draws apps edge to edge; 'native' keeps the cloud app's header clear of the status bar.
  ['status bar handled by Capacitor', () => assert.equal(cfg.plugins?.SystemBars?.insetsHandling, 'native')],
  // Other sites must open in the phone's browser, not inside the app.
  ['no other sites allowed in the app', () => assert.ok(!cfg.server?.allowNavigation?.length)],
];
let failed = 0;
for (const [name, check] of checks) {
  try { check(); console.log(`ok   ${name}`); } catch (e) { failed++; console.log(`FAIL ${name}: ${e.message.split('\n')[0]}`); }
}
if (failed) { console.log(`${failed} failing`); process.exit(1); }
console.log('All config checks passed.');
