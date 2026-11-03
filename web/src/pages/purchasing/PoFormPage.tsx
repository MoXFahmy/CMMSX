import { useEffect, useRef } from 'react';
import { ActionIcon, Button, Card, Center, Grid, Group, Loader, NumberInput, Select, SimpleGrid, Stack, Table, Text, TextInput, Textarea, Title } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconPlus, IconTrash } from '@tabler/icons-react';
import { get, post, put } from '@/api/client';
import type { PartDetail, PoDetail, SupplierLookup, WorkOrderDetail } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { useHierarchy } from '@/components/LocationPicker';
import { PartPicker, useStores } from '@/components/PartPicker';
import { WorkOrderPicker } from '@/components/WorkOrderPicker';
import { fmtPrice, handleFormError, notifySuccess, toIsoDate } from '@/components/utils';

type Line = {
  id?: string; partId: string | null; part?: { id: string; partNumber: string; name: string } | null; description: string; unitOfMeasure: string; quantity: number | '';
  unitPrice: number | ''; discountPercent: number | ''; storeId: string | null; workOrderId: string | null; workOrder?: { id: string; number: string } | null; accountCode: string;
};
type Values = {
  siteId: string | null; supplierId: string | null; orderDate: Date | string | null; expectedDate: Date | string | null; requestedBy: string; currency: string; exchangeRate: number | '';
  paymentTerms: string; shippingMethod: string; deliverTo: string; supplierQuoteRef: string; taxPercent: number | ''; shippingCost: number | ''; notes: string; lines: Line[];
};
const blankLine = (): Line => ({ partId: null, part: null, description: '', unitOfMeasure: '', quantity: 1, unitPrice: '', discountPercent: 0, storeId: null, workOrderId: null, workOrder: null, accountCode: '' });
const n = (v: number | '') => (v === '' ? 0 : v);
const lineTotal = (l: Line) => Math.round(n(l.quantity) * n(l.unitPrice) * (1 - n(l.discountPercent) / 100) * 100) / 100;

