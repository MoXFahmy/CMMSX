import { useEffect, useMemo, useState } from 'react';
import { ActionIcon, Anchor, Badge, Button, Card, Checkbox, Group, Modal, NumberInput, Progress, Select, SimpleGrid, Stack, Switch, Tabs, Text, TextInput, Textarea, Tooltip, Alert } from '@mantine/core';
import { useDebouncedValue, useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { DataTable, type DataTableSortStatus } from 'mantine-datatable';
import { IconEdit, IconFilterOff, IconPlus, IconSearch, IconShoppingCartPlus, IconTrash } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import { PO_STATUSES, type Paged, type PoListItem, type ProposalItem, type PurchaseOrderStatus, type SupplierDetail, type SupplierItem, type SupplierLookup } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { useHierarchy } from '@/components/LocationPicker';
import { PoStatusBadge } from '@/components/InventoryBadges';
import { confirmDelete } from '@/components/confirm';
import { fmtDate, fmtPrice, fmtQty, handleFormError, notifyError, notifySuccess } from '@/components/utils';

const VIEWS: { key: string; statuses: PurchaseOrderStatus[] | null }[] = [
  { key: 'open', statuses: ['Draft', 'Submitted', 'Approved', 'PartiallyReceived'] }, { key: 'Draft', statuses: ['Draft'] }, { key: 'Submitted', statuses: ['Submitted'] },
  { key: 'receiving', statuses: ['Approved', 'PartiallyReceived'] }, { key: 'done', statuses: ['Received', 'Closed'] }, { key: 'Cancelled', statuses: ['Cancelled'] }, { key: 'all', statuses: null },
];

export function PurchasingPage() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const navigate = useNavigate();
  const { tab = 'orders' } = useParams();
  return (
    <>
      <PageHeader title={t('po.title')} subtitle={t('po.subtitle')}
        actions={can(P.purchasingCreate) && <Button leftSection={<IconPlus size={16} />} component={Link} to="/purchasing/orders/new">{t('po.new')}</Button>} />
      <Tabs value={tab} onChange={(v) => navigate(v === 'orders' ? '/purchasing' : `/purchasing/${v}`)} mb="md" keepMounted={false}>
        <Tabs.List>
          <Tabs.Tab value="orders">{t('po.tabs.orders')}</Tabs.Tab>
          {can(P.inventoryView) && <Tabs.Tab value="proposal">{t('po.tabs.proposal')}</Tabs.Tab>}
          <Tabs.Tab value="suppliers">{t('po.tabs.suppliers')}</Tabs.Tab>
        </Tabs.List>
      </Tabs>
      {tab === 'orders' && <OrdersTab />}
      {tab === 'proposal' && <ProposalTab />}
      {tab === 'suppliers' && <SuppliersTab />}
    </>
  );
}

