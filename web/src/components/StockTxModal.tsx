import { useEffect, useMemo, useState } from 'react';
import { Alert, Button, Group, Modal, NumberInput, SegmentedControl, Select, SimpleGrid, Stack, Text, TextInput, Textarea } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { get, post } from '@/api/client';
import type { PartDetail, PartLookup, SupplierLookup } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { locationOptions, useHierarchy } from './LocationPicker';
import { PartPicker, useStores } from './PartPicker';
import { WorkOrderPicker } from './WorkOrderPicker';
import { fmtPrice, fmtQty, handleFormError, notifySuccess, toIsoDate } from './utils';

export type StockTxKind = 'receive' | 'issue' | 'return' | 'adjust' | 'transfer';

type Defaults = { partId?: string; part?: { id: string; partNumber: string; name: string } | null; storeId?: string | null; workOrderId?: string; workOrder?: { id: string; number: string; title?: string } | null };

/** One dialog for every stock movement: receipts, issues and returns to work, adjustments and store transfers. */
export function StockTxModal({ kind, opened, onClose, onDone, defaults }: { kind: StockTxKind; opened: boolean; onClose: () => void; onDone?: () => void | Promise<void>; defaults?: Defaults }) {
  const { t } = useTranslation();
  const { me } = useAuth();
  const qc = useQueryClient();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const { data: stores } = useStores();
  const { data: h } = useHierarchy();
  const { data: suppliers } = useQuery({ queryKey: ['suppliers', 'lookup'], queryFn: () => get<SupplierLookup[]>('/suppliers/lookup'), enabled: opened && kind === 'receive' });
  const [part, setPart] = useState<PartLookup | null>(null);
  const [target, setTarget] = useState<'wo' | 'location'>('wo');
  const [direction, setDirection] = useState<'add' | 'remove'>('remove');
  const form = useForm({
    initialValues: {
      partId: '' as string | null, storeId: '' as string | null, toStoreId: '' as string | null, quantity: '' as number | '', unitCost: '' as number | '', date: new Date() as Date | string | null,
      supplierId: null as string | null, reference: '', batchNumber: '', expiryDate: null as Date | string | null, workOrderId: null as string | null, locationId: null as string | null, notes: '',
    },
  });
  useEffect(() => {
    if (!opened) return;
    form.setValues({ partId: defaults?.partId ?? null, storeId: defaults?.storeId ?? (stores?.length === 1 ? stores[0].id : null), toStoreId: null, quantity: '', unitCost: '', date: new Date(),
      supplierId: null, reference: '', batchNumber: '', expiryDate: null, workOrderId: defaults?.workOrderId ?? null, locationId: null, notes: '' });
    form.clearErrors(); setPart(null); setTarget('wo'); setDirection('remove');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [opened]);
  const v = form.getValues();
  const { data: detail } = useQuery({ queryKey: ['part', v.partId], queryFn: () => get<PartDetail>(`/parts/${v.partId}`), enabled: opened && !!v.partId });
  useEffect(() => {
    if (kind === 'receive' && detail && form.getValues().unitCost === '') form.setFieldValue('unitCost', detail.lastPurchasePrice ?? detail.averageCost);
    if (kind === 'receive' && detail?.preferredSupplierId && !form.getValues().supplierId) form.setFieldValue('supplierId', detail.preferredSupplierId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [detail?.id]);
  const shelf = detail?.isShelfItem ?? part?.isShelfItem ?? false;
  const storeOpts = useMemo(() => (stores ?? []).filter((s) => s.isActive).map((s) => {
    const lvl = detail?.levels.find((l) => l.storeId === s.id);
    return { value: s.id, label: detail ? `${s.code} (${fmtQty(lvl?.quantityOnHand ?? 0)} ${detail.unitOfMeasure})` : `${s.code} · ${s.name}` };
  }), [stores, detail]);
  const onHand = detail?.levels.find((l) => l.storeId === v.storeId)?.quantityOnHand;
  const batches = (detail?.batches ?? []).filter((b) => b.storeId === v.storeId && b.quantity > 0);
  const locOpts = useMemo(() => (h ? (h.sites ?? []).flatMap((s) => locationOptions(h.locations, s.id).map((o) => ({ ...o, label: `${s.name} › ${o.label}` }))) : []), [h]);

  const submit = form.onSubmit(async (x) => {
    const base = { partId: x.partId, quantity: x.quantity || 0, date: toIsoDate(x.date), batchNumber: x.batchNumber || null, notes: x.notes || null };
    try {
      if (kind === 'receive') await post('/inventory/receive', { ...base, storeId: x.storeId, unitCost: x.unitCost === '' ? null : x.unitCost, supplierId: x.supplierId, reference: x.reference || null, expiryDate: toIsoDate(x.expiryDate) });
      else if (kind === 'issue' || kind === 'return')
        await post(`/inventory/${kind}`, { ...base, storeId: x.storeId, workOrderId: target === 'wo' ? x.workOrderId : null, locationId: target === 'location' ? x.locationId : null });
      else if (kind === 'adjust') await post('/inventory/adjust', { ...base, storeId: x.storeId, quantity: (direction === 'remove' ? -1 : 1) * Number(x.quantity || 0), notes: x.notes });
      else await post('/inventory/transfer', { ...base, fromStoreId: x.storeId, toStoreId: x.toStoreId });
      notifySuccess(t(`inv.done.${kind}`));
      await Promise.all([qc.invalidateQueries({ queryKey: ['part'] }), qc.invalidateQueries({ queryKey: ['parts'] }), qc.invalidateQueries({ queryKey: ['stock-tx'] }),
        qc.invalidateQueries({ queryKey: ['stores'] }), qc.invalidateQueries({ queryKey: ['part-lookup'] }), qc.invalidateQueries({ queryKey: ['proposal'] })]);
      await onDone?.();
      onClose();
    } catch (e) { handleFormError(form, e); }
  });

  return (
    <Modal opened={opened} onClose={onClose} title={t(`inv.tx.${kind}`)} size="lg">
      <form onSubmit={submit}>
        <Stack>
          <PartPicker label={t('inv.part')} required value={v.partId} initial={defaults?.part} storeId={kind === 'transfer' ? null : v.storeId}
            onChange={(id, p) => { form.setFieldValue('partId', id); setPart(p); form.setFieldValue('unitCost', ''); }} error={form.errors.partId} />
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <Select label={kind === 'transfer' ? t('inv.fromStore') : t('inv.store')} required data={storeOpts} {...form.getInputProps('storeId')} searchable />
            {kind === 'transfer'
              ? <Select label={t('inv.toStore')} required data={storeOpts.filter((o) => o.value !== v.storeId)} {...form.getInputProps('toStoreId')} searchable />
              : <DateInput label={t('inv.date')} required maxDate={new Date()} valueFormat="DD MMM YYYY" {...form.getInputProps('date')} />}
          </SimpleGrid>
          {kind === 'adjust' && <SegmentedControl value={direction} onChange={(x) => setDirection(x as 'add' | 'remove')} data={[{ value: 'remove', label: t('inv.adjustRemove') }, { value: 'add', label: t('inv.adjustAdd') }]} />}
          <SimpleGrid cols={{ base: 1, sm: kind === 'receive' ? 2 : 1 }}>
            <NumberInput label={`${t('inv.quantity')}${detail ? ` (${detail.unitOfMeasure})` : ''}`} required min={0} decimalScale={3} {...form.getInputProps('quantity')}
              description={onHand !== undefined && kind !== 'receive' ? t('inv.onHandInStore', { qty: fmtQty(onHand) }) : undefined} />
            {kind === 'receive' && <NumberInput label={t('inv.unitCost')} required min={0} decimalScale={4} thousandSeparator prefix={currency === 'USD' ? '$' : undefined}
              description={detail ? t('inv.avgCostHint', { cost: fmtPrice(detail.averageCost, currency) }) : undefined} {...form.getInputProps('unitCost')} />}
          </SimpleGrid>
          {(kind === 'issue' || kind === 'return') && (
            <>
              <SegmentedControl value={target} onChange={(x) => setTarget(x as 'wo' | 'location')} data={[{ value: 'wo', label: t('inv.toWorkOrder') }, { value: 'location', label: t('inv.toLocation') }]} />
              {target === 'wo'
                ? <WorkOrderPicker label={t('inv.workOrder')} required value={v.workOrderId} initial={defaults?.workOrder} onChange={(x) => form.setFieldValue('workOrderId', x)} error={form.errors.workOrderId} />
                : <Select label={t('inv.location')} required data={locOpts} searchable {...form.getInputProps('locationId')} />}
            </>
          )}
          {kind === 'receive' && (
            <SimpleGrid cols={{ base: 1, sm: 2 }}>
              <Select label={t('po.supplier')} data={(suppliers ?? []).map((s) => ({ value: s.id, label: s.name }))} searchable clearable {...form.getInputProps('supplierId')} />
              <TextInput label={t('inv.reference')} placeholder={t('inv.referenceHint')} {...form.getInputProps('reference')} />
            </SimpleGrid>
          )}
          {shelf && (
            <SimpleGrid cols={{ base: 1, sm: 2 }}>
              {kind === 'receive' || batches.length === 0
                ? <TextInput label={t('inv.batch')} required {...form.getInputProps('batchNumber')} />
                : <Select label={t('inv.batch')} required data={batches.map((b) => ({ value: b.batchNumber, label: `${b.batchNumber} — ${fmtQty(b.quantity)}${b.expiry ? ` · exp. ${b.expiry}` : ''}` }))} {...form.getInputProps('batchNumber')} />}
              {kind === 'receive' && <DateInput label={t('inv.expiry')} required valueFormat="DD MMM YYYY" minDate={new Date()} {...form.getInputProps('expiryDate')} />}
            </SimpleGrid>
          )}
          {shelf && <Text fz="xs" c="dimmed">{t('inv.shelfHint')}</Text>}
          {kind === 'adjust'
            ? <Textarea label={t('inv.reason')} required autosize minRows={2} {...form.getInputProps('notes')} />
            : <TextInput label={t('common.notes')} {...form.getInputProps('notes')} />}
          {kind === 'issue' && onHand !== undefined && Number(v.quantity || 0) > onHand && !detail?.allowNegativeStock && <Alert color="orange" p="xs">{t('inv.notEnough')}</Alert>}
          <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t(`inv.tx.${kind}`)}</Button></Group>
        </Stack>
      </form>
    </Modal>
  );
}
