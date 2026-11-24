import { useState } from 'react';
import { Anchor, Card, Group, Select, Switch, Table, Text, TextInput } from '@mantine/core';
import { useDebouncedValue } from '@mantine/hooks';
import { useQuery } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { get } from '@/api/client';
import type { UnitList } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { fmtDate, fmtPrice } from '@/components/utils';
import { UnitStateBadge } from './PartUnits';
import { UNIT_STATES } from './units';

/** Every serial unit (0.24.0), opened from the alert bell with the ones reaching the end of their life within 30 days. */
export function UnitsPage() {
  const { t } = useTranslation();
  const { me } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const [params, setParams] = useSearchParams();
  const ending = params.get('ending');
  const [state, setState] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [q] = useDebouncedValue(search, 300);
  const { data, isLoading } = useQuery({
    queryKey: ['serial-units', 'all', ending, state, q],
    queryFn: () => get<UnitList>('/serial-units', { endingWithinDays: ending ?? undefined, state: state ?? undefined, search: q || undefined }),
  });
  const rows = data?.items;
  return (
    <>
      <PageHeader crumbs={[{ label: t('inv.title'), to: '/inventory' }, { label: t('units.tab') }]} title={t('units.allUnits')} />
      <Card p={0}>
        <Group p="md" gap="sm">
          <TextInput size="xs" w={220} placeholder={t('units.searchHint')} value={search} onChange={(e) => setSearch(e.currentTarget.value)} />
          <Select size="xs" w={170} clearable placeholder={t('units.all')} value={state} onChange={setState} data={UNIT_STATES.map((s) => ({ value: s, label: t(`units.state.${s}`) }))} />
          <Switch size="sm" label={t('units.endingSoon')} checked={!!ending} onChange={(e) => setParams(e.currentTarget.checked ? { ending: '30' } : {})} />
        </Group>
        <Table.ScrollContainer minWidth={760}>
          <Table verticalSpacing="sm" highlightOnHover>
            <Table.Thead><Table.Tr><Table.Th>{t('units.serial')}</Table.Th><Table.Th>{t('inv.part')}</Table.Th><Table.Th>{t('common.status')}</Table.Th>
              <Table.Th>{t('units.where')}</Table.Th><Table.Th>{t('units.endOfLife')}</Table.Th><Table.Th ta="right">{t('units.repairs')}</Table.Th></Table.Tr></Table.Thead>
            <Table.Tbody>
              {!isLoading && (rows ?? []).length === 0 && <Table.Tr><Table.Td colSpan={6}><Text c="dimmed" ta="center" py="md">{t('units.none')}</Text></Table.Td></Table.Tr>}
              {(rows ?? []).map((r) => (
                <Table.Tr key={r.id}>
                  <Table.Td><Anchor component={Link} to={`/assets/${r.id}`} fz="sm" fw={600}>{r.serialNumber}</Anchor><Text fz="xs" c="dimmed">{r.assetNumber}</Text></Table.Td>
                  <Table.Td><Anchor component={Link} to={`/inventory/parts/${r.partId}`} fz="sm">{r.partNumber}</Anchor><Text fz="xs" c="dimmed">{r.partName}</Text></Table.Td>
                  <Table.Td><UnitStateBadge state={r.state} /></Table.Td>
                  <Table.Td>{r.state === 'Fitted' && r.fittedToId ? <Anchor component={Link} to={`/assets/${r.fittedToId}`} fz="sm">{r.fittedToNumber} · {r.fittedToName}</Anchor>
                    : <Text fz="sm">{r.storeName ?? '—'}</Text>}<Text fz="xs" c="dimmed">{r.siteName}</Text></Table.Td>
                  <Table.Td>{fmtDate(r.endOfLifeOn)}</Table.Td>
                  <Table.Td ta="right">{r.repairCount > 0 ? `${r.repairCount} · ${fmtPrice(r.repairCost, currency)}` : '—'}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
        {data?.capped && <Text fz="xs" c="dimmed" p="md">{t('units.capped')}</Text>}
      </Card>
    </>
  );
}
