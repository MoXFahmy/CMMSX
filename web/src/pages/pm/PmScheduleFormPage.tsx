import { useEffect, useMemo } from 'react';
import { ActionIcon, Anchor, Badge, Button, Card, Center, Checkbox, Grid, Group, Loader, NumberInput, Radio, SegmentedControl, Select, SimpleGrid, Stack, Switch, Table, Text, TextInput, Textarea, Title } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useForm } from '@mantine/form';
import { useDebouncedValue } from '@mantine/hooks';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconPlus, IconTrash } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import { DAY_NAMES, FREQ_UNITS, PM_TYPES, WORK_PRIORITIES, type AssetDetail, type Lookup, type MeterItem, type PmScheduleDetail, type PmTaskListItem } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { LocationPicker } from '@/components/LocationPicker';
import { AssetPicker } from '@/components/AssetPicker';
import { EmployeePicker } from '@/components/EmployeePicker';
import { WoStatusBadge } from '@/components/WorkBadges';
import { confirmDelete } from '@/components/confirm';
import { fmtDate, fmtDateTime, handleFormError, notifyError, notifySuccess, toIsoDate } from '@/components/utils';

type V = {
  code: string; name: string; description: string; siteId: string | null; locationId: string | null; assetId: string | null; tradeId: string | null; priority: string;
  assigneeIds: string[]; pmTaskId: string | null; extraTasks: { pmTaskId: string | null; everyNth: number | '' }[]; type: string; basis: string; frequency: number | '';
  unit: string; weekOfMonth: string | null; dayOfWeek: string | null; dayOfMonth: number | ''; workPeriodDays: number | ''; wholeMonth: boolean; leadDays: number | '';
  startDate: Date | null; nextDueDate: Date | null; meterId: string | null; meterTrigger: string; meterInterval: number | ''; nextDueReading: number | '';
  lowerLimit: number | ''; upperLimit: number | ''; lowerTaskId: string | null; upperTaskId: string | null; timeBased: boolean; isActive: boolean;
};
const blank: V = {
  code: '', name: '', description: '', siteId: null, locationId: null, assetId: null, tradeId: null, priority: 'Medium', assigneeIds: [], pmTaskId: null, extraTasks: [],
  type: 'FixedInterval', basis: 'Scheduled', frequency: 1, unit: 'Month', weekOfMonth: '1', dayOfWeek: 'Monday', dayOfMonth: 1, workPeriodDays: 1, wholeMonth: false, leadDays: 7,
  startDate: new Date(), nextDueDate: null, meterId: null, meterTrigger: 'Incremental', meterInterval: '', nextDueReading: '', lowerLimit: '', upperLimit: '', lowerTaskId: null,
  upperTaskId: null, timeBased: false, isActive: true,
};
const d = (v?: string | null) => (v ? new Date(`${v}T00:00:00`) : null);
const n = (v: number | '') => (v === '' ? null : v);

function toBody(v: V, rowVersion?: string) {
  return {
    ...v, frequency: n(v.frequency) ?? 1, dayOfMonth: n(v.dayOfMonth), workPeriodDays: n(v.workPeriodDays) ?? 1, leadDays: n(v.leadDays) ?? 0,
    weekOfMonth: v.weekOfMonth ? Number(v.weekOfMonth) : null, startDate: toIsoDate(v.startDate) ?? toIsoDate(new Date()), nextDueDate: toIsoDate(v.nextDueDate),
    meterInterval: n(v.meterInterval), nextDueReading: n(v.nextDueReading), lowerLimit: n(v.lowerLimit), upperLimit: n(v.upperLimit),
    extraTasks: v.extraTasks.filter((x) => x.pmTaskId).map((x) => ({ pmTaskId: x.pmTaskId, everyNth: n(x.everyNth) ?? 2 })), rowVersion,
  };
}

