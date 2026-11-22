import type { TFunction } from 'i18next';
import type { ReportDef } from '@/api/types';
import { fmtDate } from '@/components/utils';

/** The filters chosen for a report, one line each, for its printed copy (0.14.0); only what the report asks for. */
export function printFilters(def: ReportDef, q: Record<string, string | number | undefined>, names: { site?: string; asset?: string }, t: TFunction): string[] {
  const has = (type: string) => def.params.some((p) => p.type === type);
  const lines: string[] = [];
  if (has('dateRange') && q.from && q.to) lines.push(t('rep.printPeriod', { from: fmtDate(String(q.from)), to: fmtDate(String(q.to)) }));
  if (has('site')) lines.push(`${t('rep.printSite')}: ${q.siteId ? names.site ?? '…' : t('common.all')}`);
  const group = def.params.find((p) => p.name === 'groupBy');
  const chosen = group?.options?.find((o) => o.value === (q.groupBy ?? group.default));
  if (chosen) lines.push(`${t('rep.groupBy')}: ${chosen.label}`);
  if (has('year') && q.year) lines.push(`${t('rep.year')}: ${q.year}`);
  if (has('asset') && q.assetId) lines.push(`${t('wo.asset')}: ${names.asset ?? '…'}${has('bool') && q.components === 'true' ? ` · ${t('rep.withComponents')}` : ''}`);
  if (has('number') && q.days) lines.push(`${t('rep.idleDays')}: ${q.days}`);
  return lines;
}
