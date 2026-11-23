import type { TFunction } from 'i18next';
import { describe, expect, it } from 'vitest';
import { fmtUsd, limitError, usageColor } from './aiUsage';

const t = ((k: string, o?: Record<string, unknown>) => (o ? `${k}:${JSON.stringify(o)}` : k)) as unknown as TFunction;

describe('AI usage helpers', () => {
  it('accepts an empty limit or one from 0 to the ceiling', () => {
    expect(limitError('', 300, t)).toBeNull();
    expect(limitError(0, 300, t)).toBeNull();
    expect(limitError(300, 300, t)).toBeNull();
    expect(limitError(301, 300, t)).toBe('ai.limitMax:{"max":300}');
    expect(limitError(-1, 300, t)).toBe('ai.limitMin');
  });
  it('colours use teal, then yellow from 80 %, red when used up', () => {
    expect(usageColor(79, 100)).toBe('teal');
    expect(usageColor(80, 100)).toBe('yellow');
    expect(usageColor(100, 100)).toBe('red');
    expect(usageColor(0, 0)).toBe('red');
  });
  it('shows small costs to four places and larger ones to two', () => {
    expect(fmtUsd(0.01234)).toBe('$0.0123');
    expect(fmtUsd(0)).toBe('$0.0000');
    expect(fmtUsd(12.5)).toBe('$12.50');
  });
});