function OrdersTab() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { terms } = useAuth();
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
  const view = p('view') || 'open';
  const statuses = VIEWS.find((v) => v.key === view)?.statuses ?? null;
  const page = Number(p('page') || 1);
  const sort: DataTableSortStatus<PoListItem> = { columnAccessor: p('sort') || 'orderDate', direction: (p('dir') as 'asc' | 'desc') || 'desc' };
  const q = { search: p('search'), supplierId: p('supplierId'), siteId: p('siteId'), overdue: p('overdue') };
  const { data, isFetching } = useQuery({
    queryKey: ['purchase-orders', q, view, page, sort], placeholderData: (prev) => prev,
    queryFn: () => get<Paged<PoListItem>>('/purchase-orders', { ...q, status: statuses ?? undefined, page, pageSize: 25, sort: String(sort.columnAccessor), dir: sort.direction }),
  });
  const { data: suppliers } = useQuery({ queryKey: ['suppliers', 'lookup'], queryFn: () => get<SupplierLookup[]>('/suppliers/lookup') });
  const { data: h } = useHierarchy();
  const filtersActive = ['search', 'supplierId', 'siteId', 'overdue'].some((k) => p(k));
  return (
    <>
      <Card p="sm" mb="md">
        <Group gap="xs" wrap="wrap">
          <TextInput placeholder={t('po.searchPlaceholder')} leftSection={<IconSearch size={16} />} value={search} onChange={(e) => setSearch(e.currentTarget.value)} w={{ base: '100%', sm: 240 }} />
          <Select placeholder={t('po.supplier')} data={(suppliers ?? []).map((s) => ({ value: s.id, label: s.name }))} value={p('supplierId') || null} onChange={(v) => set({ supplierId: v })} clearable searchable w={{ base: '100%', xs: 200 }} />
          <Select placeholder={terms.site} data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} value={p('siteId') || null} onChange={(v) => set({ siteId: v })} clearable w={{ base: '100%', xs: 180 }} />
          <Checkbox label={t('po.overdueOnly')} checked={!!p('overdue')} onChange={(e) => set({ overdue: e.currentTarget.checked ? 'true' : null })} />
          {filtersActive && <Tooltip label={t('common.clear')}><ActionIcon variant="default" size="lg" aria-label={t('common.clear')} onClick={() => { setSearch(''); setParams(view !== 'open' ? { view } : {}, { replace: true }); }}><IconFilterOff size={16} /></ActionIcon></Tooltip>}
        </Group>
      </Card>
      <Tabs value={view} onChange={(v) => set({ view: v === 'open' ? null : v })} mb="sm" variant="pills" radius="xl">
        <Tabs.List>{VIEWS.map((v) => <Tabs.Tab key={v.key} value={v.key}>{t(`po.views.${v.key}`)}</Tabs.Tab>)}</Tabs.List>
      </Tabs>
      <Card p={0}>
        <DataTable<PoListItem>
          minHeight={260} withTableBorder={false} highlightOnHover striped verticalSpacing="xs" fetching={isFetching} records={data?.items ?? []}
          noRecordsText={t('po.empty')} totalRecords={data?.total ?? 0} recordsPerPage={25} page={page} onPageChange={(n) => set({ page: String(n) })}
          sortStatus={sort} onSortStatusChange={(s) => set({ sort: String(s.columnAccessor), dir: s.direction })}
          rowClassName={() => 'clickable-row'} onRowClick={({ record }) => navigate(`/purchasing/orders/${record.id}`)}
          paginationText={({ from, to, totalRecords }) => `${from}–${to} / ${totalRecords}`}
          columns={[
            { accessor: 'number', title: t('po.number'), sortable: true, noWrap: true,
              render: (x) => <Anchor component={Link} to={`/purchasing/orders/${x.id}`} fw={600} fz="sm" onClick={(e) => e.stopPropagation()}>{x.number}</Anchor> },
            { accessor: 'supplier', title: t('po.supplier'), sortable: true, width: 260,
              render: (x) => <Stack gap={0}><Text fz="sm" lineClamp={1}>{x.supplierName}</Text><Text fz="xs" c="dimmed" lineClamp={1}>{x.siteName}{x.requestedBy && ` · ${x.requestedBy}`}</Text></Stack> },
            { accessor: 'status', title: t('common.status'), sortable: true, noWrap: true, render: (x) => <PoStatusBadge status={x.status} /> },
            { accessor: 'orderDate', title: t('po.orderDate'), sortable: true, noWrap: true, render: (x) => fmtDate(x.orderDate) },
            { accessor: 'expectedDate', title: t('po.expected'), sortable: true, noWrap: true,
              render: (x) => <Group gap={4} wrap="nowrap"><Text fz="sm" c={x.isOverdue ? 'red' : undefined} fw={x.isOverdue ? 600 : undefined}>{fmtDate(x.expectedDate)}</Text>{x.isOverdue && <Badge size="xs" color="red" variant="filled">{t('wo.overdueBadge')}</Badge>}</Group> },
            { accessor: 'received', title: t('po.received'), width: 130, render: (x) => x.status === 'Draft' || x.status === 'Submitted' || x.status === 'Cancelled' ? <Text fz="sm" c="dimmed">—</Text>
              : <Group gap={6} wrap="nowrap"><Progress value={x.receivedPercent} size="sm" w={60} color={x.receivedPercent >= 100 ? 'teal' : 'blue'} /><Text fz="xs" c="dimmed">{x.receivedPercent}%</Text></Group> },
            { accessor: 'total', title: t('po.total'), textAlign: 'right', noWrap: true, render: (x) => fmtPrice(x.total, x.currency) },
          ]}
        />
      </Card>
    </>
  );
}

