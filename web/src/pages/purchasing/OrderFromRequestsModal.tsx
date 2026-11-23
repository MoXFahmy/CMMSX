import { useEffect, useState } from 'react';
import { Button, Checkbox, Group, Modal, NumberInput, Select, Table, Text } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { get, post } from '@/api/client';
import type { SupplierLookup } from '@/api/types';
import { fmtQty, notifyError, notifySuccess } from '@/components/utils';
import { previewGroups, type OrderableLine } from './purchaseRequests';

type Row = { lineId: string; on: boolean; supplierId: string | null; quantity: number | ''; unitPrice: number | '' };

/** Approved request lines (from one request or several) become draft orders grouped by supplier and site. */
export function OrderFromRequestsModal({ lines, opened, onClose }: { lines: OrderableLine[]; opened: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const navigate = useNavigate();
  const { data: suppliers } = useQuery({ queryKey: ['suppliers', 'lookup'], queryFn: () => get<SupplierLookup[]>('/suppliers/lookup'), enabled: opened });
  const [rows, setRows] = useState<Row[]>([]);
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    if (opened) setRows(lines.map((l) => ({ lineId: l.id, on: true, supplierId: l.part?.preferredSupplierId ?? null, quantity: l.quantity, unitPrice: l.estimatedUnitPrice ?? '' })));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [opened, lines.map((l) => l.id).join(',')]); // the same lines again (a fresh array each render) keep the edits
  const chosen = rows.filter((r) => r.on);
  const siteOf = new Map(lines.map((l) => [l.id, l.siteId]));
  const groups = previewGroups(chosen.map((r) => ({ supplierId: r.supplierId, siteId: siteOf.get(r.lineId) ?? '' })));
  const missing = chosen.some((r) => !r.supplierId);
  const set = (i: number, patch: Partial<Row>) => setRows((rs) => rs.map((r, k) => (k === i ? { ...r, ...patch } : r)));
  const create = async () => {
    setBusy(true);
    try {
      const res = await post<{ id: string; number: string }[]>('/purchase-requests/order', {
        lines: chosen.map((r) => ({ requestLineId: r.lineId, supplierId: r.supplierId, quantity: r.quantity === '' ? null : r.quantity, unitPrice: r.unitPrice === '' ? null : r.unitPrice })),
      });
      notifySuccess(t('pr.ordersCreated', { numbers: res.map((x) => x.number).join(', ') }));
      await qc.invalidateQueries({ queryKey: ['purchase-request'] });
      await qc.invalidateQueries({ queryKey: ['purchase-requests'] });
      await qc.invalidateQueries({ queryKey: ['purchase-orders'] });
      await qc.invalidateQueries({ queryKey: ['pr-lines-to-order'] });
      onClose();
      if (res.length === 1) navigate(`/purchasing/orders/${res[0].id}`);
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Modal opened={opened} onClose={onClose} title={t('pr.orderTitle')} size="xl">
      <Text fz="sm" c="dimmed" mb="sm">{t('pr.orderHint')}</Text>
      <Table.ScrollContainer minWidth={720}>
        <Table verticalSpacing={6}>
          <Table.Thead><Table.Tr><Table.Th w={36} /><Table.Th>{t('pr.description')}</Table.Th><Table.Th w={240}>{t('pr.supplier')}</Table.Th>
            <Table.Th w={110}>{t('pr.quantity')}</Table.Th><Table.Th w={130}>{t('pr.price')}</Table.Th></Table.Tr></Table.Thead>
          <Table.Tbody>
            {rows.map((r, i) => {
              const l = lines.find((x) => x.id === r.lineId);
              if (!l) return null;
              return (
                <Table.Tr key={r.lineId} style={{ verticalAlign: 'top' }}>
                  <Table.Td pt={10}><Checkbox checked={r.on} onChange={(e) => set(i, { on: e.currentTarget.checked })} aria-label={l.description} /></Table.Td>
                  <Table.Td><Text fz="sm" fw={500}>{l.part ? `${l.part.partNumber} · ` : ''}{l.description}</Text>
                    {l.requestNumber && <Text fz="xs" c="dimmed">{l.requestNumber}</Text>}
                    <Text fz="xs" c="dimmed">{fmtQty(l.quantity)} {l.unitOfMeasure}{l.workOrder ? ` · ${l.workOrder.number}` : ''}</Text></Table.Td>
                  <Table.Td><Select size="xs" searchable placeholder={t('pr.chooseSupplier')} data={(suppliers ?? []).map((s) => ({ value: s.id, label: s.name }))}
                    value={r.supplierId} onChange={(v) => set(i, { supplierId: v })} error={r.on && !r.supplierId ? ' ' : undefined} disabled={!r.on} /></Table.Td>
                  <Table.Td><NumberInput size="xs" min={0} decimalScale={3} value={r.quantity} onChange={(v) => set(i, { quantity: v === '' ? '' : Number(v) })} disabled={!r.on} /></Table.Td>
                  <Table.Td><NumberInput size="xs" min={0} decimalScale={4} thousandSeparator value={r.unitPrice} onChange={(v) => set(i, { unitPrice: v === '' ? '' : Number(v) })} disabled={!r.on} /></Table.Td>
                </Table.Tr>
              );
            })}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      <Group justify="flex-end" mt="md">
        <Button variant="default" onClick={onClose}>{t('common.cancel')}</Button>
        <Button loading={busy} disabled={chosen.length === 0 || missing} onClick={create}>{t('pr.orderCount', { count: groups })}</Button>
      </Group>
    </Modal>
  );
}
