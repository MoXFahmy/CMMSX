import { describe, expect, it } from 'vitest';
import { renderHtml } from '@/test/render';
import { CodeSelect } from './CodeSelect';

describe('code picker', () => {
  it('shows the current code before the list loads (and when it has been made inactive)', () => {
    const html = renderHtml(<CodeSelect path="/symptoms" label="Symptom" value="s1" onChange={() => {}} current={{ id: 's1', code: 'LEAK', name: 'Leaking' }} />);
    expect(html).toMatch(/value="LEAK · Leaking"/);
  });
});
