import { useMemo, useState } from 'react';
import { ActionIcon, Badge, Button, Card, Group, Stack, Text } from '@mantine/core';
import { useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { IconChevronLeft, IconChevronRight, IconPlus } from '@tabler/icons-react';
import { fmtQty } from '@/components/utils';
import { dayRows } from './day';
import { HoursModal } from './JobPage';
import { useTech } from './useTech';

/** My day (0.19.0): my hours per job for a day of the last 30, against the hours I have that day; hours are added offline. */
export function DayPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { bundle, queue, add } = useTech();
  const today = dayjs().format('YYYY-MM-DD');
  const [date, setDate] = useState(today);
  const [adding, setAdding] = useState<string | null>(null);
  const day = useMemo(() => (bundle ? dayRows(bundle, date, queue) : null), [bundle, date, queue]);
  const oldest = dayjs().subtract(30, 'day').format('YYYY-MM-DD');
  if (!bundle || !day) return null;
  return (
    <Stack gap="sm">
      <Group justify="space-between" wrap="nowrap">
        <ActionIcon variant="default" size="lg" disabled={date <= oldest} onClick={() => setDate(dayjs(date).subtract(1, 'day').format('YYYY-MM-DD'))} aria-label={t('planning.previous')}>
          <IconChevronLeft size={18} className="mirror-rtl" /></ActionIcon>
        <Text fw={700}>{date === today ? t('techDay.today') : dayjs(date).format('dddd D MMM')}</Text>
        <ActionIcon variant="default" size="lg" disabled={date >= today} onClick={() => setDate(dayjs(date).add(1, 'day').format('YYYY-MM-DD'))} aria-label={t('planning.next')}>
          <IconChevronRight size={18} className="mirror-rtl" /></ActionIcon>
      </Group>
      <Card withBorder p="sm">
        <Group justify="space-between">
          <Text fw={600}>{t('techDay.total', { hours: fmtQty(day.hours) })}{day.overtimeHours > 0 && ` + ${t('techDay.overtime', { hours: fmtQty(day.overtimeHours) })}`}</Text>
          {day.available != null && <Text fz="sm" c={day.hours + day.overtimeHours > day.available ? 'red' : 'dimmed'}>{t('techDay.available', { hours: fmtQty(day.available) })}</Text>}
        </Group>
        {(bundle.myDay?.available.find((d) => d.day === date)?.away.length ?? 0) > 0 && (
          <Text fz="xs" c="dimmed">{bundle.myDay!.available.find((d) => d.day === date)!.away.join(', ')}</Text>
        )}
      </Card>
      {day.rows.length === 0 && <Text c="dimmed" ta="center" py="lg">{t('techDay.nothing')}</Text>}
      {day.rows.map((r) => (
        <Card key={r.workOrderId} withBorder p="sm" onClick={() => !r.readOnly && navigate(`/tech/jobs/${r.workOrderId}`)} style={{ cursor: r.readOnly ? undefined : 'pointer' }}>
          <Group justify="space-between" wrap="nowrap" align="flex-start">
            <div style={{ minWidth: 0 }}>
              <Group gap={6}><Text fz="xs" c="dimmed">{r.number}</Text>{r.readOnly && <Badge size="xs" color="gray" variant="light">{t('techDay.finished')}</Badge>}</Group>
              <Text fw={600} lineClamp={2}>{r.title}</Text>
              <Text fz="sm">{t('techDay.hours', { hours: fmtQty(r.hours) })}{r.overtimeHours > 0 && ` + ${t('techDay.overtime', { hours: fmtQty(r.overtimeHours) })}`}</Text>
            </div>
            {!r.readOnly && (
              <Button size="xs" variant="light" leftSection={<IconPlus size={14} />} onClick={(e) => { e.stopPropagation(); setAdding(r.workOrderId); }}>{t('techDay.addHours')}</Button>
            )}
          </Group>
        </Card>
      ))}
      {adding && (
        <HoursModal key={`${adding}-${date}`} opened onClose={() => setAdding(null)} initialDate={date}
          onSave={(p) => void add({ type: 'labor.add', workOrderId: adding, payload: p })} />
      )}
    </Stack>
  );
}
