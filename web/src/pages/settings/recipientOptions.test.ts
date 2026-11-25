import { describe, expect, it } from 'vitest';
import { requestRecipientOptions } from './recipientOptions';

const viewers = [{ id: 'v1', fullName: 'Viewer' }, { id: 'v2', fullName: 'Wing viewer' }];
const everyone = [...viewers, { id: 'n1', fullName: 'Cannot view' }, { id: 'n2', fullName: 'Wing blind (one branch)' }];
const mark = (name: string) => `${name} (can't view requests)`;

describe('request recipient pickers (site settings and request types)', () => {
  it('offer only people who can view requests, branch-limited ones included only when they can', () => {
    expect(requestRecipientOptions(viewers, everyone, [], mark).map((o) => o.value)).toEqual(['v1', 'v2']);
  });
  it('keep someone already chosen who cannot view requests, marked, so they can be taken off', () => {
    const o = requestRecipientOptions(viewers, everyone, ['n2'], mark);
    expect(o.map((x) => x.value)).toEqual(['v1', 'v2', 'n2']);
    expect(o[2].label).toBe("Wing blind (one branch) (can't view requests)");
  });
  it('shows nothing extra while the lists are loading', () => {
    expect(requestRecipientOptions(undefined, undefined, ['n1'], mark)).toEqual([]);
  });
});
