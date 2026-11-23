import { useState } from 'react';
import { Badge, Button, Card, Drawer, Group, Modal, Select, Stack, Table, Text, TextInput } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { DataTable } from 'mantine-datatable';
import { IconPlus, IconShoppingCartPlus } from '@tabler/icons-react';
import { get, post } from '@/api/client';
import { P, useAuth } from '@/auth/AuthContext';
import { confirmAction } from '@/components/confirm';
import { draftFromShortages } from '@/pages/purchasing/purchaseRequests';
import { fmtDate, fmtDateTime, fmtQty, notifyError, notifySuccess } from '@/components/utils';

type Store = { id: string; code: string; name: string; siteId: string };
type Shortage = { workOrderId: string; workOrderNumber: string; dueDate?: string | null; partId: string; partNumber: string; partName: string; unitOfMeasure: string; storeId: string; storeCode: string; short: number };
type CountRow = { id: string; name: string; storeId: string; storeCode: string; status: 'Open' | 'Posted' | 'Cancelled'; lines: number; createdAt: string; postedAt?: string | null };
type CountDetail = {
  id: string; name: string; status: CountRow['status']; store: { id: string; code: string; name: string };
  lines: { partId: string; partNumber: string; partName: string; unitOfMeasure: string; bin?: string | null; expected: number; counted: number; movedSince: number; difference: number; countedByName?: string | null; countedAt: string }[];
};

const useStores = () => useQuery({ queryKey: ['stores'], queryFn: () => get<Store[]>('/stores') });

/** Inventory › Short for work (0.11.0): planned parts on open work that their store can't cover — the store keeper's list. */
export function ShortagesTab() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const [store, setStore] = useState<string | null>(null);
  const [selected, setSelected] = useState<Shortage[]>([]);
  const [busy, setBusy] = useState(false);
  const { data: stores } = useStores();
  const { data, isFetching } = useQuery({ queryKey: ['shortages', store], queryFn: () => get<Shortage[]>('/inventory/shortages', { storeId: store ?? undefined }) });
  const mayRequest = can(P.purchasingRequest);
  // 0.16.0: what's short becomes draft purchase requests, one per site, each line for its work order.
  const request = async () => {
    setBusy(true);
    try {
      const siteOf = new Map((stores ?? []).map((s) => [s.id, s.siteId]));
      const drafts = draftFromShortages(selected.map((r) => ({ partId: r.partId, storeId: r.storeId, siteId: siteOf.get(r.storeId) ?? '', short: r.short, workOrderId: r.workOrderId,
        dueDate: r.dueDate ?? null, description: r.partName, unitOfMeasure: r.unitOfMeasure }))).filter((d) => d.siteId);
      const made: { id: string; number: string }[] = [];
      for (const d of drafts) made.push(await post<{ id: string; number: string }>('/purchase-requests', { siteId: d.siteId, lines: d.lines }));
      notifySuccess(t('pr.requestsCreated', { count: made.length, numbers: made.map((m) => m.number).join(', ') }));
      setSelected([]);
      await qc.invalidateQueries({ queryKey: ['purchase-requests'] });
      if (made.length === 1) navigate(`/purchasing/requests/${made[0].id}`);
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Stack>
      <Group justify="space-between">
        <Text c="dimmed" fz="sm">{t('invPhone.shortagesHint')}</Text>
        <Group gap="xs">
          {mayRequest && <Button leftSection={<IconShoppingCartPlus size={16} />} disabled={!selected.length} loading={busy} onClick={request}>{t('pr.requestSelected', { count: selected.length })}</Button>}
          <Select placeholder={t('invPhone.allStores')} clearable w={220} data={(stores ?? []).map((s) => ({ value: s.id, label: `${s.code} · ${s.name}` }))} value={store} onChange={setStore} />
        </Group>
      </Group>
      <DataTable withTableBorder borderRadius="sm" minHeight={140} fetching={isFetching} records={data ?? []} idAccessor={(r) => `${r.workOrderId}-${r.partId}-${r.storeId}`}
        selectedRecords={mayRequest ? selected : undefined} onSelectedRecordsChange={mayRequest ? setSelected : undefined}
        noRecordsText={t('invPhone.noShortages')}
        columns={[
          { accessor: 'workOrderNumber', title: t('wo.number'), render: (r) => <Link to={`/work-orders/${r.workOrderId}`}>{r.workOrderNumber}</Link> },
          { accessor: 'dueDate', title: t('wo.due'), render: (r) => fmtDate(r.dueDate) },
          { accessor: 'partNumber', title: t('inv.part'), render: (r) => <Link to={`/inventory/parts/${r.partId}`}>{r.partNumber} · {r.partName}</Link> },
          { accessor: 'storeCode', title: t('invPhone.store') },
          { accessor: 'short', title: t('invPhone.short'), textAlign: 'right', render: (r) => <Badge color="orange" variant="light">{fmtQty(r.short)} {r.unitOfMeasure}</Badge> },
        ]} />
    </Stack>
  );
}

