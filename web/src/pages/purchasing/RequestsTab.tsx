import { useState } from 'react';
import { ActionIcon, Anchor, Badge, Button, Card, Checkbox, Group, Menu, SegmentedControl, Select, Stack, Text, TextInput } from '@mantine/core';
import { DataTable, type DataTableSortStatus } from 'mantine-datatable';
import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconDownload, IconSearch, IconShoppingCartPlus } from '@tabler/icons-react';
import { get } from '@/api/client';
import type { Paged } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { useHierarchy } from '@/components/LocationPicker';
import { exportUrl } from '@/components/list/ListTools';
import type { ListColumn } from '@/components/list/types';
import { fmtDate, fmtPrice, fmtQty } from '@/components/utils';
import { requestStatusColor, type OrderableLine, type PurchaseRequestListItem, type PurchaseRequestStatus } from './purchaseRequests';
import { OrderFromRequestsModal } from './OrderFromRequestsModal';

const STATUSES: PurchaseRequestStatus[] = ['Draft', 'Submitted', 'Approved', 'Rejected', 'PartlyOrdered', 'Ordered', 'Closed', 'Cancelled'];

export function RequestStatusBadge({ status }: { status: PurchaseRequestStatus }) {
  const { t } = useTranslation();
  return <Badge color={requestStatusColor(status)} variant="light" radius="sm" style={{ textTransform: 'none' }}>{t(`prStatus.${status}`)}</Badge>;
}

type LineToOrder = OrderableLine & { lineNumber: number; requestId: string; siteName: string; requestedByName?: string | null; neededBy?: string | null };

/** Purchasing › Requests: requests at my sites (or only mine); buyers also see every approved line waiting to be ordered. */
export function RequestsTab() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const [view, setView] = useState<'requests' | 'lines'>('requests');
  return (
    <>
      {can(P.purchasingCreate) && <SegmentedControl mb="md" value={view} onChange={(v) => setView(v as 'requests' | 'lines')}
        data={[{ value: 'requests', label: t('pr.viewRequests') }, { value: 'lines', label: t('pr.viewLines') }]} />}
      {view === 'requests' ? <RequestList /> : <LinesToOrder />}
    </>
  );
}

/** Approved lines from every request, not yet on an order: the buyer picks across requests and orders them together. */
function LinesToOrder() {
  const { t } = useTranslation();
  const { terms } = useAuth();
  const { data, isFetching } = useQuery({ queryKey: ['pr-lines-to-order'], queryFn: () => get<LineToOrder[]>('/purchase-requests/lines-to-order') });
  const [selected, setSelected] = useState<LineToOrder[]>([]);
  const [ordering, setOrdering] = useState(false);
  return (
    <>
      <Group justify="space-between" mb="sm">
        <Text fz="sm" c="dimmed">{t('pr.orderHint')}</Text>
        <Button leftSection={<IconShoppingCartPlus size={16} />} disabled={!selected.length} onClick={() => setOrdering(true)}>{t('pr.orderSelected', { count: selected.length })}</Button>
      </Group>
      <Card p={0}>
        <DataTable<LineToOrder>
          minHeight={200} withTableBorder={false} striped verticalSpacing="xs" fetching={isFetching} records={data ?? []} noRecordsText={t('pr.noLinesToOrder')}
          selectedRecords={selected} onSelectedRecordsChange={setSelected}
          columns={[
            { accessor: 'requestNumber', title: t('pr.number'), render: (l) => <Anchor component={Link} to={`/purchasing/requests/${l.requestId}`} fz="sm" fw={600}>{l.requestNumber}</Anchor> },
            { accessor: 'description', title: t('pr.description'), render: (l) => <Stack gap={0}><Text fz="sm">{l.part ? `${l.part.partNumber} · ` : ''}{l.description}</Text>
              {l.workOrder && <Text fz="xs" c="dimmed">{l.workOrder.number}</Text>}</Stack> },
            { accessor: 'quantity', title: t('pr.quantity'), textAlign: 'right', render: (l) => `${fmtQty(l.quantity)} ${l.unitOfMeasure ?? ''}` },
            { accessor: 'neededBy', title: t('pr.neededBy'), render: (l) => fmtDate(l.neededBy) },
            { accessor: 'siteName', title: terms.site },
            { accessor: 'requestedByName', title: t('pr.requestedBy') },
          ]} />
      </Card>
      <OrderFromRequestsModal lines={selected} opened={ordering} onClose={() => { setOrdering(false); setSelected([]); }} />
    </>
  );
}

