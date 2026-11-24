import { useMemo, useState } from 'react';
import { Button, Select, SimpleGrid, type SelectProps } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { get, post } from '@/api/client';
import type { AssetGroupItem, BrandItem, UnitItem } from '@/api/types';
import { notifyError } from './utils';

export const useBrands = () => useQuery({ queryKey: ['brands'], queryFn: () => get<BrandItem[]>('/brands'), staleTime: 60_000 });
export const useUnits = () => useQuery({ queryKey: ['units'], queryFn: () => get<UnitItem[]>('/units'), staleTime: 60_000 });
export const useAssetGroups = (enabled = true) => useQuery({ queryKey: ['asset-groups'], queryFn: () => get<AssetGroupItem[]>('/asset-groups'), staleTime: 60_000, enabled });

/** Unit choices (0.21.0): active units by symbol, plus the record's own even when it isn't listed or is inactive. */
export function unitData(units: UnitItem[] | undefined, current?: string | null) {
  const list = (units ?? []).filter((u) => u.isActive || u.symbol === current).map((u) => ({ value: u.symbol, label: u.name && u.name !== u.symbol ? `${u.symbol} · ${u.name}` : u.symbol }));
  return current && !list.some((x) => x.value === current) ? [{ value: current, label: current }, ...list] : list;
}

type UnitProps = Omit<SelectProps, 'data' | 'value' | 'onChange'> & { value: string | null; onChange: (v: string | null) => void };

/** Picks a unit from the organization's list; the text stored is its symbol. */
export function UnitSelect({ value, onChange, ...rest }: UnitProps) {
  const { t } = useTranslation();
  const { data } = useUnits();
  return <Select data={unitData(data, value)} value={value} onChange={onChange} searchable nothingFoundMessage={t('common.noRecords')} {...rest} />;
}

/**
 * Brand and model from the lists (0.21.0); the model list follows the brand. People who edit assets can add a missing brand or
 * model from the picker ("Add “X”").
 */
export function BrandModelPicker({ brandId, modelId, onChange, canAdd, errors }: {
  brandId: string | null; modelId: string | null; onChange: (brandId: string | null, modelId: string | null) => void; canAdd: boolean;
  errors?: { brand?: React.ReactNode; model?: React.ReactNode };
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data } = useBrands();
  const [brandSearch, setBrandSearch] = useState('');
  const [modelSearch, setModelSearch] = useState('');
  const brand = data?.find((b) => b.id === brandId);
  const brandData = useMemo(() => (data ?? []).filter((b) => b.isActive || b.id === brandId).map((b) => ({ value: b.id, label: b.name })), [data, brandId]);
  const modelData = useMemo(() => (brand?.models ?? []).filter((m) => m.isActive || m.id === modelId).map((m) => ({ value: m.id, label: m.name })), [brand, modelId]);
  const add = async (path: string, name: string) => {
    try {
      const r = await post<{ id: string }>(path, { name: name.trim(), isActive: true });
      await qc.invalidateQueries({ queryKey: ['brands'] });
      return r.id;
    } catch (e) { notifyError(e); return null; }
  };
  const addButton = (text: string, onClick: () => void) => canAdd && text.trim()
    ? <Button size="xs" variant="light" onClick={onClick}>{t('brands.addNamed', { name: text.trim() })}</Button>
    : t('common.noRecords');
  return (
    <SimpleGrid cols={{ base: 1, sm: 2 }}>
      <Select label={t('brands.brand')} data={brandData} value={brandId} searchable clearable searchValue={brandSearch} onSearchChange={setBrandSearch}
        onChange={(v) => onChange(v, null)} error={errors?.brand}
        nothingFoundMessage={addButton(brandSearch, async () => { const id = await add('/brands', brandSearch); if (id) { onChange(id, null); setBrandSearch(''); } })} />
      <Select label={t('brands.model')} data={modelData} value={modelId} searchable clearable disabled={!brandId} searchValue={modelSearch} onSearchChange={setModelSearch}
        onChange={(v) => onChange(brandId, v)} error={errors?.model}
        nothingFoundMessage={addButton(modelSearch, async () => { if (!brandId) return; const id = await add(`/brands/${brandId}/models`, modelSearch); if (id) { onChange(brandId, id); setModelSearch(''); } })} />
    </SimpleGrid>
  );
}

/** Asset group choices (0.21.0): active ones, plus the current one; a site's groups only when a site is given. */
export function groupData(groups: AssetGroupItem[] | undefined, siteId?: string | null, current?: string | null) {
  return (groups ?? [])
    .filter((g) => (g.isActive || g.id === current) && (!siteId || !g.siteId || g.siteId === siteId || g.id === current))
    .map((g) => ({ value: g.id, label: `${g.code} · ${g.name}` }));
}
