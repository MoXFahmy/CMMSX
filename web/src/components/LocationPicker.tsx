import { useMemo } from 'react';
import { Select, SimpleGrid } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { get } from '@/api/client';
import type { Hierarchy, HierarchyLocation } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';

export function useHierarchy() {
  return useQuery({ queryKey: ['hierarchy'], queryFn: () => get<Hierarchy>('/locations/hierarchy'), staleTime: 60_000 });
}

/** Flattens the location tree of one site into depth-first ordered options with full path labels. */
export function locationOptions(locations: HierarchyLocation[], siteId: string | null, excludeSubtreeOf?: string) {
  if (!siteId) return [];
  const bySite = locations.filter((l) => l.siteId === siteId);
  const children = new Map<string | null, HierarchyLocation[]>();
  for (const l of bySite) {
    const k = l.parentId ?? null;
    children.set(k, [...(children.get(k) ?? []), l]);
  }
  const out: { value: string; label: string }[] = [];
  const walk = (parent: string | null, trail: string[]) => {
    for (const l of (children.get(parent) ?? []).sort((a, b) => a.name.localeCompare(b.name))) {
      if (l.id === excludeSubtreeOf) continue;
      const path = [...trail, l.name];
      out.push({ value: l.id, label: path.join(' › ') + (l.isActive ? '' : ' (inactive)') });
      walk(l.id, path);
    }
  };
  walk(null, []);
  return out;
}

type Props = {
  siteId: string | null;
  locationId: string | null;
  onChange: (siteId: string | null, locationId: string | null) => void;
  siteError?: React.ReactNode;
  locationError?: React.ReactNode;
  locationLabel?: string;
  excludeSubtreeOf?: string;
  required?: boolean;
  cols?: number;
};

export function LocationPicker({ siteId, locationId, onChange, siteError, locationError, locationLabel, excludeSubtreeOf, required = true, cols = 2 }: Props) {
  const { t } = useTranslation();
  const { terms } = useAuth();
  const { data } = useHierarchy();
  const siteOptions = useMemo(() => (data?.sites ?? []).map((s) => ({ value: s.id, label: `${s.name} (${s.code})` })), [data]);
  const locOptions = useMemo(() => locationOptions(data?.locations ?? [], siteId, excludeSubtreeOf), [data, siteId, excludeSubtreeOf]);
  return (
    <SimpleGrid cols={{ base: 1, sm: cols }}>
      <Select label={terms.site} data={siteOptions} value={siteId} searchable required={required} error={siteError}
        onChange={(v) => onChange(v, null)} nothingFoundMessage={t('common.noRecords')} />
      <Select label={locationLabel ?? terms.location} data={locOptions} value={locationId} searchable clearable disabled={!siteId}
        onChange={(v) => onChange(siteId, v)} error={locationError} nothingFoundMessage={t('common.noRecords')}
        comboboxProps={{ width: 'max-content', position: 'bottom-start' }} maxDropdownHeight={320} />
    </SimpleGrid>
  );
}
