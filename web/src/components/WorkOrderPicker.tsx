import { useMemo, useState } from 'react';
import { Select } from '@mantine/core';
import { useDebouncedValue } from '@mantine/hooks';
import { useQuery } from '@tanstack/react-query';
import { get } from '@/api/client';
import type { Paged, WorkOrderListItem } from '@/api/types';

/** Picks an open work order (open, in progress or on hold). */
export function WorkOrderPicker({ value, onChange, label, initial, error, required, placeholder }: {
  value: string | null; onChange: (v: string | null) => void; label?: string; initial?: { id: string; number: string; title?: string } | null;
  error?: React.ReactNode; required?: boolean; placeholder?: string;
}) {
  const [search, setSearch] = useState('');
  const [debounced] = useDebouncedValue(search, 250);
  const { data } = useQuery({
    queryKey: ['wo-lookup', debounced],
    queryFn: () => get<Paged<WorkOrderListItem>>('/work-orders', { search: debounced, status: ['Open', 'InProgress', 'OnHold'], pageSize: 20, sort: 'createdAt', dir: 'desc' }),
  });
  const options = useMemo(() => {
    const list = (data?.items ?? []).map((w) => ({ value: w.id, label: `${w.number} · ${w.title}${w.assetNumber ? ` (${w.assetNumber})` : ''}` }));
    if (initial && !list.some((o) => o.value === initial.id)) list.unshift({ value: initial.id, label: `${initial.number}${initial.title ? ` · ${initial.title}` : ''}` });
    return list;
  }, [data, initial]);
  return <Select label={label} placeholder={placeholder} data={options} value={value} onChange={onChange} searchable clearable required={required}
    searchValue={search} onSearchChange={setSearch} filter={({ options: o }) => o} error={error} nothingFoundMessage="—" />;
}
