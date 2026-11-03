import { useEffect, useMemo, useState } from 'react';
import { ActionIcon, Anchor, Badge, Button, Card, Checkbox, Group, Menu, MultiSelect, Select, Stack, Switch, Text, TextInput, Tooltip } from '@mantine/core';
import { useDebouncedValue, useLocalStorage } from '@mantine/hooks';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { DataTable, type DataTableColumn, type DataTableSortStatus } from 'mantine-datatable';
import { IconColumns3, IconDownload, IconFilterOff, IconPlus, IconSearch, IconEdit } from '@tabler/icons-react';
import { get, post, toQuery } from '@/api/client';
import { ASSET_STATUSES, CRITICALITIES, type AssetListItem, type Category, type CustomField, type Paged } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { CriticalityBadge, StatusBadge } from '@/components/Badges';
import { PageHeader } from '@/components/PageHeader';
import { locationOptions, useHierarchy } from '@/components/LocationPicker';
import { fmtDate, notifyError, notifySuccess } from '@/components/utils';

const PAGE_SIZES = [25, 50, 100];
const DEFAULT_HIDDEN = ['manufacturer', 'model', 'serialNumber', 'commissionedOn', 'warrantyExpiresOn', 'parentAssetNumber'];

export function AssetsPage() {
  const { t } = useTranslation();
  const { terms, can } = useAuth();
  const navigate = useNavigate();
  const qc = useQueryClient();
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
  const sort: DataTableSortStatus<AssetListItem> = { columnAccessor: p('sort') || 'assetNumber', direction: (p('dir') as 'asc' | 'desc') || 'asc' };
  const filters = {
    search: p('search'), siteId: p('siteId'), locationId: p('locationId'), categoryId: p('categoryId'), status: p('status'), criticality: p('criticality'),
    topLevelOnly: p('topLevelOnly'), sort: sort.columnAccessor as string, dir: sort.direction,
  };
  const { data, isFetching } = useQuery({
    queryKey: ['assets', filters, page, pageSize],
    queryFn: () => get<Paged<AssetListItem>>('/assets', { ...filters, page, pageSize }),
    placeholderData: (prev) => prev,
  });
  const { data: h } = useHierarchy();
  const { data: cats } = useQuery({ queryKey: ['categories'], queryFn: () => get<Category[]>('/asset-categories') });
  const { data: fields } = useQuery({ queryKey: ['custom-fields', 'Asset'], queryFn: () => get<CustomField[]>('/custom-fields', { entityType: 'Asset' }) });
  const [hidden, setHidden] = useLocalStorage<string[]>({ key: 'assets.hiddenColumns', defaultValue: DEFAULT_HIDDEN });
  const [selected, setSelected] = useState<AssetListItem[]>([]);

  // If filtering by location only, infer the site so the location dropdown has options.
  const siteForLocations = p('siteId') || h?.locations.find((l) => l.id === p('locationId'))?.siteId || null;
  const locOpts = useMemo(() => locationOptions(h?.locations ?? [], siteForLocations), [h, siteForLocations]);
  const activeFilters = ['siteId', 'locationId', 'categoryId', 'status', 'criticality', 'topLevelOnly', 'search'].filter((k) => p(k)).length;

  const bulk = useMutation({
    mutationFn: (patch: Record<string, string>) => post<{ updated: number }>('/assets/bulk', { ids: selected.map((s) => s.id), ...patch }),
    onSuccess: (r) => { notifySuccess(t('assets.bulkUpdated', { count: r.updated })); setSelected([]); void qc.invalidateQueries({ queryKey: ['assets'] }); },
    onError: notifyError,
  });

  const allColumns: DataTableColumn<AssetListItem>[] = [
    { accessor: 'assetNumber', title: t('assets.number'), sortable: true, width: 130,
      render: (a) => <Anchor component={Link} to={`/assets/${a.id}`} fw={600} fz="sm" onClick={(e) => e.stopPropagation()}>{a.assetNumber}</Anchor> },
    { accessor: 'name', title: t('common.name'), sortable: true, width: 260, render: (a) => <Stack gap={0}><Text fz="sm" lineClamp={1}>{a.name}</Text>{a.parentAssetNumber && <Text fz="xs" c="dimmed">↳ {a.parentAssetNumber}</Text>}</Stack> },
    { accessor: 'category', title: t('assets.category'), sortable: true, noWrap: true, width: 210,
      render: (a) => a.categoryName ? <Badge variant="dot" color={a.categoryColor ?? 'gray'} style={{ maxWidth: 'none' }} styles={{ label: { textTransform: 'none', fontWeight: 500, overflow: 'visible' } }}>{a.categoryName}</Badge> : '—' },
    { accessor: 'site', title: terms.site, sortable: true, noWrap: true, render: (a) => a.siteName },
    { accessor: 'location', title: terms.location, sortable: true, width: 240, ellipsis: true, render: (a) => <span title={a.locationPath}>{a.locationPath || '—'}</span> },
    { accessor: 'status', title: t('common.status'), sortable: true, noWrap: true, width: 140, render: (a) => <StatusBadge status={a.status} /> },
    { accessor: 'criticality', title: t('criticality.label'), sortable: true, noWrap: true, width: 130, render: (a) => <CriticalityBadge value={a.criticality} /> },
    { accessor: 'manufacturer', title: t('assets.manufacturer'), sortable: true, noWrap: true },
    { accessor: 'model', title: t('assets.model'), sortable: true, noWrap: true },
    { accessor: 'serialNumber', title: t('assets.serialNumber'), sortable: true, noWrap: true },
    { accessor: 'parentAssetNumber', title: t('assets.parent') },
    { accessor: 'commissionedOn', title: t('assets.commissionedOn'), sortable: true, render: (a) => fmtDate(a.commissionedOn) },
    { accessor: 'warrantyExpiresOn', title: t('assets.warrantyExpires'), sortable: true, render: (a) => fmtDate(a.warrantyExpiresOn) },
    ...(fields ?? []).map<DataTableColumn<AssetListItem>>((f) => ({
      accessor: `cf_${f.key}`, title: f.label, noWrap: true,
      render: (a) => { const v = a.customFields?.[f.key]; return f.dataType === 'Boolean' ? (v === 'true' ? t('common.yes') : v === 'false' ? t('common.no') : '—') : f.dataType === 'Date' ? fmtDate(v) : v ?? '—'; },
    })),
  ];
  const fieldHidden = (acc: string) => {
    if (acc.startsWith('cf_')) { const f = fields?.find((x) => `cf_${x.key}` === acc); return hidden.includes(acc) || (!f?.showInList && !hidden.includes(`+${acc}`)); }
    return hidden.includes(acc);
  };
  const columns = allColumns.filter((c) => !fieldHidden(String(c.accessor)));
  const toggleColumn = (acc: string) => {
    if (acc.startsWith('cf_')) {
      const f = fields?.find((x) => `cf_${x.key}` === acc);
      const shown = !fieldHidden(acc);
      const base = hidden.filter((h2) => h2 !== acc && h2 !== `+${acc}`);
      setHidden(shown ? (f?.showInList ? [...base, acc] : base) : (f?.showInList ? base : [...base, `+${acc}`]));
    } else setHidden(hidden.includes(acc) ? hidden.filter((x) => x !== acc) : [...hidden, acc]);
  };

  return (
    <>
      <PageHeader title={terms.assets} subtitle={t('assets.subtitle')}
        actions={<>
          <Button variant="default" leftSection={<IconDownload size={16} />} component="a" href={`/api/assets/export${toQuery(filters)}`}>{t('common.export')}</Button>
          {can(P.assetsCreate) && <Button leftSection={<IconPlus size={16} />} component={Link} to="/assets/new">{t('assets.new')}</Button>}
        </>} />
      <Card p="sm" mb="md">
        <Group gap="xs" align="flex-end" wrap="wrap">
          <TextInput placeholder={t('assets.searchPlaceholder')} leftSection={<IconSearch size={16} />} value={search} onChange={(e) => setSearch(e.currentTarget.value)} w={{ base: '100%', sm: 260 }} />
          <Select placeholder={terms.site} data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} value={p('siteId') || null} clearable searchable w={{ base: '100%', xs: 190 }}
            onChange={(v) => set({ siteId: v, locationId: null })} />
          <Select placeholder={terms.location} data={locOpts} value={p('locationId') || null} clearable searchable w={{ base: '100%', xs: 220 }} disabled={!siteForLocations}
            onChange={(v) => set({ locationId: v })} comboboxProps={{ width: 'max-content', position: 'bottom-start' }} />
          <Select placeholder={t('assets.category')} data={(cats ?? []).map((c) => ({ value: c.id, label: c.name }))} value={p('categoryId') || null} clearable searchable w={{ base: '100%', xs: 190 }}
            onChange={(v) => set({ categoryId: v })} />
          <MultiSelect placeholder={p('status') ? undefined : t('common.status')} data={ASSET_STATUSES.map((s) => ({ value: s, label: t(`status.${s}`) }))} w={{ base: '100%', xs: 200 }}
            value={p('status') ? p('status').split(',') : []} onChange={(v) => set({ status: v.join(',') || null })} clearable />
          <MultiSelect placeholder={p('criticality') ? undefined : t('criticality.label')} data={CRITICALITIES.map((s) => ({ value: s, label: t(`criticality.${s}`) }))} w={{ base: '100%', xs: 190 }}
            value={p('criticality') ? p('criticality').split(',') : []} onChange={(v) => set({ criticality: v.join(',') || null })} clearable />
          <Switch label={t('assets.topLevelOnly')} checked={p('topLevelOnly') === 'true'} onChange={(e) => set({ topLevelOnly: e.currentTarget.checked ? 'true' : null })} mb={8} />
          <Group gap={6} ml="auto">
            {activeFilters > 0 && <Tooltip label={t('common.clear')}><ActionIcon variant="default" size="lg" aria-label={t('common.clear')} onClick={() => { setSearch(''); setParams({}, { replace: true }); }}><IconFilterOff size={16} /></ActionIcon></Tooltip>}
            <Menu closeOnItemClick={false} position="bottom-end" shadow="md">
              <Menu.Target><Tooltip label={t('assets.columns')}><ActionIcon variant="default" size="lg" aria-label={t('assets.columns')}><IconColumns3 size={16} /></ActionIcon></Tooltip></Menu.Target>
              <Menu.Dropdown mah={420} style={{ overflowY: 'auto' }}>
                {allColumns.slice(1).map((c) => (
                  <Menu.Item key={String(c.accessor)} onClick={() => toggleColumn(String(c.accessor))}>
                    <Checkbox size="xs" label={c.title as string} checked={!fieldHidden(String(c.accessor))} readOnly styles={{ input: { cursor: 'pointer' } }} />
                  </Menu.Item>
                ))}
              </Menu.Dropdown>
            </Menu>
          </Group>
        </Group>
      </Card>
      {selected.length > 0 && can(P.assetsEdit) && (
        <Card p="xs" mb="sm" bg="var(--mantine-color-brand-light)">
          <Group gap="xs">
            <Text fz="sm" fw={600} ml="xs">{t('common.selected', { count: selected.length })}</Text>
            <Menu><Menu.Target><Button size="xs" variant="white" leftSection={<IconEdit size={14} />}>{t('assets.setStatus')}</Button></Menu.Target>
              <Menu.Dropdown>{ASSET_STATUSES.map((s) => <Menu.Item key={s} onClick={() => bulk.mutate({ status: s })}>{t(`status.${s}`)}</Menu.Item>)}</Menu.Dropdown></Menu>
            <Menu><Menu.Target><Button size="xs" variant="white">{t('assets.setCriticality')}</Button></Menu.Target>
              <Menu.Dropdown>{CRITICALITIES.map((s) => <Menu.Item key={s} onClick={() => bulk.mutate({ criticality: s })}>{t(`criticality.${s}`)}</Menu.Item>)}</Menu.Dropdown></Menu>
            <Menu><Menu.Target><Button size="xs" variant="white">{t('assets.setCategory')}</Button></Menu.Target>
              <Menu.Dropdown mah={300} style={{ overflowY: 'auto' }}>{(cats ?? []).map((c) => <Menu.Item key={c.id} onClick={() => bulk.mutate({ categoryId: c.id })}>{c.name}</Menu.Item>)}</Menu.Dropdown></Menu>
            <Button size="xs" variant="subtle" onClick={() => setSelected([])}>{t('common.clear')}</Button>
          </Group>
        </Card>
      )}
      <Card p={0}>
        <DataTable<AssetListItem>
          minHeight={260} withTableBorder={false} highlightOnHover striped verticalSpacing="xs" fetching={isFetching}
          records={data?.items ?? []} columns={columns} idAccessor="id" noRecordsText={t('common.noRecords')}
          totalRecords={data?.total ?? 0} recordsPerPage={pageSize} page={page} onPageChange={(n) => set({ page: String(n) })}
          recordsPerPageOptions={PAGE_SIZES} onRecordsPerPageChange={(n) => set({ pageSize: String(n) })}
          sortStatus={sort} onSortStatusChange={(s) => set({ sort: String(s.columnAccessor), dir: s.direction })}
          selectedRecords={can(P.assetsEdit) ? selected : undefined} onSelectedRecordsChange={can(P.assetsEdit) ? setSelected : undefined}
          rowClassName={() => 'clickable-row'} onRowClick={({ record }) => navigate(`/assets/${record.id}`)}
          paginationText={({ from, to, totalRecords }) => `${from}–${to} / ${totalRecords}`}
        />
      </Card>
    </>
  );
}
