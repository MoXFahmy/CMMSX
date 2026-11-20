import { useState } from 'react';
import { Group, NumberInput, Select, Table, Text } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import type { SlaTarget, WorkPriority } from '@/api/types';
import { inUnit, toMinutes, unitOf, type Unit } from './targets';

const PRIORITIES: WorkPriority[] = ['Urgent', 'High', 'Medium', 'Low'];
type Field = 'respondMinutes' | 'completeMinutes';

/** Respond within / complete within per priority, in hours or days (stored as minutes; empty = no target). */
export function TargetsEditor({ value, onChange, disabled }: { value: SlaTarget[]; onChange: (v: SlaTarget[]) => void; disabled?: boolean }) {
  const { t } = useTranslation();
  const row = (p: WorkPriority) => value.find((x) => x.priority === p) ?? { priority: p };
  const set = (p: WorkPriority, field: Field, minutes: number | null) => {
    const next = { ...row(p), [field]: minutes };
    const others = value.filter((x) => x.priority !== p);
    onChange(next.respondMinutes || next.completeMinutes ? [...others, next] : others);
  };
  const cell = (p: WorkPriority, field: Field) => (
    <TargetCell key={`${p}-${field}`} minutes={row(p)[field]} disabled={disabled} label={`${t(`priority.${p}`)} ${t(`settings.${field}`)}`}
      onChange={(m) => set(p, field, m)} />
  );
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

/** One target: a number and its unit. The unit stays as chosen, even before a number is typed. */
function TargetCell({ minutes, disabled, label, onChange }: { minutes?: number | null; disabled?: boolean; label: string; onChange: (m: number | null) => void }) {
  const { t } = useTranslation();
  const [unit, setUnit] = useState<Unit>(unitOf(minutes));
  // What is typed is kept as typed ("1." on the way to "1.5"); the minutes follow it.
  const [text, setText] = useState<number | string>(inUnit(minutes, unit));
  const number = (v: number | string): number | '' => (typeof v === 'number' ? v : v.trim() === '' || Number.isNaN(Number(v)) ? '' : Number(v));
  return (
    <Group gap={4} wrap="nowrap">
      <NumberInput w={90} min={0} decimalScale={2} disabled={disabled} value={text} placeholder="—" aria-label={label}
        onChange={(n) => { setText(n); onChange(toMinutes(number(n), unit)); }} />
      <Select w={90} disabled={disabled} allowDeselect={false} value={unit} data={[{ value: 'h', label: t('settings.hours') }, { value: 'd', label: t('settings.days') }]}
        onChange={(u) => { const next = (u as Unit) ?? 'h'; setUnit(next); onChange(toMinutes(number(text), next)); }} />
    </Group>
  );
}
