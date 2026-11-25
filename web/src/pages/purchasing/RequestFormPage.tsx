import { useEffect, useRef } from 'react';
import { ActionIcon, Button, Card, Center, Group, Loader, MultiSelect, NumberInput, Select, SimpleGrid, Table, Text, TextInput, Textarea, Title } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useLocation, useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconPlus, IconTrash } from '@tabler/icons-react';
import { get, post, put } from '@/api/client';
import { useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { useHierarchy } from '@/components/LocationPicker';
import { PartPicker, useStores } from '@/components/PartPicker';
import { WorkOrderPicker } from '@/components/WorkOrderPicker';
import { handleFormError, notifySuccess, toIsoDate } from '@/components/utils';
import type { PrLineDraft, PurchaseRequestDetail } from './purchaseRequests';

type Line = {
  id?: string; partId: string | null; part?: { id: string; partNumber: string; name: string } | null; description: string; unitOfMeasure: string; quantity: number | '';
  storeId: string | null; workOrderId: string | null; workOrder?: { id: string; number: string } | null; neededBy: Date | string | null; estimatedUnitPrice: number | ''; reason: string;
};
type Values = { siteId: string | null; neededBy: Date | string | null; notes: string; recipientIds: string[]; lines: Line[] };
const blankLine = (): Line => ({ partId: null, part: null, description: '', unitOfMeasure: '', quantity: 1, storeId: null, workOrderId: null, workOrder: null, neededBy: null, estimatedUnitPrice: '', reason: '' });
const fromDraft = (l: PrLineDraft & { part?: Line['part']; workOrder?: Line['workOrder'] }): Line => ({
  partId: l.partId, part: l.part ?? null, description: l.description, unitOfMeasure: l.unitOfMeasure, quantity: l.quantity, storeId: l.storeId, workOrderId: l.workOrderId,
  workOrder: l.workOrder ?? null, neededBy: l.neededBy, estimatedUnitPrice: l.estimatedUnitPrice ?? '', reason: l.reason,
});

/** A new purchase request, or a draft or returned one being fixed. "Request purchase" buttons open it filled in (router state). */
export function RequestFormPage() {
  const { t } = useTranslation();
  const { id } = useParams();
  const editing = !!id;
  const navigate = useNavigate();
  const location = useLocation();
  const qc = useQueryClient();
  const { terms } = useAuth();
  const { data: r, isLoading } = useQuery({ queryKey: ['purchase-request', id], queryFn: () => get<PurchaseRequestDetail>(`/purchase-requests/${id}`), enabled: editing });
  const { data: h } = useHierarchy();
  const { data: stores } = useStores();
  const { data: people } = useQuery({ queryKey: ['pr-recipient-options'], queryFn: () => get<{ id: string; fullName: string }[]>('/purchase-requests/recipient-options') });
  const form = useForm<Values>({ initialValues: { siteId: null, neededBy: null, notes: '', recipientIds: [], lines: [blankLine()] } });
  const seeded = useRef(false);
  useEffect(() => {
    if (!r) return;
    form.setValues({
      siteId: r.siteId, neededBy: r.neededBy ?? null, notes: r.notes ?? '', recipientIds: r.recipientIds,
      lines: r.lines.map((l) => ({ id: l.id, partId: l.partId ?? null, part: l.part ? { id: l.part.id, partNumber: l.part.partNumber, name: l.part.name } : null,
        description: l.description, unitOfMeasure: l.unitOfMeasure ?? '', quantity: l.quantity, storeId: l.store?.id ?? null, workOrderId: l.workOrder?.id ?? null,
        workOrder: l.workOrder ? { id: l.workOrder.id, number: l.workOrder.number } : null, neededBy: l.neededBy ?? null, estimatedUnitPrice: l.estimatedUnitPrice ?? '', reason: l.reason ?? '' })),
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [r]);
  useEffect(() => {
    if (editing || seeded.current || !h) return;
    seeded.current = true;
    const draft = (location.state as { draft?: { siteId: string; lines: (PrLineDraft & { part?: Line['part']; workOrder?: Line['workOrder'] })[] } } | null)?.draft;
    if (draft) { form.setValues({ siteId: draft.siteId, lines: draft.lines.map(fromDraft) }); return; }
    if (h.sites.length === 1) form.setFieldValue('siteId', h.sites[0].id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [h]);

  const v = form.getValues();
  const siteStores = (stores ?? []).filter((s) => s.isActive && (!v.siteId || s.siteId === v.siteId));
  const submit = form.onSubmit(async (x) => {
    if (!x.siteId) { form.setFieldError('siteId', t('pr.siteRequired')); return; }
    const body = {
      siteId: x.siteId, neededBy: toIsoDate(x.neededBy), notes: x.notes || null, recipientIds: x.recipientIds, rowVersion: r?.rowVersion,
      lines: x.lines.filter((l) => l.partId || l.description.trim()).map((l) => ({
        id: l.id ?? null, partId: l.partId, description: l.description, unitOfMeasure: l.unitOfMeasure || null, quantity: l.quantity === '' ? 0 : l.quantity,
        storeId: l.partId ? l.storeId : null, workOrderId: l.workOrderId, neededBy: toIsoDate(l.neededBy), estimatedUnitPrice: l.estimatedUnitPrice === '' ? null : l.estimatedUnitPrice,
        reason: l.reason || null,
      })),
    };
    try {
      const res = editing ? await put<{ id: string }>(`/purchase-requests/${id}`, body) : await post<{ id: string }>('/purchase-requests', body);
      notifySuccess(t('common.saved'));
      await qc.invalidateQueries({ queryKey: ['purchase-requests'] });
      await qc.invalidateQueries({ queryKey: ['purchase-request'] });
      navigate(`/purchasing/requests/${res.id}`, { replace: true });
    } catch (e) { handleFormError(form, e); }
  });
  if (editing && isLoading) return <Center h={300}><Loader /></Center>;
  const buttons = <><Button variant="default" onClick={() => navigate(-1)}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{editing ? t('common.saveChanges') : t('pr.saveDraft')}</Button></>;

  return (
    <form onSubmit={submit}>
      <PageHeader crumbs={[{ label: t('pr.title'), to: '/purchasing/requests' }, { label: editing ? r?.number ?? '' : t('pr.new') }]}
        title={editing ? t('pr.editTitle', { number: r?.number }) : t('pr.new')} actions={buttons} />
      <Card>
        <SimpleGrid cols={{ base: 1, sm: 3 }}>
          <Select label={terms.site} required data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} {...form.getInputProps('siteId')} />
          <DateInput label={t('pr.neededBy')} valueFormat="DD MMM YYYY" clearable {...form.getInputProps('neededBy')} />
          <MultiSelect label={t('pr.recipients')} description={t('pr.recipientsHint')} searchable clearable data={(people ?? []).map((p) => ({ value: p.id, label: p.fullName }))}
            {...form.getInputProps('recipientIds')} />
        </SimpleGrid>
        <Textarea mt="sm" label={t('pr.notes')} autosize minRows={2} {...form.getInputProps('notes')} />
      </Card>
      <Card p={0} mt="md">
        <Group justify="space-between" p="md"><Title order={5}>{t('pr.lines')}</Title>
          <Button size="xs" variant="light" leftSection={<IconPlus size={14} />} onClick={() => form.insertListItem('lines', blankLine())}>{t('pr.addLine')}</Button></Group>
        <Table.ScrollContainer minWidth={1200}>
          <Table verticalSpacing={6}>
            <Table.Thead><Table.Tr>
              <Table.Th w={36}>#</Table.Th><Table.Th w={250}>{t('pr.part')}</Table.Th><Table.Th>{t('pr.description')}</Table.Th><Table.Th w={90}>{t('pr.quantity')}</Table.Th>
              <Table.Th w={80}>{t('pr.unit')}</Table.Th><Table.Th w={160}>{t('pr.workOrder')}</Table.Th><Table.Th w={140}>{t('pr.neededBy')}</Table.Th>
              <Table.Th w={115}>{t('pr.estimatedPrice')}</Table.Th><Table.Th w={36} />
            </Table.Tr></Table.Thead>
            <Table.Tbody>
              {v.lines.map((l, i) => (
                <Table.Tr key={l.id ?? `new-${i}`} style={{ verticalAlign: 'top' }}>
                  <Table.Td pt={12}><Text fz="sm" c="dimmed">{i + 1}</Text></Table.Td>
                  <Table.Td>
                    <PartPicker placeholder={t('pr.nonStock')} value={l.partId} initial={l.part} withStock={false} onChange={(pid, p) => {
                      form.setFieldValue(`lines.${i}.partId`, pid); form.setFieldValue(`lines.${i}.part`, p ? { id: p.id, partNumber: p.partNumber, name: p.name } : null);
                      if (p) {
                        form.setFieldValue(`lines.${i}.description`, p.mainName ?? p.name); form.setFieldValue(`lines.${i}.unitOfMeasure`, p.unitOfMeasure);
                        if (!form.getValues().lines[i].storeId && siteStores.length) form.setFieldValue(`lines.${i}.storeId`, siteStores[0].id);
                      }
                    }} />
                    {l.partId && <Select mt={4} size="xs" placeholder={t('pr.store')} aria-label={t('pr.store')} data={siteStores.map((s) => ({ value: s.id, label: `${t('pr.store')} ${s.code}` }))}
                      {...form.getInputProps(`lines.${i}.storeId`)} error={form.errors[`lines[${i}].storeId`]} />}
                  </Table.Td>
                  <Table.Td>
                    <TextInput aria-label={t('pr.description')} {...form.getInputProps(`lines.${i}.description`)} error={form.errors[`lines[${i}].description`]} />
                    <TextInput mt={4} size="xs" placeholder={t('pr.reason')} aria-label={t('pr.reason')} {...form.getInputProps(`lines.${i}.reason`)} />
                  </Table.Td>
                  <Table.Td><NumberInput min={0} decimalScale={3} aria-label={t('pr.quantity')} {...form.getInputProps(`lines.${i}.quantity`)} error={form.errors[`lines[${i}].quantity`]} /></Table.Td>
                  <Table.Td><TextInput aria-label={t('pr.unit')} {...form.getInputProps(`lines.${i}.unitOfMeasure`)} /></Table.Td>
                  <Table.Td><WorkOrderPicker placeholder={t('pr.workOrder')} value={l.workOrderId} initial={l.workOrder} onChange={(x) => form.setFieldValue(`lines.${i}.workOrderId`, x)} />
                    {form.errors[`lines[${i}].workOrderId`] && <Text c="red" fz="xs">{form.errors[`lines[${i}].workOrderId`]}</Text>}</Table.Td>
                  <Table.Td><DateInput valueFormat="DD MMM YYYY" clearable aria-label={t('pr.neededBy')} {...form.getInputProps(`lines.${i}.neededBy`)} /></Table.Td>
                  <Table.Td><NumberInput min={0} decimalScale={4} thousandSeparator aria-label={t('pr.estimatedPrice')} {...form.getInputProps(`lines.${i}.estimatedUnitPrice`)} /></Table.Td>
                  <Table.Td pt={8}><ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} disabled={v.lines.length === 1} onClick={() => form.removeListItem('lines', i)}><IconTrash size={14} /></ActionIcon></Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
        {form.errors.lines && <Text c="red" fz="sm" px="md">{form.errors.lines}</Text>}
        <Text fz="xs" c="dimmed" p="md">{t('pr.linesHint')}</Text>
      </Card>
      <Group justify="flex-end" mt="lg">{buttons}</Group>
    </form>
  );
}
