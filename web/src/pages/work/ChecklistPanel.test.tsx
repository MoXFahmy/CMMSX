import { describe, expect, it } from 'vitest';
import type { WorkOrderDetail, WorkOrderStep } from '@/api/types';
import { pressed, renderHtml } from '@/test/render';
import { ChecklistPanel } from './ChecklistPanel';

const step = (s: Partial<WorkOrderStep>): WorkOrderStep => ({ id: 's1', sequence: 1, description: 'Oil pressure', isDone: false, type: 'Check', photoRequired: false,
  beforeStart: false, followUpKind: 'Default', photos: [], ...s } as WorkOrderStep);
const render = (x: WorkOrderStep) => renderHtml(
  <ChecklistPanel w={{ id: 'w1', tasks: [x], assets: [], points: [] } as unknown as WorkOrderDetail} canExec onChange={async () => {}} />);

describe('work order checklist', () => {
  it('keeps the free-text reading on every type of step', () => {
    for (const type of ['Check', 'Number', 'Meter', 'Text'] as const) {
      const html = render(step({ type, reading: '3.2 bar', meterId: 'm1' }));
      expect(html, type).toMatch(/<input[^>]*placeholder="Reading"[^>]*value="3.2 bar"|<input[^>]*value="3.2 bar"[^>]*placeholder="Reading"/);
    }
  });

  it('shows the chosen answer as a button that can be clicked again', () => {
    expect(pressed(render(step({ answer: 'NotOk', isDone: true })))).toBe("Not OK");
    expect(pressed(render(step({})))).toBeNull();
  });
});
