import { useMemo, useState } from 'react';
import { Badge, Button, Card, Checkbox, Group, Menu, Select, Stack, Text, TextInput } from '@mantine/core';
import { useDebouncedValue } from '@mantine/hooks';
import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { DataTable } from 'mantine-datatable';
import { IconDownload, IconPlus, IconSearch } from '@tabler/icons-react';
import { get } from '@/api/client';
import type { ContractListItem, ContractStatus, Lookup, Paged, SupplierLookup } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { useHierarchy } from '@/components/LocationPicker';
import { exportUrl } from '@/components/list/ListTools';
import type { ListColumn } from '@/components/list/types';
import { fmtDate, fmtPrice } from '@/components/utils';
import { daysLeft, statusColor } from './contracts';

const STATUSES: ContractStatus[] = ['Active', 'Upcoming', 'Expired', 'Terminated'];

/** Maintenance › Contracts (0.17.0). */
export function ContractsPage() {
  const { t } = useTranslation();
  const { can, me, terms } = useAuth();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const [search, setSearch] = useState('');
  const [debounced] = useDebouncedValue(search, 300);
  const [page, setPage] = useState(1);
  const set = (key: string, value: string | null) => { const next = new URLSearchParams(params); if (value) next.set(key, value); else next.delete(key); setParams(next, { replace: true }); setPage(1); };
  const status = params.get('status');
  const attention = params.get('attention') === 'true';
  const query = { search: debounced || undefined, status: status ?? undefined, supplierId: params.get('company') ?? undefined, contractTypeId: params.get('type') ?? undefined,
    siteId: params.get('site') ?? undefined, attention: attention ? 'true' : undefined };
  const { data, isFetching } = useQuery({ queryKey: ['contracts', query, page], queryFn: () => get<Paged<ContractListItem>>('/contracts', { ...query, page, pageSize: 25 }), placeholderData: (p) => p });
  const { data: companies } = useQuery({ queryKey: ['suppliers', 'lookup', 'contractor'], queryFn: () => get<SupplierLookup[]>('/suppliers/lookup', { role: 'contractor' }) });
  const { data: types } = useQuery({ queryKey: ['contract-types'], queryFn: () => get<Lookup[]>('/contract-types') });
  const { data: h } = useHierarchy();
  const today = new Date().toISOString().slice(0, 10);
  const columns: ListColumn<ContractListItem>[] = useMemo(() => [
    { accessor: 'number', title: t('contracts.number'), width: 120, render: (c) => <Text fz="sm" fw={600}>{c.number}</Text> },
    { accessor: 'title', title: t('contracts.contractTitle'), render: (c) => <Stack gap={0}><Text fz="sm">{c.title}</Text><Text fz="xs" c="dimmed">{c.companyName}</Text></Stack> },
    { accessor: 'typeName', title: t('common.type'), render: (c) => c.typeName ?? '—' },
    { accessor: 'siteName', title: terms.site, render: (c) => c.siteName ?? t('contracts.allSites') },
    { accessor: 'startDate', title: t('contracts.start'), noWrap: true, render: (c) => fmtDate(c.startDate) },
    { accessor: 'endDate', title: t('contracts.end'), noWrap: true, render: (c) => fmtDate(c.endDate) },
    { accessor: 'deadline', title: t('contracts.deadline'), noWrap: true, render: (c) => c.deadline ? <Group gap={6} wrap="nowrap"><Text fz="sm">{fmtDate(c.deadline)}</Text>
      {c.needsAttention && <Badge size="xs" color="orange">{t('contracts.daysLeft', { count: daysLeft(c.deadline, today) })}</Badge>}</Group> : '—' },
    { accessor: 'status', title: t('common.status'), render: (c) => <Badge size="sm" variant="light" color={statusColor(c.status)}>{t(`contracts.status.${c.status}`)}</Badge> },
    { accessor: 'value', title: t('contracts.value'), textAlign: 'right', noWrap: true, render: (c) => fmtPrice(c.value, c.currency ?? me?.tenant.settings.currency ?? 'USD', 0) },
  ], [t, me, terms, today]);
  return (
    <>
      <PageHeader title={t('contracts.title')} subtitle={t('contracts.subtitle')}
        actions={can(P.contractsManage) && <Button leftSection={<IconPlus size={16} />} component={Link} to="/contracts/new">{t('contracts.new')}</Button>} />
      <Card p={0}>
        <Group p="md" gap="xs" wrap="wrap">
          <TextInput placeholder={t('contracts.search')} leftSection={<IconSearch size={16} />} value={search} onChange={(e) => { setSearch(e.currentTarget.value); setPage(1); }} w={220} />
          <Select placeholder={t('common.status')} clearable w={150} data={STATUSES.map((s) => ({ value: s, label: t(`contracts.status.${s}`) }))} value={status} onChange={(v) => set('status', v)} />
          <Select placeholder={t('contracts.company')} clearable searchable w={200} data={(companies ?? []).map((c) => ({ value: c.id, label: c.name }))} value={params.get('company')} onChange={(v) => set('company', v)} />
          <Select placeholder={t('common.type')} clearable w={160} data={(types ?? []).map((x) => ({ value: x.id, label: x.name }))} value={params.get('type')} onChange={(v) => set('type', v)} />
          <Select placeholder={terms.site} clearable w={180} data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} value={params.get('site')} onChange={(v) => set('site', v)} />
          <Checkbox label={t('contracts.needingAttention')} checked={attention} onChange={(e) => set('attention', e.currentTarget.checked ? 'true' : null)} />
          <Menu position="bottom-end">
            <Menu.Target><Button variant="default" size="xs" ml="auto" leftSection={<IconDownload size={14} />}>{t('list.export')}</Button></Menu.Target>
            <Menu.Dropdown>
              <Menu.Item component="a" href={exportUrl('/contracts/export', query, columns, 'xlsx')} download>{t('list.exportXlsx')}</Menu.Item>
              <Menu.Item component="a" href={exportUrl('/contracts/export', query, columns, 'csv')} download>{t('list.exportCsv')}</Menu.Item>
            </Menu.Dropdown>
          </Menu>
        </Group>
        <DataTable<ContractListItem> minHeight={220} withTableBorder={false} striped highlightOnHover verticalSpacing="xs" fetching={isFetching} records={data?.items ?? []}
          noRecordsText={t('contracts.none')} totalRecords={data?.total ?? 0} recordsPerPage={25} page={page} onPageChange={setPage}
          rowClassName={() => 'clickable-row'} onRowClick={({ record }) => navigate(`/contracts/${record.id}`)} columns={columns} />
      </Card>
    </>
  );
}
