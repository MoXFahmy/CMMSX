import { useEffect, useState } from 'react';
import { ActionIcon, Badge, Button, Card, Chip, Group, Modal, NumberInput, SegmentedControl, Select, SimpleGrid, Stack, Text, Textarea } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { DataTable } from 'mantine-datatable';
import dayjs from 'dayjs';
import { IconEdit, IconPlus, IconTrash } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import type { AvailabilityEntry, AvailabilityType } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { confirmDelete } from '@/components/confirm';
import { teamOptions, useTeams } from '@/components/teams';
import { fmtDate, handleFormError, notifyError, notifySuccess, toIsoDate } from '@/components/utils';

const WEEK = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'] as const;

type Values = {
  typeId: string | null; who: 'person' | 'team'; employeeId: string | null; teamId: string | null; from: Date | string | null; to: Date | string | null;
  days: string[]; hours: number | ''; notes: string;
};

/** Time away and extra time (0.19.0): entries per person or team, once or on chosen weekdays over a date range. */
export function AvailabilityPage() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const qc = useQueryClient();
  const [params, setParams] = useSearchParams();
  const editable = can(P.peopleManage) || can(P.woEdit);
  const [filters, setFilters] = useState({ employeeId: params.get('employeeId'), teamId: null as string | null, typeId: null as string | null, from: dayjs().format('YYYY-MM-DD') });
  const { data: types } = useQuery({ queryKey: ['availability-types'], queryFn: () => get<AvailabilityType[]>('/availability-types') });
  const { data: people } = useQuery({ queryKey: ['employees', 'lookup'], queryFn: () => get<{ id: string; fullName: string }[]>('/employees/lookup') });
  const { data: teams } = useTeams(null);
  const { data, isFetching } = useQuery({
    queryKey: ['availability', filters], placeholderData: (prev) => prev,
    queryFn: () => get<AvailabilityEntry[]>('/availability', { employeeId: filters.employeeId, teamId: filters.teamId, typeId: filters.typeId, from: filters.from }),
  });
  const [editing, setEditing] = useState<AvailabilityEntry | null>(null);
  const [opened, setOpened] = useState(false);
  const blank = (): Values => ({ typeId: null, who: 'person', employeeId: filters.employeeId, teamId: null, from: dayjs().format('YYYY-MM-DD'), to: null, days: [], hours: '', notes: '' });
  const form = useForm<Values>({ initialValues: blank() });
  const open = (x: AvailabilityEntry | null) => {
    setEditing(x); form.clearErrors();
    form.setValues(x ? {
      typeId: x.typeId, who: x.teamId ? 'team' : 'person', employeeId: x.employeeId ?? null, teamId: x.teamId ?? null, from: x.from, to: x.to,
      days: x.days, hours: x.hours ?? '', notes: x.notes ?? '',
    } : blank());
    setOpened(true);
  };
  // "Add time away" from a person's row on the planning board.
  useEffect(() => {
    if (params.get('add') === '1' && editable) { open(null); const next = new URLSearchParams(params); next.delete('add'); setParams(next, { replace: true }); }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  const submit = form.onSubmit(async (v) => {
    const body = {
      typeId: v.typeId, employeeId: v.who === 'person' ? v.employeeId : null, teamId: v.who === 'team' ? v.teamId : null,
      from: toIsoDate(v.from), to: toIsoDate(v.to) ?? toIsoDate(v.from), days: v.days, hours: v.hours === '' ? null : v.hours, notes: v.notes || null,
    };
    try {
      if (editing) await put(`/availability/${editing.id}`, body); else await post('/availability', body);
      notifySuccess(t('common.saved')); setOpened(false);
      await qc.invalidateQueries({ queryKey: ['availability'] });
      await qc.invalidateQueries({ queryKey: ['planning-board'] });
    } catch (e) { handleFormError(form, e); }
  });
  const typeOptions = (types ?? []).filter((x) => x.isActive || x.id === form.values.typeId).map((x) => ({ value: x.id, label: x.name }));
  const days = (x: AvailabilityEntry) => (x.days.length === 0 ? t('availability.everyDay') : x.days.map((d) => t(`availability.weekday.${d}`)).join(', '));

  return (
    <>
      <PageHeader title={t('availability.title')} subtitle={t('availability.subtitle')}
        actions={editable ? <Button leftSection={<IconPlus size={16} />} onClick={() => open(null)}>{t('availability.add')}</Button> : undefined} />
      <Card mb="sm" p="sm">
        <Group gap="sm" wrap="wrap">
          <Select size="xs" w={200} placeholder={t('availability.person')} clearable searchable data={(people ?? []).map((p) => ({ value: p.id, label: p.fullName }))}
            value={filters.employeeId} onChange={(v) => setFilters((f) => ({ ...f, employeeId: v }))} />
          <Select size="xs" w={180} placeholder={t('teams.team')} clearable data={teamOptions(teams, filters.teamId)} value={filters.teamId}
            onChange={(v) => setFilters((f) => ({ ...f, teamId: v }))} />
          <Select size="xs" w={180} placeholder={t('availability.type')} clearable data={(types ?? []).map((x) => ({ value: x.id, label: x.name }))} value={filters.typeId}
            onChange={(v) => setFilters((f) => ({ ...f, typeId: v }))} />
          <DateInput size="xs" w={150} valueFormat="DD MMM YYYY" label={undefined} placeholder={t('availability.from')} value={new Date(filters.from)}
            onChange={(v) => setFilters((f) => ({ ...f, from: toIsoDate(v) ?? dayjs().format('YYYY-MM-DD') }))} aria-label={t('availability.from')} />
        </Group>
      </Card>
      <Card p={0}>
        <DataTable minHeight={150} fetching={isFetching} records={data ?? []} noRecordsText={t('availability.none')} columns={[
          { accessor: 'typeName', title: t('availability.type'), render: (x) => (
            <Group gap={6} wrap="nowrap"><span style={{ width: 10, height: 10, borderRadius: 5, background: x.typeColor ?? 'var(--mantine-color-gray-5)', display: 'inline-block' }} />
              {x.typeName}{x.typeKind === 'Extra' && <Badge size="xs" variant="light">{t('availability.extra')}</Badge>}</Group>) },
          { accessor: 'who', title: t('availability.who'), render: (x) => x.employeeName ?? (x.teamName ? `${t('teams.team')}: ${x.teamName}` : '—') },
          { accessor: 'from', title: t('availability.dates'), render: (x) => (x.from === x.to ? fmtDate(x.from) : `${fmtDate(x.from)} – ${fmtDate(x.to)}`) },
          { accessor: 'days', title: t('availability.days'), render: days },
          { accessor: 'hours', title: t('availability.hours'), textAlign: 'right', render: (x) => (x.hours != null ? `${x.hours} h` : t('availability.wholeDay')) },
          { accessor: 'notes', title: t('common.notes'), render: (x) => <Text fz="sm" lineClamp={1}>{x.notes}</Text> },
          { accessor: 'actions', title: '', width: 90, hidden: !editable, render: (x) => (
            <Group gap={4} justify="flex-end" wrap="nowrap">
              <ActionIcon variant="subtle" onClick={() => open(x)} aria-label={t('common.edit')}><IconEdit size={16} /></ActionIcon>
              <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => confirmDelete(x.typeName, async () => {
                try { await del(`/availability/${x.id}`); await qc.invalidateQueries({ queryKey: ['availability'] }); await qc.invalidateQueries({ queryKey: ['planning-board'] }); }
                catch (e) { notifyError(e); }
              })}><IconTrash size={16} /></ActionIcon>
            </Group>) },
        ]} />
      </Card>
      <Modal opened={opened} onClose={() => setOpened(false)} title={editing ? t('availability.edit') : t('availability.add')} size="lg">
        <form onSubmit={submit}><Stack>
          <Select label={t('availability.type')} required data={typeOptions} {...form.getInputProps('typeId')} />
          <SegmentedControl value={form.values.who} onChange={(v) => form.setFieldValue('who', v as Values['who'])}
            data={[{ value: 'person', label: t('availability.person') }, { value: 'team', label: t('teams.team') }]} />
          {form.values.who === 'person'
            ? <Select label={t('availability.person')} required searchable data={(people ?? []).map((p) => ({ value: p.id, label: p.fullName }))} {...form.getInputProps('employeeId')} />
            : <Select label={t('teams.team')} required data={teamOptions(teams, form.values.teamId)} {...form.getInputProps('teamId')} />}
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <DateInput label={t('availability.from')} required valueFormat="DD MMM YYYY" {...form.getInputProps('from')} />
            <DateInput label={t('availability.to')} description={t('availability.toHint')} clearable valueFormat="DD MMM YYYY" {...form.getInputProps('to')} />
          </SimpleGrid>
          <Stack gap={4}>
            <Text fz="sm" fw={500}>{t('availability.days')}</Text>
            <Text fz="xs" c="dimmed">{t('availability.daysHint')}</Text>
            <Chip.Group multiple value={form.values.days} onChange={(v) => form.setFieldValue('days', v)}>
              <Group gap={6}>{WEEK.map((d) => <Chip key={d} size="xs" value={d}>{t(`availability.weekday.${d}`)}</Chip>)}</Group>
            </Chip.Group>
          </Stack>
          <NumberInput label={t('availability.hoursPerDay')} description={t('availability.hoursHint')} min={0} max={24} decimalScale={2} {...form.getInputProps('hours')} />
          <Textarea label={t('common.notes')} autosize {...form.getInputProps('notes')} />
          <Group justify="flex-end"><Button variant="default" onClick={() => setOpened(false)}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack></form>
      </Modal>
    </>
  );
}
