import { useEffect, useMemo, useState } from 'react';
import { ActionIcon, Anchor, Badge, Button, Card, Checkbox, Group, Menu, Modal, NumberInput, Select, SimpleGrid, Stack, Switch, Tabs, Text, TextInput, Textarea, Title, Tooltip } from '@mantine/core';
import { DatePickerInput } from '@mantine/dates';
import { useDebouncedValue, useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { DataTable, type DataTableSortStatus } from 'mantine-datatable';
import { IconAlertTriangle, IconArrowsExchange, IconClipboardCheck, IconDownload, IconEdit, IconFilterOff, IconPackageExport, IconPackageImport, IconPlus, IconSearch, IconTrash, IconAdjustments, IconArrowBackUp } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import type { Lookup, Paged, PartListItem, StockTransactionItem, StockTransactionType, StoreItem } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { LookupTable } from '@/components/LookupTable';
import { useHierarchy } from '@/components/LocationPicker';
import { useStores } from '@/components/PartPicker';
import { StockTxModal, type StockTxKind } from '@/components/StockTxModal';
import { TxTypeBadge } from '@/components/InventoryBadges';
import { confirmDelete } from '@/components/confirm';
import { fmtDate, fmtPrice, fmtQty, handleFormError, notifyError, notifySuccess, toIsoDate } from '@/components/utils';

const TABS = ['parts', 'stores', 'transactions', 'stock-take', 'groups'] as const;

export function InventoryPage() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const navigate = useNavigate();
  const { tab = 'parts' } = useParams();
  const [txKind, setTxKind] = useState<StockTxKind | null>(null);
  return (
    <>
      <PageHeader title={t('inv.title')} subtitle={t('inv.subtitle')}
        actions={<>
          {can(P.inventoryTransact) && (
            <Menu position="bottom-end">
              <Menu.Target><Button variant="default" leftSection={<IconArrowsExchange size={16} />}>{t('inv.stockMovement')}</Button></Menu.Target>
              <Menu.Dropdown>
                <Menu.Item leftSection={<IconPackageImport size={16} />} onClick={() => setTxKind('receive')}>{t('inv.tx.receive')}</Menu.Item>
                <Menu.Item leftSection={<IconPackageExport size={16} />} onClick={() => setTxKind('issue')}>{t('inv.tx.issue')}</Menu.Item>
                <Menu.Item leftSection={<IconArrowBackUp size={16} />} onClick={() => setTxKind('return')}>{t('inv.tx.return')}</Menu.Item>
                <Menu.Item leftSection={<IconArrowsExchange size={16} />} onClick={() => setTxKind('transfer')}>{t('inv.tx.transfer')}</Menu.Item>
                <Menu.Item leftSection={<IconAdjustments size={16} />} onClick={() => setTxKind('adjust')}>{t('inv.tx.adjust')}</Menu.Item>
              </Menu.Dropdown>
            </Menu>
          )}
          {can(P.inventoryManage) && <Button leftSection={<IconPlus size={16} />} component={Link} to="/inventory/parts/new">{t('inv.newPart')}</Button>}
        </>} />
      <Tabs value={tab} onChange={(v) => navigate(v === 'parts' ? '/inventory' : `/inventory/${v}`)} mb="md" keepMounted={false}>
        <Tabs.List>
          {TABS.filter((x) => x !== 'stock-take' || can(P.inventoryTransact)).map((x) => <Tabs.Tab key={x} value={x}>{t(`inv.tabs.${x}`)}</Tabs.Tab>)}
        </Tabs.List>
      </Tabs>
      {tab === 'parts' && <PartsTab />}
      {tab === 'stores' && <StoresTab />}
      {tab === 'transactions' && <TransactionsTab />}
      {tab === 'stock-take' && <StockTakeTab />}
      {tab === 'groups' && <LookupTable path="/part-groups" title={t('inv.tabs.groups')} newLabel={t('inv.newGroup')} editable={can(P.inventoryManage)} />}
      {txKind && <StockTxModal kind={txKind} opened onClose={() => setTxKind(null)} />}
    </>
  );
}

