import { useEffect, useMemo } from 'react';
import { ActionIcon, Button, Card, Center, Checkbox, Grid, Group, Loader, NumberInput, Select, SimpleGrid, Stack, TextInput, Textarea, Title } from '@mantine/core';
import { DateInput, DateTimePicker } from '@mantine/dates';
import dayjs from 'dayjs';
import { AccessWindowsEditor } from '@/components/AccessWindowsEditor';
import { useForm } from '@mantine/form';
import { CostCentrePicker } from '@/components/CostCentrePicker';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconPlus, IconTrash } from '@tabler/icons-react';
import { get, post, put } from '@/api/client';
import { type AssetDetail, type Lookup, type PmTaskDetail, type PmTaskListItem, type WorkOrderDetail } from '@/api/types';
import type { AccessWindow } from '@/api/types';
import { PageHeader } from '@/components/PageHeader';
import { LocationPicker } from '@/components/LocationPicker';
import { AssetPicker } from '@/components/AssetPicker';
import { ContractSelect } from '@/components/ContractSelect';
import { CodeSelect } from '@/components/CodeSelect';
import { EmployeePicker } from '@/components/EmployeePicker';
import { AssetCheck } from '@/components/AssetCheck';
import { teamOptions, useTeams } from '@/components/teams';
import { dateTimeFormat, handleFormError, notifyError, notifySuccess, toIsoDate } from '@/components/utils';
import { listDefault, listItems } from '@/components/listLabels';

type Values = {
  title: string; description: string; type: string; priority: string; siteId: string | null; locationId: string | null; assetId: string | null;
  tradeId: string | null; teamId: string | null; assigneeIds: string[]; plannedStart: Date | null;
  /** Due date and time, local ("YYYY-MM-DD HH:mm:ss"); empty = from the priority's target. */
  dueAt: string | null; estimatedHours: number | ''; tasks: string[]; requestedBy: string;
  /** "The asset is down": starts (or joins) the asset's stop; completing the work order ends it. */
  assetIsDown: boolean;
  /** When the work may be done (0.10.0). */
  accessWindows: AccessWindow[];
  /** Only while the asset is stopped; null on a new work order = take it from the asset. */
  onlyWhenStopped: boolean | null;
  /** The work template it is made from (0.12.0): its parts, recipients and documents come along. */
  templateId: string | null;
  /** What was seen (0.14.0). */
  symptomId: string | null;
  /** The service contract the work is done under (0.17.0). */
  contractId: string | null;
  /** 0.20.0: empty on a new work order takes the asset's or location's cost centre. */
  costCentreId: string | null;
};
const empty: Values = { title: '', description: '', type: 'Corrective', priority: 'Medium', siteId: null, locationId: null, assetId: null, tradeId: null, teamId: null,
  assigneeIds: [], plannedStart: null, dueAt: null, estimatedHours: '', tasks: [], requestedBy: '', assetIsDown: false, accessWindows: [], onlyWhenStopped: null, templateId: null,
  symptomId: null, contractId: null, costCentreId: null };
const local = (v?: string | null) => (v ? dayjs(v).format('YYYY-MM-DD HH:mm:ss') : null);
const d = (v?: string | null) => (v ? new Date(`${v}T00:00:00`) : null);