function ProposalTab() {
  const { t } = useTranslation();
  const { can, me, terms } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const qc = useQueryClient();
  const navigate = useNavigate();
  const [siteId, setSiteId] = useState<string | null>(null);
  const { data: h } = useHierarchy();
  const { data, isFetching } = useQuery({ queryKey: ['proposal', siteId], queryFn: () => get<ProposalItem[]>('/inventory/proposal', { siteId }) });
  const { data: suppliers } = useQuery({ queryKey: ['suppliers', 'lookup'], queryFn: () => get<SupplierLookup[]>('/suppliers/lookup') });
  const key = (r: ProposalItem) => `${r.partId}|${r.storeId}`;
  const [selected, setSelected] = useState<ProposalItem[]>([]);
  const [edits, setEdits] = useState<Record<string, { qty?: number; supplierId?: string | null }>>({});
  const [busy, setBusy] = useState(false);
  useEffect(() => { setSelected([]); setEdits({}); }, [data]);
  const qty = (r: ProposalItem) => edits[key(r)]?.qty ?? r.suggestedQuantity;
  const sup = (r: ProposalItem) => (edits[key(r)] && 'supplierId' in edits[key(r)] ? edits[key(r)].supplierId : r.preferredSupplierId) ?? null;
  const missingSupplier = selected.filter((r) => !sup(r));
  const total = selected.reduce((a, r) => a + qty(r) * r.unitPrice, 0);
  const groups = new Set(selected.map((r) => `${sup(r)}|${r.siteId}`)).size;
  const create = async () => {
    setBusy(true);
    try {
      const res = await post<{ id: string; number: string; supplier: string; lines: number }[]>('/purchase-orders/from-proposal', {
        lines: selected.map((r) => ({ partId: r.partId, storeId: r.storeId, quantity: qty(r), unitPrice: r.unitPrice, supplierId: sup(r) })),
      });
      notifySuccess(t('po.proposalCreated', { count: res.length, numbers: res.map((r) => r.number).join(', ') }));
      await qc.invalidateQueries({ queryKey: ['proposal'] });
      await qc.invalidateQueries({ queryKey: ['purchase-orders'] });
      if (res.length === 1) navigate(`/purchasing/orders/${res[0].id}`);
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <>
      <Card p="sm" mb="md">
        <Group gap="xs" wrap="wrap">
          <Select placeholder={terms.site} data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} value={siteId} onChange={setSiteId} clearable w={220} />
          <Text fz="sm" c="dimmed" style={{ flex: '1 1 300px' }}>{t('po.proposalHint')}</Text>
          {can(P.purchasingCreate) && <Button leftSection={<IconShoppingCartPlus size={16} />} disabled={!selected.length || missingSupplier.length > 0} loading={busy} onClick={create}>
            {selected.length ? t('po.createOrders', { count: groups, total: fmtPrice(total, currency) }) : t('po.selectLines')}</Button>}
        </Group>
        {missingSupplier.length > 0 && <Alert color="orange" mt="sm" p="xs">{t('po.chooseSupplierFor', { parts: missingSupplier.map((r) => r.partNumber).join(', ') })}</Alert>}
      </Card>
      <Card p={0}>
        <DataTable<ProposalItem>
          minHeight={220} withTableBorder={false} striped verticalSpacing={6} fetching={isFetching} records={data ?? []} idAccessor={key}
          selectedRecords={can(P.purchasingCreate) ? selected : undefined} onSelectedRecordsChange={can(P.purchasingCreate) ? setSelected : undefined}
          noRecordsText={t('po.proposalEmpty')}
          columns={[
            { accessor: 'partNumber', title: t('inv.part'), width: 260, render: (r) => <Stack gap={0}>
              <Group gap={6} wrap="nowrap"><Anchor component={Link} to={`/inventory/parts/${r.partId}`} fz="sm" fw={600}>{r.partNumber}</Anchor>
                {r.isCritical && <Badge size="xs" color="red" variant="light">{t('inv.critical')}</Badge>}</Group><Text fz="xs" c="dimmed" lineClamp={1}>{r.partName}</Text></Stack> },
            { accessor: 'storeCode', title: t('inv.store'), noWrap: true },
            { accessor: 'quantityOnHand', title: t('inv.onHand'), textAlign: 'right', noWrap: true,
              render: (r) => <Text fz="sm" fw={600} c={r.quantityOnHand <= 0 ? 'red' : r.belowMinimum ? 'orange' : undefined}>{fmtQty(r.quantityOnHand)} {r.unitOfMeasure}</Text> },
            { accessor: 'levels', title: t('po.minReorderMax'), noWrap: true, render: (r) => <Text fz="sm" c="dimmed">{fmtQty(r.minLevel)} / {fmtQty(r.reorderLevel)} / {fmtQty(r.maxLevel)}</Text> },
            { accessor: 'onOrder', title: t('inv.onOrder'), textAlign: 'right', render: (r) => r.onOrder ? fmtQty(r.onOrder) : '—' },
            { accessor: 'qty', title: t('po.orderQty'), width: 120, render: (r) => <NumberInput size="xs" min={0} decimalScale={3} value={qty(r)} aria-label={`${t('po.orderQty')} ${r.partNumber}`}
              onClick={(e) => e.stopPropagation()} onChange={(v) => setEdits((x) => ({ ...x, [key(r)]: { ...x[key(r)], qty: Number(v) || 0 } }))} /> },
            { accessor: 'supplier', title: t('po.supplier'), width: 210, render: (r) => <Select size="xs" data={(suppliers ?? []).map((s) => ({ value: s.id, label: s.name }))} value={sup(r)} searchable
              aria-label={`${t('po.supplier')} ${r.partNumber}`} placeholder={t('po.chooseSupplier')} onClick={(e) => e.stopPropagation()}
              onChange={(v) => setEdits((x) => ({ ...x, [key(r)]: { ...x[key(r)], supplierId: v } }))} /> },
            { accessor: 'value', title: t('po.estValue'), textAlign: 'right', noWrap: true, render: (r) => fmtPrice(qty(r) * r.unitPrice, currency) },
          ]}
        />
      </Card>
    </>
  );
}