function PartsTab() {
  const { t } = useTranslation();
  const { me } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const p = (k: string) => params.get(k) ?? '';
  const set = (patch: Record<string, string | null>) => {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(patch)) { if (v) next.set(k, v); else next.delete(k); }
    if (!('page' in patch)) next.delete('page');
    setParams(next, { replace: true });
  };
  const [search, setSearch] = useState(p('search'));
  const [debounced] = useDebouncedValue(search, 300);
  useEffect(() => { if (debounced !== p('search')) set({ search: debounced || null }); /* eslint-disable-next-line */ }, [debounced]);
  const page = Number(p('page') || 1);
  const pageSize = Number(p('pageSize') || 25);
  const sort: DataTableSortStatus<PartListItem> = { columnAccessor: p('sort') || 'partNumber', direction: (p('dir') as 'asc' | 'desc') || 'asc' };
  const q = { search: p('search'), groupId: p('groupId'), storeId: p('storeId'), belowReorder: p('low'), critical: p('critical'), activeOnly: p('inactive') ? '' : 'true' };
  const { data, isFetching } = useQuery({
    queryKey: ['parts', q, page, pageSize, sort], placeholderData: (prev) => prev,
    queryFn: () => get<Paged<PartListItem>>('/parts', { ...q, page, pageSize, sort: String(sort.columnAccessor), dir: sort.direction }),
  });
  const { data: groups } = useQuery({ queryKey: ['part-groups'], queryFn: () => get<Lookup[]>('/part-groups') });
  const { data: stores } = useStores();
  const filtersActive = ['search', 'groupId', 'storeId', 'low', 'critical', 'inactive'].some((k) => p(k));
  return (
    <>
      <Card p="sm" mb="md">
        <Group gap="xs" wrap="wrap">
          <TextInput placeholder={t('inv.searchParts')} leftSection={<IconSearch size={16} />} value={search} onChange={(e) => setSearch(e.currentTarget.value)} w={{ base: '100%', sm: 260 }} />
          <Select placeholder={t('inv.group')} data={(groups ?? []).map((g) => ({ value: g.id, label: g.name }))} value={p('groupId') || null} onChange={(v) => set({ groupId: v })} clearable w={{ base: '100%', xs: 180 }} />
          <Select placeholder={t('inv.store')} data={(stores ?? []).map((s) => ({ value: s.id, label: `${s.code} · ${s.name}` }))} value={p('storeId') || null} onChange={(v) => set({ storeId: v })} clearable w={{ base: '100%', xs: 200 }} />
          <Checkbox label={t('inv.belowReorder')} checked={!!p('low')} onChange={(e) => set({ low: e.currentTarget.checked ? 'true' : null })} />
          <Checkbox label={t('inv.criticalOnly')} checked={!!p('critical')} onChange={(e) => set({ critical: e.currentTarget.checked ? 'true' : null })} />
          <Checkbox label={t('common.showInactive')} checked={!!p('inactive')} onChange={(e) => set({ inactive: e.currentTarget.checked ? '1' : null })} />
          {filtersActive && <Tooltip label={t('common.clear')}><ActionIcon variant="default" size="lg" aria-label={t('common.clear')} onClick={() => { setSearch(''); setParams({}, { replace: true }); }}><IconFilterOff size={16} /></ActionIcon></Tooltip>}
        </Group>
      </Card>
      <Card p={0}>
        <DataTable<PartListItem>
          minHeight={260} withTableBorder={false} highlightOnHover striped verticalSpacing="xs" fetching={isFetching} records={data?.items ?? []}
          noRecordsText={t('inv.noParts')} totalRecords={data?.total ?? 0} recordsPerPage={pageSize} page={page} onPageChange={(n) => set({ page: String(n) })}
          recordsPerPageOptions={[25, 50, 100]} onRecordsPerPageChange={(n) => set({ pageSize: String(n) })}
          sortStatus={sort} onSortStatusChange={(s) => set({ sort: String(s.columnAccessor), dir: s.direction })}
          rowClassName={() => 'clickable-row'} onRowClick={({ record }) => navigate(`/inventory/parts/${record.id}`)}
          paginationText={({ from, to, totalRecords }) => `${from}–${to} / ${totalRecords}`}
          columns={[
            { accessor: 'partNumber', title: t('inv.partNumber'), sortable: true, noWrap: true,
              render: (x) => <Anchor component={Link} to={`/inventory/parts/${x.id}`} fw={600} fz="sm" onClick={(e) => e.stopPropagation()}>{x.partNumber}</Anchor> },
            { accessor: 'name', title: t('common.description'), sortable: true, width: 320,
              render: (x) => <Stack gap={0}><Group gap={6} wrap="nowrap"><Text fz="sm" lineClamp={1}>{x.name}</Text>{x.isCritical && <Badge size="xs" color="red" variant="light">{t('inv.critical')}</Badge>}{!x.isActive && <Badge size="xs" color="gray">{t('common.inactive')}</Badge>}</Group>
                <Text fz="xs" c="dimmed" lineClamp={1}>{[x.groupName, x.manufacturer].filter(Boolean).join(' · ') || '—'}</Text></Stack> },
            { accessor: 'onHand', title: t('inv.onHand'), textAlign: 'right', noWrap: true,
              render: (x) => <Group gap={4} justify="flex-end" wrap="nowrap">{x.low && <Tooltip label={t('inv.atReorder')}><IconAlertTriangle size={14} color="var(--mantine-color-orange-6)" /></Tooltip>}
                <Text fz="sm" fw={x.low ? 600 : undefined} c={x.onHand <= 0 ? 'red' : x.low ? 'orange' : undefined}>{fmtQty(x.onHand)} {x.unitOfMeasure}</Text></Group> },
            { accessor: 'onOrder', title: t('inv.onOrder'), textAlign: 'right', noWrap: true, render: (x) => x.onOrder ? fmtQty(x.onOrder) : <Text c="dimmed" fz="sm">—</Text> },
            { accessor: 'averageCost', title: t('inv.avgCost'), sortable: true, textAlign: 'right', noWrap: true, render: (x) => fmtPrice(x.averageCost, currency) },
            { accessor: 'value', title: t('inv.value'), textAlign: 'right', noWrap: true, render: (x) => fmtPrice(x.value, currency) },
          ]}
        />
      </Card>
    </>
  );
}

