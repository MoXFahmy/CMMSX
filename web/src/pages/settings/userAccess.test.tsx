import { describe, expect, it } from 'vitest';
import '@/i18n';
import { renderHtml } from '@/test/render';
import { AccessAfterSave, removesLastDeletedPlace } from './userAccess';

const known = new Set(['b1', 'b2']);

describe('UserForm removing the last deleted place asks for confirmation', () => {
  it('asks when the only places left on the person are ones that no longer exist', () => {
    expect(removesLastDeletedPlace(['gone'], [], known)).toBe(true);
    expect(removesLastDeletedPlace(['gone1', 'gone2'], [], known)).toBe(true);
  });
  it('does not ask while another place stays or the removed place still exists', () => {
    expect(removesLastDeletedPlace(['gone1', 'gone2'], ['gone1'], known)).toBe(false);
    expect(removesLastDeletedPlace(['gone', 'b1'], ['b1'], known)).toBe(false);
    expect(removesLastDeletedPlace(['b1'], [], known)).toBe(false); // clearing a real place is the person's own choice
    expect(removesLastDeletedPlace([], [], known)).toBe(false);
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
