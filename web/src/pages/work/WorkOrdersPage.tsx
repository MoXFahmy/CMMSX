import { useEffect, useMemo, useState } from 'react';
import { Anchor, Badge, Button, Card, Group, MultiSelect, SegmentedControl, Select, Stack, Tabs, Text, TextInput, ActionIcon, Tooltip } from '@mantine/core';
import { useDebouncedValue } from '@mantine/hooks';
import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { DataTable, type DataTableSortStatus } from 'mantine-datatable';
import { IconFilterOff, IconPlus, IconSearch } from '@tabler/icons-react';
import { get } from '@/api/client';
import { WORK_PRIORITIES, WORK_TYPES, type Lookup, type Paged, type WorkOrderListItem, type WorkOrderStatus } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { useHierarchy } from '@/components/LocationPicker';
import { PriorityBadge, WoStatusBadge } from '@/components/WorkBadges';
import { fmtDate } from '@/components/utils';

const TABS: { key: string; statuses: WorkOrderStatus[] | null }[] = [
  { key: 'active', statuses: ['Open', 'InProgress', 'OnHold'] }, { key: 'Open', statuses: ['Open'] }, { key: 'InProgress', statuses: ['InProgress'] },
  { key: 'OnHold', statuses: ['OnHold'] }, { key: 'Completed', statuses: ['Completed'] }, { key: 'Closed', statuses: ['Closed'] }, { key: 'all', statuses: null },
];

