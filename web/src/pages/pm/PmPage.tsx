import { useMemo, useState } from 'react';
import { ActionIcon, Alert, Anchor, Badge, Button, Card, Checkbox, Drawer, Group, Modal, NumberInput, Select, SimpleGrid, Stack, Switch, Table, Tabs, Text, TextInput, Textarea, Title } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { DataTable } from 'mantine-datatable';
import dayjs from 'dayjs';
import { IconArrowDown, IconArrowUp, IconCalendarPlus, IconGauge, IconPlus, IconSearch, IconTrash } from '@tabler/icons-react';
import { ApiError, get, post, put, del } from '@/api/client';
import type { ForecastItem, Lookup, MeterItem, MeterReadingItem, Paged, PmRunResult, PmScheduleListItem, PmTaskDetail, PmTaskListItem } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { AssetPicker } from '@/components/AssetPicker';
import { useHierarchy } from '@/components/LocationPicker';
import { confirmDelete } from '@/components/confirm';
import { fmtDate, fmtDateTime, fmtNumber, handleFormError, notifyError, notifySuccess, toIsoDate } from '@/components/utils';

const TABS = ['schedules', 'tasks', 'generate', 'forecast', 'meters'] as const;

export function PmPage() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const { tab = 'schedules' } = useParams();
  const navigate = useNavigate();
  const current = (TABS as readonly string[]).includes(tab) ? tab : 'schedules';
  return (
    <>
      <PageHeader title={t('pm.title')} subtitle={t('pm.subtitle')}
        actions={can(P.pmManage) && current === 'schedules' && <Button leftSection={<IconPlus size={16} />} component={Link} to="/pm/schedules/new">{t('pm.newSchedule')}</Button>} />
      <Tabs value={current} onChange={(v) => navigate(`/pm/${v}`)} keepMounted={false}>
        <Tabs.List mb="lg">{TABS.filter((x) => x !== 'generate' || can(P.pmGenerate)).map((x) => <Tabs.Tab key={x} value={x}>{t(`pm.${x}`)}</Tabs.Tab>)}</Tabs.List>
        <Tabs.Panel value="schedules"><SchedulesTab /></Tabs.Panel>
        <Tabs.Panel value="tasks"><TasksTab /></Tabs.Panel>
        <Tabs.Panel value="generate"><GenerateTab /></Tabs.Panel>
        <Tabs.Panel value="forecast"><ForecastTab /></Tabs.Panel>
        <Tabs.Panel value="meters"><MetersTab /></Tabs.Panel>
      </Tabs>
    </>
  );
}

