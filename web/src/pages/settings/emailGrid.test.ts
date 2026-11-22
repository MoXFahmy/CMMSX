import { describe, expect, it } from 'vitest';
import { kindsOn, withKind, type GridRow } from './emailGrid';

const completed: GridRow = { key: 'workorder.completed', applicable: ['requestRecipients', 'issuer'], defaults: ['issuer'] };
const assigned: GridRow = { key: 'workorder.assigned', applicable: ['technicians', 'contractors'], defaults: ['technicians', 'contractors'] };

describe('email grid', () => {
  it('uses the defaults until the organization chooses', () => {
    expect(kindsOn(undefined, completed)).toEqual(['issuer']);
    expect(kindsOn({ 'workorder.completed': [] }, completed)).toEqual([]);
    expect(kindsOn({ 'workorder.completed': ['issuer', 'technicians'] }, completed)).toEqual(['issuer']);
  });

  it('switches a cell starting from the defaults', () => {
    expect(withKind(undefined, completed, 'requestRecipients', true)).toEqual({ 'workorder.completed': ['requestRecipients', 'issuer'] });
    expect(withKind(undefined, completed, 'issuer', false)).toEqual({ 'workorder.completed': [] });
  });

  it('ignores kinds that do not apply and keeps other events', () => {
    const saved = { 'workorder.assigned': ['technicians'] };
    expect(withKind(saved, completed, 'technicians', true)).toEqual(saved);
    expect(withKind(saved, completed, 'issuer', false)).toEqual({ 'workorder.assigned': ['technicians'], 'workorder.completed': [] });
    expect(withKind(saved, assigned, 'contractors', true)).toEqual({ 'workorder.assigned': ['technicians', 'contractors'] });
  });
});
