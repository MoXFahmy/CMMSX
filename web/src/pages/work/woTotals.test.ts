import { describe, expect, it } from 'vitest';
import { costLines } from './woTotals';

describe('work order cost lines', () => {
  it('shows contractor cost so the lines add up to the total', () => {
    const lines = costLines({ laborCost: 100, partsCost: 0, otherCost: 0, contractorCost: 380, totalCost: 480 });
    expect(lines.map((l) => l.key)).toEqual(['wo.laborCost', 'wo.partsCost', 'wo.otherCost', 'contractors.cost']);
    expect(lines.reduce((a, l) => a + l.value, 0)).toBe(480);
  });
  it('leaves contractor out when there is none', () => {
    expect(costLines({ laborCost: 50, partsCost: 10, otherCost: 0, contractorCost: 0, totalCost: 60 }).map((l) => l.key)).toEqual(['wo.laborCost', 'wo.partsCost', 'wo.otherCost']);
  });
});
