import { useEffect, useMemo, useState } from 'react';
import { ActionIcon, Anchor, Badge, Button, Card, Center, Checkbox, Grid, Group, Loader, NumberInput, Radio, SegmentedControl, Select, SimpleGrid, Stack, Switch, Table, Text, TextInput, Textarea, Title, Tooltip } from '@mantine/core';
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
import { AssetListEditor, type AssetRef } from '@/components/AssetListEditor';
import { EmployeePicker } from '@/components/EmployeePicker';
import { teamOptions, useTeams } from '@/components/teams';
import { WoStatusBadge } from '@/components/WorkBadges';
import { confirmAction, confirmDelete } from '@/components/confirm';
import { MultiAssetCard, PeopleRulesCard, TriggersCard } from './PmScheduleExtras';
import { OccurrencesDrawer } from './OccurrencesDrawer';
import { generateNow } from './generateNow';
import { fmtDate, fmtDateTime, fmtMoney, handleFormError, notifyError, notifySuccess, notifyWarning, toIsoDate } from '@/components/utils';

export type V = {
  code: string; name: string; description: string; siteId: string | null; locationId: string | null; assetId: string | null; tradeId: string | null; teamId: string | null; priority: string;
  assigneeIds: string[]; pmTaskId: string | null; extraTasks: { pmTaskId: string | null; everyNth: number | '' }[]; type: string; basis: string; frequency: number | '';
  unit: string; weekOfMonth: string | null; dayOfWeek: string | null; dayOfMonth: number | ''; workPeriodDays: number | ''; wholeMonth: boolean; leadDays: number | '';
  startDate: Date | null; nextDueDate: Date | null; meterId: string | null; meterTrigger: string; meterInterval: number | ''; nextDueReading: number | '';
  lowerLimit: number | ''; upperLimit: number | ''; lowerTaskId: string | null; upperTaskId: string | null; timeBased: boolean; isActive: boolean;
  /** What the schedule maintains: one asset, a location, or several assets (a round). */
  target: Target; roundAssets: AssetRef[];
  /** Only while the asset is stopped (0.10.0); null on a new schedule = take it from the asset. */
  onlyWhenStopped: boolean | null;
  /** 0.12.0: shared calendars and extra meters, recipients, the PM's own rules, multi-asset mode, spread and each asset's template. */
  calendarIds: string[]; extraMeters: { meterId: string | null; interval: number | '' }[]; recipientIds: string[];
  useOrgRules: boolean; skipStatusesOverride: string[]; nonWorkingDaysOverride: string[]; noStackingOverride: boolean;
  multiAssetMode: 'Round' | 'Group'; groupPlan: 'Copy' | 'Split'; spreadParts: number | ''; assetTemplates: Record<string, string | null>;
};
type Target = 'asset' | 'location' | 'several';
const blank: V = {
  code: '', name: '', description: '', siteId: null, locationId: null, assetId: null, tradeId: null, teamId: null, priority: 'Medium', assigneeIds: [], pmTaskId: null, extraTasks: [],
  type: 'FixedInterval', basis: 'Scheduled', frequency: 1, unit: 'Month', weekOfMonth: '1', dayOfWeek: 'Monday', dayOfMonth: 1, workPeriodDays: 1, wholeMonth: false, leadDays: 7,
  startDate: new Date(), nextDueDate: null, meterId: null, meterTrigger: 'Incremental', meterInterval: '', nextDueReading: '', lowerLimit: '', upperLimit: '', lowerTaskId: null,
  upperTaskId: null, timeBased: false, isActive: true, target: 'asset', roundAssets: [], onlyWhenStopped: null,
  calendarIds: [], extraMeters: [], recipientIds: [], useOrgRules: true, skipStatusesOverride: [], nonWorkingDaysOverride: [], noStackingOverride: false,
  multiAssetMode: 'Round', groupPlan: 'Copy', spreadParts: '', assetTemplates: {},
};
const d = (v?: string | null) => (v ? new Date(`${v}T00:00:00`) : null);
const n = (v: number | '') => (v === '' ? null : v);

