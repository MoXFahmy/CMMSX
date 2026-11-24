import { useState } from 'react';
import { Anchor, Badge, Button, Card, Code, Group, Select, Stack, Text, TextInput, Title } from '@mantine/core';
import { DatePickerInput } from '@mantine/dates';
import { useDebouncedValue } from '@mantine/hooks';
import { useQuery } from '@tanstack/react-query';
import { IconDownload, IconSearch, IconX } from '@tabler/icons-react';
import dayjs from 'dayjs';
import { DataTable } from 'mantine-datatable';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { get } from '@/api/client';
import type { ActivityRow, User } from '@/api/types';
import { exportUrl } from '@/components/list/ListTools';
import type { ListColumn } from '@/components/list/types';
import { fmtDateTime } from '@/components/utils';
import { ACTIONS, RECORD_TYPES, SOURCES, changeSummary, humanizeField, parseChanges, recordPath, showValue } from './activity';

const PAGE_SIZE = 50;

export const sourceColor = (s?: string) => (s === 'Phone' ? 'grape' : s === 'Import' ? 'orange' : s === 'Job' ? 'gray' : 'blue');

/** Settings › Activity log (0.25.0): every change and security event, filtered, with what one action changed and an export. */
export function ActivityLog() {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [range, setRange] = useState<[string | null, string | null]>([null, null]);
  const [userId, setUserId] = useState<string | null>(null);
  const [source, setSource] = useState<string | null>(null);
  const [entityType, setEntityType] = useState<string | null>(null);
  const [action, setAction] = useState<string | null>(null);
  const [changeId, setChangeId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [debounced] = useDebouncedValue(search, 300);
  const [expanded, setExpanded] = useState<string[]>([]);
  const reset = (f: () => void) => { f(); setPage(1); };
  const query = {
    from: range[0], to: range[1], userId, source, entityType, action, changeId, search: debounced.trim() || undefined,
  };
  const { data: users } = useQuery({ queryKey: ['users'], queryFn: () => get<User[]>('/users') });
  const { data, isFetching } = useQuery({
    queryKey: ['activity', query, page],
    queryFn: () => get<{ items: ActivityRow[]; total: number }>('/activity', { ...query, page, pageSize: PAGE_SIZE }),
    placeholderData: (prev) => prev,
  });

  const cols: ListColumn<ActivityRow>[] = [
    { accessor: 'timestamp', title: t('activity.when'), width: 160, render: (r) => <Text fz="sm">{fmtDateTime(r.timestamp)}</Text> },
    { accessor: 'userName', title: t('activity.who'), render: (r) => <Text fz="sm">{r.userName ?? t('activity.system')}</Text> },
    { accessor: 'source', title: t('activity.source'), render: (r) => <Badge variant="light" color={sourceColor(r.source)}>{t(`activity.sources.${r.source}`)}</Badge> },
    {
      accessor: 'recordLabel', title: t('activity.record'), render: (r) => {
        const path = recordPath(r.parentType ?? r.entityType, r.parentId ?? r.entityId);
        return path ? <Anchor component={Link} to={path} fz="sm" onClick={(e) => e.stopPropagation()}>{r.recordLabel}</Anchor> : <Text fz="sm">{r.recordLabel}</Text>;
      },
    },
    { accessor: 'action', title: t('activity.action'), render: (r) => <Text fz="sm">{t(`activity.actions.${r.action}`, { defaultValue: r.action })}</Text> },
    { accessor: 'changes', title: t('activity.changes'), render: (r) => <Text fz="sm" c="dimmed" lineClamp={1}>{changeSummary(parseChanges(r.changes), t)}</Text> },
  ];

  const href = (format: 'xlsx' | 'csv') => exportUrl('/activity/export', query, cols, format);

  return (
    <Card><Stack>
      <Group justify="space-between" align="flex-end">
        <div><Title order={5}>{t('activity.title')}</Title><Text c="dimmed" fz="sm">{t('activity.intro')}</Text></div>
        <Group gap="xs">
          <Button size="xs" variant="default" leftSection={<IconDownload size={14} />} component="a" href={href('xlsx')} download>{t('list.exportXlsx')}</Button>
          <Button size="xs" variant="default" component="a" href={href('csv')} download>{t('list.exportCsv')}</Button>
        </Group>
      </Group>
      <Group gap="xs" align="flex-end" wrap="wrap">
        <TextInput w={220} label={t('common.search')} leftSection={<IconSearch size={14} />} value={search} onChange={(e) => reset(() => setSearch(e.currentTarget.value))} />
        <DatePickerInput type="range" w={240} clearable allowSingleDateInRange label={t('activity.dates')} valueFormat="DD MMM YYYY" value={range}
          onChange={(v) => reset(() => setRange([v[0] ? dayjs(v[0]).format('YYYY-MM-DD') : null, v[1] ? dayjs(v[1]).format('YYYY-MM-DD') : null]))} />
        <Select w={190} clearable searchable label={t('activity.who')} value={userId} onChange={(v) => reset(() => setUserId(v))}
          data={(users ?? []).map((u) => ({ value: u.id, label: u.fullName }))} />
        <Select w={130} clearable label={t('activity.source')} value={source} onChange={(v) => reset(() => setSource(v))}
          data={SOURCES.map((x) => ({ value: x, label: t(`activity.sources.${x}`) }))} />
        <Select w={180} clearable searchable label={t('activity.recordType')} value={entityType} onChange={(v) => reset(() => setEntityType(v))}
          data={RECORD_TYPES.map((x) => ({ value: x, label: t(`activity.types.${x}`, { defaultValue: humanizeField(x) }) }))} />
        <Select w={190} clearable searchable label={t('activity.action')} value={action} onChange={(v) => reset(() => setAction(v))}
          data={ACTIONS.map((x) => ({ value: x, label: t(`activity.actions.${x}`) }))} />
      </Group>
      {changeId && (
        <Group gap="xs">
          <Badge size="lg" variant="light" rightSection={<IconX size={12} style={{ cursor: 'pointer' }} onClick={() => reset(() => setChangeId(null))} />}>{t('activity.sameChangeFilter')}</Badge>
        </Group>
      )}
      <DataTable minHeight={180} fetching={isFetching} records={data?.items ?? []} columns={cols} noRecordsText={t('common.noRecords')} idAccessor="id"
        totalRecords={data?.total ?? 0} recordsPerPage={PAGE_SIZE} page={page} onPageChange={setPage}
        rowExpansion={{
          trigger: 'click', expanded: { recordIds: expanded, onRecordIdsChange: setExpanded },
          content: ({ record: r }) => {
            const changes = parseChanges(r.changes) ?? {};
            return (
              <Stack gap={4} p="sm">
                {Object.entries(changes).map(([field, c]) => (
                  <Text key={field} fz="sm">{humanizeField(field)}: <Code>{showValue(c?.from)}</Code> → <Code>{showValue(c?.to)}</Code></Text>
                ))}
                {Object.keys(changes).length === 0 && <Text fz="sm" c="dimmed">{t('activity.noDetails')}</Text>}
                {!changeId && <Group><Button size="xs" variant="subtle" onClick={() => reset(() => setChangeId(r.changeId))}>{t('activity.sameChange')}</Button></Group>}
              </Stack>
            );
          },
        }} />
    </Stack></Card>
  );
}
