import { useEffect, useMemo, useState } from 'react';
import { ActionIcon, Anchor, Badge, Button, Card, Group, Menu, Modal, MultiSelect, Select, Stack, Switch, Text, TextInput, Tooltip } from '@mantine/core';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { DataTable } from 'mantine-datatable';
import { IconFilterOff, IconPlus, IconSearch, IconEdit, IconQrcode } from '@tabler/icons-react';
import { openLabels } from '@/pages/labels/LabelsPage';
import { BulkBar } from '@/components/list';
import { apiQuery } from '@/components/list/apiQueries';
import { get, post } from '@/api/client';
import { type AssetStatus, type Criticality, type AssetListItem, type Category, type CustomField, type Paged } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { CriticalityBadge, StatusBadge } from '@/components/Badges';
import { PageHeader } from '@/components/PageHeader';
import { locationOptions, useHierarchy } from '@/components/LocationPicker';
import { fmtDate, fmtDateTime, notifyError, notifySuccess } from '@/components/utils';
import { FilterChips, ListTools, listText, useListColumns, useListParams, type FilterDef, type ListColumn } from '@/components/list';
import { listItems } from '@/components/listLabels';
import { groupData, useAssetGroups, useBrands } from '@/components/ReferencePickers';
import { TranslateButton } from '@/components/TranslateButton';
import { pick } from '@/components/bilingual';

const PAGE_SIZES = [25, 50, 100];
const DEFAULT_HIDDEN = ['manufacturer', 'model', 'serialNumber', 'commissionedOn', 'warrantyExpiresOn', 'parentAssetNumber'];

