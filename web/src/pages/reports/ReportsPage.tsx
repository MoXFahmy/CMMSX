import { useMemo, useState } from 'react';
import { useMediaQuery } from '@mantine/hooks';
import { Anchor, Badge, Button, Card, Center, Group, Loader, NumberInput, Select, SimpleGrid, Stack, Text, TextInput, ThemeIcon, Title, Alert } from '@mantine/core';
import { DatePickerInput } from '@mantine/dates';
import { BarChart, DonutChart } from '@mantine/charts';
import { useQuery } from '@tanstack/react-query';
import { Link, useParams, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { DataTable, type DataTableSortStatus } from 'mantine-datatable';
import dayjs from 'dayjs';
import { IconAlertTriangle, IconBox, IconChartBar, IconCoin, IconDownload, IconPackage, IconPrinter, IconSearch, IconShoppingCart, IconTool } from '@tabler/icons-react';
import { get } from '@/api/client';
import type { ReportColumnType, ReportDef, ReportResult } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { useHierarchy } from '@/components/LocationPicker';
import { fmtDate, fmtNumber, fmtPrice } from '@/components/utils';
import { downloadCsv } from '../inventory/InventoryPage';

const CATEGORY_ICON: Record<string, typeof IconTool> = { Work: IconTool, Reliability: IconAlertTriangle, Costs: IconCoin, Assets: IconBox, Inventory: IconPackage, Purchasing: IconShoppingCart };
const CATEGORY_COLOR: Record<string, string> = { Work: 'blue', Reliability: 'red', Costs: 'teal', Assets: 'indigo', Inventory: 'orange', Purchasing: 'grape' };
const PALETTE = ['blue.6', 'teal.6', 'orange.6', 'grape.6', 'red.6', 'cyan.6', 'lime.6', 'pink.6', 'indigo.6', 'yellow.6'];

export function ReportsPage() {
  const { t } = useTranslation();
  const { data, isLoading } = useQuery({ queryKey: ['reports'], queryFn: () => get<ReportDef[]>('/reports') });
  const [filter, setFilter] = useState('');
  const groups = useMemo(() => {
    const list = (data ?? []).filter((r) => !filter || `${r.title} ${r.description}`.toLowerCase().includes(filter.toLowerCase()));
    return Object.entries(list.reduce<Record<string, ReportDef[]>>((a, r) => { (a[r.category] ??= []).push(r); return a; }, {}));
  }, [data, filter]);
  return (
    <>
      <PageHeader title={t('rep.title')} subtitle={t('rep.subtitle')}
        actions={<TextInput placeholder={t('rep.find')} leftSection={<IconSearch size={16} />} value={filter} onChange={(e) => setFilter(e.currentTarget.value)} w={240} />} />
      {isLoading && <Center h={200}><Loader /></Center>}
      <Stack gap="xl">
        {groups.map(([cat, reports]) => {
          const Icon = CATEGORY_ICON[cat] ?? IconChartBar;
          return (
            <div key={cat}>
              <Group gap="xs" mb="sm"><ThemeIcon variant="light" color={CATEGORY_COLOR[cat]} size="md"><Icon size={16} /></ThemeIcon><Title order={4}>{t(`rep.cat.${cat}`)}</Title></Group>
              <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }}>
                {reports.map((r) => (
                  <Card key={r.key} component={Link} to={`/reports/${r.key}`} className="hover-card" padding="lg">
                    <Text fw={600}>{r.title}</Text>
                    <Text fz="sm" c="dimmed" mt={4}>{r.description}</Text>
                  </Card>
                ))}
              </SimpleGrid>
            </div>
          );
        })}
      </Stack>
    </>
  );
}

