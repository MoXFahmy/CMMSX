import type { TFunction } from 'i18next';

export type CodeRef = { id: string; code: string; name: string };
export type PartSuggestion = { partId: string; partNumber: string; name: string; unitOfMeasure?: string | null; quantity: number };

/** POST /api/ai/report (0.15.0): suggestions from the notes, using only the organization's codes and the job's parts. Nothing is saved. */
export type AiReportResult = {
  workDone: string; causeDetails?: string | null; symptom?: CodeRef | null; cause?: CodeRef | null; remedy?: CodeRef | null;
  hours?: number | null; parts: PartSuggestion[];
};

type ReportFields = { completionNotes: string; causeNotes: string; symptomId: string | null; failureCodeId: string | null; remedyId: string | null };

/** The completion form with the answer's work done, cause details and codes put in; anything the answer leaves empty stays as it was. */
export function applyReport<T extends ReportFields>(values: T, r: AiReportResult): T {
  return {
    ...values,
    completionNotes: r.workDone?.trim() ? r.workDone : values.completionNotes,
    causeNotes: r.causeDetails?.trim() ? r.causeDetails : values.causeNotes,
    symptomId: r.symptom?.id ?? values.symptomId,
    failureCodeId: r.cause?.id ?? values.failureCodeId,
    remedyId: r.remedy?.id ?? values.remedyId,
  };
}

/** Suggested hours and parts, for the person to add themselves on the web. */
export function reportHints(r: AiReportResult, t: TFunction): string[] {
  return [
    ...(r.hours ? [t('ai.hintHours', { hours: r.hours })] : []),
    ...r.parts.map((p) => t('ai.usePart', { qty: p.quantity, part: `${p.partNumber} · ${p.name}` })),
  ];
}
