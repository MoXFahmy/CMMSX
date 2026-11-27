import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

/**
 * Guard for the reading direction (0.6.0-2): nothing in the web code may be fixed to the left or right.
 * Use `ta="end"` / `ms` / `me` / `ps` / `pe` / `borderInlineStart` so the layout follows Arabic and English.
 */
const files = Object.entries(
  import.meta.glob(['./**/*.ts', './**/*.tsx', '!./**/*.test.ts', '!./**/*.test.tsx'], { query: '?raw', import: 'default', eager: true }),
) as [string, string][];

const css = readFileSync(fileURLToPath(new URL('./styles.css', import.meta.url)), 'utf8');

/** Deliberate exceptions: `path|trimmed line` -> reason. Empty today. */
export const allowList: { key: string; reason: string }[] = [];

const rules: { name: string; re: RegExp }[] = [
  { name: 'ta="left|right"', re: /\bta=(?:"(?:left|right)"|\{[^}]*'(?:left|right)')/ },
  { name: 'ml= mr= pl= pr=', re: /\b(ml|mr|pl|pr)=/ },
  { name: 'margin/padding/border Left/Right', re: /\b(margin|padding|border)(Left|Right)\b/ },
];

/** Returns `path:line  text` for every fixed-direction line. Exported shape is tested below. */
export function findHits(entries: [string, string][], allowed: string[] = allowList.map((a) => a.key)): string[] {
  const hits: string[] = [];
  for (const [path, text] of entries) {
    text.split('\n').forEach((line, i) => {
      if (allowed.includes(`${path}|${line.trim()}`)) return;
      for (const r of rules) if (r.re.test(line)) hits.push(`${path.replace('./', 'src/')}:${i + 1}  [${r.name}]  ${line.trim()}`);
    });
  }
  return hits;
}

describe('reading direction', () => {
  it('has no fixed left or right alignment, margin, padding or border in the web code', () => {
    expect(files.length).toBeGreaterThan(100);
    expect(findHits(files)).toEqual([]);
  });

  it('maps the data table\'s right and left alignment to the reading direction', () => {
    expect(css).toMatch(/\.mantine-datatable-text-align-right\s*\{[^}]*text-align:\s*end/);
    expect(css).toMatch(/\.mantine-datatable-text-align-left\s*\{[^}]*text-align:\s*start/);
  });

  it('catches planted fixed alignment (the matcher itself works)', () => {
    const planted: [string, string][] = [['./x.tsx', ['<Text ta="right">a</Text>', '<Box ml="auto" />', "style={{ borderLeft: '1px solid red' }}", '<Text ta="end" ms="auto" />'].join('\n')]];
    const hits = findHits(planted, []);
    expect(hits).toHaveLength(3);
    expect(hits[0]).toContain('src/x.tsx:1');
    expect(findHits(planted, ['./x.tsx|<Box ml="auto" />'])).toHaveLength(2);
  });

  it('allows nothing without a reason', () => {
    expect(allowList.every((a) => a.reason.length > 10)).toBe(true);
  });
});