export function WorkOrdersPage() {
  const { t } = useTranslation();
  const { can, terms } = useAuth();
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
  const tab = p('tab') || 'active';
  const view = p('view') || 'everyone';
  const statuses = TABS.find((x) => x.key === tab)?.statuses ?? null;
  const page = Number(p('page') || 1);
  const pageSize = Number(p('pageSize') || 25);
  const sort: DataTableSortStatus<WorkOrderListItem> = { columnAccessor: p('sort') || 'createdAt', direction: (p('dir') as 'asc' | 'desc') || 'desc' };
  const base = {
    search: p('search'), siteId: p('siteId'), priority: p('priority'), type: p('type'), tradeId: p('tradeId'), assetId: p('assetId'),
    mine: view === 'mine' ? 'true' : '', overdue: view === 'overdue' ? 'true' : '',
  };
  const { data, isFetching } = useQuery({
    queryKey: ['work-orders', base, tab, page, pageSize, sort],
    queryFn: () => get<Paged<WorkOrderListItem>>('/work-orders', { ...base, status: statuses ?? undefined, page, pageSize, sort: String(sort.columnAccessor), dir: sort.direction }),
    placeholderData: (prev) => prev,
  });
  const { data: summary } = useQuery({ queryKey: ['work-orders', 'summary', base], queryFn: () => get<{ byStatus: { status: WorkOrderStatus; count: number }[]; overdue: number }>('/work-orders/summary', base) });
  const { data: h } = useHierarchy();
  const { data: trades } = useQuery({ queryKey: ['trades'], queryFn: () => get<Lookup[]>('/trades') });
  const count = (s: WorkOrderStatus[] | null) => (summary?.byStatus ?? []).filter((x) => !s || s.includes(x.status)).reduce((a, b) => a + b.count, 0);
  const filtersActive = ['search', 'siteId', 'priority', 'type', 'tradeId', 'assetId'].some((k) => p(k));
  const tradeOpts = useMemo(() => (trades ?? []).map((x) => ({ value: x.id, label: x.name })), [trades]);

  return (
    <>
      <PageHeader title={t('wo.title')} subtitle={t('wo.subtitle')}
        actions={can(P.woCreate) && <Button leftSection={<IconPlus size={16} />} component={Link} to="/work-orders/new">{t('wo.new')}</Button>} />
      <Card p="sm" mb="md">
        <Group gap="xs" align="center" wrap="wrap">
          <SegmentedControl value={view} onChange={(v) => set({ view: v === 'everyone' ? null : v })} data={[
            { value: 'everyone', label: t('wo.all') }, { value: 'mine', label: t('wo.mine') },
            { value: 'overdue', label: `${t('wo.overdue')}${summary?.overdue ? ` (${summary.overdue})` : ''}` },
          ]} />
          <TextInput placeholder={t('wo.searchPlaceholder')} leftSection={<IconSearch size={16} />} value={search} onChange={(e) => setSearch(e.currentTarget.value)} w={{ base: '100%', sm: 240 }} />
          <Select placeholder={terms.site} data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} value={p('siteId') || null} onChange={(v) => set({ siteId: v })} clearable searchable w={{ base: '100%', xs: 180 }} />
          <MultiSelect placeholder={p('priority') ? undefined : t('priority.label')} data={WORK_PRIORITIES.map((x) => ({ value: x, label: t(`priority.${x}`) }))}
            value={p('priority') ? p('priority').split(',') : []} onChange={(v) => set({ priority: v.join(',') || null })} clearable w={{ base: '100%', xs: 170 }} />
          <MultiSelect placeholder={p('type') ? undefined : t('workType.label')} data={WORK_TYPES.map((x) => ({ value: x, label: t(`workType.${x}`) }))}
            value={p('type') ? p('type').split(',') : []} onChange={(v) => set({ type: v.join(',') || null })} clearable w={{ base: '100%', xs: 170 }} />
          <Select placeholder={t('wo.trade')} data={tradeOpts} value={p('tradeId') || null} onChange={(v) => set({ tradeId: v })} clearable w={{ base: '100%', xs: 160 }} />
          {filtersActive && <Tooltip label={t('common.clear')}><ActionIcon variant="default" size="lg" aria-label={t('common.clear')}
            onClick={() => { setSearch(''); setParams(tab !== 'active' ? { tab } : {}, { replace: true }); }}><IconFilterOff size={16} /></ActionIcon></Tooltip>}
        </Group>
        {p('assetId') && <Text fz="xs" c="dimmed" mt={6}>{t('wo.asset')}: {data?.items[0]?.assetNumber ?? '…'} ({t('assets.components').toLowerCase()} included)</Text>}
      </Card>
      <Tabs value={tab} onChange={(v) => set({ tab: v === 'active' ? null : v })} mb="sm">
        <Tabs.List>
          {TABS.map((x) => (
            <Tabs.Tab key={x.key} value={x.key} rightSection={<Badge size="xs" variant="default" circle={count(x.statuses) < 10}>{count(x.statuses)}</Badge>}>
              {x.key === 'active' ? t('wo.active') : x.key === 'all' ? t('wo.all') : t(`woStatus.${x.key}`)}
            </Tabs.Tab>
          ))}
        </Tabs.List>
      </Tabs>
      <Card p={0}>
        <DataTable<WorkOrderListItem>
          minHeight={260} withTableBorder={false} highlightOnHover striped verticalSpacing="xs" fetching={isFetching} records={data?.items ?? []}
          noRecordsText={t('wo.emptyTitle')} totalRecords={data?.total ?? 0} recordsPerPage={pageSize} page={page} onPageChange={(n) => set({ page: String(n) })}
          recordsPerPageOptions={[25, 50, 100]} onRecordsPerPageChange={(n) => set({ pageSize: String(n) })}
          sortStatus={sort} onSortStatusChange={(s) => set({ sort: String(s.columnAccessor), dir: s.direction })}
          rowClassName={() => 'clickable-row'} onRowClick={({ record }) => navigate(`/work-orders/${record.id}`)}
          paginationText={({ from, to, totalRecords }) => `${from}–${to} / ${totalRecords}`}
          columns={[
            { accessor: 'number', title: t('wo.number'), sortable: true, noWrap: true,
              render: (w) => <Anchor component={Link} to={`/work-orders/${w.id}`} fw={600} fz="sm" onClick={(e) => e.stopPropagation()}>{w.number}</Anchor> },
            { accessor: 'title', title: t('common.name'), sortable: true, width: 300,
              render: (w) => <Stack gap={0}><Text fz="sm" lineClamp={1}>{w.title}</Text><Text fz="xs" c="dimmed" lineClamp={1}>{t(`workType.${w.type}`)}{w.tradeName && ` · ${w.tradeName}`}</Text></Stack> },
            { accessor: 'status', title: t('common.status'), sortable: true, noWrap: true,
              render: (w) => <Stack gap={2}><WoStatusBadge status={w.status} />{w.holdReason && <Text fz={11} c="dimmed">{t(`holdReason.${w.holdReason}`)}</Text>}</Stack> },
            { accessor: 'priority', title: t('priority.label'), sortable: true, noWrap: true, render: (w) => <PriorityBadge value={w.priority} /> },
            { accessor: 'asset', title: t('wo.asset'), sortable: true, width: 220,
              render: (w) => w.assetId ? <Stack gap={0}><Text fz="sm" lineClamp={1}>{w.assetNumber}</Text><Text fz="xs" c="dimmed" lineClamp={1}>{w.assetName}</Text></Stack> : <Text fz="sm" c="dimmed" lineClamp={1}>{w.locationPath || w.siteName}</Text> },
            { accessor: 'assignees', title: t('wo.assignees'), width: 180, render: (w) => <Text fz="sm" lineClamp={1} c={w.assignees.length ? undefined : 'dimmed'}>{w.assignees.join(', ') || t('wo.noAssignees')}</Text> },
            { accessor: 'dueDate', title: t('wo.dueDate'), sortable: true, noWrap: true,
              render: (w) => <Group gap={4} wrap="nowrap"><Text fz="sm" c={w.isOverdue ? 'red' : undefined} fw={w.isOverdue ? 600 : undefined}>{fmtDate(w.dueDate)}</Text>{w.isOverdue && <Badge size="xs" color="red" variant="filled">{t('wo.overdueBadge')}</Badge>}</Group> },
            { accessor: 'createdAt', title: t('wo.createdOn'), sortable: true, noWrap: true, render: (w) => fmtDate(w.createdAt) },
          ]}
        />
      </Card>
    </>
  );
}