function StoresTab() {
  const { t } = useTranslation();
  const { can, me, terms } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const qc = useQueryClient();
  const { data, isFetching } = useStores();
  const { data: h } = useHierarchy();
  const [opened, m] = useDisclosure();
  const [editing, setEditing] = useState<StoreItem | null>(null);
  const form = useForm({ initialValues: { code: '', name: '', siteId: null as string | null, description: '', isActive: true } });
  const open = (s: StoreItem | null) => {
    setEditing(s); form.clearErrors();
    form.setValues(s ? { code: s.code, name: s.name, siteId: s.siteId, description: s.description ?? '', isActive: s.isActive } : { code: '', name: '', siteId: h?.sites[0]?.id ?? null, description: '', isActive: true });
    m.open();
  };
  const submit = form.onSubmit(async (v) => {
    try { if (editing) await put(`/stores/${editing.id}`, v); else await post('/stores', v); notifySuccess(t('common.saved')); await qc.invalidateQueries({ queryKey: ['stores'] }); m.close(); }
    catch (e) { handleFormError(form, e); }
  });
  const total = (data ?? []).reduce((a, s) => a + s.stockValue, 0);
  return (
    <Card p={0}>
      <Group justify="space-between" p="md">
        <Title order={5}>{t('inv.tabs.stores')} · {fmtPrice(total, currency)}</Title>
        {can(P.inventoryManage) && <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('inv.newStore')}</Button>}
      </Group>
      <DataTable minHeight={150} fetching={isFetching} records={data ?? []} noRecordsText={t('inv.noStores')} columns={[
        { accessor: 'code', title: t('common.code'), width: 120, render: (s) => <Text fw={600} fz="sm">{s.code}</Text> },
        { accessor: 'name', title: t('common.name') },
        { accessor: 'siteName', title: terms.site },
        { accessor: 'parts', title: t('inv.partsStocked'), textAlign: 'right' },
        { accessor: 'stockValue', title: t('inv.value'), textAlign: 'right', render: (s) => fmtPrice(s.stockValue, currency) },
        { accessor: 'isActive', title: t('common.status'), width: 110, render: (s) => <Badge color={s.isActive ? 'teal' : 'gray'}>{s.isActive ? t('common.active') : t('common.inactive')}</Badge> },
        { accessor: 'actions', title: '', width: 90, hidden: !can(P.inventoryManage), render: (s) => (
          <Group gap={4} justify="flex-end" wrap="nowrap">
            <ActionIcon variant="subtle" onClick={() => open(s)} aria-label={t('common.edit')}><IconEdit size={16} /></ActionIcon>
            <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => confirmDelete(s.name, async () => { try { await del(`/stores/${s.id}`); await qc.invalidateQueries({ queryKey: ['stores'] }); } catch (e) { notifyError(e); } })}><IconTrash size={16} /></ActionIcon>
          </Group>) },
      ]} />
      <Modal opened={opened} onClose={m.close} title={editing ? t('inv.editStore') : t('inv.newStore')}>
        <form onSubmit={submit}><Stack>
          <SimpleGrid cols={3}><TextInput label={t('common.code')} required {...form.getInputProps('code')} /><TextInput label={t('common.name')} required style={{ gridColumn: 'span 2' }} {...form.getInputProps('name')} /></SimpleGrid>
          <Select label={terms.site} required data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} {...form.getInputProps('siteId')} />
          <Textarea label={t('common.description')} autosize minRows={2} {...form.getInputProps('description')} />
          <Switch label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack></form>
      </Modal>
    </Card>
  );
}

