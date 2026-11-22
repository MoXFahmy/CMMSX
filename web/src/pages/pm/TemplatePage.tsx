import { useEffect } from 'react';
import { ActionIcon, Anchor, Badge, Button, Card, Center, Checkbox, Grid, Group, Loader, MultiSelect, NumberInput, Select, SimpleGrid, Stack, Table, Text, TextInput, Textarea, Title } from '@mantine/core';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconTrash } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import { WORK_PRIORITIES, WORK_TYPES, type Lookup, type PmTaskDetail, type StepDetail } from '@/api/types';
import { TemplateSteps } from './TemplateSteps';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { PartPicker } from '@/components/PartPicker';
import { Attachments } from '@/components/Attachments';
import { confirmDelete } from '@/components/confirm';
import { fmtMoney, handleFormError, notifyError, notifySuccess } from '@/components/utils';

type PartRow = { partId: string; label: string; unit: string; quantity: number | ''; unitCost: number };
type F = {
  code: string; name: string; title: string; workType: string; priority: string | null; tradeId: string | null; estimatedHours: number | ''; expectedStopHours: number | '';
  description: string; safetyNotes: string; steps: StepDetail[]; parts: PartRow[]; recipientIds: string[]; isActive: boolean;
};
const blank: F = {
  code: '', name: '', title: '', workType: 'Preventive', priority: null, tradeId: null, estimatedHours: '', expectedStopHours: '', description: '', safetyNotes: '', steps: [],
  parts: [], recipientIds: [], isActive: true,
};
const num = (x: number | '') => (x === '' ? null : x);

