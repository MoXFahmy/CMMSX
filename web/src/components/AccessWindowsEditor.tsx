import { ActionIcon, Badge, Button, Group, Stack, Text, TextInput } from '@mantine/core';
import { DateTimePicker } from '@mantine/dates';
import { useTranslation } from 'react-i18next';
import { IconClockHour4, IconHandStop, IconPlus, IconTrash } from '@tabler/icons-react';
import type { AccessWindow } from '@/api/types';
import { MAX_WINDOWS } from './accessWindows';
import { dateTimeFormat, fmtDateTime } from './utils';

/** A picker's local "YYYY-MM-DD HH:mm:ss" (or Date) as an ISO instant. */
const iso = (v: string | Date | null) => (v ? new Date(typeof v === 'string' ? v.replace(' ', 'T') : v).toISOString() : '');

/** Tomorrow at <hour>:00, local time. */
function tomorrowAt(hour: number) {
  const d = new Date(); d.setDate(d.getDate() + 1); d.setHours(hour, 0, 0, 0); return d.toISOString();
}

/** Edits the times when the work may be done (0.10.0): start, end and a note for each. */
export function AccessWindowsEditor({ value, onChange, error }: { value: AccessWindow[]; onChange: (v: AccessWindow[]) => void; error?: React.ReactNode }) {
  const { t } = useTranslation();
  const set = (i: number, patch: Partial<AccessWindow>) => onChange(value.map((w, n) => (n === i ? { ...w, ...patch } : w)));
  return (
    <Stack gap="xs">
      <Group justify="space-between">
        <div><Text fz="sm" fw={500}>{t('accessWindows.title')}</Text><Text fz="xs" c="dimmed">{t('accessWindows.hint')}</Text></div>
        <Button size="xs" variant="light" leftSection={<IconPlus size={14} />} disabled={value.length >= MAX_WINDOWS}
          onClick={() => onChange([...value, { start: tomorrowAt(8), end: tomorrowAt(12), note: '' }])}>{t('accessWindows.add')}</Button>
      </Group>
      {value.map((w, i) => (
        <Group key={i} align="flex-end" gap="xs" wrap="nowrap">
          <DateTimePicker label={t('accessWindows.from')} valueFormat={dateTimeFormat()} value={w.start ? new Date(w.start) : null} onChange={(v) => set(i, { start: iso(v) })} style={{ flex: 1 }} />
          <DateTimePicker label={t('accessWindows.to')} valueFormat={dateTimeFormat()} value={w.end ? new Date(w.end) : null} onChange={(v) => set(i, { end: iso(v) })} style={{ flex: 1 }} />
          <TextInput label={t('accessWindows.note')} value={w.note ?? ''} maxLength={200} onChange={(e) => set(i, { note: e.currentTarget.value })} style={{ flex: 1 }} />
          <ActionIcon variant="subtle" color="red" mb={4} aria-label={t('common.delete')} onClick={() => onChange(value.filter((_, n) => n !== i))}><IconTrash size={16} /></ActionIcon>
        </Group>
      ))}
      {error && <Text c="red" fz="sm">{error}</Text>}
    </Stack>
  );
}

/** The windows, read-only, with a warning when the work is planned outside every one of them. */
export function AccessWindowsList({ windows, outside }: { windows?: AccessWindow[] | null; outside?: boolean }) {
  const { t } = useTranslation();
  if (!windows || windows.length === 0) return null;
  return (
    <Stack gap={4}>
      <Group gap={6}><IconClockHour4 size={16} /><Text fz="sm" fw={500}>{t('accessWindows.title')}</Text>
        {outside && <Badge color="orange" variant="light">{t('accessWindows.outside')}</Badge>}</Group>
      {windows.map((w, i) => <Text key={i} fz="sm">{fmtDateTime(w.start)} – {fmtDateTime(w.end)}{w.note ? ` · ${w.note}` : ''}</Text>)}
    </Stack>
  );
}

/** "Only while the asset is stopped" (0.10.0). */
export function StoppedBadge({ show, size }: { show?: boolean; size?: 'xs' | 'sm' | 'md' }) {
  const { t } = useTranslation();
  if (!show) return null;
  return <Badge color="red" variant="outline" size={size} leftSection={<IconHandStop size={12} />}>{t('accessWindows.onlyWhenStopped')}</Badge>;
}
