import { describe, expect, it } from 'vitest';
import { daysLeft, deadlineLabel, statusColor } from './contracts';
import type { ContractStatus } from '@/api/types';

describe('contracts', () => {
  it('colours statuses', () => expect((['Upcoming', 'Active', 'Expired', 'Terminated'] as ContractStatus[]).map(statusColor)).toEqual(['blue', 'green', 'gray', 'red']));
  it('labels the deadline by renewal', () => {
    expect(deadlineLabel({ autoRenew: true })).toBe('contracts.noticeDeadline');
    expect(deadlineLabel({ autoRenew: false })).toBe('contracts.endsOn');
  });
  it('counts days left to the deadline', () => {
    expect(daysLeft('2027-12-01', '2027-11-01')).toBe(30);
    expect(daysLeft('2027-11-01', '2027-11-02')).toBe(-1);
  });
});
