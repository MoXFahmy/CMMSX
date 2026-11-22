import { Checkbox, Group, Stack, Switch, Table, Text, TextInput } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { DAY_NAMES } from '@/api/types';
import { hoursOn, problem, type WorkingDay } from './workingHours';

const DEFAULT_DAY = { start: '08:00', end: '16:00' };

/**
 * A weekly working pattern (0.9.0): a row per day with its start and end. With <paramref name="inheritLabel"/>, a switch
 * hands the choice back to the level above (null = use <paramref name="inherited"/>, shown greyed).
 */
export function WorkingHoursEditor({ value, onChange, inheritLabel, inherited, disabled, error }: {
  value: WorkingDay[] | null; onChange: (v: WorkingDay[] | null) => void; inheritLabel?: string; inherited?: WorkingDay[]; disabled?: boolean; error?: React.ReactNode;
}) {
  const { t } = useTranslation();
  const inheriting = !!inheritLabel && value === null;
  const shown = (inheriting ? inherited : value) ?? [];
  const off = disabled || inheriting;
  const setDay = (day: WorkingDay['day'], patch: Partial<WorkingDay> | null) => {
    const rest = shown.filter((d) => d.day !== day);
    const current = shown.find((d) => d.day === day);
    onChange(patch === null ? rest : [...rest, { day, ...DEFAULT_DAY, ...current, ...patch }]);
  };
  const wrong = !inheriting && value ? problem(value) : null;
  const total = DAY_NAMES.reduce((sum, d) => sum + hoursOn(shown, d), 0);
  return (
    <Stack gap="xs">
      {inheritLabel && (
        <Switch label={inheritLabel} checked={inheriting} disabled={disabled}
          onChange={(e) => onChange(e.currentTarget.checked ? null : (inherited ?? []).map((d) => ({ ...d })))} />
      )}
      <Table withRowBorders={false} verticalSpacing={2} maw={460}>
        <Table.Tbody>
          {DAY_NAMES.map((day) => {
            const d = shown.find((x) => x.day === day);
            return (
              <Table.Tr key={day}>
                <Table.Td w={150}><Checkbox label={t(`pm.days.${day}`)} checked={!!d} disabled={off} onChange={(e) => setDay(day, e.currentTarget.checked ? {} : null)} /></Table.Td>
                <Table.Td><TextInput size="xs" w={80} placeholder="08:00" aria-label={t('hours.start')} disabled={off || !d} value={d?.start ?? ''}
                  onChange={(e) => setDay(day, { start: e.currentTarget.value })} /></Table.Td>
                <Table.Td><TextInput size="xs" w={80} placeholder="16:00" aria-label={t('hours.end')} disabled={off || !d} value={d?.end ?? ''}
                  onChange={(e) => setDay(day, { end: e.currentTarget.value })} /></Table.Td>
                <Table.Td><Text fz="xs" c="dimmed">{d ? t('hours.hoursShort', { hours: hoursOn(shown, day) }) : t('hours.dayOff')}</Text></Table.Td>
              </Table.Tr>
            );
          })}
        </Table.Tbody>
      </Table>
      <Group gap="xs"><Text fz="xs" c="dimmed">{t('hours.weekTotal', { hours: Math.round(total * 10) / 10 })}</Text></Group>
      {(wrong || error) && <Text fz="xs" c="red">{wrong ? t(wrong) : error}</Text>}
    </Stack>
  );
}

/** Before 0.9.0 an organization had only "hours per day": every day from 08:00 for that long. */
export function organizationHours(s: { workingHours?: WorkingDay[]; dailyCapacityHours: number }): WorkingDay[] {
  if (s.workingHours?.length) return s.workingHours;
  const minutes = Math.round(Math.min(Math.max(s.dailyCapacityHours, 0), 24) * 60);
  const start = minutes <= 16 * 60 ? 8 * 60 : 24 * 60 - minutes;
  const clock = (m: number) => (m === 24 * 60 ? '24:00' : `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`);
  return minutes === 0 ? [] : DAY_NAMES.map((day) => ({ day, start: clock(start), end: clock(start + minutes) }));
}
