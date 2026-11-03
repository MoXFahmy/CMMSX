import { useMemo } from 'react';
import { MultiSelect } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { get } from '@/api/client';
import type { EmployeeLookup, Lookup } from '@/api/types';

export function useEmployees(siteId?: string | null) {
  return useQuery({ queryKey: ['employees', 'lookup', siteId ?? null], queryFn: () => get<EmployeeLookup[]>('/employees/lookup', { siteId: siteId ?? undefined }) });
}

export function EmployeePicker({ value, onChange, label, siteId, tradeId, error }: {
  value: string[]; onChange: (v: string[]) => void; label: string; siteId?: string | null; tradeId?: string | null; error?: React.ReactNode;
}) {
  const { data } = useEmployees(siteId);
  const { data: trades } = useQuery({ queryKey: ['trades'], queryFn: () => get<Lookup[]>('/trades') });
  const options = useMemo(() => {
    const tradeName = (ids: string[]) => ids.map((id) => trades?.find((t) => t.id === id)?.name).filter(Boolean).join(', ');
    const list = [...(data ?? [])].sort((a, b) => Number(tradeId ? !b.tradeIds.includes(tradeId) : 0) - Number(tradeId ? !a.tradeIds.includes(tradeId) : 0) || a.fullName.localeCompare(b.fullName));
    return list.map((e) => ({ value: e.id, label: `${e.fullName}${e.tradeIds.length ? ` · ${tradeName(e.tradeIds)}` : ''}${e.isContractor ? ` (${e.company ?? 'contractor'})` : ''}` }));
  }, [data, trades, tradeId]);
  return <MultiSelect label={label} data={options} value={value} onChange={onChange} searchable clearable error={error} nothingFoundMessage="—" />;
}
