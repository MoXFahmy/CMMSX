import { describe, expect, it } from 'vitest';
import en from '@/locales/en.json';
import ar from '@/locales/ar.json';
import { changeLabelKey } from './changeLabel';

/* eslint-disable @typescript-eslint/no-explicit-any */
const sources = Object.values(import.meta.glob(['./*.ts', './*.tsx', '!./*.test.ts'], { query: '?raw', import: 'default', eager: true })) as string[];
const code = sources.join('\n');
const languages: [string, any][] = [['en', en], ['ar', ar]];
/** Looks a key up the way i18next does: nested groups, but a last part may itself contain dots ("photo.add"). */
const find = (tree: any, key: string): unknown => {
  if (tree == null || typeof tree !== 'object') return undefined;
  if (key in tree) return tree[key];
  const parts = key.split('.');
  for (let i = 1; i < parts.length; i++) {
    const head = parts.slice(0, i).join('.');
    if (head in tree) { const v = find(tree[head], parts.slice(i).join('.')); if (v !== undefined) return v; }
  }
  return undefined;
};
const has = (tree: any, key: string) => typeof find(tree, key) === 'string' || typeof find(tree, `${key}_one`) === 'string';

describe('phone labels', () => {
  it('names every change the phone sends on the Sync page, in English and Arabic', () => {
    const families = ['comment', 'labor', 'photo', 'reading', 'request', 'status', 'task', 'tasks', 'part', 'stock', 'po', 'count', 'downtime', 'workorder'];
    const sent = [...new Set([...code.matchAll(new RegExp(`(?<!\\bt\\()'((?:${families.join('|')})\\.[a-zA-Z]+)'`, 'g'))].map((m) => m[1]))];
    expect(sent.length).toBeGreaterThan(12);
    const missing = languages.flatMap(([lang, tree]) => [...sent, 'file'].filter((type) => !has(tree, `tech.change.${type}`)).map((type) => `${lang}: ${type}`));
    expect(missing).toEqual([]);
  });

  it('has every label the phone screens use, in English and Arabic', () => {
    const keys = [...new Set([...code.matchAll(/\bt\('([a-zA-Z]+(?:\.[a-zA-Z0-9]+)+)'/g)].map((m) => m[1]))];
    expect(keys.length).toBeGreaterThan(50);
    const missing = languages.flatMap(([lang, tree]) => keys.filter((k) => !has(tree, k)).map((k) => `${lang}: ${k}`));
    expect(missing).toEqual([]);
  });

  it('labels videos and documents as files, not photos', () => {
    expect(changeLabelKey({ type: 'photo.add', payload: { kind: 'file', fileName: 'a.mp4' } })).toBe('tech.change.file');
    expect(changeLabelKey({ type: 'photo.add', payload: { kind: 'photo' } })).toBe('tech.change.photo.add');
    expect(changeLabelKey({ type: 'part.use', payload: {} })).toBe('tech.change.part.use');
  });
});
