import { useState } from 'react';
import { Card, Group, Stack, Switch, Table, Text } from '@mantine/core';
import { BarChart } from '@mantine/charts';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { get } from '@/api/client';
import { useAuth } from '@/auth/AuthContext';
import { fmtNumber, fmtPrice } from '@/components/utils';

type CostYear = { year: number; labor: number; parts: number; other: number; contractor: number; downtimeHours: number; downtimeCost: number | null; total: number };
type AssetCostsData = { years: CostYear[]; costPerHour: number | null; fromCategory: boolean; components: boolean };

/** An asset's costs by year (0.14.0): labour, parts, other costs and what its downtime cost, with its components if asked. */
export function AssetCosts({ assetId, hasComponents }: { assetId: string; hasComponents: boolean }) {
  const { t } = useTranslation();
  const { me } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const [components, setComponents] = useState(false);
  const { data } = useQuery({ queryKey: ['asset-costs', assetId, components], queryFn: () => get<AssetCostsData>(`/assets/${assetId}/costs`, components ? { components: 'true' } : {}) });
  const money = (v: number | null) => (v === null ? '—' : fmtPrice(v, currency, Math.abs(v) >= 1000 || v === 0 ? 0 : 2));
  if (!data) return null;
  const chart = data.years.map((y) => ({ year: String(y.year), labor: y.labor, parts: y.parts, other: y.other, contractor: y.contractor ?? 0, downtime: y.downtimeCost ?? 0 }));
  const sum = (k: keyof CostYear) => data.years.reduce((a, y) => a + Number(y[k] ?? 0), 0);
  return (
    <Stack>
      <Group justify="space-between">
        <Text fz="sm" c="dimmed">{data.costPerHour === null ? t('costs.noRate')
          : `${t('costs.rate', { cost: money(data.costPerHour) })}${data.fromCategory ? ` · ${t('costs.fromCategory')}` : ''}`}</Text>
        {hasComponents && <Switch label={t('rep.withComponents')} checked={components} onChange={(e) => setComponents(e.currentTarget.checked)} />}
      </Group>
      <Card>
        <BarChart h={260} data={chart} dataKey="year" type="stacked" withLegend valueFormatter={(v) => fmtPrice(v, currency, 0)} series={[
          { name: 'labor', label: t('costs.labour'), color: 'blue.6' }, { name: 'parts', label: t('costs.parts'), color: 'teal.6' },
          { name: 'other', label: t('costs.other'), color: 'orange.6' }, { name: 'contractor', label: t('contractors.cost'), color: 'grape.6' }, { name: 'downtime', label: t('costs.downtime'), color: 'red.6' }]} />
      </Card>
      <Card p={0}>
        <Table striped verticalSpacing="xs" fz="sm">
          <Table.Thead><Table.Tr>
            <Table.Th>{t('rep.year')}</Table.Th><Table.Th ta="right">{t('costs.labour')}</Table.Th><Table.Th ta="right">{t('costs.parts')}</Table.Th>
            <Table.Th ta="right">{t('costs.other')}</Table.Th><Table.Th ta="right">{t('contractors.cost')}</Table.Th><Table.Th ta="right">{t('costs.downtimeHours')}</Table.Th><Table.Th ta="right">{t('costs.downtime')}</Table.Th>
            <Table.Th ta="right">{t('costs.total')}</Table.Th>
          </Table.Tr></Table.Thead>
          <Table.Tbody>
            {[...data.years].reverse().map((y) => (
              <Table.Tr key={y.year}>
                <Table.Td>{y.year}</Table.Td><Table.Td ta="right">{money(y.labor)}</Table.Td><Table.Td ta="right">{money(y.parts)}</Table.Td><Table.Td ta="right">{money(y.other)}</Table.Td>
                <Table.Td ta="right">{money(y.contractor ?? 0)}</Table.Td><Table.Td ta="right">{fmtNumber(y.downtimeHours)}</Table.Td><Table.Td ta="right">{money(y.downtimeCost)}</Table.Td><Table.Td ta="right" fw={600}>{money(y.total)}</Table.Td>
              </Table.Tr>
            ))}
            <Table.Tr fw={700}>
              <Table.Td>{t('costs.total')}</Table.Td><Table.Td ta="right">{money(sum('labor'))}</Table.Td><Table.Td ta="right">{money(sum('parts'))}</Table.Td>
              <Table.Td ta="right">{money(sum('other'))}</Table.Td><Table.Td ta="right">{money(sum('contractor'))}</Table.Td><Table.Td ta="right">{fmtNumber(sum('downtimeHours'))}</Table.Td>
              <Table.Td ta="right">{data.costPerHour === null && sum('downtimeCost') === 0 ? '—' : money(sum('downtimeCost'))}</Table.Td><Table.Td ta="right">{money(sum('total'))}</Table.Td>
            </Table.Tr>
          </Table.Tbody>
        </Table>
      </Card>
    </Stack>
  );
}