function RequestList() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { can, me, terms } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const { data: h } = useHierarchy();
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<string | null>(null);
  const [siteId, setSiteId] = useState<string | null>(null);
  const [mine, setMine] = useState(false);
  const [waiting, setWaiting] = useState(false);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [sort, setSort] = useState<DataTableSortStatus<PurchaseRequestListItem>>({ columnAccessor: 'createdAt', direction: 'desc' });
  const query = { search: search || undefined, status: status ?? undefined, siteId: siteId ?? undefined, mine: mine || undefined, waitingForMe: waiting || undefined,
    sort: String(sort.columnAccessor), dir: sort.direction };
  const { data, isFetching } = useQuery({
    queryKey: ['purchase-requests', query, page, pageSize], placeholderData: (prev) => prev,
    queryFn: () => get<Paged<PurchaseRequestListItem>>('/purchase-requests', { ...query, page, pageSize }),
  });
  const columns: ListColumn<PurchaseRequestListItem>[] = [
    { accessor: 'number', title: t('pr.number'), sortable: true, noWrap: true,
      render: (r) => <Anchor component={Link} to={`/purchasing/requests/${r.id}`} fw={600} fz="sm" onClick={(e) => e.stopPropagation()}>{r.number}</Anchor> },
    { accessor: 'status', title: t('common.status'), sortable: true, noWrap: true, render: (r) => <RequestStatusBadge status={r.status} /> },
    { accessor: 'requestedBy', title: t('pr.requestedBy'), sortable: true, export: [{ key: 'requestedByName' }, { key: 'siteName', title: terms.site }],
      render: (r) => <Stack gap={0}><Text fz="sm">{r.requestedByName ?? '—'}</Text><Text fz="xs" c="dimmed">{r.siteName}</Text></Stack> },
    { accessor: 'neededBy', title: t('pr.neededBy'), sortable: true, noWrap: true,
      render: (r) => <Group gap={4} wrap="nowrap"><Text fz="sm" c={r.isLate ? 'red' : undefined} fw={r.isLate ? 600 : undefined}>{fmtDate(r.neededBy)}</Text>
        {r.isLate && <Badge size="xs" color="red" variant="filled">{t('pr.late')}</Badge>}</Group> },
    { accessor: 'lines', title: t('pr.lines'), textAlign: 'right', noWrap: true },
    { accessor: 'estimatedTotal', title: t('pr.estimate'), textAlign: 'right', noWrap: true, render: (r) => (r.estimatedTotal == null ? '—' : fmtPrice(r.estimatedTotal, currency)) },
    { accessor: 'submittedAt', title: t('pr.submittedAt'), sortable: true, noWrap: true, render: (r) => fmtDate(r.submittedAt) },
  ];
  return (
    <>
      <Card p="sm" mb="md">
        <Group gap="xs" wrap="wrap">
          <TextInput placeholder={t('pr.searchPlaceholder')} leftSection={<IconSearch size={16} />} value={search} onChange={(e) => { setSearch(e.currentTarget.value); setPage(1); }} w={{ base: '100%', sm: 240 }} />
          <Select placeholder={t('pr.anyStatus')} data={STATUSES.map((s) => ({ value: s, label: t(`prStatus.${s}`) }))} value={status} onChange={(v) => { setStatus(v); setPage(1); }} clearable w={{ base: '100%', xs: 180 }} />
          <Select placeholder={terms.site} data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} value={siteId} onChange={(v) => { setSiteId(v); setPage(1); }} clearable w={{ base: '100%', xs: 180 }} />
          {can(P.purchasingRequest) && <Checkbox label={t('pr.mine')} checked={mine} onChange={(e) => { setMine(e.currentTarget.checked); setPage(1); }} />}
          {can(P.purchasingApproveRequests) && <Checkbox label={t('pr.waitingForMe')} checked={waiting} onChange={(e) => { setWaiting(e.currentTarget.checked); setPage(1); }} />}
          <Menu position="bottom-end">
            <Menu.Target><ActionIcon variant="default" size="lg" ms="auto" aria-label={t('list.export')}><IconDownload size={16} /></ActionIcon></Menu.Target>
            <Menu.Dropdown>
              <Menu.Item component="a" href={exportUrl('/purchase-requests/export', query, columns, 'xlsx')} download>{t('list.exportXlsx')}</Menu.Item>
              <Menu.Item component="a" href={exportUrl('/purchase-requests/export', query, columns, 'csv')} download>{t('list.exportCsv')}</Menu.Item>
            </Menu.Dropdown>
          </Menu>
        </Group>
      </Card>
      <Card p={0}>
        <DataTable<PurchaseRequestListItem>
          minHeight={260} withTableBorder={false} highlightOnHover striped verticalSpacing="xs" fetching={isFetching} records={data?.items ?? []} columns={columns}
          noRecordsText={t('pr.empty')} totalRecords={data?.total ?? 0} recordsPerPage={pageSize} page={page} onPageChange={setPage}
          recordsPerPageOptions={[25, 50, 100]} recordsPerPageLabel={t('common.perPage')} onRecordsPerPageChange={(n) => { setPageSize(n); setPage(1); }}
          sortStatus={sort} onSortStatusChange={setSort}
          rowClassName={() => 'clickable-row'} onRowClick={({ record }) => navigate(`/purchasing/requests/${record.id}`)}
          paginationText={({ from, to, totalRecords }) => `${from}–${to} / ${totalRecords}`}
        />
      </Card>
    </>
  );
}
