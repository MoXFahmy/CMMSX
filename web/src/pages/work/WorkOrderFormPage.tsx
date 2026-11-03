import { useEffect } from 'react';
import { ActionIcon, Button, Card, Center, Grid, Group, Loader, NumberInput, Select, SimpleGrid, Stack, TextInput, Textarea, Title } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconPlus, IconTrash } from '@tabler/icons-react';
import { get, post, put } from '@/api/client';
import { WORK_PRIORITIES, WORK_TYPES, type AssetDetail, type Lookup, type WorkOrderDetail } from '@/api/types';
import { PageHeader } from '@/components/PageHeader';
import { LocationPicker } from '@/components/LocationPicker';
import { AssetPicker } from '@/components/AssetPicker';
import { EmployeePicker } from '@/components/EmployeePicker';
import { AssetCheck } from '@/components/AssetCheck';
import { handleFormError, notifySuccess, toIsoDate } from '@/components/utils';

type Values = {
  title: string; description: string; type: string; priority: string; siteId: string | null; locationId: string | null; assetId: string | null;
  tradeId: string | null; assigneeIds: string[]; plannedStart: Date | null; dueDate: Date | null; estimatedHours: number | ''; tasks: string[]; requestedBy: string;
};
const empty: Values = { title: '', description: '', type: 'Corrective', priority: 'Medium', siteId: null, locationId: null, assetId: null, tradeId: null,
  assigneeIds: [], plannedStart: null, dueDate: null, estimatedHours: '', tasks: [], requestedBy: '' };
const d = (v?: string | null) => (v ? new Date(`${v}T00:00:00`) : null);