function toBody({ target, roundAssets, useOrgRules, assetTemplates, ...v }: V, rowVersion?: string) {
  const several = target === 'several';
  return {
    ...v, assetId: target === 'asset' ? v.assetId : null, assetIds: target === 'several' ? roundAssets.map((a) => a.id) : [], frequency: n(v.frequency) ?? 1, dayOfMonth: n(v.dayOfMonth), workPeriodDays: n(v.workPeriodDays) ?? 1, leadDays: n(v.leadDays) ?? 0,
    weekOfMonth: v.weekOfMonth ? Number(v.weekOfMonth) : null, startDate: toIsoDate(v.startDate) ?? toIsoDate(new Date()), nextDueDate: toIsoDate(v.nextDueDate),
    meterInterval: n(v.meterInterval), nextDueReading: n(v.nextDueReading), lowerLimit: n(v.lowerLimit), upperLimit: n(v.upperLimit),
    extraTasks: v.extraTasks.filter((x) => x.pmTaskId).map((x) => ({ pmTaskId: x.pmTaskId, everyNth: n(x.everyNth) ?? 2 })), rowVersion,
    managesTeam: true, // this form shows the team: an empty one clears it
    extraMeters: target === 'asset' ? v.extraMeters.filter((m) => m.meterId).map((m) => ({ meterId: m.meterId, interval: n(m.interval) ?? 0 })) : [],
    managesRules: true, skipStatusesOverride: useOrgRules ? null : v.skipStatusesOverride, nonWorkingDaysOverride: useOrgRules ? null : v.nonWorkingDaysOverride,
    noStackingOverride: useOrgRules ? null : v.noStackingOverride,
    multiAssetMode: several ? v.multiAssetMode : 'Round', groupPlan: v.groupPlan, spreadParts: several ? n(v.spreadParts) ?? 0 : 0,
    assetTemplates: several ? roundAssets.map((a) => ({ assetId: a.id, pmTaskId: assetTemplates[a.id] ?? null })) : [],
  };
}