export function PoFormPage() {
  const { t } = useTranslation();
  const { id } = useParams();
  const [params] = useSearchParams();
  const editing = !!id;
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { me, terms } = useAuth();
  const base = me?.tenant.settings.currency ?? 'USD';
  const { data: po, isLoading } = useQuery({ queryKey: ['purchase-order', id], queryFn: () => get<PoDetail>(`/purchase-orders/${id}`), enabled: editing });
  const { data: suppliers } = useQuery({ queryKey: ['suppliers', 'lookup'], queryFn: () => get<SupplierLookup[]>('/suppliers/lookup') });
  const { data: h } = useHierarchy();
  const { data: stores } = useStores();
  const form = useForm<Values>({
    initialValues: { siteId: null, supplierId: null, orderDate: new Date(), expectedDate: null, requestedBy: me?.user.fullName ?? '', currency: base, exchangeRate: 1, paymentTerms: '', shippingMethod: '',
      deliverTo: '', supplierQuoteRef: '', taxPercent: me?.tenant.settings.defaultTaxPercent ?? 0, shippingCost: 0, notes: '', lines: [blankLine()] },
  });
  const seeded = useRef(false);
  useEffect(() => {
    if (!po) return;
    form.setValues({
      siteId: po.siteId, supplierId: po.supplierId, orderDate: po.orderDate, expectedDate: po.expectedDate ?? null, requestedBy: po.requestedBy ?? '', currency: po.currency, exchangeRate: po.exchangeRate,
      paymentTerms: po.paymentTerms ?? '', shippingMethod: po.shippingMethod ?? '', deliverTo: po.deliverTo ?? '', supplierQuoteRef: po.supplierQuoteRef ?? '', taxPercent: po.taxPercent, shippingCost: po.shippingCost,
      notes: po.notes ?? '', lines: po.lines.map((l) => ({ id: l.id, partId: l.partId ?? null, part: l.partId ? { id: l.partId, partNumber: l.partNumber ?? '', name: l.description } : null, description: l.description,
        unitOfMeasure: l.unitOfMeasure ?? '', quantity: l.quantity, unitPrice: l.unitPrice, discountPercent: l.discountPercent, storeId: l.storeId ?? null, workOrderId: l.workOrderId ?? null,
        workOrder: l.workOrderId ? { id: l.workOrderId, number: l.workOrderNumber ?? '' } : null, accountCode: l.accountCode ?? '' })),
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [po]);
  // Seed a new order from a part or a work order (links on those pages).
  useEffect(() => {
    if (editing || seeded.current || !h || !stores) return;
    const partId = params.get('partId'); const woId = params.get('workOrderId');
    if (!partId && !woId) { if (h.sites.length === 1) form.setFieldValue('siteId', h.sites[0].id); seeded.current = true; return; }
    seeded.current = true;
    void (async () => {
      if (partId) {
        const p = await get<PartDetail>(`/parts/${partId}`);
        const store = p.levels[0]?.storeId ?? null;
        form.setFieldValue('lines', [{ ...blankLine(), partId: p.id, part: { id: p.id, partNumber: p.partNumber, name: p.name }, description: p.name, unitOfMeasure: p.unitOfMeasure,
          unitPrice: p.lastPurchasePrice ?? p.averageCost, storeId: store, quantity: p.levels[0]?.reorderQuantity || 1 }]);
        if (p.preferredSupplierId) onSupplier(p.preferredSupplierId);
        const site = stores.find((s) => s.id === store)?.siteId;
        form.setFieldValue('siteId', site ?? h.sites[0]?.id ?? null);
      }
      if (woId) {
        const w = await get<WorkOrderDetail>(`/work-orders/${woId}`);
        form.setFieldValue('siteId', w.siteId);
        form.setFieldValue('lines', [{ ...blankLine(), workOrderId: w.id, workOrder: { id: w.id, number: w.number } }]);
      }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [h, stores]);
  const onSupplier = (sid: string | null) => {
    form.setFieldValue('supplierId', sid);
    const s = suppliers?.find((x) => x.id === sid);
    if (s?.currency) form.setFieldValue('currency', s.currency);
    if (s?.paymentTerms) form.setFieldValue('paymentTerms', s.paymentTerms);
  };
  const v = form.getValues();
  const subtotal = v.lines.reduce((a, l) => a + lineTotal(l), 0);
  const tax = Math.round(subtotal * n(v.taxPercent)) / 100;
  const total = subtotal + tax + n(v.shippingCost);
  const cur = v.currency || base;
  const siteStores = (stores ?? []).filter((s) => s.isActive && (!v.siteId || s.siteId === v.siteId));

  const submit = form.onSubmit(async (x) => {
    const body = {
      ...x, orderDate: toIsoDate(x.orderDate), expectedDate: toIsoDate(x.expectedDate), exchangeRate: x.exchangeRate === '' ? 1 : x.exchangeRate, taxPercent: n(x.taxPercent), shippingCost: n(x.shippingCost),
      rowVersion: po?.rowVersion,
      lines: x.lines.filter((l) => l.partId || l.description.trim()).map((l) => ({ id: l.id ?? null, partId: l.partId, description: l.description, unitOfMeasure: l.unitOfMeasure, quantity: n(l.quantity),
        unitPrice: n(l.unitPrice), discountPercent: n(l.discountPercent), storeId: l.partId ? l.storeId : null, workOrderId: l.workOrderId, accountCode: l.accountCode })),
    };
    try {
      const r = editing ? await put<{ id: string }>(`/purchase-orders/${id}`, body) : await post<{ id: string; number: string }>('/purchase-orders', body);
      notifySuccess(t('common.saved'));
      await qc.invalidateQueries({ queryKey: ['purchase-orders'] });
      await qc.invalidateQueries({ queryKey: ['purchase-order'] });
      navigate(`/purchasing/orders/${r.id}`, { replace: true });
    } catch (e) { handleFormError(form, e); }
  });
  if (editing && isLoading) return <Center h={300}><Loader /></Center>;
  const buttons = <><Button variant="default" onClick={() => navigate(-1)}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{editing ? t('common.saveChanges') : t('po.saveDraft')}</Button></>;

  return (
    <form onSubmit={submit}>
      <PageHeader crumbs={[{ label: t('po.title'), to: '/purchasing' }, { label: editing ? po?.number ?? '' : t('po.new') }]} title={editing ? t('po.editTitle', { number: po?.number }) : t('po.new')} actions={buttons} />
      <Grid>
        <Grid.Col span={{ base: 12, lg: 8 }}>
          <Card>
            <Title order={5} mb="md">{t('po.order')}</Title>
            <SimpleGrid cols={{ base: 1, sm: 2 }}>
              <Select label={t('po.supplier')} required searchable data={(suppliers ?? []).map((s) => ({ value: s.id, label: `${s.name} (${s.code})` }))} value={v.supplierId} onChange={onSupplier} error={form.errors.supplierId} />
              <Select label={terms.site} required data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} {...form.getInputProps('siteId')} />
            </SimpleGrid>
            <SimpleGrid cols={{ base: 1, sm: 3 }} mt="sm">
              <DateInput label={t('po.orderDate')} required valueFormat="DD MMM YYYY" {...form.getInputProps('orderDate')} />
              <DateInput label={t('po.expected')} valueFormat="DD MMM YYYY" clearable {...form.getInputProps('expectedDate')} />
              <TextInput label={t('po.requestedBy')} {...form.getInputProps('requestedBy')} />
            </SimpleGrid>
          </Card>
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 4 }}>
          <Card h="100%">
            <Title order={5} mb="md">{t('po.terms')}</Title>
            <SimpleGrid cols={2}>
              <TextInput label={t('po.currency')} maxLength={3} {...form.getInputProps('currency')} />
              <NumberInput label={t('po.exchangeRate')} min={0} decimalScale={6} disabled={cur.toUpperCase() === base} description={cur.toUpperCase() !== base ? `1 ${cur.toUpperCase()} = ? ${base}` : undefined} {...form.getInputProps('exchangeRate')} />
              <TextInput label={t('po.paymentTerms')} {...form.getInputProps('paymentTerms')} />
              <TextInput label={t('po.quoteRef')} {...form.getInputProps('supplierQuoteRef')} />
            </SimpleGrid>
          </Card>
        </Grid.Col>
      </Grid>
      <Card p={0} mt="md">
        <Group justify="space-between" p="md"><Title order={5}>{t('po.lines')}</Title>
          <Button size="xs" variant="light" leftSection={<IconPlus size={14} />} onClick={() => form.insertListItem('lines', blankLine())}>{t('po.addLine')}</Button></Group>
        <Table.ScrollContainer minWidth={1180}>
          <Table verticalSpacing={6}>
            <Table.Thead><Table.Tr>
              <Table.Th w={36}>#</Table.Th><Table.Th w={250}>{t('inv.part')}</Table.Th><Table.Th>{t('common.description')}</Table.Th><Table.Th w={90}>{t('inv.quantity')}</Table.Th><Table.Th w={80}>{t('inv.unit')}</Table.Th>
              <Table.Th w={115}>{t('po.unitPrice')}</Table.Th><Table.Th w={80}>{t('po.discount')}</Table.Th><Table.Th w={150}>{t('po.deliverTo')}</Table.Th><Table.Th w={110} ta="right">{t('po.lineTotal')}</Table.Th><Table.Th w={36} />
            </Table.Tr></Table.Thead>
            <Table.Tbody>
              {v.lines.map((l, i) => (
                <Table.Tr key={l.id ?? `new-${i}`} style={{ verticalAlign: 'top' }}>
                  <Table.Td pt={12}><Text fz="sm" c="dimmed">{i + 1}</Text></Table.Td>
                  <Table.Td><PartPicker placeholder={t('po.nonStock')} value={l.partId} initial={l.part} withStock={false} onChange={(pid, p) => {
                    form.setFieldValue(`lines.${i}.partId`, pid); form.setFieldValue(`lines.${i}.part`, p ? { id: p.id, partNumber: p.partNumber, name: p.name } : null);
                    if (p) { form.setFieldValue(`lines.${i}.description`, p.name); form.setFieldValue(`lines.${i}.unitOfMeasure`, p.unitOfMeasure); form.setFieldValue(`lines.${i}.unitPrice`, p.lastPurchasePrice ?? p.averageCost);
                      if (!form.getValues().lines[i].storeId && siteStores.length) form.setFieldValue(`lines.${i}.storeId`, siteStores[0].id); }
                  }} />
                  {l.partId && <Select mt={4} size="xs" placeholder={t('inv.store')} aria-label={t('inv.store')} data={siteStores.map((s) => ({ value: s.id, label: `${t('po.intoStore')} ${s.code}` }))} {...form.getInputProps(`lines.${i}.storeId`)} error={form.errors[`lines[${i}].storeId`]} />}
                  </Table.Td>
                  <Table.Td><TextInput aria-label={t('common.description')} {...form.getInputProps(`lines.${i}.description`)} error={form.errors[`lines[${i}].description`]} />
                    <TextInput mt={4} size="xs" placeholder={t('po.accountCode')} aria-label={t('po.accountCode')} {...form.getInputProps(`lines.${i}.accountCode`)} /></Table.Td>
                  <Table.Td><NumberInput min={0} decimalScale={3} aria-label={t('inv.quantity')} {...form.getInputProps(`lines.${i}.quantity`)} error={form.errors[`lines[${i}].quantity`]} /></Table.Td>
                  <Table.Td><TextInput aria-label={t('inv.unit')} {...form.getInputProps(`lines.${i}.unitOfMeasure`)} /></Table.Td>
                  <Table.Td><NumberInput min={0} decimalScale={4} thousandSeparator aria-label={t('po.unitPrice')} {...form.getInputProps(`lines.${i}.unitPrice`)} error={form.errors[`lines[${i}].unitPrice`]} /></Table.Td>
                  <Table.Td><NumberInput min={0} max={100} suffix="%" aria-label={t('po.discount')} {...form.getInputProps(`lines.${i}.discountPercent`)} /></Table.Td>
                  <Table.Td><WorkOrderPicker placeholder={t('po.forWorkOrder')} value={l.workOrderId} initial={l.workOrder} onChange={(x) => form.setFieldValue(`lines.${i}.workOrderId`, x)} /></Table.Td>
                  <Table.Td ta="right" pt={12}><Text fz="sm" fw={600}>{fmtPrice(lineTotal(l), cur)}</Text></Table.Td>
                  <Table.Td pt={8}><ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} disabled={v.lines.length === 1} onClick={() => form.removeListItem('lines', i)}><IconTrash size={14} /></ActionIcon></Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
        {form.errors.lines && <Text c="red" fz="sm" px="md">{form.errors.lines}</Text>}
        <Text fz="xs" c="dimmed" px="md" pt="xs">{t('po.linesHint')}</Text>
        <Group justify="flex-end" p="md" align="flex-start">
          <Stack gap={6} w={320}>
            <Group justify="space-between"><Text fz="sm">{t('po.subtotal')}</Text><Text fz="sm">{fmtPrice(subtotal, cur)}</Text></Group>
            <Group justify="space-between" wrap="nowrap"><Group gap={6} wrap="nowrap"><Text fz="sm">{t('po.tax')}</Text><NumberInput size="xs" w={80} min={0} max={100} suffix="%" aria-label={t('po.tax')} {...form.getInputProps('taxPercent')} /></Group><Text fz="sm">{fmtPrice(tax, cur)}</Text></Group>
            <Group justify="space-between" wrap="nowrap"><Text fz="sm">{t('po.shipping')}</Text><NumberInput size="xs" w={120} min={0} decimalScale={2} aria-label={t('po.shipping')} {...form.getInputProps('shippingCost')} /></Group>
            <Group justify="space-between" pt={6} style={{ borderTop: '1px solid var(--mantine-color-default-border)' }}><Text fw={700}>{t('po.total')}</Text><Text fw={700}>{fmtPrice(total, cur)}</Text></Group>
            {cur.toUpperCase() !== base && <Text fz="xs" c="dimmed" ta="right">≈ {fmtPrice(total * n(v.exchangeRate || 1), base)}</Text>}
          </Stack>
        </Group>
      </Card>
      <Card mt="md">
        <SimpleGrid cols={{ base: 1, sm: 2 }}>
          <TextInput label={t('po.shippingMethod')} {...form.getInputProps('shippingMethod')} />
          <TextInput label={t('po.deliveryAddress')} {...form.getInputProps('deliverTo')} />
        </SimpleGrid>
        <Textarea mt="sm" label={t('po.notesToSupplier')} autosize minRows={2} {...form.getInputProps('notes')} />
      </Card>
      <Group justify="flex-end" mt="lg">{buttons}</Group>
    </form>
  );
}