/** A work template (0.12.0; "PM task" before): what PMs and work orders made from it take, its documents and where it is used. */
export function TemplatePage() {
  const { t } = useTranslation();
  const { id } = useParams();
  const editing = !!id && id !== 'new';
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { can, me } = useAuth();
  const editable = can(P.pmManage);
  const currency = me?.tenant.settings.currency ?? 'USD';
  const { data: x, isLoading } = useQuery({ queryKey: ['pm-task', id], queryFn: () => get<PmTaskDetail>(`/pm-tasks/${id}`), enabled: editing });
  const { data: trades } = useQuery({ queryKey: ['trades'], queryFn: () => get<Lookup[]>('/trades') });
  const { data: people } = useQuery({ queryKey: ['users-lookup'], queryFn: () => get<{ id: string; fullName: string }[]>('/users/lookup'), retry: false });
  const form = useForm<F>({ initialValues: blank });

  useEffect(() => {
    if (!x) return;
    const saved: F = {
      code: x.code, name: x.name, title: x.title ?? '', workType: x.workType ?? 'Preventive', priority: x.priority ?? null, tradeId: x.tradeId ?? null,
      estimatedHours: x.estimatedHours ?? '', expectedStopHours: x.expectedStopHours ?? '', description: x.description ?? '', safetyNotes: x.safetyNotes ?? '',
      steps: x.stepDetails ?? x.steps.map((d) => ({ description: d, type: 'Check', photoRequired: false, beforeStart: false, followUp: 'Default' })),
      parts: x.parts.map((p) => ({ partId: p.partId, label: `${p.partNumber} · ${p.partName}`, unit: p.unitOfMeasure, quantity: p.quantity, unitCost: p.unitCost })),
      recipientIds: x.recipientIds ?? [], isActive: x.isActive,
    };
    form.setValues(saved); form.resetDirty(saved);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [x]);

  const v = form.values;
  const submit = form.onSubmit(async (vals) => {
    const body = {
      ...vals, title: vals.title || null, estimatedHours: num(vals.estimatedHours), expectedStopHours: num(vals.expectedStopHours), rowVersion: x?.rowVersion,
      parts: vals.parts.map((p) => ({ partId: p.partId, quantity: p.quantity || 0 })),
      steps: vals.steps.map((s) => s.description), stepDetails: vals.steps,
    };
    try {
      if (editing) await put(`/pm-tasks/${id}`, body);
      else { const r = await post<{ id: string }>('/pm-tasks', body); navigate(`/pm/templates/${r.id}`, { replace: true }); }
      notifySuccess(t('common.saved'));
      await qc.invalidateQueries({ queryKey: ['pm-tasks'] }); await qc.invalidateQueries({ queryKey: ['pm-task', id] });
    } catch (e) { handleFormError(form, e); }
  });
  if (editing && isLoading) return <Center h={300}><Loader /></Center>;

  return (
    <form onSubmit={submit}>
      <PageHeader title={editing ? `${x?.code} · ${x?.name}` : t('pm12.newTemplate')} crumbs={[{ label: t('pm.tasks'), to: '/pm/tasks' }, { label: editing ? x?.code ?? '' : t('pm12.newTemplate') }]}
        badge={editing && <Badge color={x?.isActive ? 'teal' : 'gray'}>{x?.isActive ? t('common.active') : t('common.inactive')}</Badge>}
        actions={editable && <>
          {editing && <Button variant="subtle" color="red" onClick={() => confirmDelete(x!.code, async () => {
            try { await del(`/pm-tasks/${id}`); await qc.invalidateQueries({ queryKey: ['pm-tasks'] }); navigate('/pm/tasks'); } catch (e) { notifyError(e); }
          })}>{t('common.delete')}</Button>}
          {editing && <Button variant="light" component={Link} to={`/work-orders/new?templateId=${id}`}>{t('pm12.newWorkOrderFromTemplate')}</Button>}
          <Button variant="default" onClick={() => navigate(-1)}>{t('common.cancel')}</Button>
          <Button type="submit" loading={form.submitting}>{editing ? t('common.saveChanges') : t('common.create')}</Button>
        </>} />
      <Grid gutter="lg">
        <Grid.Col span={{ base: 12, lg: 8 }}>
          <Stack gap="lg">
            <Card><Stack gap="sm">
              <SimpleGrid cols={{ base: 1, sm: 3 }}>
                <TextInput label={t('pm.taskCode')} required disabled={editing} {...form.getInputProps('code')} />
                <TextInput label={t('common.name')} required style={{ gridColumn: 'span 2' }} {...form.getInputProps('name')} />
              </SimpleGrid>
              <TextInput label={t('pm12.woTitle')} description={t('pm12.woTitleHint')} {...form.getInputProps('title')} />
              <SimpleGrid cols={{ base: 1, sm: 3 }}>
                <Select label={t('workType.label')} data={WORK_TYPES.map((w) => ({ value: w, label: t(`workType.${w}`) }))} {...form.getInputProps('workType')} />
                <Select label={t('priority.label')} clearable placeholder={t('pm12.priorityFromPm')} data={WORK_PRIORITIES.map((p) => ({ value: p, label: t(`priority.${p}`) }))} {...form.getInputProps('priority')} />
                <Select label={t('wo.trade')} clearable data={(trades ?? []).map((tr) => ({ value: tr.id, label: tr.name }))} {...form.getInputProps('tradeId')} />
                <NumberInput label={t('pm.estimatedHours')} min={0} decimalScale={2} {...form.getInputProps('estimatedHours')} />
                <NumberInput label={t('pm12.expectedStop')} min={0} decimalScale={2} {...form.getInputProps('expectedStopHours')} />
              </SimpleGrid>
              <Textarea label={t('common.description')} autosize minRows={2} {...form.getInputProps('description')} />
              <Textarea label={t('pm.safetyNotes')} autosize minRows={2} {...form.getInputProps('safetyNotes')} />
              <MultiSelect label={t('pm12.recipients')} description={t('pm12.templateRecipientsHint')} searchable clearable maxValues={20}
                data={(people ?? []).map((p) => ({ value: p.id, label: p.fullName }))} value={v.recipientIds} onChange={(r) => form.setFieldValue('recipientIds', r)} error={form.errors.recipientIds} />
              <Checkbox label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
            </Stack></Card>
            <TemplateSteps value={v.steps} onChange={(x) => form.setFieldValue('steps', x)} editable={editable} error={form.errors.steps} selfId={editing ? id : undefined} />
            <Card><Stack gap={4}>
              <Title order={5}>{t('pm.parts')} · {fmtMoney(v.parts.reduce((a, p) => a + Number(p.quantity || 0) * p.unitCost, 0), currency)}</Title>
              <Text fz="xs" c="dimmed">{t('pm.partsHint')}</Text>
              {v.parts.map((p, i) => (
                <Group key={p.partId} gap={6} wrap="nowrap">
                  <Text fz="sm" style={{ flex: 1 }} lineClamp={1}>{p.label}</Text>
                  <NumberInput size="xs" w={130} min={0} decimalScale={3} suffix={` ${p.unit}`} aria-label={t('inv.quantity')} {...form.getInputProps(`parts.${i}.quantity`)} />
                  <ActionIcon variant="subtle" color="red" size="sm" aria-label={t('common.delete')} onClick={() => form.removeListItem('parts', i)}><IconTrash size={14} /></ActionIcon>
                </Group>
              ))}
              {editable && <PartPicker placeholder={t('pm.addPart')} value={null} withStock={false} onChange={(pid, part) => {
                if (!pid || !part || v.parts.some((r) => r.partId === pid)) return;
                form.insertListItem('parts', { partId: pid, label: `${part.partNumber} · ${part.name}`, unit: part.unitOfMeasure, quantity: 1, unitCost: part.averageCost });
              }} />}
              {form.errors.parts && <Text fz="xs" c="red">{form.errors.parts}</Text>}
            </Stack></Card>
          </Stack>
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 4 }}>
          <Stack gap="lg">
            {editing && <Card><Title order={5} mb="xs">{t('pm12.documents')}</Title><Text fz="xs" c="dimmed" mb="xs">{t('pm12.documentsHint')}</Text>
              <Attachments entityType="PmTask" entityId={id!} canEdit={editable} /></Card>}
            {editing && x && (
              <Card p={0}>
                <Title order={5} p="md" pb="xs">{t('pm12.usedBy')}</Title>
                {x.usedBy.schedules.length === 0 && x.usedBy.checklists.length === 0 ? <Text c="dimmed" fz="sm" px="md" pb="md">{t('pm12.notUsed')}</Text> : (
                  <Table verticalSpacing={6}><Table.Tbody>
                    {x.usedBy.schedules.map((s) => (
                      <Table.Tr key={`${s.id}-${s.role}`}><Table.Td><Anchor component={Link} to={`/pm/schedules/${s.id}`} fz="sm">{s.code}</Anchor></Table.Td>
                        <Table.Td><Text fz="xs" c="dimmed">{s.name}</Text></Table.Td><Table.Td><Badge variant="light" size="sm">{t(`pm12.roles.${s.role}`)}</Badge></Table.Td></Table.Tr>
                    ))}
                    {x.usedBy.checklists.map((c) => (
                      <Table.Tr key={c.id}><Table.Td colSpan={2}>
                        {c.assetId ? <Anchor component={Link} to={`/assets/${c.assetId}`} fz="sm">{c.name}</Anchor> : <Text fz="sm">{c.name}</Text>}</Table.Td>
                        <Table.Td><Badge variant="light" size="sm" color="grape">{t(c.assetId ? 'pm12.roles.assetChecklist' : 'pm12.roles.categoryChecklist')}</Badge></Table.Td></Table.Tr>
                    ))}
                  </Table.Tbody></Table>
                )}
                <Text fz="xs" c="dimmed" px="md" pb="md">{t('pm12.openWorkOrders', { count: x.usedBy.openWorkOrders })}</Text>
              </Card>
            )}
          </Stack>
        </Grid.Col>
      </Grid>
    </form>
  );
}
