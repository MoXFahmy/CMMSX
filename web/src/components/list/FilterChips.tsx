import { Group, Pill } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import type { FilterDef } from './types';

/** One removable chip per active filter, reading "Label: value". */
export function FilterChips({ defs, p, set }: { defs: FilterDef[]; p: (k: string) => string; set: (patch: Record<string, string | null>) => void }) {
  const { t } = useTranslation();
  const active = defs.filter((d) => d.keys.some((k) => p(k)));
  if (active.length === 0) return null;
  return (
    <Group gap={6} mt="xs">
      {active.map((d) => {
        const values = d.keys.map(p);
        const text = d.format ? d.format(values) : values.filter(Boolean).join(' – ');
        return (
          <Pill key={d.keys.join(',')} size="md" withRemoveButton removeButtonProps={{ 'aria-label': t('list.removeFilter', { name: d.label }) }}
            onRemove={() => set(Object.fromEntries(d.keys.map((k) => [k, null])))}>
            {d.label}: {text}
          </Pill>
        );
      })}
    </Group>
  );
}

/** "a – b" for a date range, "from a" / "until b" when one end is open. */
export const rangeText = (fmt: (v: string) => string, t: (k: string, o?: Record<string, string>) => string) => ([from, to]: string[]) =>
  from && to ? `${fmt(from)} – ${fmt(to)}` : from ? t('list.from', { date: fmt(from) }) : t('list.until', { date: fmt(to) });

/** Labels of the comma-separated values of a multi-select filter. */
export const listText = (label: (v: string) => string) => ([v]: string[]) => v.split(',').filter(Boolean).map(label).join(', ');
