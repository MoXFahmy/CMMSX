import { Anchor, Badge, Button, Card, Group, Progress, Select, SimpleGrid, Stack, Table, Text, Title } from '@mantine/core';
import { BarChart } from '@mantine/charts';
import { useChartAxes } from '@/components/chartDirection';
import { DatePickerInput } from '@mantine/dates';
import { IconDownload } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { DataTable } from 'mantine-datatable';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { get } from '@/api/client';
import type { Paged } from '@/api/types';
import { exportUrl, type ListColumn } from '@/components/list';
import { fmtDate, fmtDateTime } from '@/components/utils';
import { fmtUsd, usageColor, type AiCallRow, type AiUsageSummary } from './aiUsage';

const RESULT_COLOR: Record<AiCallRow['result'], string> = { Pending: 'gray', Answered: 'teal', Refused: 'orange', Failed: 'red', OverLimit: 'grape' };

/** Settings › AI usage (0.15.0): this month against the limit, the last twelve months, by feature and person, and every call. */
export function AiUsageSettings() {
  const { t } = useTranslation();
  const axes = useChartAxes();
  const { data: u } = useQuery({ queryKey: ['ai-usage'], queryFn: () => get<AiUsageSummary>('/ai/usage') });
  const [feature, setFeature] = useState<string | null>(null);
  const [result, setResult] = useState<string | null>(null);
  const [userId, setUserId] = useState<string | null>(null);
  const [range, setRange] = useState<[string | null, string | null]>([null, null]);
  const [page, setPage] = useState(1);
  const pageSize = 25;
  const query = { feature, result, userId, from: range[0], to: range[1] };
  const { data: calls, isFetching } = useQuery({
    queryKey: ['ai-calls', query, page],
    queryFn: () => get<Paged<AiCallRow>>('/ai/calls', { ...query, page, pageSize }),
  });
  if (!u) return null;
  const pct = u.limit > 0 ? Math.min(100, (u.used / u.limit) * 100) : 100;
  const columns: ListColumn<AiCallRow>[] = [
    { accessor: 'createdAt', title: t('ai.when'), render: (x) => fmtDateTime(x.createdAt) },
    { accessor: 'userName', title: t('ai.who') },
    { accessor: 'feature', title: t('ai.feature'), render: (x) => t(`ai.features.${x.feature}`) },
    { accessor: 'workOrderNumber', title: t('ai.record'), export: [{ key: 'workOrderNumber' }, { key: 'requestNumber', title: t('ai.request') }],
      render: (x) => x.workOrderId && x.workOrderNumber ? <Anchor component={Link} to={`/work-orders/${x.workOrderId}`} fz="sm">{x.workOrderNumber}</Anchor> : x.requestNumber ?? '—' },
    { accessor: 'model', title: t('ai.model') },
    { accessor: 'inputTokens', title: t('ai.inputTokens'), textAlign: 'right' },
    { accessor: 'outputTokens', title: t('ai.outputTokens'), textAlign: 'right' },
    { accessor: 'cacheReadTokens', title: t('ai.cacheTokens'), textAlign: 'right', export: [{ key: 'cacheReadTokens' }, { key: 'cacheWriteTokens', title: t('ai.cacheWriteTokens') }] },
    { accessor: 'estimatedCost', title: t('ai.cost'), textAlign: 'right', render: (x) => fmtUsd(x.estimatedCost) },
    { accessor: 'result', title: t('ai.result'), render: (x) => <Badge variant="light" color={RESULT_COLOR[x.result]}>{t(`ai.results.${x.result}`)}</Badge> },
  ];
  const href = (format: 'xlsx' | 'csv') => exportUrl('/ai/calls/export', query, columns, format);
  const reset = (f: () => void) => { f(); setPage(1); };
  return (
    <Stack>
      <SimpleGrid cols={{ base: 1, sm: 2 }}>
        <Card><Stack gap="xs">
          <Title order={5}>{t('ai.thisMonth')}</Title>
          <Text fz="xl" fw={700}>{t('ai.usedOf', { used: u.used, limit: u.limit })}</Text>
          <Progress value={pct} color={usageColor(u.used, u.limit)} />
          <Text c="dimmed" fz="sm">{t('ai.renewsOn', { date: fmtDate(u.renewsOn) })} · {t('ai.ceilingNote', { max: u.ceiling })}</Text>
        </Stack></Card>
        <Card><Stack gap="xs">
          <Title order={5}>{t('ai.byFeature')}</Title>
          <Table><Table.Tbody>{u.byFeature.map((f) => (
            <Table.Tr key={f.feature}><Table.Td>{t(`ai.features.${f.feature}`)}</Table.Td><Table.Td ta="end">{f.actions}</Table.Td><Table.Td ta="end">{fmtUsd(f.cost)}</Table.Td></Table.Tr>
          ))}</Table.Tbody></Table>
          {u.byFeature.length === 0 && <Text c="dimmed" fz="sm">{t('common.noRecords')}</Text>}
        </Stack></Card>
      </SimpleGrid>
      <Card><Stack gap="xs">
        <Title order={5}>{t('ai.lastYear')}</Title>
        <BarChart h={220} dataKey="label" gridAxis="y" {...axes}
          data={u.months.map((m) => ({ label: dayjs(`${m.month}-01`).format('MMM YY'), actions: m.actions, cost: Number(m.cost.toFixed(4)) }))}
          series={[{ name: 'actions', label: t('ai.actions'), color: 'brand.6' }]}
          tooltipProps={{ content: ({ payload }) => payload?.[0] ? (
            <Card withBorder p="xs"><Text fz="sm">{t('ai.actions')}: {payload[0].payload.actions}</Text><Text fz="sm">{t('ai.cost')}: {fmtUsd(payload[0].payload.cost)}</Text></Card>
          ) : null }} />
      </Stack></Card>
      <Card><Stack gap="xs">
        <Title order={5}>{t('ai.byPerson')}</Title>
        <Table><Table.Tbody>{u.byPerson.map((p) => (
          <Table.Tr key={`${p.userId}-${p.name}`}><Table.Td>{p.name}</Table.Td><Table.Td ta="end">{p.actions}</Table.Td><Table.Td ta="end">{fmtUsd(p.cost)}</Table.Td></Table.Tr>
        ))}</Table.Tbody></Table>
        {u.byPerson.length === 0 && <Text c="dimmed" fz="sm">{t('common.noRecords')}</Text>}
      </Stack></Card>
      <Card><Stack gap="xs">
        <Group justify="space-between" align="flex-end">
          <Title order={5}>{t('ai.log')}</Title>
          <Group gap="xs">
            <Button size="xs" variant="default" leftSection={<IconDownload size={14} />} component="a" href={href('xlsx')} download>{t('list.exportXlsx')}</Button>
            <Button size="xs" variant="default" component="a" href={href('csv')} download>{t('list.exportCsv')}</Button>
          </Group>
        </Group>
        <Group gap="xs" align="flex-end" wrap="wrap">
          <Select w={160} clearable label={t('ai.feature')} value={feature} onChange={(v) => reset(() => setFeature(v))}
            data={(['Report', 'Diagnosis', 'Translation'] as const).map((x) => ({ value: x, label: t(`ai.features.${x}`) }))} />
          <Select w={170} clearable label={t('ai.result')} value={result} onChange={(v) => reset(() => setResult(v))}
            data={(['Answered', 'Refused', 'Failed', 'OverLimit'] as const).map((x) => ({ value: x, label: t(`ai.results.${x}`) }))} />
          <Select w={200} clearable searchable label={t('ai.who')} value={userId} onChange={(v) => reset(() => setUserId(v))}
            data={u.byPerson.filter((p) => p.userId).map((p) => ({ value: p.userId!, label: p.name }))} />
          <DatePickerInput type="range" w={260} clearable label={t('ai.dates')} valueFormat="DD MMM YYYY" value={range}
            onChange={(v) => reset(() => setRange([v[0] ? dayjs(v[0]).format('YYYY-MM-DD') : null, v[1] ? dayjs(v[1]).format('YYYY-MM-DD') : null]))} />
        </Group>
        <DataTable minHeight={150} fetching={isFetching} records={calls?.items ?? []} columns={columns} noRecordsText={t('common.noRecords')}
          totalRecords={calls?.total ?? 0} recordsPerPage={pageSize} page={page} onPageChange={setPage} />
      </Stack></Card>
    </Stack>
  );
}