export function AssetsPage() {
  const { t } = useTranslation();
  const { terms, can } = useAuth();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const lp = useListParams<AssetListItem>({ sort: 'assetNumber', dir: 'asc' });
  const { p, set, page, pageSize, sort } = lp;
  const filters = apiQuery('assets', lp.params, can);
  const { data, isFetching } = useQuery({
    queryKey: ['assets', filters, page, pageSize],
    queryFn: () => get<Paged<AssetListItem>>('/assets', { ...filters, page, pageSize }),
    placeholderData: (prev) => prev,
  });
  const { data: h } = useHierarchy();
  const { data: cats } = useQuery({ queryKey: ['categories'], queryFn: () => get<Category[]>('/asset-categories') });
  const { data: fields } = useQuery({ queryKey: ['custom-fields', 'Asset'], queryFn: () => get<CustomField[]>('/custom-fields', { entityType: 'Asset' }) });
  const [selected, setSelected] = useState<AssetListItem[]>([]);
  // Ticked rows belong to the list on screen: changing its filters, tab or sort starts a new selection.
  const selectionKey = JSON.stringify(filters);
  useEffect(() => setSelected([]), [selectionKey]);

  // If filtering by location only, infer the site so the location dropdown has options.
  const siteForLocations = p('siteId') || h?.locations.find((l) => l.id === p('locationId'))?.siteId || null;
  const locOpts = useMemo(() => locationOptions(h?.locations ?? [], siteForLocations), [h, siteForLocations]);
  const activeFilters = ['siteId', 'locationId', 'categoryId', 'status', 'criticality', 'topLevelOnly', 'downNow', 'search', 'brandId', 'modelId', 'assetGroupId', 'spareUnits'].filter((k) => p(k)).length;
  // Brands, models and asset groups (0.21.0).
  const { data: brands } = useBrands();
  const { data: groups } = useAssetGroups();
  const brandOf = brands?.find((b) => b.id === p('brandId'));
  const [groupFor, setGroupFor] = useState<'add' | 'remove' | null>(null);
  const [groupChoice, setGroupChoice] = useState<string | null>(null);
  const changeGroup = async () => {
    if (!groupChoice || !groupFor) return;
    try {
      const ids = selected.map((s) => s.id);
      const r = await post<{ added: number; removed: number }>(`/asset-groups/${groupChoice}/members`, groupFor === 'add' ? { add: ids, remove: [] } : { add: [], remove: ids });
      notifySuccess(groupFor === 'add' ? t('assetGroups.added', { count: r.added }) : t('assetGroups.removed', { count: r.removed }));
      setGroupFor(null); setGroupChoice(null); setSelected([]);
      await qc.invalidateQueries({ queryKey: ['assets'] }); await qc.invalidateQueries({ queryKey: ['asset-groups'] });
    } catch (e) { notifyError(e); }
  };

  const bulk = useMutation({
    mutationFn: (patch: Record<string, string>) => post<{ updated: number }>('/assets/bulk', { ids: selected.map((s) => s.id), ...patch }),
    onSuccess: (r) => { notifySuccess(t('assets.bulkUpdated', { count: r.updated })); setSelected([]); void qc.invalidateQueries({ queryKey: ['assets'] }); },
    onError: notifyError,
  });

  const allColumns: ListColumn<AssetListItem>[] = [
    { accessor: 'assetNumber', title: t('assets.number'), sortable: true, width: 130,
      render: (a) => <Anchor component={Link} to={`/assets/${a.id}`} fw={600} fz="sm" onClick={(e) => e.stopPropagation()}>{a.assetNumber}</Anchor> },
    { accessor: 'name', title: t('common.name'), sortable: true, width: 260, render: (a) => <Stack gap={0}><Text fz="sm" lineClamp={1}>{a.name}</Text>{a.parentAssetNumber && <Text fz="xs" c="dimmed">↳ {a.parentAssetNumber}</Text>}</Stack> },
    { accessor: 'category', title: t('assets.category'), sortable: true, noWrap: true, width: 210, export: [{ key: 'categoryName' }],
      render: (a) => a.categoryName ? <Badge variant="dot" color={a.categoryColor ?? 'gray'} style={{ maxWidth: 'none' }} styles={{ label: { textTransform: 'none', fontWeight: 500, overflow: 'visible' } }}>{a.categoryName}</Badge> : '—' },
    { accessor: 'site', title: terms.site, sortable: true, noWrap: true, export: [{ key: 'siteName' }], render: (a) => a.siteName },
    { accessor: 'location', title: terms.location, sortable: true, width: 240, ellipsis: true, export: [{ key: 'locationPath' }], render: (a) => <span title={a.locationPath}>{a.locationPath || '—'}</span> },
    { accessor: 'status', title: t('common.status'), sortable: true, noWrap: true, width: 160, render: (a) => <Group gap={4} wrap="nowrap"><StatusBadge status={a.status} />
      {a.downSince && <Tooltip label={t('downtime.downSinceShort', { when: fmtDateTime(a.downSince) })}><Badge color="red" variant="filled" size="sm">{t('downtime.down')}</Badge></Tooltip>}</Group> },
    { accessor: 'criticality', title: t('criticality.label'), sortable: true, noWrap: true, width: 130, render: (a) => <CriticalityBadge value={a.criticality} /> },
    { accessor: 'manufacturer', title: t('assets.manufacturer'), sortable: true, noWrap: true },
    { accessor: 'model', title: t('assets.model'), sortable: true, noWrap: true },
    { accessor: 'serialNumber', title: t('assets.serialNumber'), sortable: true, noWrap: true },
    { accessor: 'parentAssetNumber', title: t('assets.parent') },
    { accessor: 'commissionedOn', title: t('assets.commissionedOn'), sortable: true, render: (a) => fmtDate(a.commissionedOn) },
    { accessor: 'warrantyExpiresOn', title: t('assets.warrantyExpires'), sortable: true, render: (a) => fmtDate(a.warrantyExpiresOn) },
    ...(fields ?? []).map<ListColumn<AssetListItem>>((f) => ({
      accessor: `cf_${f.key}`, title: f.label, noWrap: true, export: [{ key: `customFields.${f.key}` }],
      render: (a) => { const v = a.customFields?.[f.key]; return f.dataType === 'Boolean' ? (v === 'true' ? t('common.yes') : v === 'false' ? t('common.no') : '—') : f.dataType === 'Date' ? fmtDate(v) : v ?? '—'; },
    })),
  ];
  const cols = useListColumns('assets', allColumns, {
    defaultHidden: DEFAULT_HIDDEN, legacyKey: 'assets.hiddenColumns',
    hiddenByDefault: (acc) => acc.startsWith('cf_') && !fields?.find((x) => `cf_${x.key}` === acc)?.showInList,
  });
  const statusLabel = (v: string) => t(`status.${v}`);
  const chips: FilterDef[] = [
    { keys: ['search'], label: t('common.search') },
    { keys: ['siteId'], label: terms.site, format: ([v]) => h?.sites.find((x) => x.id === v)?.name ?? '…' },
    { keys: ['locationId'], label: terms.location, format: ([v]) => h?.locations.find((x) => x.id === v)?.name ?? '…' },
    { keys: ['categoryId'], label: t('assets.category'), format: ([v]) => cats?.find((c) => c.id === v)?.name ?? '…' },
    { keys: ['status'], label: t('common.status'), format: listText(statusLabel) },
    { keys: ['criticality'], label: t('criticality.label'), format: listText((v) => t(`criticality.${v}`)) },
    { keys: ['topLevelOnly'], label: t('assets.topLevelOnly'), format: () => t('common.yes') },
    { keys: ['downNow'], label: t('downtime.downNow'), format: () => t('common.yes') },
    { keys: ['spareUnits'], label: t('units.showSpares'), format: () => t('common.yes') },
    { keys: ['brandId'], label: t('brands.brand'), format: ([v]) => brands?.find((b) => b.id === v)?.name ?? '…' },
    { keys: ['modelId'], label: t('brands.model'), format: ([v]) => brands?.flatMap((b) => b.models).find((x) => x.id === v)?.name ?? '…' },
    { keys: ['assetGroupId'], label: t('assetGroups.one'), format: ([v]) => groups?.find((g) => g.id === v)?.name ?? '…' },
  ];

  return (
    <>
      <PageHeader title={terms.assets} subtitle={t('assets.subtitle')}
        actions={<>
          <TranslateButton list="assets" refresh={[['assets']]} />
          {can(P.assetsCreate) && <Button leftSection={<IconPlus size={16} />} component={Link} to="/assets/new">{t('assets.new')}</Button>}
        </>} />
      <Card p="sm" mb="md">
        <Group gap="xs" align="flex-end" wrap="wrap">
          <TextInput placeholder={t('assets.searchPlaceholder')} leftSection={<IconSearch size={16} />} value={lp.search} onChange={(e) => lp.setSearch(e.currentTarget.value)} w={{ base: '100%', sm: 260 }} />
          <Select placeholder={terms.site} data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} value={p('siteId') || null} clearable searchable w={{ base: '100%', xs: 190 }}
            onChange={(v) => set({ siteId: v, locationId: null })} />
          <Select placeholder={terms.location} data={locOpts} value={p('locationId') || null} clearable searchable w={{ base: '100%', xs: 220 }} disabled={!siteForLocations}
            onChange={(v) => set({ locationId: v })} comboboxProps={{ width: 'max-content', position: 'bottom-start' }} />
          <Select placeholder={t('assets.category')} data={(cats ?? []).map((c) => ({ value: c.id, label: pick(c.name, c.nameAr) }))} value={p('categoryId') || null} clearable searchable w={{ base: '100%', xs: 190 }}
            onChange={(v) => set({ categoryId: v })} />
          <MultiSelect placeholder={p('status') ? undefined : t('common.status')} data={listItems('assetStatus', t, null, true)} w={{ base: '100%', xs: 200 }}
            value={p('status') ? p('status').split(',') : []} onChange={(v) => set({ status: v.join(',') || null })} clearable />
          <MultiSelect placeholder={p('criticality') ? undefined : t('criticality.label')} data={listItems('criticality', t, null, true)} w={{ base: '100%', xs: 190 }}
            value={p('criticality') ? p('criticality').split(',') : []} onChange={(v) => set({ criticality: v.join(',') || null })} clearable />
          <Select placeholder={t('brands.brand')} data={(brands ?? []).map((b) => ({ value: b.id, label: b.name }))} value={p('brandId') || null} clearable searchable w={{ base: '100%', xs: 170 }}
            onChange={(v) => set({ brandId: v, modelId: null })} />
          <Select placeholder={t('brands.model')} data={(brandOf?.models ?? []).map((x) => ({ value: x.id, label: x.name }))} value={p('modelId') || null} clearable searchable
            disabled={!brandOf} w={{ base: '100%', xs: 150 }} onChange={(v) => set({ modelId: v })} />
          <Select placeholder={t('assetGroups.one')} data={groupData(groups, null, p('assetGroupId') || null)} value={p('assetGroupId') || null} clearable searchable
            w={{ base: '100%', xs: 190 }} onChange={(v) => set({ assetGroupId: v })} />
          <Switch label={t('assets.topLevelOnly')} checked={p('topLevelOnly') === 'true'} onChange={(e) => set({ topLevelOnly: e.currentTarget.checked ? 'true' : null })} mb={8} />
          <Switch label={t('downtime.downNow')} color="red" checked={p('downNow') === 'true'} onChange={(e) => set({ downNow: e.currentTarget.checked ? 'true' : null })} mb={8} />
          <Switch label={t('units.showSpares')} checked={p('spareUnits') === 'true'} onChange={(e) => set({ spareUnits: e.currentTarget.checked ? 'true' : null })} mb={8} />
          <Group gap={6} ml="auto">
            {activeFilters > 0 && <Tooltip label={t('common.clear')}><ActionIcon variant="default" size="lg" aria-label={t('common.clear')} onClick={() => lp.replaceAll('')}><IconFilterOff size={16} /></ActionIcon></Tooltip>}
            <ListTools list="assets" lp={lp} cols={cols} exportPath="/assets/export" exportQuery={filters} total={data?.total} />
          </Group>
        </Group>
        <FilterChips defs={chips} p={p} set={set} />
      </Card>
      {can(P.assetsEdit) && (
        <BulkBar count={selected.length} onClear={() => setSelected([])}>
            <Menu><Menu.Target><Button size="xs" variant="white" leftSection={<IconEdit size={14} />}>{t('assets.setStatus')}</Button></Menu.Target>
              <Menu.Dropdown>{listItems('assetStatus', t).map((s) => <Menu.Item key={s.value} onClick={() => bulk.mutate({ status: s.value as AssetStatus })}>{s.label}</Menu.Item>)}</Menu.Dropdown></Menu>
            <Menu><Menu.Target><Button size="xs" variant="white">{t('assets.setCriticality')}</Button></Menu.Target>
              <Menu.Dropdown>{listItems('criticality', t).map((s) => <Menu.Item key={s.value} onClick={() => bulk.mutate({ criticality: s.value as Criticality })}>{s.label}</Menu.Item>)}</Menu.Dropdown></Menu>
            <Menu><Menu.Target><Button size="xs" variant="white">{t('assets.setCategory')}</Button></Menu.Target>
              <Menu.Dropdown mah={300} style={{ overflowY: 'auto' }}>{(cats ?? []).map((c) => <Menu.Item key={c.id} onClick={() => bulk.mutate({ categoryId: c.id })}>{pick(c.name, c.nameAr)}</Menu.Item>)}</Menu.Dropdown></Menu>
            <Menu><Menu.Target><Button size="xs" variant="white">{t('assetGroups.one')}</Button></Menu.Target>
              <Menu.Dropdown>
                <Menu.Item onClick={() => setGroupFor('add')}>{t('assetGroups.addTo')}</Menu.Item>
                <Menu.Item onClick={() => setGroupFor('remove')}>{t('assetGroups.removeFrom')}</Menu.Item>
              </Menu.Dropdown></Menu>
            <Button size="xs" variant="white" leftSection={<IconQrcode size={14} />} onClick={() => openLabels('asset', selected.map((s) => s.id))}>{t('labels.printLabels')}</Button>
        </BulkBar>
      )}
      {groupFor && (
        <Modal opened onClose={() => setGroupFor(null)} title={t(groupFor === 'add' ? 'assetGroups.addTo' : 'assetGroups.removeFrom')}>
          <Stack>
            <Text fz="sm" c="dimmed">{t('assetGroups.selectedCount', { count: selected.length })}</Text>
            <Select label={t('assetGroups.one')} data={groupData(groups)} value={groupChoice} onChange={setGroupChoice} searchable data-autofocus />
            <Group justify="flex-end"><Button variant="default" onClick={() => setGroupFor(null)}>{t('common.cancel')}</Button>
              <Button disabled={!groupChoice} onClick={changeGroup}>{t('common.save')}</Button></Group>
          </Stack>
        </Modal>
      )}
      <Card p={0}>
        <DataTable<AssetListItem>
          minHeight={260} withTableBorder={false} highlightOnHover striped verticalSpacing="xs" fetching={isFetching}
          records={data?.items ?? []} columns={cols.columns} idAccessor="id" noRecordsText={t('common.noRecords')}
          totalRecords={data?.total ?? 0} recordsPerPage={pageSize} page={page} onPageChange={(n) => set({ page: String(n) })}
          recordsPerPageOptions={PAGE_SIZES} recordsPerPageLabel={t('common.perPage')} onRecordsPerPageChange={(n) => set({ pageSize: String(n) })}
          sortStatus={sort} onSortStatusChange={lp.setSort}
          selectedRecords={can(P.assetsEdit) ? selected : undefined} onSelectedRecordsChange={can(P.assetsEdit) ? setSelected : undefined}
          rowClassName={() => 'clickable-row'} onRowClick={({ record }) => navigate(`/assets/${record.id}`)}
          paginationText={({ from, to, totalRecords }) => `${from}–${to} / ${totalRecords}`}
        />
      </Card>
    </>
  );
}