function SchedulesTab() {
  const { t } = useTranslation();
  const { terms } = useAuth();
  const navigate = useNavigate();
  const [search, setSearch] = useState('');
  const [siteId, setSiteId] = useState<string | null>(null);
  const [activeOnly, setActiveOnly] = useState(true);
  const [page, setPage] = useState(1);
  const { data: h } = useHierarchy();
  const { data, isFetching } = useQuery({
    queryKey: ['pm-schedules', search, siteId, activeOnly, page], placeholderData: (prev) => prev,
    queryFn: () => get<Paged<PmScheduleListItem>>('/pm-schedules', { search, siteId, activeOnly, page, pageSize: 25, sort: 'nextDueDate' }),
  });
  return (
    <Card p={0}>
      <Group p="sm" gap="xs" wrap="wrap">
        <TextInput placeholder={t('common.search')} leftSection={<IconSearch size={16} />} value={search} onChange={(e) => { setSearch(e.currentTarget.value); setPage(1); }} w={{ base: '100%', sm: 260 }} />
        <Select placeholder={terms.site} data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} value={siteId} onChange={(v) => { setSiteId(v); setPage(1); }} clearable w={{ base: '100%', xs: 200 }} />
        <Switch label={t('pm.active')} checked={activeOnly} onChange={(e) => setActiveOnly(e.currentTarget.checked)} />
      </Group>
      <DataTable<PmScheduleListItem> minHeight={240} withTableBorder={false} highlightOnHover striped verticalSpacing="xs" fetching={isFetching} records={data?.items ?? []}
        noRecordsText={t('common.noRecords')} totalRecords={data?.total ?? 0} recordsPerPage={25} page={page} onPageChange={setPage}
        rowClassName={() => 'clickable-row'} onRowClick={({ record }) => navigate(`/pm/schedules/${record.id}`)}
        columns={[
          { accessor: 'code', title: t('pm.code'), noWrap: true, render: (s) => <Text fw={600} fz="sm" c={s.isActive ? 'brand' : 'dimmed'}>{s.code}</Text> },
          { accessor: 'name', title: t('common.name'), width: 260, render: (s) => <Stack gap={0}><Text fz="sm" lineClamp={1}>{s.name}</Text><Text fz="xs" c="dimmed" lineClamp={1}>{s.taskCode} · {s.taskName}{s.multipleTasks > 0 && ` +${s.multipleTasks}`}</Text></Stack> },
          { accessor: 'asset', title: t('pm.target'), width: 220, render: (s) => <Stack gap={0}><Text fz="sm" lineClamp={1}>{s.assetNumber ?? s.locationName}</Text><Text fz="xs" c="dimmed" lineClamp={1}>{s.assetName ?? s.siteName}</Text></Stack> },
          { accessor: 'schedule', title: t('pm.schedule'), render: (s) => <Stack gap={0}><Text fz="sm">{s.schedule}{s.type === 'Meter' && s.meterUnit ? ` ${s.meterUnit}` : ''}</Text><Text fz="xs" c="dimmed">{t(`pm.types.${s.type}`)}</Text></Stack> },
          { accessor: 'nextDueDate', title: t('pm.nextDue'), noWrap: true, render: (s) => (
            <Stack gap={2}>
              {s.nextDueDate ? <Text fz="sm" c={s.isOverdue ? 'red' : undefined} fw={s.isOverdue ? 600 : undefined}>{fmtDate(s.nextDueDate)}</Text> : s.basis === 'Actual' && s.openWorkOrders > 0 ? <Badge color="yellow" variant="light">{t('pm.waiting')}</Badge> : null}
              {s.type === 'Meter' && s.nextDueReading != null && <Text fz="xs" c="dimmed">@ {fmtNumber(s.nextDueReading)} {s.meterUnit} ({t('pm.current').toLowerCase()} {fmtNumber(s.meterReading)})</Text>}
            </Stack>) },
          { accessor: 'lastWorkOrderNumber', title: t('pm.lastWo'), noWrap: true, render: (s) => s.lastWorkOrderId ? <Anchor component={Link} to={`/work-orders/${s.lastWorkOrderId}`} fz="sm" onClick={(e) => e.stopPropagation()}>{s.lastWorkOrderNumber}</Anchor> : '—' },
          { accessor: 'isActive', title: t('common.status'), noWrap: true, render: (s) => <Badge color={s.isActive ? 'teal' : 'gray'}>{s.isActive ? t('common.active') : t('common.inactive')}</Badge> },
        ]} />
    </Card>
  );
}