export function TransactionsTable({ partId, workOrderId, purchaseOrderId, compact }: { partId?: string; workOrderId?: string; purchaseOrderId?: string; compact?: boolean }) {
  const { t } = useTranslation();
  const { me } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const [page, setPage] = useState(1);
  const [storeId, setStoreId] = useState<string | null>(null);
  const [type, setType] = useState<StockTransactionType | null>(null);
  const [range, setRange] = useState<[string | null, string | null]>([null, null]);
  const { data: stores } = useStores();
  const q = { partId, workOrderId, purchaseOrderId, storeId, type, from: toIsoDate(range[0]), to: toIsoDate(range[1]) };
  const { data, isFetching } = useQuery({ queryKey: ['stock-tx', q, page], queryFn: () => get<Paged<StockTransactionItem>>('/inventory/transactions', { ...q, page, pageSize: 25 }), placeholderData: (p) => p });
  const exportCsv = async () => {
    const all = await get<Paged<StockTransactionItem>>('/inventory/transactions', { ...q, page: 1, pageSize: 5000 });
    const head = ['Date', 'Type', 'Part', 'Description', 'Store', 'Quantity', 'Unit', 'Unit cost', 'Total', 'Work order', 'Reference', 'Batch', 'Notes', 'By'];
    const rows = all.items.map((x) => [x.date, x.type, x.partNumber, x.partName, x.storeCode, x.quantity, x.unit, x.unitCost, x.total, x.workOrderNumber, x.reference, x.batchNumber, x.notes, x.createdByName]);
    downloadCsv('stock-transactions.csv', [head, ...rows]);
  };
  return (
    <Card p={0}>
      {!compact && (
        <Group p="sm" gap="xs" wrap="wrap">
          <Select placeholder={t('inv.store')} data={(stores ?? []).map((s) => ({ value: s.id, label: s.code }))} value={storeId} onChange={(v) => { setStoreId(v); setPage(1); }} clearable w={160} />
          <Select placeholder={t('common.type')} data={(['Receipt', 'Issue', 'Return', 'Adjustment', 'TransferOut', 'TransferIn'] as const).map((x) => ({ value: x, label: t(`txType.${x}`) }))}
            value={type} onChange={(v) => { setType(v as StockTransactionType | null); setPage(1); }} clearable w={170} />
          <DatePickerInput type="range" placeholder={t('inv.dateRange')} value={range} onChange={(v) => { setRange(v as [string | null, string | null]); setPage(1); }} clearable w={250} valueFormat="DD MMM YYYY" />
          <Button variant="default" leftSection={<IconDownload size={16} />} onClick={exportCsv} ml="auto">{t('common.export')}</Button>
        </Group>
      )}
      <DataTable<StockTransactionItem>
        minHeight={compact ? 120 : 260} withTableBorder={false} striped verticalSpacing="xs" fetching={isFetching} records={data?.items ?? []} noRecordsText={t('inv.noTransactions')}
        totalRecords={data?.total ?? 0} recordsPerPage={25} page={page} onPageChange={setPage} paginationText={({ from, to, totalRecords }) => `${from}–${to} / ${totalRecords}`}
        columns={[
          { accessor: 'date', title: t('inv.date'), noWrap: true, render: (x) => fmtDate(x.date) },
          { accessor: 'type', title: t('common.type'), noWrap: true, render: (x) => <TxTypeBadge type={x.type} /> },
          { accessor: 'partNumber', title: t('inv.part'), hidden: !!partId, width: 240,
            render: (x) => <Stack gap={0}><Anchor component={Link} to={`/inventory/parts/${x.partId}`} fz="sm" fw={600}>{x.partNumber}</Anchor><Text fz="xs" c="dimmed" lineClamp={1}>{x.partName}</Text></Stack> },
          { accessor: 'storeCode', title: t('inv.store'), noWrap: true },
          { accessor: 'quantity', title: t('inv.quantity'), textAlign: 'right', noWrap: true, render: (x) => <Text fz="sm" c={x.quantity < 0 ? 'red' : 'teal'} fw={500}>{x.quantity > 0 ? '+' : ''}{fmtQty(x.quantity)} {x.unit}</Text> },
          { accessor: 'unitCost', title: t('inv.unitCost'), textAlign: 'right', noWrap: true, render: (x) => fmtPrice(x.unitCost, currency) },
          { accessor: 'total', title: t('inv.value'), textAlign: 'right', noWrap: true, render: (x) => fmtPrice(x.total, currency) },
          { accessor: 'ref', title: t('inv.reference'), width: 200, render: (x) => <Stack gap={0}>
            {x.workOrderId && <Anchor component={Link} to={`/work-orders/${x.workOrderId}`} fz="sm">{x.workOrderNumber}</Anchor>}
            {x.purchaseOrderId && <Anchor component={Link} to={`/purchasing/orders/${x.purchaseOrderId}`} fz="sm">{x.reference}</Anchor>}
            {!x.purchaseOrderId && x.reference && <Text fz="sm">{x.reference}</Text>}
            {(x.notes || x.batchNumber) && <Text fz="xs" c="dimmed" lineClamp={2}>{[x.batchNumber && `${t('inv.batch')} ${x.batchNumber}`, x.notes].filter(Boolean).join(' · ')}</Text>}</Stack> },
          { accessor: 'createdByName', title: t('inv.by'), noWrap: true, render: (x) => <Text fz="sm" c="dimmed">{x.createdByName ?? '—'}</Text> },
        ]}
      />
    </Card>
  );
}

