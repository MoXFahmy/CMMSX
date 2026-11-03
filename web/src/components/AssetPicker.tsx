import { useMemo, useState } from 'react';
import { Select } from '@mantine/core';
import { useDebouncedValue } from '@mantine/hooks';
import { useQuery } from '@tanstack/react-query';
import { get } from '@/api/client';

type Option = { id: string; assetNumber: string; name: string };

export function AssetPicker({ value, onChange, label, description, excludeId, initial, error }: {
  value: string | null; onChange: (v: string | null) => void; label: string; description?: string; excludeId?: string; initial?: Option | null; error?: React.ReactNode;
}) {
  const [search, setSearch] = useState('');
  const [debounced] = useDebouncedValue(search, 250);
  const { data } = useQuery({ queryKey: ['asset-lookup', debounced, excludeId], queryFn: () => get<Option[]>('/assets/lookup', { search: debounced, excludeId }) });
  const options = useMemo(() => {
    const list = [...(data ?? [])];
    if (initial && !list.some((o) => o.id === initial.id)) list.unshift(initial);
    return list.map((o) => ({ value: o.id, label: `${o.assetNumber} · ${o.name}` }));
  }, [data, initial]);
  return (
    <Select label={label} description={description} data={options} value={value} onChange={onChange} searchable clearable
      searchValue={search} onSearchChange={setSearch} filter={({ options: o }) => o} error={error} nothingFoundMessage="—" />
  );
}
