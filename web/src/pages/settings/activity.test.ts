import { describe, expect, it } from 'vitest';
import { changeSummary, fileKind, parseChanges } from './activity';

const t = ((key: string, o?: Record<string, unknown>) => (key === 'activity.more' ? `+${o?.count} more` : key)) as never;

describe('activity log helpers (0.25.0)', () => {
  it('changeSummary lists the first three changes', () => {
    const s = changeSummary({
      Title: { from: 'Old', to: 'New' }, Priority: { from: 'Low', to: 'High' }, PasswordHash: { from: '••••', to: '••••' },
      Status: { from: 'Open', to: 'Closed' }, Notes: { to: 'x' },
    }, t);
    expect(s).toBe('Title: Old → New · Priority: Low → High · Password Hash: •••• → •••• · +2 more');
    expect(changeSummary(null, t)).toBe('');
    expect(changeSummary({ Summary: { to: 'Brand merged into Grundfos: 14 assets' } }, t)).toBe('Brand merged into Grundfos: 14 assets');
    expect(changeSummary({ Name: { from: null, to: 'Pump' } }, t)).toBe('Name: ∅ → Pump');
  });

  it('parseChanges reads the stored JSON and survives bad text', () => {
    expect(parseChanges('{"Title":{"from":"A","to":"B"}}')).toEqual({ Title: { from: 'A', to: 'B' } });
    expect(parseChanges('not json')).toBeNull();
    expect(parseChanges(null)).toBeNull();
  });

  it('fileKind groups like the server', () => {
    expect(fileKind('image/png', false)).toBe('Images');
    expect(fileKind('application/pdf', false)).toBe('Documents');
    expect(fileKind('image/png', true)).toBe('Signatures');
    expect(fileKind('video/mp4', false)).toBe('Videos');
    expect(fileKind('application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', false)).toBe('Documents');
    expect(fileKind('application/zip', false)).toBe('Other');
  });
});
