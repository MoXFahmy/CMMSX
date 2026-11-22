import { describe, expect, it } from 'vitest';
import { pressed, renderHtml } from '@/test/render';
import { Checklist } from './Checklist';
import type { TechJob, TechStep } from './types';

const step = (s: Partial<TechStep>): TechStep => ({ id: 's1', sequence: 1, description: 'Oil pressure', isDone: false, type: 'Check', ...s });
const render = (x: TechStep) => renderHtml(
  <Checklist job={{ id: 'w1', tasks: [x], roundAssets: [], files: [], asset: { id: 'a1' } } as unknown as TechJob} meters={[]} stepPhotos={{}}
    onChange={() => {}} onPhoto={() => {}} />);

describe('phone checklist', () => {
  it('keeps the free-text reading on every type of step', () => {
    for (const type of ['Check', 'Number', 'Meter', 'Text'] as const) {
      const html = render(step({ type, reading: '3.2 bar', meterId: 'm1' }));
      expect(html, type).toMatch(/<input[^>]*placeholder="Reading"[^>]*value="3.2 bar"|<input[^>]*value="3.2 bar"[^>]*placeholder="Reading"/);
    }
  });

  it('shows the chosen answer as a button that can be tapped again', () => {
    expect(pressed(render(step({ answer: 'Ok', isDone: true })))).toBe("OK");
  });
});