function TransactionsTab() { return <TransactionsTable />; }

type CountRow = { partId: string; partNumber: string; name: string; unitOfMeasure: string; bin?: string; quantityOnHand: number; isShelfItem: boolean };

function StockTakeTab() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data: stores } = useStores();
  const [storeId, setStoreId] = useState<string | null>(null);
  const [counts, setCounts] = useState<Record<string, number | ''>>({});
  const [filter, setFilter] = useState('');
  const [saving, setSaving] = useState(false);
  const { data, isFetching } = useQuery({ queryKey: ['stock-take', storeId], queryFn: () => get<CountRow[]>('/inventory/stock-take', { storeId }), enabled: !!storeId });
  useEffect(() => setCounts({}), [storeId]);
  const rows = useMemo(() => (data ?? []).filter((r) => !filter || `${r.partNumber} ${r.name} ${r.bin ?? ''}`.toLowerCase().includes(filter.toLowerCase())), [data, filter]);
  const changed = (data ?? []).filter((r) => counts[r.partId] !== undefined && counts[r.partId] !== '' && counts[r.partId] !== r.quantityOnHand);
  const postCount = async () => {
    if (!storeId) return;
    setSaving(true);
    try {
      const res = await post<{ adjusted: number }>('/inventory/stock-take', { storeId, lines: changed.map((r) => ({ partId: r.partId, countedQuantity: counts[r.partId] })) });
      notifySuccess(t('inv.stockTakePosted', { count: res.adjusted }));
      setCounts({});
      await Promise.all([qc.invalidateQueries({ queryKey: ['stock-take'] }), qc.invalidateQueries({ queryKey: ['parts'] }), qc.invalidateQueries({ queryKey: ['stock-tx'] }), qc.invalidateQueries({ queryKey: ['stores'] })]);
    } catch (e) { notifyError(e); } finally { setSaving(false); }
  };
  const printSheet = () => window.print();
  return (
    <Card p={0}>
      <Group p="md" gap="xs" wrap="wrap" className="no-print">
        <Select placeholder={t('inv.chooseStore')} data={(stores ?? []).filter((s) => s.isActive).map((s) => ({ value: s.id, label: `${s.code} · ${s.name}` }))} value={storeId} onChange={setStoreId} w={260} />
        <TextInput placeholder={t('inv.filterRows')} leftSection={<IconSearch size={16} />} value={filter} onChange={(e) => setFilter(e.currentTarget.value)} w={220} disabled={!storeId} />
        <Group ml="auto" gap="xs">
          <Button variant="default" onClick={printSheet} disabled={!data?.length}>{t('inv.printSheet')}</Button>
          <Button leftSection={<IconClipboardCheck size={16} />} disabled={!changed.length} loading={saving} onClick={postCount}>{t('inv.postCount', { count: changed.length })}</Button>
        </Group>
      </Group>
      <Text px="md" pb="sm" fz="sm" c="dimmed" className="no-print">{t('inv.stockTakeHint')}</Text>
      <DataTable<CountRow> minHeight={200} withTableBorder={false} striped verticalSpacing={6} fetching={isFetching} records={rows} idAccessor="partId"
        noRecordsText={storeId ? t('inv.noStockInStore') : t('inv.chooseStore')}
        columns={[
          { accessor: 'bin', title: t('inv.bin'), width: 90, render: (r) => r.bin ?? '—' },
          { accessor: 'partNumber', title: t('inv.partNumber'), noWrap: true, render: (r) => <Text fz="sm" fw={600}>{r.partNumber}</Text> },
          { accessor: 'name', title: t('common.description') },
          { accessor: 'quantityOnHand', title: t('inv.systemQty'), textAlign: 'right', render: (r) => `${fmtQty(r.quantityOnHand)} ${r.unitOfMeasure}` },
          { accessor: 'count', title: t('inv.counted'), width: 150, render: (r) => r.isShelfItem
            ? <Text fz="xs" c="dimmed">{t('inv.countPerBatch')}</Text>
            : <NumberInput size="xs" min={0} decimalScale={3} value={counts[r.partId] ?? ''} aria-label={`${t('inv.counted')} ${r.partNumber}`}
                onChange={(v) => setCounts((c) => ({ ...c, [r.partId]: v === '' ? '' : Number(v) }))} /> },
          { accessor: 'diff', title: t('inv.difference'), textAlign: 'right', width: 110, render: (r) => {
            const c = counts[r.partId];
            if (c === undefined || c === '') return '';
            const d = c - r.quantityOnHand;
            return <Text fz="sm" fw={600} c={d === 0 ? 'dimmed' : d < 0 ? 'red' : 'teal'}>{d > 0 ? '+' : ''}{fmtQty(d)}</Text>;
          } },
        ]}
      />
    </Card>
  );
}

export function downloadCsv(name: string, rows: unknown[][]) {
  const esc = (v: unknown) => { const s = v === null || v === undefined ? '' : String(v); return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s; };
  const blob = new Blob(['﻿' + rows.map((r) => r.map(esc).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob); a.download = name; a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}
