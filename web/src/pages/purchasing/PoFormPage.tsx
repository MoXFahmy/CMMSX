import { useEffect, useRef, useState } from 'react';
import { ActionIcon, Badge, Button, Card, Center, Grid, Group, Loader, Modal, NumberInput, Select, SimpleGrid, Stack, Table, Text, TextInput, Textarea, Title } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconPlus, IconTool, IconTrash } from '@tabler/icons-react';
import { get, post, put } from '@/api/client';
import type { FilledLine, PartDetail, PoDetail, SupplierLookup, UnitRow, WorkOrderDetail } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { useHierarchy } from '@/components/LocationPicker';
import { PartPicker, useStores } from '@/components/PartPicker';
import { WorkOrderPicker } from '@/components/WorkOrderPicker';
import { fmtPrice, fmtQty, handleFormError, notifyError, notifySuccess, toIsoDate } from '@/components/utils';
import dayjs from 'dayjs';
import { perPieceOf, piecesOf, refill } from './packs';

type Line = {
  id?: string; partId: string | null; part?: { id: string; partNumber: string; name: string } | null; description: string; unitOfMeasure: string; quantity: number | '';
  unitPrice: number | ''; discountPercent: number | ''; storeId: string | null; workOrderId: string | null; workOrder?: { id: string; number: string } | null; accountCode: string;
  /** 0.18.0: pieces per ordered unit, the supplier's part number, the part's own unit, its lead time and whether the price came from the catalogue. */
  packSize: number; supplierPartNumber: string; partUnit: string; leadTimeDays: number | null; notCatalogue: boolean;
  /** 0.24.0: a repair line sends this unit (under repair) to the supplier; it has no part and is for one unit. */
  unitId?: string | null; unitLabel?: string | null;
};
type Values = {
  siteId: string | null; supplierId: string | null; orderDate: Date | string | null; expectedDate: Date | string | null; requestedBy: string; currency: string; exchangeRate: number | '';
  paymentTerms: string; shippingMethod: string; deliverTo: string; supplierQuoteRef: string; taxPercent: number | ''; shippingCost: number | ''; notes: string; lines: Line[];
  orderTypeId: string | null;
};
const blankLine = (): Line => ({ partId: null, part: null, description: '', unitOfMeasure: '', quantity: 1, unitPrice: '', discountPercent: 0, storeId: null, workOrderId: null, workOrder: null, accountCode: '',
  packSize: 1, supplierPartNumber: '', partUnit: '', leadTimeDays: null, notCatalogue: false });
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
  const { data: orderTypes } = useQuery({ queryKey: ['order-types'], queryFn: () => get<{ id: string; code: string; name: string; isActive: boolean }[]>('/order-types') });
  const [repairPick, setRepairPick] = useState(false); // 0.24.0: choosing a unit under repair for a repair line
  const form = useForm<Values>({
    initialValues: { siteId: null, supplierId: null, orderDate: new Date(), expectedDate: null, requestedBy: me?.user.fullName ?? '', currency: base, exchangeRate: 1, paymentTerms: '', shippingMethod: '',
      deliverTo: '', supplierQuoteRef: '', taxPercent: me?.tenant.settings.defaultTaxPercent ?? 0, shippingCost: 0, notes: '', lines: [blankLine()], orderTypeId: null },
  });
  const seeded = useRef(false);
  useEffect(() => {
    if (!po) return;
    form.setValues({
      siteId: po.siteId, supplierId: po.supplierId, orderDate: po.orderDate, expectedDate: po.expectedDate ?? null, requestedBy: po.requestedBy ?? '', currency: po.currency, exchangeRate: po.exchangeRate,
      paymentTerms: po.paymentTerms ?? '', shippingMethod: po.shippingMethod ?? '', deliverTo: po.deliverTo ?? '', supplierQuoteRef: po.supplierQuoteRef ?? '', taxPercent: po.taxPercent, shippingCost: po.shippingCost,
      notes: po.notes ?? '', orderTypeId: po.orderTypeId ?? null, lines: po.lines.map((l) => ({ id: l.id, partId: l.partId ?? null, part: l.partId ? { id: l.partId, partNumber: l.partNumber ?? '', name: l.description } : null, description: l.description,
        unitOfMeasure: l.unitOfMeasure ?? '', quantity: l.quantity, unitPrice: l.unitPrice, discountPercent: l.discountPercent, storeId: l.storeId ?? null, workOrderId: l.workOrderId ?? null,
        workOrder: l.workOrderId ? { id: l.workOrderId, number: l.workOrderNumber ?? '' } : null, accountCode: l.accountCode ?? '',
        packSize: l.packSize ?? 1, supplierPartNumber: l.supplierPartNumber ?? '', partUnit: l.partUnit ?? l.unitOfMeasure ?? '', leadTimeDays: null, notCatalogue: false,
        unitId: l.unitId ?? null, unitLabel: l.unitSerial ?? null })),
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
          unitPrice: p.lastPurchasePrice ?? p.averageCost, storeId: store, quantity: p.levels[0]?.reorderQuantity || 1, partUnit: p.unitOfMeasure, leadTimeDays: p.leadTimeDays ?? null }]);
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
  // New orders are Parts orders unless someone picks another type (0.16.0).
  useEffect(() => {
    if (editing || !orderTypes || form.getValues().orderTypeId) return;
    const parts = orderTypes.find((x) => x.code === 'PARTS' && x.isActive);
    if (parts) form.setFieldValue('orderTypeId', parts.id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [orderTypes]);
  /**
   * Fills part lines from the supplier's catalogue (0.18.0): each keeps at least its pieces, in the supplier's whole packs at its price. The
   * expected date, when not typed, becomes the order date + the longest lead time.
   */
  const fill = async (supplierId: string | null, only?: number) => {
    if (!supplierId) return;
    const lines = form.getValues().lines;
    const wanted = lines.map((l, i) => ({ l, i })).filter(({ l, i }) => l.partId && (only === undefined || i === only));
    if (!wanted.length) return;
    const currency = form.getValues().currency || null;
    try {
      // Each line sends its own price per piece: kept when the new supplier's catalogue has no price for it.
      const filled = await post<FilledLine[]>('/purchase-orders/fill', { supplierId, currency,
        lines: wanted.map(({ l }) => ({ partId: l.partId, pieces: piecesOf(n(l.quantity) || 1, l.packSize), perPiece: perPieceOf(l) })) });
      // An answer for a supplier or currency changed since is dropped; a line is written back only when it is still the same line.
      if (form.getValues().supplierId !== supplierId || (form.getValues().currency || null) !== currency) return;
      const changed: string[] = [];
      wanted.forEach(({ l, i }, k) => {
        const f = filled[k];
        const cur = form.getValues().lines[i];
        if (!f || f.partId !== l.partId || !cur || cur.partId !== l.partId || (cur.id ?? null) !== (l.id ?? null)) return;
        const r = refill(cur, f);
        form.setFieldValue(`lines.${i}`, r.line);
        if (r.changed) changed.push(cur.part?.partNumber ?? cur.description);
      });
      if (only === undefined && changed.length) notifySuccess(t('catalogue.refilled', { parts: changed.join(', ') }));
      const x = form.getValues();
      const lead = Math.max(0, ...x.lines.map((l) => l.leadTimeDays ?? 0));
      if (!x.expectedDate && lead > 0 && x.orderDate) form.setFieldValue('expectedDate', dayjs(x.orderDate).add(lead, 'day').toDate());
    } catch (e) { notifyError(e); }
  };
  const onSupplier = (sid: string | null) => {
    form.setFieldValue('supplierId', sid);
    const s = suppliers?.find((x) => x.id === sid);
    if (s?.currency) form.setFieldValue('currency', s.currency);
    if (s?.paymentTerms) form.setFieldValue('paymentTerms', s.paymentTerms);
    void fill(sid);
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
      lines: x.lines.filter((l) => l.partId || l.unitId || l.description.trim()).map((l) => ({ id: l.id ?? null, partId: l.unitId ? null : l.partId, description: l.description, unitOfMeasure: l.unitOfMeasure, quantity: l.unitId ? 1 : n(l.quantity),
        unitId: l.unitId ?? undefined,
        unitPrice: n(l.unitPrice), discountPercent: n(l.discountPercent), storeId: l.partId ? l.storeId : null, workOrderId: l.workOrderId, accountCode: l.accountCode,
        packSize: l.partId ? l.packSize || 1 : 1, supplierPartNumber: l.supplierPartNumber || null })),
      orderTypeId: x.orderTypeId,
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
            <SimpleGrid cols={{ base: 1, sm: 3 }} mt="sm">
              <Select label={t('approvals.orderType')} clearable data={(orderTypes ?? []).filter((x) => x.isActive || x.id === v.orderTypeId).map((x) => ({ value: x.id, label: x.name }))}
                {...form.getInputProps('orderTypeId')} />
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
          <Group gap="xs">
            <Button size="xs" variant="default" leftSection={<IconTool size={14} />} onClick={() => setRepairPick(true)}>{t('units.addRepairLine')}</Button>
            <Button size="xs" variant="light" leftSection={<IconPlus size={14} />} onClick={() => form.insertListItem('lines', blankLine())}>{t('po.addLine')}</Button>
          </Group></Group>
        <Table.ScrollContainer minWidth={1180}>
          <Table verticalSpacing={6}>
            <Table.Thead><Table.Tr>
              <Table.Th w={36}>#</Table.Th><Table.Th w={250}>{t('inv.part')}</Table.Th><Table.Th>{t('common.description')}</Table.Th><Table.Th w={90}>{t('inv.quantity')}</Table.Th><Table.Th w={80}>{t('inv.unit')}</Table.Th>
              <Table.Th w={115}>{t('po.unitPrice')}</Table.Th><Table.Th w={80}>{t('po.discount')}</Table.Th><Table.Th w={150}>{t('po.deliverTo')}</Table.Th><Table.Th w={110} ta="end">{t('po.lineTotal')}</Table.Th><Table.Th w={36} />
            </Table.Tr></Table.Thead>
            <Table.Tbody>
              {v.lines.map((l, i) => (
                <Table.Tr key={l.id ?? `new-${i}`} style={{ verticalAlign: 'top' }}>
                  <Table.Td pt={12}><Text fz="sm" c="dimmed">{i + 1}</Text></Table.Td>
                  <Table.Td>{l.unitId ? <Badge variant="light" color="orange" leftSection={<IconTool size={12} />}>{t('units.repairOf', { serial: l.unitLabel ?? '' })}</Badge>
                  : <PartPicker placeholder={t('po.nonStock')} value={l.partId} initial={l.part} withStock={false} onChange={(pid, p) => {
                    form.setFieldValue(`lines.${i}.partId`, pid); form.setFieldValue(`lines.${i}.part`, p ? { id: p.id, partNumber: p.partNumber, name: p.name } : null);
                    if (p) { form.setFieldValue(`lines.${i}.description`, p.mainName ?? p.name); form.setFieldValue(`lines.${i}.unitOfMeasure`, p.unitOfMeasure); form.setFieldValue(`lines.${i}.unitPrice`, p.lastPurchasePrice ?? p.averageCost);
                      form.setFieldValue(`lines.${i}.partUnit`, p.unitOfMeasure); form.setFieldValue(`lines.${i}.packSize`, 1); form.setFieldValue(`lines.${i}.supplierPartNumber`, '');
                      if (!form.getValues().lines[i].storeId && siteStores.length) form.setFieldValue(`lines.${i}.storeId`, siteStores[0].id);
                      if (!form.getValues().siteId && siteStores.length) form.setFieldValue('siteId', siteStores[0].siteId);
                      void fill(form.getValues().supplierId, i); }
                  }} />}
                  {l.partId && <Select mt={4} size="xs" placeholder={t('inv.store')} aria-label={t('inv.store')} data={siteStores.map((s) => ({ value: s.id, label: `${t('po.intoStore')} ${s.code}` }))} {...form.getInputProps(`lines.${i}.storeId`)} error={form.errors[`lines[${i}].storeId`]} />}
                  </Table.Td>
                  <Table.Td><TextInput aria-label={t('common.description')} {...form.getInputProps(`lines.${i}.description`)} error={form.errors[`lines[${i}].description`]} />
                    {l.supplierPartNumber && <Text fz="xs" c="dimmed" mt={2}>{t('catalogue.theirNumber', { number: l.supplierPartNumber })}</Text>}
                    <TextInput mt={4} size="xs" placeholder={t('po.accountCode')} aria-label={t('po.accountCode')} {...form.getInputProps(`lines.${i}.accountCode`)} /></Table.Td>
                  <Table.Td><NumberInput min={0} decimalScale={3} aria-label={t('inv.quantity')} disabled={!!l.unitId} {...form.getInputProps(`lines.${i}.quantity`)} error={form.errors[`lines[${i}].quantity`]} /></Table.Td>
                  <Table.Td><TextInput aria-label={t('inv.unit')} {...form.getInputProps(`lines.${i}.unitOfMeasure`)} />
                    {l.packSize !== 1 && <Text fz="xs" c="dimmed" mt={2}>= {fmtQty(piecesOf(n(l.quantity), l.packSize))} {l.partUnit}</Text>}</Table.Td>
                  <Table.Td><NumberInput min={0} decimalScale={4} thousandSeparator aria-label={t('po.unitPrice')} {...form.getInputProps(`lines.${i}.unitPrice`)} error={form.errors[`lines[${i}].unitPrice`]} />
                    {l.notCatalogue && <Text fz="xs" c="orange" mt={2}>{t('catalogue.notCataloguePrice')}</Text>}</Table.Td>
                  <Table.Td><NumberInput min={0} max={100} suffix="%" aria-label={t('po.discount')} {...form.getInputProps(`lines.${i}.discountPercent`)} /></Table.Td>
                  <Table.Td><WorkOrderPicker placeholder={t('po.forWorkOrder')} value={l.workOrderId} initial={l.workOrder} onChange={(x) => form.setFieldValue(`lines.${i}.workOrderId`, x)} /></Table.Td>
                  <Table.Td ta="end" pt={12}><Text fz="sm" fw={600}>{fmtPrice(lineTotal(l), cur)}</Text></Table.Td>
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
            {cur.toUpperCase() !== base && <Text fz="xs" c="dimmed" ta="end">≈ {fmtPrice(total * n(v.exchangeRate || 1), base)}</Text>}
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
      {repairPick && <RepairPickModal taken={v.lines.map((l) => l.unitId).filter((x): x is string => !!x)} onClose={() => setRepairPick(false)} onPick={(u) => {
        const line: Line = { ...blankLine(), unitId: u.id, unitLabel: u.serialNumber ?? u.assetNumber, quantity: 1,
          description: t('units.repairLineText', { part: u.partName ?? '', serial: u.serialNumber ?? '' }) };
        const lines = form.getValues().lines;
        if (lines.length === 1 && !lines[0].partId && !lines[0].unitId && !lines[0].description.trim()) form.setFieldValue('lines', [line]);
        else form.insertListItem('lines', line);
        setRepairPick(false);
      }} />}
    </form>
  );
}

/** Units under repair not yet on this order (0.24.0); one becomes a repair line. */
function RepairPickModal({ taken, onPick, onClose }: { taken: string[]; onPick: (u: UnitRow) => void; onClose: () => void }) {
  const { t } = useTranslation();
  const { data: units } = useQuery({ queryKey: ['serial-units', 'at-repair'], queryFn: () => get<UnitRow[]>('/serial-units/at-repair') });
  const [id, setId] = useState<string | null>(null);
  const open = (units ?? []).filter((u) => !taken.includes(u.id));
  return (
    <Modal opened onClose={onClose} title={t('units.addRepairLine')}>
      <Stack>
        <Text fz="sm" c="dimmed">{t('units.repairLineHint')}</Text>
        <Select label={t('units.unit')} searchable value={id} onChange={setId} nothingFoundMessage={t('units.noneAtRepair')}
          data={open.map((u) => ({ value: u.id, label: `${u.serialNumber} · ${u.partNumber ?? ''} ${u.partName ?? ''}`.trim() }))} />
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>{t('common.cancel')}</Button>
          <Button disabled={!id} onClick={() => { const u = open.find((x) => x.id === id); if (u) onPick(u); }}>{t('po.addLine')}</Button>
        </Group>
      </Stack>
    </Modal>
  );
}
