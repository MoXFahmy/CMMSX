import { describe, expect, it } from 'vitest';
import { hoursOn, problem, type WorkingDay } from './workingHours';

describe('working hours', () => {
  it('counts the hours of a day, 0 on a day off', () => {
    const week: WorkingDay[] = [{ day: 'Monday', start: '08:00', end: '16:30' }];
    expect(hoursOn(week, 'Monday')).toBe(8.5);
    expect(hoursOn(week, 'Friday')).toBe(0);
    expect(hoursOn([{ day: 'Sunday', start: '00:00', end: '24:00' }], 'Sunday')).toBe(24);
  });

  it('says what is wrong with a pattern', () => {
    expect(problem([{ day: 'Monday', start: '08:00', end: '16:00' }])).toBeNull();
    expect(problem([{ day: 'Monday', start: '08:00', end: '12:00' }, { day: 'Monday', start: '13:00', end: '17:00' }])).toBe('hours.oncePerDay');
    expect(problem([{ day: 'Monday', start: '17:00', end: '08:00' }])).toBe('hours.endAfterStart');
    expect(problem([{ day: 'Monday', start: '8am', end: '16:00' }])).toBe('hours.timeFormat');
  });
});
