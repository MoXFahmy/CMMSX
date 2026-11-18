import { useMemo, useState } from 'react';
import { Select } from '@mantine/core';
import { useDebouncedValue } from '@mantine/hooks';
import { useQuery } from '@tanstack/react-query';
import { get } from '@/api/client';

export type AssetOption = { id: string; assetNumber: string; name: string };
type Option = AssetOption;

export function AssetPicker({ value, onChange, onPick, label, description, excludeId, siteId, initial, error }: {
  value: string | null; onChange: (v: string | null) => void; label: string; description?: string; excludeId?: string; initial?: Option | null; error?: React.ReactNode;
  /** Also receives the chosen asset's number and name (null when cleared). */
  onPick?: (o: Option | null) => void;
  /** Only offer assets of this site. */
  siteId?: string | null;
}) {
  const [search, setSearch] = useState('');
  const [debounced] = useDebouncedValue(search, 250);
  const { data } = useQuery({ queryKey: ['asset-lookup', debounced, excludeId, siteId ?? null], queryFn: () => get<Option[]>('/assets/lookup', { search: debounced, excludeId, siteId: siteId ?? undefined }) });
  const options = useMemo(() => {
    const list = [...(data ?? [])];
    if (initial && !list.some((o) => o.id === initial.id)) list.unshift(initial);
    return list.map((o) => ({ value: o.id, label: `${o.assetNumber} · ${o.name}` }));
  }, [data, initial]);
  return (
    <Select label={label} description={description} data={options} value={value} searchable clearable
      onChange={(id) => { onChange(id); onPick?.(id ? [...(data ?? []), ...(initial ? [initial] : [])].find((o) => o.id === id) ?? null : null); }}
      searchValue={search} onSearchChange={setSearch} filter={({ options: o }) => o} error={error} nothingFoundMessage="—" />
  );
}