const PRESETS: { key: string; range: () => [string, string] }[] = [
  { key: 'thisMonth', range: () => [dayjs().startOf('month').format('YYYY-MM-DD'), dayjs().format('YYYY-MM-DD')] },
  { key: 'lastMonth', range: () => [dayjs().subtract(1, 'month').startOf('month').format('YYYY-MM-DD'), dayjs().subtract(1, 'month').endOf('month').format('YYYY-MM-DD')] },
  { key: 'last90', range: () => [dayjs().subtract(89, 'day').format('YYYY-MM-DD'), dayjs().format('YYYY-MM-DD')] },
  { key: 'ytd', range: () => [dayjs().startOf('year').format('YYYY-MM-DD'), dayjs().format('YYYY-MM-DD')] },
  { key: 'last12', range: () => [dayjs().subtract(12, 'month').add(1, 'day').format('YYYY-MM-DD'), dayjs().format('YYYY-MM-DD')] },
  { key: 'lastYear', range: () => [dayjs().subtract(1, 'year').startOf('year').format('YYYY-MM-DD'), dayjs().subtract(1, 'year').endOf('year').format('YYYY-MM-DD')] },
];

export function ReportViewPage() {
  const { t } = useTranslation();
  const { key = '' } = useParams();
  const { me, terms } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const [params, setParams] = useSearchParams();
  const { data: catalog } = useQuery({ queryKey: ['reports'], queryFn: () => get<ReportDef[]>('/reports') });
  const def = catalog?.find((r) => r.key === key);
  const { data: h } = useHierarchy();
  const p = (k: string) => params.get(k) ?? '';
  const set = (patch: Record<string, string | null>) => { const n = new URLSearchParams(params); for (const [k, v] of Object.entries(patch)) { if (v) n.set(k, v); else n.delete(k); } setParams(n, { replace: true }); };
  const has = (name: string) => !!def?.params.some((x) => x.name === name);
  const preset = p('preset') || (p('from') ? '' : 'last12');
  const [from, to] = preset ? PRESETS.find((x) => x.key === preset)!.range() : [p('from'), p('to')];
  const groupParam = def?.params.find((x) => x.name === 'groupBy');
  const daysParam = def?.params.find((x) => x.name === 'days');
  const q = { from: has('range') ? from : undefined, to: has('range') ? to : undefined, siteId: p('siteId') || undefined, groupBy: p('groupBy') || (groupParam?.default as string | undefined), days: p('days') || (daysParam?.default as number | undefined) };
  const { data, isFetching, error } = useQuery({ queryKey: ['report', key, q], queryFn: () => get<ReportResult>(`/reports/${key}`, q), enabled: !!def, placeholderData: (prev) => (prev?.key === key ? prev : undefined) });
  const NONE: DataTableSortStatus<Record<string, unknown>> = { columnAccessor: '__none', direction: 'asc' };
  const [sort, setSort] = useState<DataTableSortStatus<Record<string, unknown>>>(NONE);
  const fmt = (v: unknown, type: ReportColumnType) => {
    if (v === null || v === undefined || v === '') return '—';
    switch (type) {
      case 'currency': return fmtPrice(Number(v), currency, Math.abs(Number(v)) >= 1000 || Number(v) === 0 ? 0 : 2);
      case 'percent': return `${fmtNumber(Number(v))}%`;
      case 'hours': return fmtNumber(Math.round(Number(v) * 10) / 10);
      case 'number': return fmtNumber(Number(v));
      case 'date': return fmtDate(String(v));
      default: return String(v);
    }
  };
  const rows = useMemo(() => {
    const list = [...(data?.rows ?? [])];
    if (sort.columnAccessor !== '__none') list.sort((a, b) => {
      const x = a[sort.columnAccessor as string], y = b[sort.columnAccessor as string];
      const c = typeof x === 'number' && typeof y === 'number' ? x - y : String(x ?? '').localeCompare(String(y ?? ''), undefined, { numeric: true });
      return sort.direction === 'asc' ? c : -c;
    });
    return list;
  }, [data, sort]);
  const link = (r: Record<string, unknown>) => (r.partId ? `/inventory/parts/${r.partId}` : r.assetId ? `/assets/${r.assetId}` : key === 'open-purchase-orders' && r.id ? `/purchasing/orders/${r.id}` : null);
  const exportCsv = () => data && downloadCsv(`${data.key}-${dayjs().format('YYYYMMDD')}.csv`, [data.columns.map((c) => c.label), ...data.rows.map((r) => data.columns.map((c) => r[c.key]))]);
  const narrow = useMediaQuery('(max-width: 48em)');
  const axisW = narrow ? 110 : 240;
  const clip = (v: string) => { const max = narrow ? 14 : 34; return v.length > max ? `${v.slice(0, max - 1)}…` : v; };
  // Single-line category labels (Recharts would otherwise wrap long names onto several lines).
  const AxisTick = ({ x, y, payload }: { x: number; y: number; payload: { value: unknown } }) => (
    <text x={x} y={y} dy={4} textAnchor="end" fontSize={12} fill="var(--mantine-color-dimmed)"><title>{String(payload.value)}</title>{clip(String(payload.value))}</text>
  );
  const chartRows = (data?.rows ?? []).slice(0, data?.chart?.type === 'donut' ? 10 : 15);
  const label = (k: string) => data?.columns.find((c) => c.key === k)?.label ?? k;
  const chartFmt = (v: number) => (data?.columns.find((c) => data.chart?.series.includes(c.key))?.type === 'currency' ? fmtPrice(v, currency, 0) : fmtNumber(v));

  if (catalog && !def) return <Alert color="red">{t('rep.notFound')}</Alert>;
  return (
    <>
      <PageHeader crumbs={[{ label: t('rep.title'), to: '/reports' }, { label: def?.title ?? '…' }]} title={def?.title ?? '…'} subtitle={data?.subtitle ?? def?.description}
        actions={<><Button variant="default" leftSection={<IconPrinter size={16} />} onClick={() => window.print()} disabled={!data}>{t('rep.print')}</Button>
          <Button variant="default" leftSection={<IconDownload size={16} />} onClick={exportCsv} disabled={!data?.rows.length}>{t('common.export')}</Button></>} />
      {def && (def.params.length > 0) && (
        <Card p="sm" mb="md" className="no-print">
          <Group gap="xs" wrap="wrap" align="flex-end">
            {has('range') && <>
              <Select label={t('rep.period')} w={180} value={preset || 'custom'} onChange={(v) => v === 'custom' ? set({ preset: null, from, to }) : set({ preset: v === 'last12' ? null : v, from: null, to: null })}
                data={[...PRESETS.map((x) => ({ value: x.key, label: t(`rep.presets.${x.key}`) })), { value: 'custom', label: t('rep.presets.custom') }]} allowDeselect={false} />
              <DatePickerInput type="range" label={t('rep.dates')} w={260} valueFormat="DD MMM YYYY" value={[from || null, to || null]}
                onChange={([a, b]) => { if (a && b) set({ preset: null, from: dayjs(a).format('YYYY-MM-DD'), to: dayjs(b).format('YYYY-MM-DD') }); }} />
            </>}
            {has('siteId') && <Select label={terms.site} placeholder={t('common.all')} w={200} clearable data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} value={p('siteId') || null} onChange={(v) => set({ siteId: v })} />}
            {groupParam && <Select label={t('rep.groupBy')} w={180} allowDeselect={false} data={groupParam.options ?? []} value={q.groupBy ?? null} onChange={(v) => { setSort(NONE); set({ groupBy: v }); }} />}
            {daysParam && <NumberInput label={t('rep.idleDays')} w={140} min={1} max={3650} value={Number(q.days)} onChange={(v) => set({ days: v ? String(v) : null })} />}
          </Group>
        </Card>
      )}
      {error && <Alert color="red" mb="md">{(error as Error).message}</Alert>}
      {!data && isFetching && <Center h={240}><Loader /></Center>}
      {data && (
        <Stack style={{ opacity: isFetching ? 0.6 : 1, transition: 'opacity 120ms' }}>
          <div className="print-only"><Title order={3}>{me?.tenant.name} — {data.title}</Title><Text c="dimmed" fz="sm">{data.subtitle}</Text></div>
          {data.summary.length > 0 && (
            <SimpleGrid cols={{ base: 2, md: data.summary.length }}>
              {data.summary.map((s) => <Card key={s.label} p="md"><Text fz="xs" c="dimmed" tt="uppercase" fw={600}>{s.label}</Text><Text fz={26} fw={700} lh={1.3}>{fmt(s.value, s.type)}</Text></Card>)}
            </SimpleGrid>
          )}
          {data.chart && chartRows.length > 0 && (
            <Card>
              {data.chart.type === 'donut'
                ? <Group justify="center" gap="xl" wrap="wrap">
                    <DonutChart size={220} thickness={34} withTooltip valueFormatter={chartFmt}
                      data={chartRows.map((r, i) => ({ name: String(r[data.chart!.x]), value: Number(r[data.chart!.series[0]] ?? 0), color: PALETTE[i % PALETTE.length] }))} />
                    <Stack gap={4}>{chartRows.map((r, i) => <Group key={i} gap={8} wrap="nowrap"><span style={{ width: 10, height: 10, borderRadius: 5, background: `var(--mantine-color-${PALETTE[i % PALETTE.length].replace('.', '-')})` }} /><Text fz="sm">{String(r[data.chart!.x])}</Text><Text fz="sm" c="dimmed">{fmt(r[data.chart!.series[0]], 'currency')}</Text></Group>)}</Stack>
                  </Group>
                : <BarChart h={Math.min(420, Math.max(220, chartRows.length * (data.chart.x === 'month' || data.chart.x === 'group' && q.groupBy === 'month' ? 0 : 28) + 60))} data={chartRows}
                    dataKey={data.chart.x} type={data.chart.stacked ? 'stacked' : 'default'} withLegend={data.chart.series.length > 1} valueFormatter={chartFmt}
                    orientation={chartRows.length > 6 && !(data.chart.x === 'month' || (data.chart.x === 'group' && q.groupBy === 'month')) ? 'vertical' : 'horizontal'}
                    yAxisProps={chartRows.length > 6 && !(data.chart.x === 'month' || (data.chart.x === 'group' && q.groupBy === 'month')) ? { width: axisW, interval: 0, tick: AxisTick } : undefined}
                    series={data.chart.series.map((s, i) => ({ name: s, label: label(s), color: PALETTE[i % PALETTE.length] }))} />}
            </Card>
          )}
          <Card p={0}>
            <DataTable<Record<string, unknown>>
              minHeight={rows.length ? undefined : 160} withTableBorder={false} striped highlightOnHover verticalSpacing="xs" records={rows} idAccessor={(r) => JSON.stringify(r)}
              noRecordsText={t('rep.noData')} sortStatus={sort} onSortStatusChange={setSort}
              columns={data.columns.map((c, i) => ({
                accessor: c.key, title: c.label, sortable: true, textAlign: c.type === 'text' || c.type === 'date' ? 'left' as const : 'right' as const, noWrap: c.type !== 'text',
                render: (r: Record<string, unknown>) => {
                  const v = fmt(r[c.key], c.type);
                  const to = i === 0 ? link(r) : null;
                  if (to) return <Anchor component={Link} to={to} fz="sm">{v}</Anchor>;
                  if (c.key === 'status' && typeof r[c.key] === 'string') return <Badge variant="light" color={/out of stock|below/i.test(String(r[c.key])) ? 'red' : /re-order/i.test(String(r[c.key])) ? 'orange' : 'gray'}>{v}</Badge>;
                  return <Text fz="sm" c={c.key === 'daysLate' && r[c.key] ? 'red' : undefined} fw={c.key === 'total' ? 600 : undefined}>{v}</Text>;
                },
              }))}
            />
          </Card>
          <Text fz="xs" c="dimmed" ta="right">{t('rep.generated', { date: dayjs().format('DD MMM YYYY HH:mm') })} · {t('common.recordsCount', { count: data.rows.length })}</Text>
        </Stack>
      )}
    </>
  );
}