/** Inventory › Stock counts (0.11.0): open a count, people count on the phone, review the differences and post. */
export function StockCountsTab() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const qc = useQueryClient();
  const { data: stores } = useStores();
  const { data, isFetching } = useQuery({ queryKey: ['stock-counts'], queryFn: () => get<CountRow[]>('/stock-counts') });
  const [creating, setCreating] = useState(false);
  const [store, setStore] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [open, setOpen] = useState<string | null>(null);
  const create = async () => {
    try { await post('/stock-counts', { storeId: store, name: name.trim() || null }); notifySuccess(t('common.saved')); setCreating(false); setName(''); await qc.invalidateQueries({ queryKey: ['stock-counts'] }); }
    catch (e) { notifyError(e); }
  };
  return (
    <Stack>
      <Group justify="space-between">
        <Text c="dimmed" fz="sm">{t('invPhone.countsHint')}</Text>
        {can(P.inventoryTransact) && <Button leftSection={<IconPlus size={16} />} onClick={() => setCreating(true)}>{t('invPhone.newCount')}</Button>}
      </Group>
      <DataTable withTableBorder borderRadius="sm" minHeight={140} fetching={isFetching} records={data ?? []} noRecordsText={t('common.noRecords')} onRowClick={({ record }) => setOpen(record.id)}
        columns={[
          { accessor: 'name', title: t('common.name') },
          { accessor: 'storeCode', title: t('invPhone.store') },
          { accessor: 'lines', title: t('invPhone.partsCounted'), textAlign: 'right' },
          { accessor: 'status', title: t('common.status'), render: (r) => <Badge variant="light" color={r.status === 'Open' ? 'blue' : r.status === 'Posted' ? 'teal' : 'gray'}>{t(`invPhone.status.${r.status}`)}</Badge> },
          { accessor: 'createdAt', title: t('invPhone.opened'), render: (r) => fmtDateTime(r.createdAt) },
        ]} />
      <Modal opened={creating} onClose={() => setCreating(false)} title={t('invPhone.newCount')}>
        <Stack>
          <Select label={t('invPhone.store')} required data={(stores ?? []).map((s) => ({ value: s.id, label: `${s.code} · ${s.name}` }))} value={store} onChange={setStore} />
          <TextInput label={t('common.name')} placeholder={t('invPhone.countNameHint')} value={name} onChange={(e) => setName(e.currentTarget.value)} />
          <Button disabled={!store} onClick={create}>{t('invPhone.openCount')}</Button>
        </Stack>
      </Modal>
      <CountDrawer id={open} onClose={() => setOpen(null)} />
    </Stack>
  );
}

function CountDrawer({ id, onClose }: { id: string | null; onClose: () => void }) {
  const { t } = useTranslation();
  const { can } = useAuth();
  const qc = useQueryClient();
  const { data: c } = useQuery({ queryKey: ['stock-count', id], queryFn: () => get<CountDetail>(`/stock-counts/${id}`), enabled: !!id });
  const refresh = async () => { await qc.invalidateQueries({ queryKey: ['stock-count', id] }); await qc.invalidateQueries({ queryKey: ['stock-counts'] }); };
  const act = (path: 'post' | 'cancel') => confirmAction(t(path === 'post' ? 'invPhone.postConfirm' : 'invPhone.cancelConfirm'), async () => {
    try { const r = await post<{ adjusted?: number }>(`/stock-counts/${id}/${path}`); notifySuccess(path === 'post' ? t('invPhone.posted', { count: r?.adjusted ?? 0 }) : t('common.saved')); await refresh(); }
    catch (e) { notifyError(e); }
  });
  const changed = (c?.lines ?? []).filter((l) => l.difference !== 0).length;
  return (
    <Drawer opened={!!id} onClose={onClose} position="right" size="xl" title={c ? `${c.name} · ${c.store.code}` : ''}>
      {c && (
        <Stack>
          <Group justify="space-between">
            <Badge variant="light">{t(`invPhone.status.${c.status}`)}</Badge>
            {c.status === 'Open' && can(P.inventoryTransact) && (
              <Group gap="xs">
                <Button variant="default" color="red" onClick={() => act('cancel')}>{t('invPhone.cancelCount')}</Button>
                <Button disabled={c.lines.length === 0} onClick={() => act('post')}>{t('invPhone.post', { count: changed })}</Button>
              </Group>
            )}
          </Group>
          <Card p={0}>
            <Table striped verticalSpacing={6}>
              <Table.Thead><Table.Tr>
                <Table.Th>{t('inv.part')}</Table.Th><Table.Th>{t('invPhone.bin')}</Table.Th><Table.Th ta="right">{t('invPhone.expected')}</Table.Th>
                <Table.Th ta="right">{t('invPhone.counted')}</Table.Th><Table.Th ta="right">{t('invPhone.difference')}</Table.Th><Table.Th>{t('invPhone.countedBy')}</Table.Th>
              </Table.Tr></Table.Thead>
              <Table.Tbody>
                {c.lines.length === 0 && <Table.Tr><Table.Td colSpan={6}><Text c="dimmed" ta="center" py="md">{t('invPhone.nothingCounted')}</Text></Table.Td></Table.Tr>}
                {c.lines.map((l) => (
                  <Table.Tr key={l.partId}>
                    <Table.Td><Text fz="sm">{l.partNumber} · {l.partName}</Text></Table.Td><Table.Td>{l.bin ?? '—'}</Table.Td>
                    <Table.Td ta="right">{fmtQty(l.expected)}</Table.Td><Table.Td ta="right">{fmtQty(l.counted)}{l.movedSince !== 0 && <Text fz="xs" c="dimmed">{t('invPhone.movedSince', { qty: `${l.movedSince > 0 ? '+' : ''}${fmtQty(l.movedSince)}` })}</Text>}</Table.Td>
                    <Table.Td ta="right"><Text fz="sm" c={l.difference < 0 ? 'red' : l.difference > 0 ? 'teal' : undefined}>{l.difference > 0 ? '+' : ''}{fmtQty(l.difference)}</Text></Table.Td>
                    <Table.Td><Text fz="xs" c="dimmed">{l.countedByName} · {fmtDateTime(l.countedAt)}</Text></Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Card>
        </Stack>
      )}
    </Drawer>
  );
}
