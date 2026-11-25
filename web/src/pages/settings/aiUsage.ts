import type { TFunction } from 'i18next';

export type AiFeature = 'Report' | 'Diagnosis' | 'Translation';
export type AiResult = 'Pending' | 'Answered' | 'Refused' | 'Failed' | 'OverLimit';

/** Settings › AI usage (0.15.0). Actions count answered and refused calls; cost sums every call's estimate. */
export type AiUsageSummary = {
  ceiling: number; limit: number; used: number; renewsOn: string;
  months: { month: string; actions: number; cost: number }[];
  byFeature: { feature: AiFeature; actions: number; cost: number }[];
  byPerson: { userId?: string | null; name: string; actions: number; cost: number }[];
};

export type AiCallRow = {
  id: string; createdAt: string; userId?: string | null; userName: string; feature: AiFeature; workOrderId?: string | null; workOrderNumber?: string | null;
  workRequestId?: string | null; requestNumber?: string | null; model: string; inputTokens: number; outputTokens: number; cacheReadTokens: number;
  cacheWriteTokens: number; estimatedCost: number; result: AiResult; durationMs: number;
};

/** The organization's monthly limit: empty (the ceiling) or 0 to the ceiling. */
export function limitError(value: number | string | null | undefined, ceiling: number, t: TFunction): string | null {
  if (value === '' || value === null || value === undefined) return null;
  const n = Number(value);
  if (n < 0) return t('ai.limitMin');
  if (n > ceiling) return t('ai.limitMax', { max: ceiling });
  return null;
}

/** Red when the month is used up, yellow from 80 %, teal before. */
export function usageColor(used: number, limit: number): 'teal' | 'yellow' | 'red' {
  if (limit <= 0 || used >= limit) return 'red';
  return used / limit >= 0.8 ? 'yellow' : 'teal';
}

/** US dollars: four places under $1 (AI calls cost cents), two above. */
export const fmtUsd = (n: number) => '$' + (n < 1 ? n.toFixed(4) : n.toFixed(2));
