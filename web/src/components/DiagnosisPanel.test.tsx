import { describe, expect, it } from 'vitest';
import { renderHtml } from '@/test/render';
import { DiagnosisView } from './DiagnosisPanel';
import type { Diagnosis } from './diagnosis';

const d = (over: Partial<Diagnosis> = {}): Diagnosis => ({
  id: 'd1', createdAt: '2026-11-23T10:00:00Z', createdByName: 'Admin', workOrderId: 'w1', workRequestId: null,
  context: 'Pump A, Grundfos CR5', observed: 'Leak at the seal', probableCause: 'Worn mechanical seal', confidence: 'high',
  actions: [{ title: 'Replace the seal', why: 'Fixed it twice', parts: [{ partId: 'p1', partNumber: 'SEAL-1', name: 'Seal', unitOfMeasure: 'EA', quantity: 1 }], hours: 2,
    sources: [{ id: 'src1', number: 'WO-00042', title: 'Seal leak' }] }],
  historyTooThin: false, message: null, sentCount: 12, up: 2, down: 0, myVote: null, ...over,
});

describe('diagnosis view', () => {
  it('shows the cause, its confidence, the sources and the votes', () => {
    const html = renderHtml(<DiagnosisView d={d()} canPlan={false} plannedPartIds={[]} />);
    expect(html).toContain('Worn mechanical seal');
    expect(html).toContain('High');
    expect(html).toContain('href="/work-orders/src1"');
    expect(html).toContain('WO-00042');
    expect(html).toContain('SEAL-1');
    expect(html).toMatch(/aria-label="Helpful"[\s\S]*?2/);
  });
  it('shows the hidden message with Run again and nothing of the diagnosis', () => {
    const html = renderHtml(<DiagnosisView d={d({ hidden: true, context: '', observed: '', probableCause: null, confidence: null, actions: [], up: 0, down: 0 })}
      canPlan={false} plannedPartIds={[]} onRun={() => {}} />);
    expect(html).toContain("This diagnosis used work you can&#x27;t see");
    expect(html).toContain('Run again');
    expect(html).not.toContain('Worn mechanical seal');
    expect(html).not.toContain('aria-label="Helpful"');
    expect(renderHtml(<DiagnosisView d={d({ hidden: true })} canPlan={false} plannedPartIds={[]} />)).not.toContain('Run again');
  });
  it('says the history is too thin instead of listing actions', () => {
    const html = renderHtml(<DiagnosisView d={d({ id: null, historyTooThin: true, message: 'Only 2 completed work orders', actions: [], probableCause: null, confidence: null })}
      canPlan={false} plannedPartIds={[]} />);
    expect(html).toContain('Only 2 completed work orders');
    expect(html).not.toContain('Replace the seal');
    expect(html).not.toContain('aria-label="Helpful"');
  });
});
