import type { PartSuggestion } from './aiReport';

export type SourceRef = { id: string; number: string; title: string };
export type DiagnosisAction = { title: string; why: string; parts: PartSuggestion[]; hours?: number | null; sources: SourceRef[] };

/** A diagnosis (0.15.0). `id` is empty for a thin-history answer the server gave itself (not kept, no votes). */
export type Diagnosis = {
  id: string | null; createdAt: string; createdByName: string; workOrderId?: string | null; workRequestId?: string | null;
  context: string; observed: string; probableCause?: string | null; confidence?: 'low' | 'medium' | 'high' | null;
  actions: DiagnosisAction[]; historyTooThin: boolean; message?: string | null; sentCount: number; up: number; down: number; myVote?: boolean | null;
};

/** Pressing the thumb already chosen takes the vote back. */
export const nextVote = (current: boolean | null | undefined, clicked: boolean): boolean | null => (current === clicked ? null : clicked);

export const confidenceColor = (c?: string | null) => (c === 'high' ? 'teal' : c === 'medium' ? 'yellow' : 'gray');

/** An action's parts the work order hasn't planned yet. */
export const toPlan = (action: { parts: PartSuggestion[] }, plannedPartIds: string[]) => action.parts.filter((p) => !plannedPartIds.includes(p.partId));