export function WorkOrderFormPage() {
  const { t } = useTranslation();
  const { id } = useParams();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const editing = !!id;
  const { data: wo, isLoading } = useQuery({ queryKey: ['work-order', id], queryFn: () => get<WorkOrderDetail>(`/work-orders/${id}`), enabled: editing });
  const copyFrom = params.get('copyFrom');
  const { data: source } = useQuery({ queryKey: ['work-order', copyFrom], queryFn: () => get<WorkOrderDetail>(`/work-orders/${copyFrom}`), enabled: !!copyFrom && !editing });
  const assetParam = params.get('assetId');
  const { data: asset } = useQuery({ queryKey: ['asset', assetParam], queryFn: () => get<AssetDetail>(`/assets/${assetParam}`), enabled: !!assetParam && !editing });
  const { data: trades } = useQuery({ queryKey: ['trades'], queryFn: () => get<Lookup[]>('/trades') });
  // A new work order's type and priority, unless the organization hid them (0.21.0).
  const start = useMemo<Values>(() => ({ ...empty, type: listDefault('workType', empty.type), priority: listDefault('priority', empty.priority) }), []);
  const form = useForm<Values>({ initialValues: start });
  const { data: teams } = useTeams(form.values.siteId);
  const { data: templates } = useQuery({ queryKey: ['pm-tasks'], queryFn: () => get<PmTaskListItem[]>('/pm-tasks'), enabled: !editing });
  // From a work template (0.12.0): fills what the job is; on save its parts and recipients come along and its documents show.
  const applyTemplate = async (templateId: string | null) => {
    form.setFieldValue('templateId', templateId);
    if (!templateId) return;
    try {
      const x = await get<PmTaskDetail>(`/pm-tasks/${templateId}`);
      const v = form.values;
      form.setValues({
        templateId, title: x.title || x.name, description: [x.description, x.safetyNotes ? `${t('pm.safetyNotes')}: ${x.safetyNotes}` : null].filter(Boolean).join('\n\n'),
        type: x.workType ?? v.type, priority: x.priority ?? v.priority, tradeId: x.tradeId ?? v.tradeId, estimatedHours: x.estimatedHours ?? v.estimatedHours, tasks: x.steps,
      });
    } catch (e) { notifyError(e); }
  };
  useEffect(() => {
    const fromTemplate = params.get('templateId');
    if (!editing && fromTemplate) void applyTemplate(fromTemplate);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (wo) {
      form.setValues({
        title: wo.title, description: wo.description ?? '', type: wo.type, priority: wo.priority, siteId: wo.siteId, locationId: wo.locationId ?? null,
        assetId: wo.assetId ?? null, tradeId: wo.tradeId ?? null, teamId: wo.teamId ?? null, assigneeIds: wo.assignees.map((a) => a.employeeId), plannedStart: d(wo.plannedStart),
        dueAt: local(wo.dueAt), estimatedHours: wo.estimatedHours ?? '', tasks: [], requestedBy: wo.requestedBy ?? '', assetIsDown: !!wo.downtime,
        accessWindows: wo.accessWindows ?? [], onlyWhenStopped: wo.onlyWhenStopped ?? false, symptomId: wo.symptomId ?? null, contractId: wo.contractId ?? null,
        costCentreId: wo.costCentreId ?? null,
      });
      form.resetDirty();
    } else if (source) {
      // Duplicate: what the job is and who does it; not its dates, status, bookings or history.
      form.setValues({
        ...empty, title: source.title, description: source.description ?? '', type: source.type, priority: source.priority, siteId: source.siteId,
        locationId: source.locationId ?? null, assetId: source.assetId ?? null, tradeId: source.tradeId ?? null, teamId: source.teamId ?? null,
        assigneeIds: source.assignees.map((a) => a.employeeId), estimatedHours: source.estimatedHours ?? '',
        tasks: [...source.tasks].sort((a, b) => a.sequence - b.sequence).map((x) => x.description),
        accessWindows: source.accessWindows ?? [], onlyWhenStopped: source.onlyWhenStopped ?? null, symptomId: source.symptomId ?? null, contractId: source.contractId ?? null,
      });
    } else if (asset) {
      form.setValues({ ...start, siteId: asset.siteId, locationId: asset.locationId ?? null, assetId: asset.id, priority: asset.criticality === 'Critical' ? 'High' : 'Medium' });
    } else if (!editing && params.get('siteId')) {
      form.setValues({ ...start, siteId: params.get('siteId'), locationId: params.get('locationId') });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [wo, asset, source]);

  const submit = form.onSubmit(async (v) => {
    const body = {
      ...v, plannedStart: toIsoDate(v.plannedStart), dueAt: v.dueAt ? dayjs(v.dueAt).toISOString() : null, dueDate: null,
      estimatedHours: v.estimatedHours === '' ? null : v.estimatedHours,
      tasks: v.tasks.filter((x) => x.trim()), rowVersion: wo?.rowVersion, assetIsDown: v.assetId ? v.assetIsDown : null,
      // Duplicate: the source's planned parts and hours come along (server side).
      copyPlanFromId: !editing && source ? source.id : undefined,
      templateId: !editing ? v.templateId : undefined,
      managesTeam: true, // this form shows the team: an empty one clears it
      managesSymptom: true, // and the symptom (0.14.0)
      managesContract: true, // and the contract (0.17.0)
      managesCostCentre: true, // and the cost centre (0.20.0); empty on a new one takes the asset's or location's
    };
    try {
      const r = editing ? await put<{ id: string }>(`/work-orders/${id}`, body) : await post<{ id: string; number: string }>('/work-orders', body);
      notifySuccess(editing ? t('common.saved') : `${t('common.created')}: ${(r as { number?: string }).number ?? ''}`);
      await qc.invalidateQueries({ queryKey: ['work-orders'] });
      await qc.invalidateQueries({ queryKey: ['work-order'] });
      await qc.invalidateQueries({ queryKey: ['dashboard'] });
      if (v.assetId) { await qc.invalidateQueries({ queryKey: ['asset'] }); await qc.invalidateQueries({ queryKey: ['assets'] }); }
      navigate(`/work-orders/${r.id}`, { replace: true });
    } catch (e) { handleFormError(form, e); }
  });

  if (editing && isLoading) return <Center h={300}><Loader /></Center>;
  const err = (k: string) => form.errors[k];
  return (
    <form onSubmit={submit}>
      <PageHeader title={editing ? `${t('wo.edit')} · ${wo?.number}` : source ? t('wo.duplicateTitle', { number: source.number }) : t('wo.new')}
        crumbs={[{ label: t('wo.title'), to: '/work-orders' }, ...(editing ? [{ label: wo?.number ?? '', to: `/work-orders/${id}` }] : [])]}
        actions={<><Button variant="default" onClick={() => navigate(-1)}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{editing ? t('common.saveChanges') : t('common.create')}</Button></>} />
      <Grid gutter="lg">
        <Grid.Col span={{ base: 12, lg: 8 }}>
          <Stack gap="lg">
            <Card><Stack gap="sm">
              {!editing && !source && (templates?.length ?? 0) > 0 && (
                <Select label={t('pm12.fromTemplate')} placeholder={t('pm12.fromTemplatePlaceholder')} clearable searchable value={form.values.templateId} onChange={(x) => void applyTemplate(x)}
                  data={(templates ?? []).filter((x) => x.isActive).map((x) => ({ value: x.id, label: `${x.code} · ${x.name}` }))} />
              )}
              <TextInput label={t('wr.whatsWrong')} required data-autofocus {...form.getInputProps('title')} />
              <Textarea label={t('wo.description')} autosize minRows={3} {...form.getInputProps('description')} />
              <SimpleGrid cols={{ base: 1, sm: 3 }}>
                <Select label={t('workType.label')} required data={listItems('workType', t, form.values.type)} {...form.getInputProps('type')} />
                <Select label={t('priority.label')} required data={listItems('priority', t, form.values.priority)} {...form.getInputProps('priority')} />
                <Select label={t('wo.trade')} clearable data={(trades ?? []).filter((x) => x.isActive || x.id === form.values.tradeId).map((x) => ({ value: x.id, label: x.name }))} {...form.getInputProps('tradeId')} />
              </SimpleGrid>
            </Stack></Card>
            <Card><Stack gap="sm">
              <Title order={5}>{t('assets.placement')}</Title>
              <LocationPicker siteId={form.values.siteId} locationId={form.values.locationId} siteError={err('siteId')} locationError={err('locationId')}
                onChange={(s, l) => form.setValues({ siteId: s, locationId: l, assetId: s === form.values.siteId ? form.values.assetId : null })} />
              <AssetPicker label={t('wo.asset')} value={form.values.assetId} error={err('assetId')} onChange={(v) => form.setFieldValue('assetId', v)}
                initial={wo?.asset ?? source?.asset ?? (asset ? { id: asset.id, assetNumber: asset.assetNumber, name: asset.name } : null)} />
              <AssetCheck assetId={form.values.assetId} excludeWorkOrderId={id} />
              <CodeSelect path="/symptoms" label={t('codes.symptom')} value={form.values.symptomId} onChange={(v) => form.setFieldValue('symptomId', v)}
                current={wo?.symptom ?? source?.symptom ?? null} error={err('symptomId')} />
              <ContractSelect siteId={form.values.siteId} assetId={form.values.assetId} value={form.values.contractId} onChange={(v) => form.setFieldValue('contractId', v)}
                current={wo?.contract ?? source?.contract ?? null} error={err('contractId')} />
              <CostCentrePicker value={form.values.costCentreId} onChange={(v) => form.setFieldValue('costCentreId', v)} siteId={form.values.siteId}
                description={editing ? undefined : t('costCentres.workOrderHint')} error={err('costCentreId')} />
              {form.values.assetId && <Checkbox label={t('downtime.assetIsDown')} color="red"
                description={editing && wo?.downtime && form.values.assetId !== wo.assetId
                  ? (wo.downtime.source === 'WorkOrder' && wo.downtime.sourceNumber === wo.number && !wo.downtime.endedAt ? t('downtime.assetMovesHint') : t('downtime.assetChangedHint'))
                  : t('downtime.assetIsDownHint')}
                {...form.getInputProps('assetIsDown', { type: 'checkbox' })} />}
              <Checkbox label={t('accessWindows.onlyWhenStopped')} description={form.values.onlyWhenStopped === null ? t('accessWindows.fromAsset') : undefined}
                checked={!!form.values.onlyWhenStopped} onChange={(e) => form.setFieldValue('onlyWhenStopped', e.currentTarget.checked)} />
              <AccessWindowsEditor value={form.values.accessWindows} onChange={(v) => form.setFieldValue('accessWindows', v)} error={err('accessWindows')} />
            </Stack></Card>
            {!editing && (
              <Card><Stack gap="xs">
                <Title order={5}>{t('wo.tasks')}</Title>
                {form.values.tasks.map((_, i) => (
                  <Group key={i} gap="xs" wrap="nowrap">
                    <TextInput style={{ flex: 1 }} placeholder={`${i + 1}.`} {...form.getInputProps(`tasks.${i}`)} />
                    <ActionIcon variant="subtle" color="gray" aria-label={t('common.delete')} onClick={() => form.removeListItem('tasks', i)}><IconTrash size={16} /></ActionIcon>
                  </Group>
                ))}
                <Button variant="subtle" leftSection={<IconPlus size={14} />} w="fit-content" onClick={() => form.insertListItem('tasks', '')}>{t('wo.addTask')}</Button>
              </Stack></Card>
            )}
          </Stack>
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 4 }}>
          <Card><Stack gap="sm">
            <Title order={5}>{t('wo.details')}</Title>
            {(teams?.length ?? 0) > 0 && (
              <Select label={t('teams.team')} description={t('teams.fieldHint')} clearable data={teamOptions(teams, form.values.teamId)} {...form.getInputProps('teamId')} />
            )}
            <EmployeePicker label={t('wo.assignees')} value={form.values.assigneeIds} onChange={(v) => form.setFieldValue('assigneeIds', v)}
              siteId={form.values.siteId} tradeId={form.values.tradeId} error={err('assigneeIds')} />
            <SimpleGrid cols={2}>
              <DateInput label={t('wo.plannedStart')} clearable valueFormat="DD MMM YYYY" {...form.getInputProps('plannedStart')} />
              <DateTimePicker label={t('wo.dueAt')} description={wo?.group ? t('groups.dueFromGroup', { number: wo.group.number }) : t('wo.dueAtHint')} clearable valueFormat={dateTimeFormat()} {...form.getInputProps('dueAt')}
                disabled={!!wo?.group} error={err('dueDate')} />
            </SimpleGrid>
            <NumberInput label={t('wo.estimatedHours')} min={0} decimalScale={2} {...form.getInputProps('estimatedHours')} />
            <TextInput label={t('wo.requestedBy')} {...form.getInputProps('requestedBy')} />
          </Stack></Card>
        </Grid.Col>
      </Grid>
    </form>
  );
}