export function WorkOrderFormPage() {
  const { t } = useTranslation();
  const { id } = useParams();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const editing = !!id;
  const { data: wo, isLoading } = useQuery({ queryKey: ['work-order', id], queryFn: () => get<WorkOrderDetail>(`/work-orders/${id}`), enabled: editing });
  const assetParam = params.get('assetId');
  const { data: asset } = useQuery({ queryKey: ['asset', assetParam], queryFn: () => get<AssetDetail>(`/assets/${assetParam}`), enabled: !!assetParam && !editing });
  const { data: trades } = useQuery({ queryKey: ['trades'], queryFn: () => get<Lookup[]>('/trades') });
  const form = useForm<Values>({ initialValues: empty });

  useEffect(() => {
    if (wo) {
      form.setValues({
        title: wo.title, description: wo.description ?? '', type: wo.type, priority: wo.priority, siteId: wo.siteId, locationId: wo.locationId ?? null,
        assetId: wo.assetId ?? null, tradeId: wo.tradeId ?? null, assigneeIds: wo.assignees.map((a) => a.employeeId), plannedStart: d(wo.plannedStart),
        dueDate: d(wo.dueDate), estimatedHours: wo.estimatedHours ?? '', tasks: [], requestedBy: wo.requestedBy ?? '',
      });
      form.resetDirty();
    } else if (asset) {
      form.setValues({ ...empty, siteId: asset.siteId, locationId: asset.locationId ?? null, assetId: asset.id, priority: asset.criticality === 'Critical' ? 'High' : 'Medium' });
    } else if (!editing && params.get('siteId')) {
      form.setValues({ ...empty, siteId: params.get('siteId'), locationId: params.get('locationId') });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [wo, asset]);

  const submit = form.onSubmit(async (v) => {
    const body = {
      ...v, plannedStart: toIsoDate(v.plannedStart), dueDate: toIsoDate(v.dueDate), estimatedHours: v.estimatedHours === '' ? null : v.estimatedHours,
      tasks: v.tasks.filter((x) => x.trim()), rowVersion: wo?.rowVersion,
    };
    try {
      const r = editing ? await put<{ id: string }>(`/work-orders/${id}`, body) : await post<{ id: string; number: string }>('/work-orders', body);
      notifySuccess(editing ? t('common.saved') : `${t('common.created')}: ${(r as { number?: string }).number ?? ''}`);
      await qc.invalidateQueries({ queryKey: ['work-orders'] });
      await qc.invalidateQueries({ queryKey: ['work-order'] });
      await qc.invalidateQueries({ queryKey: ['dashboard'] });
      navigate(`/work-orders/${r.id}`, { replace: true });
    } catch (e) { handleFormError(form, e); }
  });

  if (editing && isLoading) return <Center h={300}><Loader /></Center>;
  const err = (k: string) => form.errors[k];
  return (
    <form onSubmit={submit}>
      <PageHeader title={editing ? `${t('wo.edit')} · ${wo?.number}` : t('wo.new')}
        crumbs={[{ label: t('wo.title'), to: '/work-orders' }, ...(editing ? [{ label: wo?.number ?? '', to: `/work-orders/${id}` }] : [])]}
        actions={<><Button variant="default" onClick={() => navigate(-1)}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{editing ? t('common.saveChanges') : t('common.create')}</Button></>} />
      <Grid gutter="lg">
        <Grid.Col span={{ base: 12, lg: 8 }}>
          <Stack gap="lg">
            <Card><Stack gap="sm">
              <TextInput label={t('wr.whatsWrong')} required data-autofocus {...form.getInputProps('title')} />
              <Textarea label={t('wo.description')} autosize minRows={3} {...form.getInputProps('description')} />
              <SimpleGrid cols={{ base: 1, sm: 3 }}>
                <Select label={t('workType.label')} required data={WORK_TYPES.map((x) => ({ value: x, label: t(`workType.${x}`) }))} {...form.getInputProps('type')} />
                <Select label={t('priority.label')} required data={WORK_PRIORITIES.map((x) => ({ value: x, label: t(`priority.${x}`) }))} {...form.getInputProps('priority')} />
                <Select label={t('wo.trade')} clearable data={(trades ?? []).filter((x) => x.isActive || x.id === form.values.tradeId).map((x) => ({ value: x.id, label: x.name }))} {...form.getInputProps('tradeId')} />
              </SimpleGrid>
            </Stack></Card>
            <Card><Stack gap="sm">
              <Title order={5}>{t('assets.placement')}</Title>
              <LocationPicker siteId={form.values.siteId} locationId={form.values.locationId} siteError={err('siteId')} locationError={err('locationId')}
                onChange={(s, l) => form.setValues({ siteId: s, locationId: l, assetId: s === form.values.siteId ? form.values.assetId : null })} />
              <AssetPicker label={t('wo.asset')} value={form.values.assetId} error={err('assetId')} onChange={(v) => form.setFieldValue('assetId', v)}
                initial={wo?.asset ?? (asset ? { id: asset.id, assetNumber: asset.assetNumber, name: asset.name } : null)} />
              <AssetCheck assetId={form.values.assetId} excludeWorkOrderId={id} />
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
            <EmployeePicker label={t('wo.assignees')} value={form.values.assigneeIds} onChange={(v) => form.setFieldValue('assigneeIds', v)}
              siteId={form.values.siteId} tradeId={form.values.tradeId} error={err('assigneeIds')} />
            <SimpleGrid cols={2}>
              <DateInput label={t('wo.plannedStart')} clearable valueFormat="DD MMM YYYY" {...form.getInputProps('plannedStart')} />
              <DateInput label={t('wo.dueDate')} clearable valueFormat="DD MMM YYYY" {...form.getInputProps('dueDate')} />
            </SimpleGrid>
            <NumberInput label={t('wo.estimatedHours')} min={0} decimalScale={2} {...form.getInputProps('estimatedHours')} />
            <TextInput label={t('wo.requestedBy')} {...form.getInputProps('requestedBy')} />
          </Stack></Card>
        </Grid.Col>
      </Grid>
    </form>
  );
}
