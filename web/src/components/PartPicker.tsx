import { useMemo, useState } from 'react';
import { Select } from '@mantine/core';
import { useDebouncedValue } from '@mantine/hooks';
import { useQuery } from '@tanstack/react-query';
import { get } from '@/api/client';
import type { PartLookup } from '@/api/types';
import { fmtQty } from './utils';

/** Searchable part selector (number, description or barcode). Reports the chosen part so callers can default unit and price. */
export function PartPicker({ value, onChange, label, placeholder, storeId, initial, error, required, withStock = true, w }: {
  value: string | null; onChange: (id: string | null, part: PartLookup | null) => void; label?: string; placeholder?: string; storeId?: string | null;
  initial?: { id: string; partNumber: string; name: string } | null; error?: React.ReactNode; required?: boolean; withStock?: boolean; w?: number | string;
}) {
  const [search, setSearch] = useState('');
  const [debounced] = useDebouncedValue(search, 250);
  const { data } = useQuery({ queryKey: ['part-lookup', debounced, storeId ?? null], queryFn: () => get<PartLookup[]>('/parts/lookup', { search: debounced, storeId: storeId ?? undefined }) });
  const options = useMemo(() => {
    const list = (data ?? []).map((p) => ({ value: p.id, label: `${p.partNumber} · ${p.name}${withStock ? ` (${fmtQty(p.onHand)} ${p.unitOfMeasure})` : ''}` }));
    if (initial && !list.some((o) => o.value === initial.id)) list.unshift({ value: initial.id, label: `${initial.partNumber} · ${initial.name}` });
    return list;
  }, [data, initial, withStock]);
  return (
    <Select label={label} placeholder={placeholder} data={options} value={value} required={required} w={w}
      onChange={(v) => onChange(v, (data ?? []).find((p) => p.id === v) ?? null)} searchable clearable
      searchValue={search} onSearchChange={setSearch} filter={({ options: o }) => o} error={error} nothingFoundMessage="—" />
  );
}

export function useStores() {
  return useQuery({ queryKey: ['stores'], queryFn: () => get<import('@/api/types').StoreItem[]>('/stores'), staleTime: 60_000 });
}
