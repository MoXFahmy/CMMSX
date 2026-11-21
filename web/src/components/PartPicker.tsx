import { useMemo, useState } from 'react';
import { Select } from '@mantine/core';
import { useDebouncedValue } from '@mantine/hooks';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { get } from '@/api/client';
import type { PartLookup } from '@/api/types';
import { fmtQty } from './utils';
import { groupPicked } from './downtime';

/**
 * Searchable part selector (number, description or barcode). Reports the chosen part so callers can default unit and price.
 * With an asset (planning or issuing parts for its work), the asset's own spare parts come first, with the quantity from its list.
 */
export function PartPicker({ value, onChange, label, placeholder, storeId, assetId, initial, error, required, withStock = true, w }: {
  value: string | null; onChange: (id: string | null, part: PartLookup | null) => void; label?: string; placeholder?: string; storeId?: string | null;
  assetId?: string | null; initial?: { id: string; partNumber: string; name: string } | null; error?: React.ReactNode; required?: boolean; withStock?: boolean; w?: number | string;
}) {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const [debounced] = useDebouncedValue(search, 250);
  const { data } = useQuery({ queryKey: ['part-lookup', debounced, storeId ?? null, assetId ?? null],
    queryFn: () => get<PartLookup[]>('/parts/lookup', { search: debounced, storeId: storeId ?? undefined, assetId: assetId ?? undefined }) });
  const options = useMemo(() => {
    const item = (p: PartLookup) => ({ value: p.id, label: `${p.partNumber} · ${p.name}${withStock ? ` (${fmtQty(p.onHand)} ${p.unitOfMeasure})` : ''}${p.onAsset != null ? ` · ${t('partPicker.onAssetQty', { qty: fmtQty(p.onAsset) })}` : ''}` });
    const { onAsset, others } = groupPicked(data ?? []);
    const flat = (data ?? []).map(item);
    const known = (id: string) => flat.some((o) => o.value === id);
    const extra = initial && !known(initial.id) ? [{ value: initial.id, label: `${initial.partNumber} · ${initial.name}` }] : [];
    if (onAsset.length === 0) return [...extra, ...flat];
    return [
      ...(extra.length ? [{ group: t('partPicker.chosen'), items: extra }] : []),
      { group: t('partPicker.ofThisAsset'), items: onAsset.map(item) },
      ...(others.length ? [{ group: t('partPicker.otherParts'), items: others.map(item) }] : []),
    ];
  }, [data, initial, withStock, t]);
  return (
    <Select label={label} placeholder={placeholder} data={options} value={value} required={required} w={w}
      onChange={(v) => onChange(v, (data ?? []).find((p) => p.id === v) ?? null)} searchable clearable
      searchValue={search} onSearchChange={setSearch} filter={({ options: o }) => o} error={error} nothingFoundMessage="—" />
  );
}

export function useStores() {
  return useQuery({ queryKey: ['stores'], queryFn: () => get<import('@/api/types').StoreItem[]>('/stores'), staleTime: 60_000 });
}