export function PmScheduleFormPage() {
  const { t } = useTranslation();
  const { id } = useParams();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { can } = useAuth();
  const editing = !!id;
  const editable = can(P.pmManage);
  const { data: s, isLoading } = useQuery({ queryKey: ['pm-schedule', id], queryFn: () => get<PmScheduleDetail>(`/pm-schedules/${id}`), enabled: editing });
  const assetParam = params.get('assetId');
  const { data: asset } = useQuery({ queryKey: ['asset', assetParam], queryFn: () => get<AssetDetail>(`/assets/${assetParam}`), enabled: !!assetParam && !editing });
  const { data: tasks } = useQuery({ queryKey: ['pm-tasks'], queryFn: () => get<PmTaskListItem[]>('/pm-tasks') });
  const { data: trades } = useQuery({ queryKey: ['trades'], queryFn: () => get<Lookup[]>('/trades') });
  const form = useForm<V>({ initialValues: blank });
  const { data: meters } = useQuery({ queryKey: ['meters', form.values.assetId ?? 'none'], queryFn: () => get<MeterItem[]>('/meters', { assetId: form.values.assetId ?? undefined }), enabled: !!form.values.assetId });

  useEffect(() => {
    if (s) {
      form.setValues({
        code: s.code, name: s.name, description: s.description ?? '', siteId: s.siteId, locationId: s.locationId ?? null, assetId: s.assetId ?? null, tradeId: s.tradeId ?? null,
        priority: s.priority, assigneeIds: s.assigneeIds, pmTaskId: s.pmTaskId, extraTasks: s.extraTasks.map((x) => ({ pmTaskId: x.pmTaskId, everyNth: x.everyNth })), type: s.type,
        basis: s.basis, frequency: s.frequency, unit: s.unit, weekOfMonth: s.weekOfMonth ? String(s.weekOfMonth) : '1', dayOfWeek: s.dayOfWeek ?? 'Monday', dayOfMonth: s.dayOfMonth ?? 1,
        workPeriodDays: s.workPeriodDays, wholeMonth: s.wholeMonth, leadDays: s.leadDays, startDate: d(s.startDate), nextDueDate: d(s.nextDueDate), meterId: s.meterId ?? null,
        meterTrigger: s.meterTrigger ?? 'Incremental', meterInterval: s.meterInterval ?? '', nextDueReading: s.nextDueReading ?? '', lowerLimit: s.lowerLimit ?? '', upperLimit: s.upperLimit ?? '',
        lowerTaskId: s.lowerTaskId ?? null, upperTaskId: s.upperTaskId ?? null, timeBased: s.timeBased, isActive: s.isActive,
      });
      form.resetDirty();
    } else if (asset) form.setValues({ ...blank, siteId: asset.siteId, locationId: asset.locationId ?? null, assetId: asset.id, name: `PM · ${asset.name}` });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [s, asset]);

  const v = form.values;
  const calendar = v.type !== 'Meter' || v.timeBased;
  const [debounced] = useDebouncedValue(JSON.stringify(toBody(v)), 400);
  const draft = JSON.parse(debounced) as { siteId?: string | null; pmTaskId?: string | null };
  const { data: sim } = useQuery({
    // Gate on the debounced payload itself so a stale draft (e.g. before the task was picked) is never sent.
    queryKey: ['pm-simulate', debounced], enabled: !!draft.siteId && !!draft.pmTaskId && calendar, retry: false,
    queryFn: () => post<{ schedule: string; upcoming: string[]; dueDates: string[] }>('/pm-schedules/simulate', { ...JSON.parse(debounced), name: v.name || 'x', nextDueDate: editing ? toIsoDate(v.nextDueDate) : null }),
  });
  const taskOpts = useMemo(() => (tasks ?? []).filter((x) => x.isActive || [v.pmTaskId, v.lowerTaskId, v.upperTaskId].includes(x.id)).map((x) => ({ value: x.id, label: `${x.code} · ${x.name}` })), [tasks, v.pmTaskId, v.lowerTaskId, v.upperTaskId]);

  const submit = form.onSubmit(async (vals) => {
    try {
      const r = editing ? await put<{ id: string }>(`/pm-schedules/${id}`, toBody(vals, s?.rowVersion)) : await post<{ id: string; code: string }>('/pm-schedules', toBody(vals));
      notifySuccess(t('common.saved'));
      await qc.invalidateQueries({ queryKey: ['pm-schedules'] }); await qc.invalidateQueries({ queryKey: ['pm-schedule'] });
      if (!editing) navigate(`/pm/schedules/${r.id}`, { replace: true });
    } catch (e) { handleFormError(form, e); }
  });
  if (editing && isLoading) return <Center h={300}><Loader /></Center>;
  const err = (k: string) => form.errors[k];

  return (
    <form onSubmit={submit}>
      <PageHeader title={editing ? `${s?.code} · ${s?.name}` : t('pm.newSchedule')} crumbs={[{ label: t('pm.title'), to: '/pm/schedules' }, { label: editing ? s?.code ?? '' : t('pm.newSchedule') }]}
        badge={editing && <Badge color={s?.isActive ? 'teal' : 'gray'}>{s?.isActive ? t('common.active') : t('common.inactive')}</Badge>}
        subtitle={editing && s ? `${s.schedule} · ${t('pm.generatedCount', { count: s.generationCount })}` : undefined}
        actions={editable && <>
          {editing && <Button variant="subtle" color="red" onClick={() => confirmDelete(s!.code, async () => { try { await del(`/pm-schedules/${id}`); await qc.invalidateQueries({ queryKey: ['pm-schedules'] }); navigate('/pm/schedules'); } catch (e) { notifyError(e); } })}>{t('common.delete')}</Button>}
          <Button variant="default" onClick={() => navigate(-1)}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{editing ? t('common.saveChanges') : t('common.create')}</Button>
        </>} />
      <Grid gutter="lg">
        <Grid.Col span={{ base: 12, lg: 8 }}>
          <Stack gap="lg">
            <Card><Stack gap="sm">
              <SimpleGrid cols={{ base: 1, sm: 3 }}>
                <TextInput label={t('pm.code')} placeholder="Auto" {...form.getInputProps('code')} />
                <TextInput label={t('common.name')} required style={{ gridColumn: 'span 2' }} {...form.getInputProps('name')} />
              </SimpleGrid>
              <SimpleGrid cols={{ base: 1, sm: 3 }}>
                <Select label={t('pm.task')} required searchable data={taskOpts} {...form.getInputProps('pmTaskId')} />
                <Select label={t('wo.trade')} clearable data={(trades ?? []).map((x) => ({ value: x.id, label: x.name }))} {...form.getInputProps('tradeId')} />
                <Select label={t('priority.label')} data={WORK_PRIORITIES.map((x) => ({ value: x, label: t(`priority.${x}`) }))} {...form.getInputProps('priority')} />
              </SimpleGrid>
              <Textarea label={t('common.description')} autosize {...form.getInputProps('description')} />
            </Stack></Card>
            <Card><Stack gap="sm">
              <Title order={5}>{t('pm.target')}</Title>
              <LocationPicker siteId={v.siteId} locationId={v.locationId} siteError={err('siteId')} locationError={err('locationId')} onChange={(si, l) => form.setValues({ siteId: si, locationId: l, assetId: si === v.siteId ? v.assetId : null })} />
              <AssetPicker label={t('wo.asset')} value={v.assetId} onChange={(x) => form.setValues({ assetId: x, meterId: null })} error={err('assetId')} initial={s?.asset ?? (asset ? { id: asset.id, assetNumber: asset.assetNumber, name: asset.name } : null)} />
              <EmployeePicker label={t('wo.assignees')} value={v.assigneeIds} onChange={(x) => form.setFieldValue('assigneeIds', x)} siteId={v.siteId} tradeId={v.tradeId} />
            </Stack></Card>
            <Card><Stack gap="sm">
              <Title order={5}>{t('pm.type')}</Title>
              <Radio.Group value={v.type} onChange={(x) => form.setValues({ type: x, basis: x === 'DayOfWeekInMonth' || x === 'DateOfMonth' ? 'Scheduled' : v.basis })}>
                <SimpleGrid cols={{ base: 1, sm: 2 }}>
                  {PM_TYPES.map((x) => <Radio.Card key={x} value={x} p="sm" radius="md" disabled={editing && (s?.generationCount ?? 0) > 0 && x !== s?.type}>
                    <Group wrap="nowrap" align="flex-start"><Radio.Indicator /><div><Text fw={500} fz="sm">{t(`pm.types.${x}`)}</Text><Text fz="xs" c="dimmed">{t(`pm.typeHints.${x}`)}</Text></div></Group>
                  </Radio.Card>)}
                </SimpleGrid>
              </Radio.Group>
              {v.type === 'Meter' && (
                <>
                  <SimpleGrid cols={{ base: 1, sm: 2 }}>
                    <Select label={t('pm.meter')} required data={(meters ?? []).map((m) => ({ value: m.id, label: `${m.code} · ${m.name} (${m.unit})` }))} disabled={!v.assetId} error={err('meterId')}
                      {...form.getInputProps('meterId')} description={v.assetId && !meters?.length ? <Anchor component={Link} to={`/assets/${v.assetId}`} fz="xs">{t('pm.newMeter')}</Anchor> : undefined} />
                    <SegmentedControl value={v.meterTrigger} onChange={(x) => form.setFieldValue('meterTrigger', x)} data={[{ value: 'Incremental', label: t('pm.triggers.Incremental') }, { value: 'Threshold', label: t('pm.triggers.Threshold') }]} mt={24} />
                  </SimpleGrid>
                  {v.meterTrigger === 'Incremental' ? (
                    <SimpleGrid cols={{ base: 1, sm: 3 }}>
                      <NumberInput label={t('pm.meterInterval')} min={0} decimalScale={3} {...form.getInputProps('meterInterval')} />
                      <NumberInput label={t('pm.nextDueReading')} min={0} decimalScale={3} {...form.getInputProps('nextDueReading')} placeholder="current + interval" />
                      <Switch label={t('pm.timeBased')} mt={28} {...form.getInputProps('timeBased', { type: 'checkbox' })} />
                    </SimpleGrid>
                  ) : (
                    <SimpleGrid cols={{ base: 1, sm: 2 }}>
                      <NumberInput label={t('pm.lowerLimit')} decimalScale={3} {...form.getInputProps('lowerLimit')} />
                      <NumberInput label={t('pm.upperLimit')} decimalScale={3} {...form.getInputProps('upperLimit')} />
                      <Select label={t('pm.lowerTask')} clearable data={taskOpts} {...form.getInputProps('lowerTaskId')} />
                      <Select label={t('pm.upperTask')} clearable data={taskOpts} {...form.getInputProps('upperTaskId')} />
                    </SimpleGrid>
                  )}
                </>
              )}
              {calendar && (
                <>
                  {(v.type === 'FixedInterval' || v.type === 'Meter') && (
                    <SimpleGrid cols={{ base: 1, sm: 3 }}>
                      <NumberInput label={t('pm.every')} min={1} {...form.getInputProps('frequency')} />
                      <Select label={t('pm.unit')} data={FREQ_UNITS.map((x) => ({ value: x, label: t(`pm.units.${x}`) }))} {...form.getInputProps('unit')} />
                      <Select label={t('pm.basis')} data={['Scheduled', 'Actual'].map((x) => ({ value: x, label: t(`pm.bases.${x}`) }))} disabled={editing && (s?.generationCount ?? 0) > 0} {...form.getInputProps('basis')} />
                    </SimpleGrid>
                  )}
                  {v.type === 'FixedInterval' && v.basis === 'Actual' && <Text fz="xs" c="dimmed" mt={-6}>{t('pm.basisHint')}</Text>}
                  {v.type === 'DayOfWeekInMonth' && (
                    <SimpleGrid cols={{ base: 1, sm: 3 }}>
                      <Select label={t('pm.weekOfMonth')} data={['1', '2', '3', '4', '5'].map((x) => ({ value: x, label: t(`pm.weeks.${x}`) }))} {...form.getInputProps('weekOfMonth')} error={err('weekOfMonth')} />
                      <Select label={t('pm.dayOfWeek')} data={DAY_NAMES.map((x) => ({ value: x, label: t(`pm.days.${x}`) }))} {...form.getInputProps('dayOfWeek')} />
                      <NumberInput label={t('pm.everyMonths')} min={1} {...form.getInputProps('frequency')} />
                    </SimpleGrid>
                  )}
                  {v.type === 'DateOfMonth' && (
                    <SimpleGrid cols={{ base: 1, sm: 3 }}>
                      <NumberInput label={t('pm.dayOfMonth')} min={1} max={31} {...form.getInputProps('dayOfMonth')} />
                      <NumberInput label={t('pm.everyMonths')} min={1} {...form.getInputProps('frequency')} />
                      <Switch label={t('pm.wholeMonth')} mt={28} {...form.getInputProps('wholeMonth', { type: 'checkbox' })} />
                    </SimpleGrid>
                  )}
                  <SimpleGrid cols={{ base: 1, sm: 3 }}>
                    {editing && (s?.generationCount ?? 0) > 0
                      ? <DateInput label={t('pm.nextDueOverride')} clearable valueFormat="DD MMM YYYY" {...form.getInputProps('nextDueDate')} />
                      : <DateInput label={t('pm.startDate')} required valueFormat="DD MMM YYYY" {...form.getInputProps('startDate')} />}
                    <NumberInput label={t('pm.workPeriod')} min={1} disabled={v.type === 'DateOfMonth' && v.wholeMonth} {...form.getInputProps('workPeriodDays')} />
                    <NumberInput label={t('pm.leadDays')} min={0} max={90} {...form.getInputProps('leadDays')} />
                  </SimpleGrid>
                </>
              )}
            </Stack></Card>
            <Card><Stack gap="xs">
              <div><Title order={5}>{t('pm.multiple')}</Title><Text fz="sm" c="dimmed">{t('pm.multipleHint')}</Text></div>
              {v.extraTasks.map((_, i) => (
                <Group key={i} gap="xs" wrap="nowrap">
                  <Select style={{ flex: 1 }} data={taskOpts} {...form.getInputProps(`extraTasks.${i}.pmTaskId`)} />
                  <NumberInput w={140} min={2} prefix={`${t('pm.everyNth')}: `} {...form.getInputProps(`extraTasks.${i}.everyNth`)} />
                  <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => form.removeListItem('extraTasks', i)}><IconTrash size={16} /></ActionIcon>
                </Group>
              ))}
              {err('extraTasks') && <Text c="red" fz="sm">{err('extraTasks')}</Text>}
              <Button variant="subtle" w="fit-content" leftSection={<IconPlus size={14} />} onClick={() => form.insertListItem('extraTasks', { pmTaskId: null, everyNth: 4 })}>{t('pm.addMultiple')}</Button>
            </Stack></Card>
          </Stack>
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 4 }}>
          <Stack gap="lg">
            <Card>
              <Title order={5} mb="xs">{t('pm.preview')}</Title>
              {!calendar ? <Text fz="sm" c="dimmed">{s?.nextDueReading != null ? `${t('pm.nextDueReading')}: ${s.nextDueReading} ${s.meter?.unit ?? ''}` : t('pm.typeHints.Meter')}</Text>
                : !sim ? <Text fz="sm" c="dimmed">{t('pm.previewEmpty')}</Text> : (
                  <Stack gap={6}>
                    <Text fz="sm" fw={500}>{sim.schedule}</Text>
                    <Table verticalSpacing={4}><Table.Tbody>
                      {sim.upcoming.map((x, i) => <Table.Tr key={x}><Table.Td><Text fz="sm">{fmtDate(x)}</Text></Table.Td><Table.Td><Text fz="xs" c="dimmed">→ {fmtDate(sim.dueDates[i])}</Text></Table.Td></Table.Tr>)}
                    </Table.Tbody></Table>
                    {v.basis === 'Actual' && v.type === 'FixedInterval' && <Text fz="xs" c="dimmed">{t('pm.basisHint')}</Text>}
                  </Stack>
                )}
              <Checkbox mt="md" label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
            </Card>
            {editing && s && (
              <Card p={0}>
                <Title order={5} p="md" pb="xs">{t('pm.recentWos')}</Title>
                {s.recentWorkOrders.length === 0 ? <Text c="dimmed" fz="sm" px="md" pb="md">{t('common.noRecords')}</Text> : (
                  <Table verticalSpacing={6}><Table.Tbody>
                    {s.recentWorkOrders.map((w) => (
                      <Table.Tr key={w.id}><Table.Td><Anchor component={Link} to={`/work-orders/${w.id}`} fz="sm">{w.number}</Anchor></Table.Td>
                        <Table.Td><Text fz="xs" c="dimmed">{fmtDate(w.pmDueDate ?? w.completedAt)}</Text></Table.Td><Table.Td><WoStatusBadge status={w.status} /></Table.Td></Table.Tr>
                    ))}
                  </Table.Tbody></Table>
                )}
                {s.lastGeneratedAt && <Text fz="xs" c="dimmed" p="md" pt="xs">{fmtDateTime(s.lastGeneratedAt)}</Text>}
              </Card>
            )}
          </Stack>
        </Grid.Col>
      </Grid>
    </form>
  );
}