function TasksTab() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const qc = useQueryClient();
  const editable = can(P.pmManage);
  const { data, isFetching } = useQuery({ queryKey: ['pm-tasks'], queryFn: () => get<PmTaskListItem[]>('/pm-tasks') });
  const { data: trades } = useQuery({ queryKey: ['trades'], queryFn: () => get<Lookup[]>('/trades') });
  const [opened, m] = useDisclosure();
  const [editing, setEditing] = useState<PmTaskDetail | null>(null);
  const [step, setStep] = useState('');
  const blank = { code: '', name: '', description: '', tradeId: null as string | null, estimatedHours: '' as number | '', safetyNotes: '', steps: [] as string[], isActive: true };
  const form = useForm({ initialValues: blank });
  const open = async (id: string | null) => {
    form.clearErrors(); setStep('');
    if (!id) { setEditing(null); form.setValues(blank); m.open(); return; }
    try {
      const x = await get<PmTaskDetail>(`/pm-tasks/${id}`);
      setEditing(x);
      form.setValues({ code: x.code, name: x.name, description: x.description ?? '', tradeId: x.tradeId ?? null, estimatedHours: x.estimatedHours ?? '', safetyNotes: x.safetyNotes ?? '', steps: x.steps, isActive: x.isActive });
      m.open();
    } catch (e) { notifyError(e); }
  };
  const addStep = () => { if (step.trim()) { form.insertListItem('steps', step.trim()); setStep(''); } };
  const move = (i: number, d: number) => { const s = [...form.values.steps]; const j = i + d; if (j < 0 || j >= s.length) return; [s[i], s[j]] = [s[j], s[i]]; form.setFieldValue('steps', s); };
  const submit = form.onSubmit(async (v) => {
    const body = { ...v, estimatedHours: v.estimatedHours === '' ? null : v.estimatedHours, rowVersion: editing?.rowVersion };
    try { if (editing) await put(`/pm-tasks/${editing.id}`, body); else await post('/pm-tasks', body); notifySuccess(t('common.saved')); await qc.invalidateQueries({ queryKey: ['pm-tasks'] }); m.close(); }
    catch (e) { handleFormError(form, e); }
  });
  return (
    <Card p={0}>
      <Group justify="space-between" p="md"><Title order={5}>{t('pm.tasks')}</Title>{editable && <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('pm.newTask')}</Button>}</Group>
      <DataTable minHeight={160} fetching={isFetching} records={data ?? []} noRecordsText={t('common.noRecords')} highlightOnHover rowClassName={() => 'clickable-row'} onRowClick={({ record }) => open(record.id)}
        columns={[
          { accessor: 'code', title: t('pm.taskCode'), noWrap: true, render: (x) => <Text fw={600} fz="sm">{x.code}</Text> },
          { accessor: 'name', title: t('common.name') },
          { accessor: 'stepCount', title: t('pm.steps'), textAlign: 'right' },
          { accessor: 'estimatedHours', title: t('pm.estimatedHours'), textAlign: 'right' },
          { accessor: 'scheduleCount', title: t('pm.schedules'), textAlign: 'right' },
          { accessor: 'isActive', title: t('common.status'), render: (x) => <Badge color={x.isActive ? 'teal' : 'gray'}>{x.isActive ? t('common.active') : t('common.inactive')}</Badge> },
        ]} />
      <Modal opened={opened} onClose={m.close} title={editing ? `${t('pm.editTask')} · ${editing.code}` : t('pm.newTask')} size="xl">
        <form onSubmit={submit}><Stack>
          <SimpleGrid cols={{ base: 1, sm: 3 }}>
            <TextInput label={t('pm.taskCode')} required disabled={!!editing} {...form.getInputProps('code')} />
            <TextInput label={t('common.name')} required style={{ gridColumn: 'span 2' }} {...form.getInputProps('name')} />
          </SimpleGrid>
          <SimpleGrid cols={2}>
            <Select label={t('wo.trade')} clearable data={(trades ?? []).map((x) => ({ value: x.id, label: x.name }))} {...form.getInputProps('tradeId')} />
            <NumberInput label={t('pm.estimatedHours')} min={0} decimalScale={2} {...form.getInputProps('estimatedHours')} />
          </SimpleGrid>
          <Textarea label={t('common.description')} autosize minRows={2} {...form.getInputProps('description')} />
          <Textarea label={t('pm.safetyNotes')} autosize minRows={2} {...form.getInputProps('safetyNotes')} />
          <Stack gap={4}>
            <Text fz="sm" fw={500}>{t('pm.steps')} ({form.values.steps.length})</Text>
            {form.values.steps.map((_, i) => (
              <Group key={i} gap={6} wrap="nowrap">
                <Text fz="sm" c="dimmed" w={28} ta="right">{i + 1}.</Text>
                <TextInput style={{ flex: 1 }} size="xs" {...form.getInputProps(`steps.${i}`)} />
                <ActionIcon variant="subtle" color="gray" size="sm" aria-label="Up" onClick={() => move(i, -1)}><IconArrowUp size={14} /></ActionIcon>
                <ActionIcon variant="subtle" color="gray" size="sm" aria-label="Down" onClick={() => move(i, 1)}><IconArrowDown size={14} /></ActionIcon>
                <ActionIcon variant="subtle" color="red" size="sm" aria-label={t('common.delete')} onClick={() => form.removeListItem('steps', i)}><IconTrash size={14} /></ActionIcon>
              </Group>
            ))}
            <Group gap="xs"><TextInput style={{ flex: 1 }} placeholder={t('pm.addStep')} value={step} onChange={(e) => setStep(e.currentTarget.value)} onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); addStep(); } }} />
              <Button variant="light" leftSection={<IconPlus size={14} />} onClick={addStep}>{t('common.create')}</Button></Group>
          </Stack>
          <Checkbox label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
          <Group justify="space-between">
            {editing && editable ? <Button variant="subtle" color="red" onClick={() => confirmDelete(editing.code, async () => { try { await del(`/pm-tasks/${editing.id}`); await qc.invalidateQueries({ queryKey: ['pm-tasks'] }); m.close(); } catch (e) { notifyError(e); } })}>{t('common.delete')}</Button> : <span />}
            <Group><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button>{editable && <Button type="submit" loading={form.submitting}>{t('common.save')}</Button>}</Group>
          </Group>
        </Stack></form>
      </Modal>
    </Card>
  );
}

