import { useEffect, useMemo, useState } from 'react';
import { Anchor, Badge, Button, Card, Collapse, Group, Menu, Modal, MultiSelect, SegmentedControl, Select, SimpleGrid, Stack, Tabs, Text, Textarea, TextInput, ActionIcon, Tooltip } from '@mantine/core';
import { DatePickerInput } from '@mantine/dates';
import { useDisclosure } from '@mantine/hooks';
import dayjs from 'dayjs';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { SlaText } from '@/components/SlaBadge';
import { DataTable } from 'mantine-datatable';
import { IconAdjustmentsHorizontal, IconFilterOff, IconFlag, IconPlus, IconSearch, IconUserPlus, IconArrowsExchange } from '@tabler/icons-react';
import { get, post } from '@/api/client';
import { HOLD_REASONS, WORK_PRIORITIES, WORK_TYPES, type HoldReason, type Lookup, type Paged, type WorkOrderListItem, type WorkOrderStatus, type WorkPriority } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { locationOptions, useHierarchy } from '@/components/LocationPicker';
import { AssetPicker } from '@/components/AssetPicker';
import { EmployeePicker, useEmployees } from '@/components/EmployeePicker';
import { PriorityBadge, WoStatusBadge } from '@/components/WorkBadges';
import { fmtDate, notifyError } from '@/components/utils';
import { BulkBar, FilterChips, ListTools, listText, rangeText, showBulkResult, useListColumns, useListParams, type BulkResult, type FilterDef, type ListColumn } from '@/components/list';

/** Filters behind "More filters"; they live in the address like the others. */
const EXTRA_FILTERS = ['locationId', 'assetId', 'assigneeId', 'createdFrom', 'createdTo', 'dueFrom', 'dueTo', 'source'] as const;
const isoDay = (d: string | Date | null) => (d ? dayjs(d).format('YYYY-MM-DD') : null);

const TABS: { key: string; statuses: WorkOrderStatus[] | null }[] = [
  { key: 'active', statuses: ['Open', 'InProgress', 'OnHold'] }, { key: 'Open', statuses: ['Open'] }, { key: 'InProgress', statuses: ['InProgress'] },
  { key: 'OnHold', statuses: ['OnHold'] }, { key: 'Completed', statuses: ['Completed'] }, { key: 'Closed', statuses: ['Closed'] }, { key: 'all', statuses: null },
];

/** Statuses a bulk change offers; closing, cancelling (and reopening those) need the close permission, as one at a time. */
const BULK_STATUSES: WorkOrderStatus[] = ['InProgress', 'OnHold', 'Completed', 'Open', 'Closed', 'Cancelled'];

type BulkBody = { status?: { status: WorkOrderStatus; holdReason?: HoldReason | null; completionNotes?: string | null }; priority?: WorkPriority; addAssigneeIds?: string[] };