const blankSupplier = { code: '', name: '', contactName: '', email: '', phone: '', address: '', city: '', country: '', taxNumber: '', paymentTerms: '', currency: '', isContractor: false, notes: '', isActive: true };

function SuppliersTab() {
  const { t } = useTranslation();
  const { can, me } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const qc = useQueryClient();
  const [search, setSearch] = useState('');
  const [debounced] = useDebouncedValue(search, 300);
  const [page, setPage] = useState(1);
  const [inactive, setInactive] = useState(false);
  const { data, isFetching } = useQuery({ queryKey: ['suppliers', debounced, inactive, page], queryFn: () => get<Paged<SupplierItem>>('/suppliers', { search: debounced, activeOnly: inactive ? '' : 'true', page, pageSize: 25 }), placeholderData: (p) => p });
  const [opened, m] = useDisclosure();
  const [editingId, setEditingId] = useState<string | null>(null);
  const form = useForm({ initialValues: blankSupplier });
  const open = async (id: string | null) => {
    setEditingId(id); form.clearErrors();
    if (id) { const s = await get<SupplierDetail>(`/suppliers/${id}`); form.setValues(Object.fromEntries(Object.keys(blankSupplier).map((k) => [k, (s as Record<string, unknown>)[k] ?? (blankSupplier as Record<string, unknown>)[k]])) as typeof blankSupplier); }
    else form.setValues(blankSupplier);
    m.open();
  };
  const submit = form.onSubmit(async (v) => {
    try { if (editingId) await put(`/suppliers/${editingId}`, v); else await post('/suppliers', v); notifySuccess(t('common.saved')); await qc.invalidateQueries({ queryKey: ['suppliers'] }); m.close(); }
    catch (e) { handleFormError(form, e); }
  });
  const manage = can(P.suppliersManage);
  const rows = useMemo(() => data?.items ?? [], [data]);
  return (
    <Card p={0}>
      <Group p="md" gap="xs" wrap="wrap">
        <TextInput placeholder={t('po.searchSuppliers')} leftSection={<IconSearch size={16} />} value={search} onChange={(e) => { setSearch(e.currentTarget.value); setPage(1); }} w={260} />
        <Checkbox label={t('common.showInactive')} checked={inactive} onChange={(e) => setInactive(e.currentTarget.checked)} />
        {manage && <Button size="xs" ml="auto" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('po.newSupplier')}</Button>}
      </Group>
      <DataTable<SupplierItem> minHeight={200} withTableBorder={false} striped highlightOnHover verticalSpacing="xs" fetching={isFetching} records={rows} noRecordsText={t('po.noSuppliers')}
        totalRecords={data?.total ?? 0} recordsPerPage={25} page={page} onPageChange={setPage}
        rowClassName={() => (manage ? 'clickable-row' : '')} onRowClick={manage ? ({ record }) => open(record.id) : undefined}
        columns={[
          { accessor: 'code', title: t('common.code'), width: 110, render: (s) => <Text fz="sm" fw={600}>{s.code}</Text> },
          { accessor: 'name', title: t('common.name'), render: (s) => <Group gap={6} wrap="nowrap"><Text fz="sm">{s.name}</Text>{s.isContractor && <Badge size="xs" variant="light">{t('po.contractor')}</Badge>}{!s.isActive && <Badge size="xs" color="gray">{t('common.inactive')}</Badge>}</Group> },
          { accessor: 'contact', title: t('po.contact'), render: (s) => <Stack gap={0}><Text fz="sm">{s.contactName ?? '—'}</Text><Text fz="xs" c="dimmed">{[s.email, s.phone].filter(Boolean).join(' · ')}</Text></Stack> },
          { accessor: 'city', title: t('po.city'), render: (s) => [s.city, s.country].filter(Boolean).join(', ') || '—' },
          { accessor: 'paymentTerms', title: t('po.paymentTerms'), render: (s) => s.paymentTerms ?? '—' },
          { accessor: 'openOrders', title: t('po.openOrders'), textAlign: 'right' },
          { accessor: 'receivedYtd', title: t('po.receivedYtd'), textAlign: 'right', noWrap: true, render: (s) => fmtPrice(s.receivedYtd, currency, 0) },
          { accessor: 'actions', title: '', width: 80, hidden: !manage, render: (s) => <Group gap={4} justify="flex-end" wrap="nowrap">
            <ActionIcon variant="subtle" aria-label={t('common.edit')} onClick={(e) => { e.stopPropagation(); void open(s.id); }}><IconEdit size={16} /></ActionIcon>
            <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={(e) => { e.stopPropagation(); confirmDelete(s.name, async () => { try { await del(`/suppliers/${s.id}`); await qc.invalidateQueries({ queryKey: ['suppliers'] }); } catch (err) { notifyError(err); } }); }}><IconTrash size={16} /></ActionIcon></Group> },
        ]} />
      <Modal opened={opened} onClose={m.close} title={editingId ? t('po.editSupplier') : t('po.newSupplier')} size="lg">
        <form onSubmit={submit}><Stack>
          <SimpleGrid cols={3}><TextInput label={t('common.code')} required {...form.getInputProps('code')} /><TextInput label={t('common.name')} required style={{ gridColumn: 'span 2' }} {...form.getInputProps('name')} /></SimpleGrid>
          <SimpleGrid cols={{ base: 1, sm: 3 }}><TextInput label={t('po.contact')} {...form.getInputProps('contactName')} /><TextInput label={t('settings.email')} type="email" {...form.getInputProps('email')} /><TextInput label={t('settings.phone')} {...form.getInputProps('phone')} /></SimpleGrid>
          <Textarea label={t('po.address')} autosize minRows={2} {...form.getInputProps('address')} />
          <SimpleGrid cols={{ base: 1, sm: 3 }}><TextInput label={t('po.city')} {...form.getInputProps('city')} /><TextInput label={t('po.country')} {...form.getInputProps('country')} /><TextInput label={t('po.taxNumber')} {...form.getInputProps('taxNumber')} /></SimpleGrid>
          <SimpleGrid cols={{ base: 1, sm: 2 }}><TextInput label={t('po.paymentTerms')} placeholder="Net 30" {...form.getInputProps('paymentTerms')} /><TextInput label={t('po.currency')} placeholder={currency} maxLength={3} {...form.getInputProps('currency')} /></SimpleGrid>
          <Textarea label={t('common.notes')} autosize minRows={2} {...form.getInputProps('notes')} />
          <Group><Switch label={t('po.contractor')} {...form.getInputProps('isContractor', { type: 'checkbox' })} /><Switch label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} /></Group>
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack></form>
      </Modal>
    </Card>
  );
}

export { PO_STATUSES };
