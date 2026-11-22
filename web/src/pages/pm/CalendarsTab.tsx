import { useState } from 'react';
import { Anchor, Badge, Button, Card, Checkbox, Group, Modal, NumberInput, SegmentedControl, Select, SimpleGrid, Stack, Text, TextInput, Title } from '@mantine/core';
import { DateInput, DatePicker } from '@mantine/dates';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { DataTable } from 'mantine-datatable';
import { IconPlus } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import { FREQ_UNITS, type ScheduleCalendarDetail, type ScheduleCalendarItem } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { confirmDelete } from '@/components/confirm';
import { fmtDate, handleFormError, notifyError, notifySuccess, toIsoDate } from '@/components/utils';

type F = { code: string; name: string; kind: 'Interval' | 'Dates'; frequency: number | ''; unit: string; anchorDate: Date | null; dates: Date[]; isActive: boolean };
const blank: F = { code: '', name: '', kind: 'Dates', frequency: 1, unit: 'Month', anchorDate: new Date(), dates: [], isActive: true };
const day = (v: string) => new Date(`${v}T00:00:00`);

/** PM › Calendars (0.12.0): named sets of dates many PMs share as triggers (a hotel's low-season weeks, every quarter from a date). */
export function CalendarsTab() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const editable = can(P.pmManage);
  const qc = useQueryClient();
  const { data, isFetching } = useQuery({ queryKey: ['pm-calendars'], queryFn: () => get<ScheduleCalendarItem[]>('/pm-calendars') });
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<ScheduleCalendarDetail | null>(null);
  const form = useForm<F>({ initialValues: blank });
  const show = async (id: string | null) => {
    form.clearErrors();
    if (!id) { setEditing(null); form.setValues(blank); setOpen(true); return; }
    try {
      const c = await get<ScheduleCalendarDetail>(`/pm-calendars/${id}`);
      setEditing(c);
      form.setValues({ code: c.code, name: c.name, kind: c.kind, frequency: c.frequency, unit: c.unit, anchorDate: c.anchorDate ? day(c.anchorDate) : null, dates: c.dates.map(day), isActive: c.isActive });
      setOpen(true);
    } catch (e) { notifyError(e); }
  };
  const submit = form.onSubmit(async (v) => {
    const body = { ...v, frequency: v.frequency === '' ? 1 : v.frequency, anchorDate: toIsoDate(v.anchorDate), dates: v.dates.map((d) => toIsoDate(d)), rowVersion: editing?.rowVersion };
    try {
      if (editing) await put(`/pm-calendars/${editing.id}`, body); else await post('/pm-calendars', body);
      notifySuccess(t('common.saved')); setOpen(false); await qc.invalidateQueries({ queryKey: ['pm-calendars'] });
    } catch (e) { handleFormError(form, e); }
  });
  const v = form.values;
  return (
    <Card p={0}>
      <Group justify="space-between" p="md">
        <div><Title order={5}>{t('pm12.calendars')}</Title><Text fz="sm" c="dimmed">{t('pm12.calendarsHint')}</Text></div>
        {editable && <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => show(null)}>{t('pm12.newCalendar')}</Button>}
      </Group>
      <DataTable minHeight={160} fetching={isFetching} records={data ?? []} noRecordsText={t('common.noRecords')} highlightOnHover rowClassName={() => 'clickable-row'}
        onRowClick={({ record }) => show(record.id)}
        columns={[
          { accessor: 'code', title: t('pm.code'), render: (c) => <Text fw={600} fz="sm">{c.code}</Text> },
          { accessor: 'name', title: t('common.name') },
          { accessor: 'kind', title: t('pm12.kind'), render: (c) => c.kind === 'Dates' ? t('pm12.datesCount', { count: c.dateCount }) : t('pm12.everyUnit', { n: c.frequency, unit: t(`pm.units.${c.unit}`) }) },
          { accessor: 'next', title: t('pm.nextDue'), render: (c) => fmtDate(c.next) },
          { accessor: 'scheduleCount', title: t('pm.schedules'), textAlign: 'right' },
          { accessor: 'isActive', title: t('common.status'), render: (c) => <Badge color={c.isActive ? 'teal' : 'gray'}>{c.isActive ? t('common.active') : t('common.inactive')}</Badge> },
        ]} />
      <Modal opened={open} onClose={() => setOpen(false)} title={editing ? `${t('pm12.editCalendar')} · ${editing.code}` : t('pm12.newCalendar')} size="lg">
        <form onSubmit={submit}><Stack>
          <SimpleGrid cols={{ base: 1, sm: 3 }}>
            <TextInput label={t('pm.code')} required {...form.getInputProps('code')} />
            <TextInput label={t('common.name')} required style={{ gridColumn: 'span 2' }} {...form.getInputProps('name')} />
          </SimpleGrid>
          <SegmentedControl value={v.kind} onChange={(k) => form.setFieldValue('kind', k as F['kind'])}
            data={[{ value: 'Dates', label: t('pm12.kindDates') }, { value: 'Interval', label: t('pm12.kindInterval') }]} />
          {v.kind === 'Interval' ? (
            <SimpleGrid cols={{ base: 1, sm: 3 }}>
              <NumberInput label={t('pm.every')} min={1} max={999} {...form.getInputProps('frequency')} />
              <Select label={t('pm.unit')} data={FREQ_UNITS.map((u) => ({ value: u, label: t(`pm.units.${u}`) }))} {...form.getInputProps('unit')} />
              <DateInput label={t('pm12.firstDate')} required valueFormat="DD MMM YYYY" {...form.getInputProps('anchorDate')} />
            </SimpleGrid>
          ) : (
            <Stack gap={4}>
              <Text fz="sm" fw={500}>{t('pm12.chooseDates', { count: v.dates.length })}</Text>
              <DatePicker type="multiple" numberOfColumns={2} value={v.dates.map((x) => toIsoDate(x)!)} onChange={(d) => form.setFieldValue('dates', (d as unknown as string[]).map(day))} />
              {form.errors.dates && <Text c="red" fz="sm">{form.errors.dates}</Text>}
            </Stack>
          )}
          <Checkbox label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
          {editing && editing.usedBy.length > 0 && (
            <Text fz="xs" c="dimmed">{t('pm12.usedByPms')}: {editing.usedBy.map((s, i) => <span key={s.id}>{i > 0 && ', '}<Anchor component={Link} to={`/pm/schedules/${s.id}`} fz="xs">{s.code}</Anchor></span>)}</Text>
          )}
          {editing && editing.next.length > 0 && <Text fz="xs" c="dimmed">{t('pm12.nextDates')}: {editing.next.slice(0, 6).map((d) => fmtDate(d)).join(' · ')}</Text>}
          <Group justify="space-between">
            {editing && editable ? <Button variant="subtle" color="red" onClick={() => confirmDelete(editing.code, async () => {
              try { await del(`/pm-calendars/${editing.id}`); setOpen(false); await qc.invalidateQueries({ queryKey: ['pm-calendars'] }); } catch (e) { notifyError(e); }
            })}>{t('common.delete')}</Button> : <span />}
            <Group><Button variant="default" onClick={() => setOpen(false)}>{t('common.cancel')}</Button>{editable && <Button type="submit" loading={form.submitting}>{t('common.save')}</Button>}</Group>
          </Group>
        </Stack></form>
      </Modal>
    </Card>
  );
}