export function PmScheduleFormPage() {
  const { t } = useTranslation();
  const { id } = useParams();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { can, me } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const editing = !!id;
  const editable = can(P.pmManage);
  const { data: s, isLoading } = useQuery({ queryKey: ['pm-schedule', id], queryFn: () => get<PmScheduleDetail>(`/pm-schedules/${id}`), enabled: editing });
  const canGenerateNext = editing && can(P.pmGenerate) && !!s?.isActive && !!s?.nextDueDate;
  const assetParam = params.get('assetId');
  const { data: asset } = useQuery({ queryKey: ['asset', assetParam], queryFn: () => get<AssetDetail>(`/assets/${assetParam}`), enabled: !!assetParam && !editing });
  const { data: tasks } = useQuery({ queryKey: ['pm-tasks'], queryFn: () => get<PmTaskListItem[]>('/pm-tasks') });
  const { data: trades } = useQuery({ queryKey: ['trades'], queryFn: () => get<Lookup[]>('/trades') });
  const form = useForm<V>({ initialValues: blank });
  const { data: teams } = useTeams(form.values.siteId);
  const { data: meters } = useQuery({ queryKey: ['meters', form.values.assetId ?? 'none'], queryFn: () => get<MeterItem[]>('/meters', { assetId: form.values.assetId ?? undefined }), enabled: !!form.values.assetId });

  useEffect(() => {
    if (s) {
      const saved: V = {
        ...blank,
        code: s.code, name: s.name, description: s.description ?? '', siteId: s.siteId, locationId: s.locationId ?? null, assetId: s.assetId ?? null, tradeId: s.tradeId ?? null, teamId: s.teamId ?? null,
        priority: s.priority, assigneeIds: s.assigneeIds, pmTaskId: s.pmTaskId, extraTasks: s.extraTasks.map((x) => ({ pmTaskId: x.pmTaskId, everyNth: x.everyNth })), type: s.type,
        basis: s.basis, frequency: s.frequency, unit: s.unit, weekOfMonth: s.weekOfMonth ? String(s.weekOfMonth) : '1', dayOfWeek: s.dayOfWeek ?? 'Monday', dayOfMonth: s.dayOfMonth ?? 1,
        workPeriodDays: s.workPeriodDays, wholeMonth: s.wholeMonth, leadDays: s.leadDays, startDate: d(s.startDate), nextDueDate: d(s.nextDueDate), meterId: s.meterId ?? null,
        meterTrigger: s.meterTrigger ?? 'Incremental', meterInterval: s.meterInterval ?? '', nextDueReading: s.nextDueReading ?? '', lowerLimit: s.lowerLimit ?? '', upperLimit: s.upperLimit ?? '',
        lowerTaskId: s.lowerTaskId ?? null, upperTaskId: s.upperTaskId ?? null, timeBased: s.timeBased, isActive: s.isActive,
        target: s.assets.length > 0 ? 'several' : s.assetId ? 'asset' : 'location', roundAssets: s.assets, onlyWhenStopped: s.onlyWhenStopped ?? false,
        calendarIds: s.calendarIds ?? [], extraMeters: (s.extraMeters ?? []).map((m) => ({ meterId: m.meterId, interval: m.interval })), recipientIds: s.recipientIds ?? [],
        useOrgRules: s.skipStatusesOverride == null && s.nonWorkingDaysOverride == null && s.noStackingOverride == null,
        skipStatusesOverride: s.skipStatusesOverride ?? [], nonWorkingDaysOverride: s.nonWorkingDaysOverride ?? [], noStackingOverride: !!s.noStackingOverride,
        multiAssetMode: s.multiAssetMode ?? 'Round', groupPlan: s.groupPlan ?? 'Copy', spreadParts: s.spreadParts ?? '',
        assetTemplates: Object.fromEntries(s.assets.map((a) => [a.id, a.pmTaskId ?? null])),
      };
      form.setValues(saved);
      form.resetDirty(saved); // the saved schedule is the baseline: "unsaved changes" means edits since
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

  const [occurrences, setOccurrences] = useState(false);
  // "Make a copy for this PM" (0.12.0): later edits to the shared template no longer reach this PM.
  const ownTemplate = () => confirmAction(t('pm12.ownTemplateConfirm'), async () => {
    try {
      const r = await post<{ pmTaskId: string; code: string }>(`/pm-schedules/${id}/own-template`);
      notifySuccess(t('pm12.ownTemplateDone', { code: r.code }));
      await qc.invalidateQueries({ queryKey: ['pm-schedule', id] }); await qc.invalidateQueries({ queryKey: ['pm-tasks'] });
    } catch (e) { notifyError(e); }
  });
  const submit = form.onSubmit(async (vals) => {
    try {
      const r = editing ? await put<{ id: string; warnings?: string[] }>(`/pm-schedules/${id}`, toBody(vals, s?.rowVersion))
        : await post<{ id: string; code: string; warnings?: string[] }>('/pm-schedules', toBody(vals));
      notifySuccess(t('common.saved'));
      for (const w of r.warnings ?? []) notifyWarning(w);
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
        actions={(editable || canGenerateNext) && <>
          {/* Generating reloads the schedule, which would replace edits not saved yet: save them first. */}
          {canGenerateNext && <Tooltip label={t('pm.saveFirst')} disabled={!form.isDirty()}>
            <Button variant="light" disabled={form.isDirty()} onClick={() => generateNow(s!.id, s!.nextDueDate!, () => Promise.all([
              qc.invalidateQueries({ queryKey: ['pm-schedule', id] }), qc.invalidateQueries({ queryKey: ['pm-forecast'] }), qc.invalidateQueries({ queryKey: ['work-orders'] })]))}>
              {t('pm.generateNextNow')}</Button>
          </Tooltip>}
          {editing && <Button variant="light" onClick={() => setOccurrences(true)}>{t('pm12.occurrences')}</Button>}
          {editable && editing && <Button variant="subtle" onClick={ownTemplate}>{t('pm12.ownTemplate')}</Button>}
          {editable && editing && <Button variant="subtle" color="red" onClick={() => confirmDelete(s!.code, async () => { try { await del(`/pm-schedules/${id}`); await qc.invalidateQueries({ queryKey: ['pm-schedules'] }); navigate('/pm/schedules'); } catch (e) { notifyError(e); } })}>{t('common.delete')}</Button>}
          {editable && <><Button variant="default" onClick={() => navigate(-1)}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{editing ? t('common.saveChanges') : t('common.create')}</Button></>}
        </>} />
      <Grid gutter="lg">
        <Grid.Col span={{ base: 12, lg: 8 }}>
          <Stack gap="lg">
            <Card><Stack gap="sm">
              <SimpleGrid cols={{ base: 1, sm: 3 }}>
                <TextInput label={t('pm.code')} placeholder={t('pm.autoPlaceholder')} {...form.getInputProps('code')} />
                <TextInput label={t('common.name')} required style={{ gridColumn: 'span 2' }} {...form.getInputProps('name')} />
              </SimpleGrid>
              <SimpleGrid cols={{ base: 1, sm: 3 }}>
                <Select label={t('pm.task')} required searchable data={taskOpts} {...form.getInputProps('pmTaskId')} />
                <Select label={t('wo.trade')} clearable data={(trades ?? []).map((x) => ({ value: x.id, label: x.name }))} {...form.getInputProps('tradeId')} />
                {(teams?.length ?? 0) > 0 && <Select label={t('teams.team')} clearable data={teamOptions(teams, form.values.teamId)} {...form.getInputProps('teamId')} />}
                <Select label={t('priority.label')} data={WORK_PRIORITIES.map((x) => ({ value: x, label: t(`priority.${x}`) }))} {...form.getInputProps('priority')} />
                <Checkbox label={t('accessWindows.onlyWhenStopped')} description={v.onlyWhenStopped === null ? t('accessWindows.fromAsset') : undefined} mt="sm"
                  checked={!!v.onlyWhenStopped} onChange={(e) => form.setFieldValue('onlyWhenStopped', e.currentTarget.checked)} />
              </SimpleGrid>
              <Textarea label={t('common.description')} autosize {...form.getInputProps('description')} />
            </Stack></Card>
            <Card><Stack gap="sm">
              <Group justify="space-between" wrap="wrap" gap="xs">
                <Title order={5}>{t('pm.target')}</Title>
                <SegmentedControl size="xs" value={v.target} onChange={(x) => form.setValues({ target: x as Target, type: x === 'several' && v.type === 'Meter' ? 'FixedInterval' : v.type, meterId: x === 'asset' ? v.meterId : null })}
                  data={[{ value: 'asset', label: t('pm.targetAsset') }, { value: 'location', label: t('pm.targetLocation') }, { value: 'several', label: t('pm.targetSeveral') }]} />
              </Group>
              <LocationPicker siteId={v.siteId} locationId={v.locationId} siteError={err('siteId')} locationError={err('locationId')} onChange={(si, l) => form.setValues({ siteId: si, locationId: l, assetId: si === v.siteId ? v.assetId : null, roundAssets: si === v.siteId ? v.roundAssets : [] })} />
              {v.target === 'asset' && <AssetPicker label={t('wo.asset')} value={v.assetId} onChange={(x) => form.setValues({ assetId: x, meterId: null })} error={err('assetId')} initial={s?.asset ?? (asset ? { id: asset.id, assetNumber: asset.assetNumber, name: asset.name } : null)} />}
              {v.target === 'several' && <AssetListEditor value={v.roundAssets} siteId={v.siteId} onChange={(x) => form.setFieldValue('roundAssets', x)} error={err('assetIds') ?? err('assetId')} />}
              {v.target === 'location' && err('assetId') && <Text c="red" fz="sm">{err('assetId')}</Text>}
              <EmployeePicker label={t('wo.assignees')} value={v.assigneeIds} onChange={(x) => form.setFieldValue('assigneeIds', x)} siteId={v.siteId} tradeId={v.tradeId} />
            </Stack></Card>
            <Card><Stack gap="sm">
              <Title order={5}>{t('pm.type')}</Title>
              <Radio.Group value={v.type} onChange={(x) => form.setValues({ type: x, basis: x === 'DayOfWeekInMonth' || x === 'DateOfMonth' ? 'Scheduled' : v.basis })}>
                <SimpleGrid cols={{ base: 1, sm: 2 }}>
                  {PM_TYPES.map((x) => <Radio.Card key={x} value={x} p="sm" radius="md" disabled={(editing && (s?.generationCount ?? 0) > 0 && x !== s?.type) || (x === 'Meter' && v.target === 'several')}>
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
                      <NumberInput label={t('pm.nextDueReading')} min={0} decimalScale={3} {...form.getInputProps('nextDueReading')} placeholder={t('pm.nextReadingPlaceholder')} />
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
            <TriggersCard form={form} meters={meters ?? []} />
            {v.target === 'several' && <MultiAssetCard form={form} templates={taskOpts} locked={editing && (s?.generationCount ?? 0) > 0} />}
            <PeopleRulesCard form={form} />
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
                <Title order={5} p="md" pb="xs">{t('pm.visitCost')}</Title>
                <Table verticalSpacing={6}><Table.Tbody>
                  <Table.Tr><Table.Td>{t('plan.parts')}</Table.Td><Table.Td ta="right">{fmtMoney(s.visitCost.parts, currency)}</Table.Td></Table.Tr>
                  <Table.Tr><Table.Td>{t('plan.laborCost')}</Table.Td><Table.Td ta="right">{fmtMoney(s.visitCost.labour, currency)}</Table.Td></Table.Tr>
                  <Table.Tr><Table.Td fw={700}>{t('plan.total')}</Table.Td><Table.Td ta="right" fw={700}>{fmtMoney(s.visitCost.total, currency)}</Table.Td></Table.Tr>
                </Table.Tbody></Table>
                <Text fz="xs" c="dimmed" px="md" pb="md">{t('pm.visitCostHint')}</Text>
              </Card>
            )}
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
      {editing && <OccurrencesDrawer scheduleId={id!} opened={occurrences} onClose={() => setOccurrences(false)} />}
    </form>
  );
}
