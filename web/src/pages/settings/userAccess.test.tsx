import { describe, expect, it } from 'vitest';
import '@/i18n';
import { renderHtml } from '@/test/render';
import { AccessAfterSave, removesLastDeletedPlace } from './userAccess';

// What the server marks as deleted; "hidden" is a place that still exists but the editor can't see.
const deleted = ['gone', 'gone1', 'gone2'];

describe('UserForm removing the last deleted place asks for confirmation', () => {
  it('asks when the only places left on the person are ones the server says no longer exist', () => {
    expect(removesLastDeletedPlace(['gone'], [], deleted)).toBe(true);
    expect(removesLastDeletedPlace(['gone1', 'gone2'], [], deleted)).toBe(true);
  });
  it('does not ask while another place stays, or for a place that merely is not visible to the editor', () => {
    expect(removesLastDeletedPlace(['gone1', 'gone2'], ['gone1'], deleted)).toBe(false);
    expect(removesLastDeletedPlace(['gone', 'b1'], [], deleted)).toBe(false);
    expect(removesLastDeletedPlace(['hidden-from-editor'], [], deleted)).toBe(false);
    expect(removesLastDeletedPlace([], [], deleted)).toBe(false);
  });
  it('shows the access they will have afterwards: the stored sites, or everything', () => {
    const sites = renderHtml(<AccessAfterSave sites={['Cairo Plant', 'Alex Plant']} />);
    expect(sites).toContain('Cairo Plant');
    expect(sites).toContain('Alex Plant');
    expect(sites).toContain('has been deleted');
    const all = renderHtml(<AccessAfterSave sites={[]} />);
    expect(all).toContain('every site');
  });
});