export function WorkOrdersPage() {
  const { t } = useTranslation();
  const { can, terms } = useAuth();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const lp = useListParams<WorkOrderListItem>({ sort: 'createdAt', dir: 'desc' });
  const { p, set } = lp;
  const tab = p('tab') || 'active';
  const view = p('view') || 'everyone';
  const statuses = TABS.find((x) => x.key === tab)?.statuses ?? null;
  const base = {
    search: p('search'), siteId: p('siteId'), priority: p('priority'), type: p('type'), tradeId: p('tradeId'), assetId: p('assetId'),
    mine: view === 'mine' ? 'true' : '', overdue: view === 'overdue' ? 'true' : '',
    locationId: p('locationId'), assigneeId: p('assigneeId'), createdFrom: p('createdFrom'), createdTo: p('createdTo'),
    dueFrom: p('dueFrom'), dueTo: p('dueTo'), source: p('source'),
  };
  const listQuery = { ...base, status: statuses ?? undefined, sort: String(lp.sort.columnAccessor), dir: lp.sort.direction };
  const { data, isFetching } = useQuery({
    queryKey: ['work-orders', listQuery, lp.page, lp.pageSize],
    queryFn: () => get<Paged<WorkOrderListItem>>('/work-orders', { ...listQuery, page: lp.page, pageSize: lp.pageSize }),
    placeholderData: (prev) => prev,
  });
  const { data: summary } = useQuery({ queryKey: ['work-orders', 'summary', base], queryFn: () => get<{ byStatus: { status: WorkOrderStatus; count: number }[]; overdue: number }>('/work-orders/summary', base) });
  const { data: h } = useHierarchy();
  const { data: trades } = useQuery({ queryKey: ['trades'], queryFn: () => get<Lookup[]>('/trades') });
  const { data: filterAsset } = useQuery({ queryKey: ['asset', p('assetId')], queryFn: () => get<{ assetNumber: string; name: string }>(`/assets/${p('assetId')}`), enabled: !!p('assetId') });
  const count = (s: WorkOrderStatus[] | null) => (summary?.byStatus ?? []).filter((x) => !s || s.includes(x.status)).reduce((a, b) => a + b.count, 0);
  const filtersActive = ['search', 'siteId', 'priority', 'type', 'tradeId', ...EXTRA_FILTERS].some((k) => p(k));
  const tradeOpts = useMemo(() => (trades ?? []).map((x) => ({ value: x.id, label: x.name })), [trades]);
  const extraCount = [p('locationId'), p('assetId'), p('assigneeId'), p('createdFrom') || p('createdTo'), p('dueFrom') || p('dueTo'), p('source')].filter(Boolean).length;
  const [moreOpen, more] = useDisclosure(extraCount > 0);
  const locOpts = useMemo(() => locationOptions(h?.locations ?? [], p('siteId') || null), [h, lp.params]); // eslint-disable-line react-hooks/exhaustive-deps
  const { data: employees } = useEmployees(p('siteId') || null);
  const empOpts = useMemo(() => (employees ?? []).map((e) => ({ value: e.id, label: e.fullName })), [employees]);
  const sources: Record<string, string> = { pm: t('wo.sourcePm'), request: t('wo.sourceRequest'), direct: t('wo.sourceDirect') };

  const allColumns: ListColumn<WorkOrderListItem>[] = [
    { accessor: 'number', title: t('wo.number'), sortable: true, noWrap: true,
      render: (w) => <Anchor component={Link} to={`/work-orders/${w.id}`} fw={600} fz="sm" onClick={(e) => e.stopPropagation()}>{w.number}</Anchor> },
    { accessor: 'title', title: t('common.name'), sortable: true, width: 300,
      render: (w) => <Stack gap={0}><Text fz="sm" lineClamp={1}>{w.title}</Text><Text fz="xs" c="dimmed" lineClamp={1}>{t(`workType.${w.type}`)}{w.tradeName && ` · ${w.tradeName}`}</Text></Stack> },
    { accessor: 'status', title: t('common.status'), sortable: true, noWrap: true,
      export: [{ key: 'status' }, { key: 'holdReason', title: t('wo.holdReason') }],
      render: (w) => <Stack gap={2}><WoStatusBadge status={w.status} />{w.holdReason && <Text fz={11} c="dimmed">{t(`holdReason.${w.holdReason}`)}</Text>}</Stack> },
    { accessor: 'priority', title: t('priority.label'), sortable: true, noWrap: true, render: (w) => <PriorityBadge value={w.priority} /> },
    { accessor: 'type', title: t('workType.label'), sortable: true, noWrap: true, render: (w) => t(`workType.${w.type}`) },
    { accessor: 'trade', title: t('wo.trade'), noWrap: true, export: [{ key: 'tradeName' }], render: (w) => w.tradeName ?? '—' },
    { accessor: 'asset', title: terms.asset, sortable: true, width: 220,
      export: [{ key: 'assetNumber' }, { key: 'assetName', title: `${terms.asset} · ${t('common.name')}` }],
      render: (w) => w.assetId ? <Stack gap={0}><Text fz="sm" lineClamp={1}>{w.assetNumber}</Text><Text fz="xs" c="dimmed" lineClamp={1}>{w.assetName}</Text></Stack> : <Text fz="sm" c="dimmed" lineClamp={1}>{w.locationPath ?? w.siteName}</Text> },
    { accessor: 'site', title: terms.site, sortable: true, noWrap: true, export: [{ key: 'siteName' }], render: (w) => w.siteName },
    { accessor: 'location', title: terms.location, width: 220, ellipsis: true, export: [{ key: 'locationPath' }], render: (w) => <span title={w.locationPath}>{w.locationPath ?? '—'}</span> },
    { accessor: 'assignees', title: t('wo.assignees'), width: 180, render: (w) => <Text fz="sm" lineClamp={1} c={w.assignees.length ? undefined : 'dimmed'}>{w.assignees.join(', ') || t('wo.noAssignees')}</Text> },
    { accessor: 'dueDate', title: t('wo.due'), sortable: true, noWrap: true, export: [{ key: 'dueAt' }, { key: 'completionState' }, { key: 'responseState' }],
      render: (w) => (
        <Stack gap={0}>
          <SlaText deadline={w.dueAt ?? w.dueDate} state={w.completionState} />
          {w.responseState === 'Late' && <Text fz="xs" c="red">{t('sla.notStarted')}</Text>}
        </Stack>
      ) },
    { accessor: 'createdAt', title: t('wo.createdOn'), sortable: true, noWrap: true, render: (w) => fmtDate(w.createdAt) },
    { accessor: 'completedAt', title: t('wo.completedOn'), sortable: true, noWrap: true, render: (w) => fmtDate(w.completedAt) },
  ];
  const cols = useListColumns('work-orders', allColumns, { defaultHidden: ['type', 'trade', 'site', 'location', 'completedAt'] });

  const chips: FilterDef[] = [
    { keys: ['search'], label: t('common.search') },
    { keys: ['siteId'], label: terms.site, format: ([v]) => h?.sites.find((s) => s.id === v)?.name ?? '…' },
    { keys: ['priority'], label: t('priority.label'), format: listText((v) => t(`priority.${v}`)) },
    { keys: ['type'], label: t('workType.label'), format: listText((v) => t(`workType.${v}`)) },
    { keys: ['tradeId'], label: t('wo.trade'), format: ([v]) => tradeOpts.find((o) => o.value === v)?.label ?? '…' },
    { keys: ['locationId'], label: terms.location, format: ([v]) => h?.locations.find((l) => l.id === v)?.name ?? '…' },
    { keys: ['assetId'], label: terms.asset, format: () => (filterAsset ? `${filterAsset.assetNumber} ${filterAsset.name}` : '…') },
    { keys: ['assigneeId'], label: t('wo.filterAssignee'), format: ([v]) => empOpts.find((o) => o.value === v)?.label ?? '…' },
    { keys: ['createdFrom', 'createdTo'], label: t('wo.createdBetween'), format: rangeText(fmtDate, t) },
    { keys: ['dueFrom', 'dueTo'], label: t('wo.dueBetween'), format: rangeText(fmtDate, t) },
    { keys: ['source'], label: t('wo.source'), format: ([v]) => sources[v] ?? v },
  ];

  // ---- bulk actions ----
  const canBulkStatus = can(P.woExecute);
  const canBulkEdit = can(P.woEdit);
  const [selected, setSelected] = useState<WorkOrderListItem[]>([]);
  // Ticked rows belong to the list on screen: changing its filters, tab or sort starts a new selection.
  const selectionKey = JSON.stringify(listQuery);
  useEffect(() => setSelected([]), [selectionKey]);
  const [statusTarget, setStatusTarget] = useState<WorkOrderStatus | null>(null);
  const [holdReason, setHoldReason] = useState<HoldReason | null>(null);
  const [notes, setNotes] = useState('');
  const [techOpen, setTechOpen] = useState(false);
  const [techs, setTechs] = useState<string[]>([]);
  const bulk = useMutation({
    mutationFn: (body: BulkBody) => post<BulkResult>('/work-orders/bulk', { ids: selected.map((s) => s.id), ...body }),
    onSuccess: (r) => {
      showBulkResult(t, r);
      setSelected([]); setStatusTarget(null); setTechOpen(false); setTechs([]); setNotes(''); setHoldReason(null);
      void qc.invalidateQueries({ queryKey: ['work-orders'] });
    },
    onError: notifyError,
  });
  // Statuses that need more (a reason, notes) or can't be taken back lightly (closing, cancelling) ask first.
  const pickStatus = (s: WorkOrderStatus) => (['OnHold', 'Completed', 'Closed', 'Cancelled'].includes(s) ? setStatusTarget(s) : bulk.mutate({ status: { status: s } }));
  const statusOptions = BULK_STATUSES.filter((s) => can(P.woClose) || (s !== 'Closed' && s !== 'Cancelled'));

  return (
    <>
      <PageHeader title={t('wo.title')} subtitle={t('wo.subtitle')}
        actions={can(P.woCreate) && <Button leftSection={<IconPlus size={16} />} component={Link} to="/work-orders/new">{t('wo.new')}</Button>} />
      <Card p="sm" mb="md">
        <Group gap="xs" align="center" wrap="wrap">
          <SegmentedControl value={view} onChange={(v) => set({ view: v === 'everyone' ? null : v })} data={[
            { value: 'everyone', label: t('wo.all') }, { value: 'mine', label: t('wo.mine') },
            { value: 'overdue', label: `${t('sla.lateFilter')}${summary?.overdue ? ` (${summary.overdue})` : ''}` },
          ]} />
          <TextInput placeholder={t('wo.searchPlaceholder')} leftSection={<IconSearch size={16} />} value={lp.search} onChange={(e) => lp.setSearch(e.currentTarget.value)} w={{ base: '100%', sm: 240 }} />
          <Select placeholder={terms.site} data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} value={p('siteId') || null} onChange={(v) => set({ siteId: v, locationId: null })} clearable searchable w={{ base: '100%', xs: 180 }} />
          <MultiSelect placeholder={p('priority') ? undefined : t('priority.label')} data={WORK_PRIORITIES.map((x) => ({ value: x, label: t(`priority.${x}`) }))}
            value={p('priority') ? p('priority').split(',') : []} onChange={(v) => set({ priority: v.join(',') || null })} clearable w={{ base: '100%', xs: 170 }} />
          <MultiSelect placeholder={p('type') ? undefined : t('workType.label')} data={WORK_TYPES.map((x) => ({ value: x, label: t(`workType.${x}`) }))}
            value={p('type') ? p('type').split(',') : []} onChange={(v) => set({ type: v.join(',') || null })} clearable w={{ base: '100%', xs: 170 }} />
          <Select placeholder={t('wo.trade')} data={tradeOpts} value={p('tradeId') || null} onChange={(v) => set({ tradeId: v })} clearable w={{ base: '100%', xs: 160 }} />
          <Button variant={moreOpen ? 'light' : 'default'} leftSection={<IconAdjustmentsHorizontal size={16} />} onClick={more.toggle} aria-expanded={moreOpen}
            rightSection={extraCount ? <Badge size="xs" circle>{extraCount}</Badge> : null}>{t('wo.moreFilters')}</Button>
          {filtersActive && <Tooltip label={t('common.clear')}><ActionIcon variant="default" size="lg" aria-label={t('common.clear')}
            onClick={() => lp.replaceAll(new URLSearchParams(Object.entries({ tab: tab !== 'active' ? tab : '', view: view !== 'everyone' ? view : '' }).filter(([, v]) => v)).toString())}>
            <IconFilterOff size={16} /></ActionIcon></Tooltip>}
          <Group gap={6} ml="auto">
            <ListTools list="work-orders" lp={lp} cols={cols} exportPath="/work-orders/export" exportQuery={listQuery} total={data?.total} />
          </Group>
        </Group>
        <Collapse in={moreOpen}>
          <SimpleGrid cols={{ base: 1, sm: 2, lg: 4 }} mt="sm" spacing="xs">
            <Select label={terms.location} placeholder={p('siteId') ? t('common.all') : t('wo.pickSiteFirst')} disabled={!p('siteId')} data={locOpts}
              value={p('locationId') || null} onChange={(v) => set({ locationId: v })} clearable searchable comboboxProps={{ width: 'max-content', position: 'bottom-start' }} />
            <AssetPicker label={terms.asset} siteId={p('siteId') || null} value={p('assetId') || null} onChange={(v) => set({ assetId: v })} />
            <Select label={t('wo.filterAssignee')} placeholder={t('common.all')} data={empOpts} value={p('assigneeId') || null} onChange={(v) => set({ assigneeId: v })} clearable searchable />
            <Select label={t('wo.source')} placeholder={t('common.all')} value={p('source') || null} onChange={(v) => set({ source: v })} clearable
              data={[{ value: 'pm', label: t('wo.sourcePm') }, { value: 'request', label: t('wo.sourceRequest') }, { value: 'direct', label: t('wo.sourceDirect') }]} />
            <DatePickerInput type="range" label={t('wo.createdBetween')} placeholder={t('common.all')} clearable valueFormat="DD MMM YYYY" allowSingleDateInRange
              value={[p('createdFrom') || null, p('createdTo') || null]} onChange={([a, b]) => set({ createdFrom: isoDay(a), createdTo: isoDay(b) })} />
            <DatePickerInput type="range" label={t('wo.dueBetween')} placeholder={t('common.all')} clearable valueFormat="DD MMM YYYY" allowSingleDateInRange
              value={[p('dueFrom') || null, p('dueTo') || null]} onChange={([a, b]) => set({ dueFrom: isoDay(a), dueTo: isoDay(b) })} />
          </SimpleGrid>
          {extraCount > 0 && <Group justify="flex-end" mt="xs"><Button variant="subtle" size="xs" onClick={() => set(Object.fromEntries(EXTRA_FILTERS.map((k) => [k, null])))}>{t('wo.clearFilters')}</Button></Group>}
        </Collapse>
        <FilterChips defs={chips} p={p} set={set} />
        {p('assetId') && <Text fz="xs" c="dimmed" mt={6}>{t('wo.assetFilterHint')}</Text>}
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
      <BulkBar count={selected.length} onClear={() => setSelected([])}>
        {canBulkStatus && (
          <Menu><Menu.Target><Button size="xs" variant="white" leftSection={<IconArrowsExchange size={14} />} loading={bulk.isPending}>{t('list.setStatus')}</Button></Menu.Target>
            <Menu.Dropdown>{statusOptions.map((s) => <Menu.Item key={s} onClick={() => pickStatus(s)}>{t(`woStatus.${s}`)}</Menu.Item>)}</Menu.Dropdown></Menu>
        )}
        {canBulkEdit && (
          <Menu><Menu.Target><Button size="xs" variant="white" leftSection={<IconFlag size={14} />}>{t('list.setPriority')}</Button></Menu.Target>
            <Menu.Dropdown>{WORK_PRIORITIES.map((x) => <Menu.Item key={x} onClick={() => bulk.mutate({ priority: x })}>{t(`priority.${x}`)}</Menu.Item>)}</Menu.Dropdown></Menu>
        )}
        {canBulkEdit && <Button size="xs" variant="white" leftSection={<IconUserPlus size={14} />} onClick={() => setTechOpen(true)}>{t('list.addTechnicians')}</Button>}
      </BulkBar>
      <Card p={0}>
        <DataTable<WorkOrderListItem>
          minHeight={260} withTableBorder={false} highlightOnHover striped verticalSpacing="xs" fetching={isFetching} records={data?.items ?? []} columns={cols.columns}
          noRecordsText={t('wo.emptyTitle')} totalRecords={data?.total ?? 0} recordsPerPage={lp.pageSize} page={lp.page} onPageChange={(n) => set({ page: String(n) })}
          recordsPerPageOptions={[25, 50, 100]} onRecordsPerPageChange={(n) => set({ pageSize: String(n) })}
          sortStatus={lp.sort} onSortStatusChange={lp.setSort}
          selectedRecords={canBulkStatus || canBulkEdit ? selected : undefined} onSelectedRecordsChange={canBulkStatus || canBulkEdit ? setSelected : undefined}
          rowClassName={() => 'clickable-row'} onRowClick={({ record }) => navigate(`/work-orders/${record.id}`)}
          paginationText={({ from, to, totalRecords }) => `${from}–${to} / ${totalRecords}`}
        />
      </Card>

      <Modal opened={statusTarget !== null} onClose={() => setStatusTarget(null)} size="md"
        title={t('list.statusTitle', { count: selected.length })}>
        <Stack>
          <Text fz="sm" c="dimmed">{t('list.statusHint')}</Text>
          {statusTarget === 'OnHold' && <Select label={t('wo.holdReason')} required data={HOLD_REASONS.map((r) => ({ value: r, label: t(`holdReason.${r}`) }))}
            value={holdReason} onChange={(v) => setHoldReason(v as HoldReason | null)} />}
          {(statusTarget === 'Closed' || statusTarget === 'Cancelled') && <Text fz="sm" c="orange">{t('list.statusConfirm', { count: selected.length, status: t(`woStatus.${statusTarget}`).toLowerCase() })}</Text>}
          {statusTarget === 'Completed' && <Textarea label={t('list.completionNotesShared')} autosize minRows={3} value={notes} onChange={(e) => setNotes(e.currentTarget.value)} />}
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setStatusTarget(null)}>{t('common.cancel')}</Button>
            <Button loading={bulk.isPending} disabled={statusTarget === 'OnHold' && !holdReason}
              onClick={() => statusTarget && bulk.mutate({ status: { status: statusTarget, holdReason, completionNotes: notes.trim() || null } })}>
              {statusTarget ? t(`woStatus.${statusTarget}`) : ''}
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal opened={techOpen} onClose={() => setTechOpen(false)} size="md" title={t('list.techniciansTitle', { count: selected.length })}>
        <Stack>
          <Text fz="sm" c="dimmed">{t('list.techniciansHint')}</Text>
          <EmployeePicker label={t('wo.assignees')} value={techs} onChange={setTechs} siteId={p('siteId') || null} />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setTechOpen(false)}>{t('common.cancel')}</Button>
            <Button loading={bulk.isPending} disabled={techs.length === 0} onClick={() => bulk.mutate({ addAssigneeIds: techs })}>{t('list.addTechnicians')}</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