function GenerateTab() {
  const { t } = useTranslation();
  const { me, terms } = useAuth();
  const qc = useQueryClient();
  const [until, setUntil] = useState<Date | null>(dayjs().add(7, 'day').toDate());
  const [siteId, setSiteId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const { data: h } = useHierarchy();
  const { data, isFetching, refetch } = useQuery({
    queryKey: ['pm-preview', toIsoDate(until), siteId], enabled: !!until,
    queryFn: () => get<PmRunResult[]>('/pm/preview', { until: toIsoDate(until), siteId }),
  });
  const due = (data ?? []).filter((r) => !r.skipped);
  const generate = async () => {
    setBusy(true);
    try {
      const r = await post<{ created: number; results: PmRunResult[] }>('/pm/generate', { until: toIsoDate(until), siteId });
      notifySuccess(t('pm.generated', { count: r.created }));
      await qc.invalidateQueries({ queryKey: ['work-orders'] }); await qc.invalidateQueries({ queryKey: ['pm-schedules'] }); await refetch();
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Stack>
      <Alert variant="light" color={me?.tenant.settings.autoGeneratePm ? 'teal' : 'gray'}>{t('pm.autoInfo', { state: t(me?.tenant.settings.autoGeneratePm ? 'pm.on' : 'pm.off') })}</Alert>
      <Card><Group align="flex-end" wrap="wrap">
        <DateInput label={t('pm.generateUntil')} value={until} onChange={(v) => setUntil(v ? new Date(v) : null)} valueFormat="DD MMM YYYY" minDate={new Date()} maxDate={dayjs().add(1, 'year').toDate()} w={200} />
        <Select label={terms.site} data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} value={siteId} onChange={setSiteId} clearable placeholder={t('common.all')} w={220} />
        <Button variant="default" onClick={() => refetch()} loading={isFetching}>{t('pm.viewPm')}</Button>
        <Button leftSection={<IconCalendarPlus size={16} />} disabled={due.length === 0} loading={busy} onClick={generate}>{t('pm.createWos')} ({due.length})</Button>
      </Group></Card>
      <Card p={0}>
        <Title order={5} p="md">{t('pm.previewTitle')}</Title>
        <DataTable<PmRunResult> minHeight={160} fetching={isFetching} records={data ?? []} idAccessor={(r) => `${r.scheduleId}-${r.dueDate ?? 'x'}`} noRecordsText={t('pm.nothingDue')}
          columns={[
            { accessor: 'dueDate', title: t('wo.plannedStart'), noWrap: true, render: (r) => fmtDate(r.dueDate) },
            { accessor: 'code', title: t('pm.code'), noWrap: true, render: (r) => <Anchor component={Link} to={`/pm/schedules/${r.scheduleId}`} fz="sm">{r.code}</Anchor> },
            { accessor: 'name', title: t('common.name') }, { accessor: 'taskName', title: t('pm.task') }, { accessor: 'assetNumber', title: t('wo.asset') },
            { accessor: 'skipped', title: t('common.status'), render: (r) => r.skipped ? <Badge color="yellow" variant="light">{r.skipped}</Badge> : <Badge color="blue" variant="light">{t('pm.nextDue')}</Badge> },
          ]} />
      </Card>
    </Stack>
  );
}

function ForecastTab() {
  const { t } = useTranslation();
  const { terms } = useAuth();
  const [from, setFrom] = useState<Date | null>(new Date());
  const [to, setTo] = useState<Date | null>(dayjs().add(3, 'month').toDate());
  const [siteId, setSiteId] = useState<string | null>(null);
  const { data: h } = useHierarchy();
  const { data, isFetching } = useQuery({
    queryKey: ['pm-forecast', toIsoDate(from), toIsoDate(to), siteId], enabled: !!from && !!to,
    queryFn: () => get<ForecastItem[]>('/pm/forecast', { from: toIsoDate(from), to: toIsoDate(to), siteId }),
  });
  const weeks = useMemo(() => {
    const groups = new Map<string, ForecastItem[]>();
    for (const x of data ?? []) { const k = x.overdue ? 'overdue' : dayjs(x.date).startOf('week').add(1, 'day').format('YYYY-MM-DD'); groups.set(k, [...(groups.get(k) ?? []), x]); }
    return [...groups.entries()];
  }, [data]);
  return (
    <Stack>
      <Card><Group align="flex-end" wrap="wrap">
        <DateInput label={t('pm.from')} value={from} onChange={(v) => setFrom(v ? new Date(v) : null)} valueFormat="DD MMM YYYY" w={180} />
        <DateInput label={t('pm.to')} value={to} onChange={(v) => setTo(v ? new Date(v) : null)} valueFormat="DD MMM YYYY" w={180} />
        <Select label={terms.site} data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} value={siteId} onChange={setSiteId} clearable placeholder={t('common.all')} w={220} />
        <Text c="dimmed" fz="sm">{isFetching ? t('common.loading') : `${data?.length ?? 0} ${t('pm.schedules').toLowerCase()}`}</Text>
      </Group></Card>
      {weeks.length === 0 && !isFetching && <Card><Text c="dimmed" ta="center">{t('pm.nothingDue')}</Text></Card>}
      {weeks.map(([week, items]) => (
        <Card key={week} p={0}>
          <Group justify="space-between" px="md" py="xs" style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}>
            <Text fw={600} c={week === 'overdue' ? 'red' : undefined}>{week === 'overdue' ? t('pm.overdue') : `${fmtDate(week)} – ${fmtDate(dayjs(week).add(6, 'day').format('YYYY-MM-DD'))}`}</Text>
            <Badge variant="default">{items.length}</Badge>
          </Group>
          <Table verticalSpacing={6}><Table.Tbody>
            {items.map((x, i) => (
              <Table.Tr key={`${x.id}-${x.date}-${i}`}>
                <Table.Td w={120}><Text fz="sm" c={x.overdue ? 'red' : undefined}>{dayjs(x.date).format('ddd DD MMM')}</Text></Table.Td>
                <Table.Td w={120}><Anchor component={Link} to={`/pm/schedules/${x.id}`} fz="sm">{x.code}</Anchor></Table.Td>
                <Table.Td><Text fz="sm" lineClamp={1}>{x.name}</Text></Table.Td>
                <Table.Td visibleFrom="sm"><Text fz="sm" c="dimmed" lineClamp={1}>{x.taskName}</Text></Table.Td>
                <Table.Td visibleFrom="md"><Text fz="sm" c="dimmed">{x.assetNumber ?? x.siteName}</Text></Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody></Table>
        </Card>
      ))}
    </Stack>
  );
}

export function MetersTab({ assetId }: { assetId?: string }) {
  const { t } = useTranslation();
  const { can } = useAuth();
  const qc = useQueryClient();
  const { data, isFetching } = useQuery({ queryKey: ['meters', assetId ?? 'all'], queryFn: () => get<MeterItem[]>('/meters', { assetId }) });
  const [opened, m] = useDisclosure();
  const [editing, setEditing] = useState<MeterItem | null>(null);
  const [reading, setReading] = useState<MeterItem | null>(null);
  const form = useForm({ initialValues: { code: '', name: '', assetId: (assetId ?? null) as string | null, unit: 'h', maxDailyIncrement: '' as number | '', isActive: true } });
  const open = (x: MeterItem | null) => {
    setEditing(x); form.clearErrors();
    form.setValues(x ? { code: x.code, name: x.name, assetId: x.assetId, unit: x.unit, maxDailyIncrement: x.maxDailyIncrement ?? '', isActive: x.isActive } : { code: '', name: '', assetId: assetId ?? null, unit: 'h', maxDailyIncrement: '', isActive: true });
    m.open();
  };
  const submit = form.onSubmit(async (v) => {
    const body = { ...v, maxDailyIncrement: v.maxDailyIncrement === '' ? null : v.maxDailyIncrement };
    try { if (editing) await put(`/meters/${editing.id}`, body); else await post('/meters', body); await qc.invalidateQueries({ queryKey: ['meters'] }); m.close(); } catch (e) { handleFormError(form, e); }
  });
  return (
    <Card p={0}>
      <Group justify="space-between" p="md"><Title order={5}>{t('pm.meters')}</Title>{can(P.pmManage) && <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('pm.newMeter')}</Button>}</Group>
      <DataTable<MeterItem> minHeight={140} fetching={isFetching} records={data ?? []} noRecordsText={t('common.noRecords')}
        columns={[
          { accessor: 'code', title: t('pm.meterCode'), noWrap: true, render: (x) => can(P.pmManage) ? <Anchor fz="sm" onClick={() => open(x)}>{x.code}</Anchor> : x.code },
          { accessor: 'name', title: t('common.name') },
          ...(assetId ? [] : [{ accessor: 'assetNumber', title: t('wo.asset'), render: (x: MeterItem) => <Anchor component={Link} to={`/assets/${x.assetId}`} fz="sm">{x.assetNumber}</Anchor> }]),
          { accessor: 'currentReading', title: t('pm.current'), textAlign: 'right', render: (x) => x.currentReading != null ? `${fmtNumber(x.currentReading)} ${x.unit}` : '—' },
          { accessor: 'lastReadAt', title: t('pm.lastRead'), noWrap: true, render: (x) => fmtDateTime(x.lastReadAt) },
          { accessor: 'schedules', title: t('pm.schedules'), textAlign: 'right' },
          { accessor: 'actions', title: '', textAlign: 'right', render: (x) => <Button size="compact-xs" variant="light" leftSection={<IconGauge size={12} />} onClick={() => setReading(x)}>{can(P.metersRecord) ? t('pm.logReading') : t('pm.readings')}</Button> },
        ]} />
      <Modal opened={opened} onClose={m.close} title={editing ? editing.code : t('pm.newMeter')}>
        <form onSubmit={submit}><Stack>
          <SimpleGrid cols={2}><TextInput label={t('pm.meterCode')} required {...form.getInputProps('code')} /><TextInput label={t('pm.unitLabel')} required placeholder="h, km, cycles, °C" {...form.getInputProps('unit')} /></SimpleGrid>
          <TextInput label={t('common.name')} required {...form.getInputProps('name')} />
          {!assetId && <AssetPicker label={t('wo.asset')} value={form.values.assetId} onChange={(v) => form.setFieldValue('assetId', v)} error={form.errors.assetId}
            initial={editing ? { id: editing.assetId, assetNumber: editing.assetNumber, name: editing.assetName } : null} />}
          <NumberInput label={t('pm.maxDaily')} min={0} decimalScale={3} {...form.getInputProps('maxDailyIncrement')} />
          <Checkbox label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack></form>
      </Modal>
      <ReadingDrawer meter={reading} onClose={() => setReading(null)} />
    </Card>
  );
}

function ReadingDrawer({ meter, onClose }: { meter: MeterItem | null; onClose: () => void }) {
  const { t } = useTranslation();
  const { can } = useAuth();
  const qc = useQueryClient();
  const [needsConfirm, setNeedsConfirm] = useState(false);
  const form = useForm({ initialValues: { value: '' as number | '', readAt: new Date() as Date | null, note: '' } });
  const { data } = useQuery({ queryKey: ['meter-readings', meter?.id], queryFn: () => get<MeterReadingItem[]>(`/meters/${meter!.id}/readings`), enabled: !!meter });
  const submit = form.onSubmit(async (v) => {
    try {
      const r = await post<{ generated: PmRunResult[] }>(`/meters/${meter!.id}/readings`, { ...v, value: v.value === '' ? null : v.value, readAt: v.readAt ? dayjs(v.readAt).toISOString() : null, confirm: needsConfirm });
      const created = r.generated.filter((x) => x.workOrderNumber).map((x) => x.workOrderNumber);
      notifySuccess(created.length ? t('pm.readingTriggered', { list: created.join(', ') }) : t('pm.readingSaved'));
      setNeedsConfirm(false); form.setValues({ value: '', readAt: new Date(), note: '' });
      await qc.invalidateQueries({ queryKey: ['meters'] }); await qc.invalidateQueries({ queryKey: ['meter-readings', meter?.id] }); await qc.invalidateQueries({ queryKey: ['pm-schedules'] });
    } catch (e) {
      if (e instanceof ApiError && e.fieldErrors.value) setNeedsConfirm(true);
      handleFormError(form, e);
    }
  });
  return (
    <Drawer opened={!!meter} onClose={() => { setNeedsConfirm(false); onClose(); }} position="right" size="md" title={meter ? `${meter.code} · ${meter.name}` : ''}>
      {meter && <Stack>
        <Group><Text c="dimmed" fz="sm">{t('pm.current')}:</Text><Text fw={600}>{meter.currentReading != null ? `${fmtNumber(meter.currentReading)} ${meter.unit}` : '—'}</Text></Group>
        {can(P.metersRecord) && (
          <form onSubmit={submit}><Stack gap="xs">
            <SimpleGrid cols={2}>
              <NumberInput label={`${t('pm.reading')} (${meter.unit})`} required decimalScale={3} {...form.getInputProps('value')} onChange={(v) => { setNeedsConfirm(false); form.setFieldValue('value', v === '' ? '' : Number(v)); }} />
              <DateInput label={t('wo.date')} valueFormat="DD MMM YYYY" maxDate={new Date()} {...form.getInputProps('readAt')} />
            </SimpleGrid>
            <TextInput label={t('common.notes')} {...form.getInputProps('note')} />
            <Button type="submit" color={needsConfirm ? 'orange' : undefined} loading={form.submitting}>{needsConfirm ? t('pm.confirmReading') : t('pm.logReading')}</Button>
          </Stack></form>
        )}
        <Title order={6} mt="md">{t('pm.readings')}</Title>
        <Table verticalSpacing={4}><Table.Tbody>
          {(data ?? []).map((r) => (
            <Table.Tr key={r.id}><Table.Td>{fmtDateTime(r.readAt)}</Table.Td><Table.Td ta="right" fw={500}>{fmtNumber(r.value)} {meter.unit}</Table.Td><Table.Td><Text fz="xs" c="dimmed">{r.byName}{r.note && ` · ${r.note}`}</Text></Table.Td></Table.Tr>
          ))}
        </Table.Tbody></Table>
      </Stack>}
    </Drawer>
  );
}
