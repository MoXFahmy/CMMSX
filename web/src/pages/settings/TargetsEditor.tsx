import { Group, NumberInput, Select, Table, Text } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import type { SlaTarget, WorkPriority } from '@/api/types';

const PRIORITIES: WorkPriority[] = ['Urgent', 'High', 'Medium', 'Low'];
type Unit = 'h' | 'd';
type Field = 'respondMinutes' | 'completeMinutes';

/** Minutes shown as days when they are whole days, else hours. */
const split = (minutes?: number | null): { value: number | ''; unit: Unit } =>
  !minutes ? { value: '', unit: 'h' } : minutes % 1440 === 0 ? { value: minutes / 1440, unit: 'd' } : { value: Math.round((minutes / 60) * 100) / 100, unit: 'h' };

/** Respond within / complete within per priority, in hours or days (stored as minutes; empty = no target). */
export function TargetsEditor({ value, onChange, disabled }: { value: SlaTarget[]; onChange: (v: SlaTarget[]) => void; disabled?: boolean }) {
  const { t } = useTranslation();
  const row = (p: WorkPriority) => value.find((x) => x.priority === p) ?? { priority: p };
  const set = (p: WorkPriority, field: Field, minutes: number | null) => {
    const next = { ...row(p), [field]: minutes };
    const others = value.filter((x) => x.priority !== p);
    onChange(next.respondMinutes || next.completeMinutes ? [...others, next] : others);
  };
  const cell = (p: WorkPriority, field: Field) => {
    const { value: v, unit } = split(row(p)[field]);
    const save = (n: number | '', u: Unit) => set(p, field, n === '' || n <= 0 ? null : Math.round(n * (u === 'd' ? 1440 : 60)));
    return (
      <Group gap={4} wrap="nowrap">
        <NumberInput w={90} min={0} decimalScale={2} disabled={disabled} value={v} placeholder="—" aria-label={`${t(`priority.${p}`)} ${t(`settings.${field}`)}`}
          onChange={(n) => save(typeof n === 'number' ? n : n === '' ? '' : Number(n), unit)} />
        <Select w={90} disabled={disabled} allowDeselect={false} value={unit} data={[{ value: 'h', label: t('settings.hours') }, { value: 'd', label: t('settings.days') }]}
          onChange={(u) => save(v, (u as Unit) ?? 'h')} />
      </Group>
    );
  };
  return (
    <Table withRowBorders={false} verticalSpacing={4}>
      <Table.Thead><Table.Tr><Table.Th>{t('priority.label')}</Table.Th><Table.Th>{t('settings.respondMinutes')}</Table.Th><Table.Th>{t('settings.completeMinutes')}</Table.Th></Table.Tr></Table.Thead>
      <Table.Tbody>
        {PRIORITIES.map((p) => (
          <Table.Tr key={p}><Table.Td><Text fz="sm">{t(`priority.${p}`)}</Text></Table.Td><Table.Td>{cell(p, 'respondMinutes')}</Table.Td><Table.Td>{cell(p, 'completeMinutes')}</Table.Td></Table.Tr>
        ))}
      </Table.Tbody>
    </Table>
  );
}

/** Every time zone the browser knows (IANA names), with the current one kept even if unknown. */
export function timeZones(current?: string): string[] {
  const all = (Intl as unknown as { supportedValuesOf?: (k: string) => string[] }).supportedValuesOf?.('timeZone') ?? [];
  return Array.from(new Set(['UTC', ...(current ? [current] : []), ...all]));
}
