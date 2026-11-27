import { useState } from 'react';
import { ActionIcon, Badge, Button, Card, Group, Modal, NumberInput, Select, SimpleGrid, Stack, Switch, Table, Text, TextInput, Textarea, Title, Tooltip } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { IconEdit, IconPlus, IconStar, IconStarFilled, IconTrash, IconHistory } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import type { SupplierLookup, SupplierPartRow } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { confirmDelete } from '@/components/confirm';
import { fmtDate, fmtPrice, handleFormError, notifyError, notifySuccess } from '@/components/utils';

type Values = {
  supplierId: string | null; supplierPartNumber: string; packName: string; packSize: number | ''; packPrice: number | ''; currency: string; discountPercent: number | '';
  leadTimeDays: number | ''; minPacks: number | ''; notes: string; isActive: boolean; isPreferred: boolean;
};

/** A part's Suppliers tab (0.18.0): each supplier's part number, pack, price, discount, lead time and minimum, the last price paid, and the preferred star. */
export function PartSuppliers({ partId, unit }: { partId: string; unit: string }) {
  const { t } = useTranslation();
  const { can } = useAuth();
  const qc = useQueryClient();
  const editable = can(P.inventoryManage) || can(P.suppliersManage);
  const { data: rows } = useQuery({ queryKey: ['part-suppliers', partId], queryFn: () => get<SupplierPartRow[]>(`/parts/${partId}/suppliers`) });
  const { data: suppliers } = useQuery({ queryKey: ['suppliers', 'lookup'], queryFn: () => get<SupplierLookup[]>('/suppliers/lookup'), enabled: editable });
  const [opened, m] = useDisclosure();
  const [editing, setEditing] = useState<SupplierPartRow | null>(null);
  const blank: Values = { supplierId: null, supplierPartNumber: '', packName: unit, packSize: 1, packPrice: '', currency: '', discountPercent: 0, leadTimeDays: '', minPacks: 1, notes: '', isActive: true, isPreferred: false };
  const form = useForm<Values>({ initialValues: blank });
  const refresh = async () => { await qc.invalidateQueries({ queryKey: ['part-suppliers', partId] }); await qc.invalidateQueries({ queryKey: ['part', partId] }); };
  const open = (r: SupplierPartRow | null) => {
    setEditing(r); form.clearErrors();
    form.setValues(r ? {
      supplierId: r.supplierId, supplierPartNumber: r.supplierPartNumber ?? '', packName: r.packName, packSize: r.packSize, packPrice: r.packPrice ?? '', currency: r.currency,
      discountPercent: r.discountPercent, leadTimeDays: r.leadTimeDays ?? '', minPacks: r.minPacks, notes: r.notes ?? '', isActive: r.isActive, isPreferred: r.isPreferred,
    } : blank);
    m.open();
  };
  const body = (v: Values) => ({
    supplierId: v.supplierId, supplierPartNumber: v.supplierPartNumber || null, packName: v.packName || null, packSize: v.packSize === '' ? 0 : v.packSize,
    packPrice: v.packPrice === '' ? null : v.packPrice, currency: v.currency || null, discountPercent: v.discountPercent === '' ? 0 : v.discountPercent,
    leadTimeDays: v.leadTimeDays === '' ? null : v.leadTimeDays, minPacks: v.minPacks === '' ? 0 : v.minPacks, notes: v.notes || null, isActive: v.isActive, isPreferred: v.isPreferred,
  });
  const submit = form.onSubmit(async (v) => {
    try {
      if (editing) await put(`/parts/${partId}/suppliers/${editing.id}`, body(v)); else await post(`/parts/${partId}/suppliers`, body(v));
      notifySuccess(t('common.saved')); m.close(); await refresh();
    } catch (e) { handleFormError(form, e); }
  });
  const star = async (r: SupplierPartRow) => {
    try {
      await put(`/parts/${partId}/suppliers/${r.id}`, body({ ...blank, ...{ supplierId: r.supplierId, supplierPartNumber: r.supplierPartNumber ?? '', packName: r.packName, packSize: r.packSize,
        packPrice: r.packPrice ?? '', currency: r.currency, discountPercent: r.discountPercent, leadTimeDays: r.leadTimeDays ?? '', minPacks: r.minPacks, notes: r.notes ?? '', isActive: r.isActive },
        isPreferred: !r.isPreferred }));
      await refresh();
    } catch (e) { notifyError(e); }
  };
  const useLast = async (r: SupplierPartRow) => {
    try { await post(`/parts/${partId}/suppliers/${r.id}/use-last-price`, {}); notifySuccess(t('common.saved')); await refresh(); } catch (e) { notifyError(e); }
  };
  // A company no longer offered (not a supplier, inactive) still shows on its own row.
  const supplierOptions = [...(suppliers ?? []).filter((s) => !(rows ?? []).some((r) => r.supplierId === s.id && r.id !== editing?.id)).map((s) => ({ value: s.id, label: s.name })),
    ...(editing && !(suppliers ?? []).some((s) => s.id === editing.supplierId) ? [{ value: editing.supplierId, label: editing.supplierName }] : [])];
  return (
    <Card p={0}>
      <Group justify="space-between" p="md">
        <div><Title order={5}>{t('catalogue.title')}</Title><Text fz="sm" c="dimmed">{t('catalogue.hint', { unit })}</Text></div>
        {editable && <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('catalogue.add')}</Button>}
      </Group>
      <Table.ScrollContainer minWidth={820}>
        <Table striped verticalSpacing="xs">
          <Table.Thead><Table.Tr>
            <Table.Th w={36} /><Table.Th>{t('catalogue.supplier')}</Table.Th><Table.Th>{t('catalogue.pack')}</Table.Th><Table.Th ta="end">{t('catalogue.price')}</Table.Th>
            <Table.Th ta="end">{t('catalogue.discount')}</Table.Th><Table.Th ta="end">{t('catalogue.leadTime')}</Table.Th><Table.Th ta="end">{t('catalogue.minPacks')}</Table.Th>
            <Table.Th>{t('catalogue.lastPaid')}</Table.Th><Table.Th />
          </Table.Tr></Table.Thead>
          <Table.Tbody>
            {(rows ?? []).length === 0 && <Table.Tr><Table.Td colSpan={9}><Text fz="sm" c="dimmed" ta="center">{t('catalogue.none')}</Text></Table.Td></Table.Tr>}
            {(rows ?? []).map((r) => (
              <Table.Tr key={r.id}>
                <Table.Td>
                  <Tooltip label={r.isPreferred ? t('catalogue.preferred') : t('catalogue.makePreferred')}>
                    <ActionIcon variant="subtle" color="yellow" disabled={!editable} aria-label={t('catalogue.preferred')} onClick={() => void star(r)}>
                      {r.isPreferred ? <IconStarFilled size={16} /> : <IconStar size={16} />}</ActionIcon>
                  </Tooltip>
                </Table.Td>
                <Table.Td>
                  <Group gap={6} wrap="nowrap"><Text fz="sm" fw={500}>{r.supplierName}</Text>
                    {(!r.isActive || !r.supplierActive) && <Badge size="xs" color="gray">{t('common.inactive')}</Badge>}</Group>
                  {r.supplierPartNumber && <Text fz="xs" c="dimmed">{r.supplierPartNumber}</Text>}
                </Table.Td>
                <Table.Td><Text fz="sm">{r.packName}</Text>{r.packSize !== 1 && <Text fz="xs" c="dimmed">{t('catalogue.piecesPerPack', { count: r.packSize, unit })}</Text>}</Table.Td>
                <Table.Td ta="end">{r.packPrice != null ? fmtPrice(r.packPrice, r.currency) : '—'}</Table.Td>
                <Table.Td ta="end">{r.discountPercent ? `${r.discountPercent}%` : '—'}</Table.Td>
                <Table.Td ta="end">{r.leadTimeDays != null ? t('catalogue.days', { count: r.leadTimeDays }) : '—'}</Table.Td>
                <Table.Td ta="end">{r.minPacks}</Table.Td>
                <Table.Td>
                  {r.lastPaidPrice != null ? <Group gap={4} wrap="nowrap">
                    <Text fz="sm">{fmtPrice(r.lastPaidPrice, r.lastPaidCurrency ?? r.currency)}{r.lastPaidDiscount ? ` −${r.lastPaidDiscount}%` : ''}</Text>
                    <Text fz="xs" c="dimmed">{fmtDate(r.lastPaidOn)}</Text>
                    {editable && r.lastPaidCurrency === r.currency && (r.lastPaidPrice !== r.packPrice || (r.lastPaidDiscount ?? 0) !== r.discountPercent) &&
                      <Tooltip label={t('catalogue.useLastPrice')}><ActionIcon size="sm" variant="subtle" aria-label={t('catalogue.useLastPrice')} onClick={() => void useLast(r)}><IconHistory size={14} /></ActionIcon></Tooltip>}
                  </Group> : <Text fz="sm" c="dimmed">—</Text>}
                </Table.Td>
                <Table.Td>{editable && <Group gap={2} justify="flex-end" wrap="nowrap">
                  <ActionIcon variant="subtle" aria-label={t('common.edit')} onClick={() => open(r)}><IconEdit size={16} /></ActionIcon>
                  <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => confirmDelete(r.supplierName, async () => {
                    try { await del(`/parts/${partId}/suppliers/${r.id}`); await refresh(); } catch (e) { notifyError(e); } })}><IconTrash size={16} /></ActionIcon>
                </Group>}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      <Modal opened={opened} onClose={m.close} title={editing ? t('catalogue.edit') : t('catalogue.add')} size="lg">
        <form onSubmit={submit}><Stack>
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <Select label={t('catalogue.supplier')} required searchable data={supplierOptions} {...form.getInputProps('supplierId')} />
            <TextInput label={t('catalogue.supplierPartNumber')} {...form.getInputProps('supplierPartNumber')} />
          </SimpleGrid>
          <SimpleGrid cols={{ base: 1, sm: 3 }}>
            <TextInput label={t('catalogue.pack')} placeholder={unit} description={t('catalogue.packHint')} {...form.getInputProps('packName')} />
            <NumberInput label={t('catalogue.packSize', { unit })} min={0} decimalScale={4} {...form.getInputProps('packSize')} />
            <NumberInput label={t('catalogue.minPacks')} min={1} {...form.getInputProps('minPacks')} />
          </SimpleGrid>
          <SimpleGrid cols={{ base: 1, sm: 4 }}>
            <NumberInput label={t('catalogue.price')} description={t('catalogue.pricePerPack')} min={0} decimalScale={4} {...form.getInputProps('packPrice')} />
            <TextInput label={t('po.currency')} description={t('catalogue.currencyHint')} maxLength={3} {...form.getInputProps('currency')} />
            <NumberInput label={t('catalogue.discount')} min={0} max={100} decimalScale={3} rightSection="%" {...form.getInputProps('discountPercent')} />
            <NumberInput label={t('catalogue.leadTime')} min={0} rightSection={t('catalogue.daysShort')} rightSectionWidth={44} {...form.getInputProps('leadTimeDays')} />
          </SimpleGrid>
          <Textarea label={t('common.notes')} autosize {...form.getInputProps('notes')} />
          <Group>
            <Switch label={t('catalogue.preferred')} {...form.getInputProps('isPreferred', { type: 'checkbox' })} />
            <Switch label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
          </Group>
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack></form>
      </Modal>
    </Card>
  );
}
