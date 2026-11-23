import { Select } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { get } from '@/api/client';
import type { ContractOption } from '@/api/types';

/** The contract the work is done under (0.17.0): active and upcoming contracts usable at the site, those covering the asset first. */
export function ContractSelect({ siteId, assetId, value, onChange, current, error }: {
  siteId: string | null; assetId?: string | null; value: string | null; onChange: (v: string | null) => void;
  /** The contract already named (it may have ended since): still shown. */
  current?: { id: string; number: string; title: string } | null; error?: React.ReactNode;
}) {
  const { t } = useTranslation();
  const { data } = useQuery({ queryKey: ['contract-options', siteId, assetId ?? null], queryFn: () => get<ContractOption[]>('/contracts/options', { siteId: siteId ?? undefined, assetId: assetId ?? undefined }),
    enabled: !!siteId });
  const options = (data ?? []).map((c) => ({ value: c.id, label: `${c.number} · ${c.title} · ${c.companyName}${c.covers ? ` ✓` : ''}` }));
  if (current && !options.some((o) => o.value === current.id)) options.push({ value: current.id, label: `${current.number} · ${current.title}` });
  return <Select label={t('contracts.contract')} description={t('contracts.pickHint')} clearable searchable data={options} value={value} onChange={onChange} error={error} />;
}
