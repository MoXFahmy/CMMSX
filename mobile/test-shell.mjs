// Tests for android/app/src/main/assets/cmms-shell.js, the script the app injects into every page.
// Run in mobile/: node test-shell.mjs
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const source = readFileSync(new URL('./android/app/src/main/assets/cmms-shell.js', import.meta.url), 'utf8');

function page({ fetch = async (url) => ({ blob: async () => new Blob(['Part,Qty\n1202678,102\n'], { type: 'text/csv' }) }) } = {}) {
  const calls = [];
  // download: the attribute's value, or null when the link has no download attribute.
  class HTMLAnchorElement {
    constructor(href, download) { this.href = href; this.attr = download; this.download = download ?? ''; }
    hasAttribute(name) { return name === 'download' && this.attr !== null && this.attr !== undefined; }
    click() { calls.push(['nativeClick', this.href]); }
  }
  const window = { CmmsAndroid: {
    print: (t) => calls.push(['print', t]),
    saveFile: (n, m, b) => calls.push(['saveFile', n, m, b]),
    couldNotSave: (n) => calls.push(['couldNotSave', n]),
  } };
  const context = vm.createContext({ window, HTMLAnchorElement, document: { title: 'PO-00042' }, btoa, Blob, fetch });
  window.print = () => calls.push(['browserPrint']);
  context.window = window;
  vm.runInContext(source, context);
  return { context, window, calls, HTMLAnchorElement };
}
const tick = () => new Promise((r) => setTimeout(r, 10));

const tests = {
  'print opens the Android print dialog with the page title': async () => {
    const p = page();
    p.window.print();
    assert.deepEqual(p.calls, [['print', 'PO-00042']]);
  },
  'a blob download is saved through the app': async () => {
    const p = page();
    new p.HTMLAnchorElement('blob:https://x/1', 'stock-take.csv').click();
    await tick();
    assert.equal(p.calls.length, 1);
    const [kind, name, mime, b64] = p.calls[0];
    assert.equal(kind, 'saveFile'); assert.equal(name, 'stock-take.csv'); assert.equal(mime, 'text/csv');
    assert.equal(Buffer.from(b64, 'base64').toString('utf8'), 'Part,Qty\n1202678,102\n');
  },
  'ordinary links keep their normal click': async () => {
    const p = page();
    new p.HTMLAnchorElement('https://cmx.cworks.com.eg/work-orders', null).click();
    new p.HTMLAnchorElement('blob:https://x/9', null).click();
    assert.deepEqual(p.calls, [['nativeClick', 'https://cmx.cworks.com.eg/work-orders'], ['nativeClick', 'blob:https://x/9']]);
  },
  'a blob download with an empty name is still saved (the app names it)': async () => {
    const p = page();
    new p.HTMLAnchorElement('blob:https://x/3', '').click();
    await tick();
    assert.deepEqual(p.calls.map((c) => c.slice(0, 2)), [['saveFile', '']]);
  },
  'a save that fails says so instead of doing nothing': async () => {
    const p = page({ fetch: async () => { throw new TypeError('Failed to fetch'); } });
    new p.HTMLAnchorElement('blob:https://x/4', 'stock-take.csv').click();
    await tick();
    assert.deepEqual(p.calls, [['couldNotSave', 'stock-take.csv']]);
  },
  'injecting twice wraps only once': async () => {
    const p = page();
    vm.runInContext(source, p.context);
    new p.HTMLAnchorElement('blob:https://x/2', 'a.csv').click();
    await tick();
    assert.equal(p.calls.filter((c) => c[0] === 'saveFile').length, 1);
  },
};
let failed = 0;
for (const [name, t] of Object.entries(tests)) {
  try { await t(); console.log(`ok   ${name}`); } catch (e) { failed++; console.log(`FAIL ${name}: ${e.message.split('\n')[0]}`); }
}
if (failed) { console.log(`${failed} failing`); process.exit(1); }
console.log('All shell script checks passed.');
