import type { TFunction } from 'i18next';
import { describe, expect, it } from 'vitest';
import { applyReport, reportHints, type AiReportResult } from './aiReport';

const t = ((k: string, o?: Record<string, unknown>) => (o ? `${k}:${JSON.stringify(o)}` : k)) as unknown as TFunction;

const answer = (over: Partial<AiReportResult> = {}): AiReportResult => ({
  workDone: 'Clean', causeDetails: null, symptom: null, cause: { id: 'c1', code: 'C', name: 'n' }, remedy: null, hours: null, parts: [], ...over,
});

describe('AI report in the completion dialog', () => {
  it('replaces only what the answer has', () => {
    const v = applyReport({ completionNotes: 'raw', causeNotes: 'kept', symptomId: 's0', failureCodeId: null, remedyId: 'r0', other: 1 }, answer());
    expect(v).toEqual({ completionNotes: 'Clean', causeNotes: 'kept', symptomId: 's0', failureCodeId: 'c1', remedyId: 'r0', other: 1 });
  });
  it('fills cause details and every code when given', () => {
    const v = applyReport({ completionNotes: '', causeNotes: '', symptomId: null, failureCodeId: null, remedyId: null },
      answer({ causeDetails: 'Worn', symptom: { id: 's1', code: 'S', name: 'x' }, remedy: { id: 'r1', code: 'R', name: 'y' } }));
    expect(v).toMatchObject({ causeNotes: 'Worn', symptomId: 's1', failureCodeId: 'c1', remedyId: 'r1' });
  });
  it('keeps typed work done when the answer leaves it empty', () => {
    expect(applyReport({ completionNotes: 'typed', causeNotes: '', symptomId: null, failureCodeId: null, remedyId: null }, answer({ workDone: '' })).completionNotes).toBe('typed');
  });
  it('lists hours and parts as hints to add by hand', () => {
    const hints = reportHints(answer({ hours: 1.5, parts: [{ partId: 'p1', partNumber: 'P1', name: 'Seal', unitOfMeasure: 'EA', quantity: 2 }] }), t);
    expect(hints).toEqual(['ai.hintHours:{"hours":1.5}', 'ai.usePart:{"qty":2,"part":"P1 · Seal"}']);
    expect(reportHints(answer(), t)).toEqual([]);
  });
});
