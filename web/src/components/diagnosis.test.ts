import { describe, expect, it } from 'vitest';
import { confidenceColor, nextVote, toPlan } from './diagnosis';

describe('diagnosis helpers', () => {
  it('pressing the same thumb again takes the vote back', () => {
    expect(nextVote(null, true)).toBe(true);
    expect(nextVote(true, true)).toBeNull();
    expect(nextVote(true, false)).toBe(false);
    expect(nextVote(false, false)).toBeNull();
  });
  it('colours confidence', () => {
    expect(confidenceColor('high')).toBe('teal');
    expect(confidenceColor('medium')).toBe('yellow');
    expect(confidenceColor('low')).toBe('gray');
    expect(confidenceColor(null)).toBe('gray');
  });
  it('plans only parts the work order does not have yet', () => {
    const part = (partId: string) => ({ partId, partNumber: partId.toUpperCase(), name: partId, quantity: 1 });
    expect(toPlan({ parts: [part('a'), part('b')] }, ['a']).map((p) => p.partId)).toEqual(['b']);
    expect(toPlan({ parts: [] }, [])).toEqual([]);
  });
});
