import { useState } from 'react';
import { Anchor, Card, Group, SimpleGrid, Stack, Table, Text, TextInput, Title } from '@mantine/core';
import { useDebouncedValue } from '@mantine/hooks';
import { useQuery } from '@tanstack/react-query';
import { IconSearch } from '@tabler/icons-react';
import { DataTable } from 'mantine-datatable';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { get } from '@/api/client';
import type { StorageFile, StorageOverview } from '@/api/types';
import { fmtDateTime } from '@/components/utils';
import { formatBytes, humanizeField, recordPath } from './activity';

const PAGE_SIZE = 100;

function Stat({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <Card withBorder><Stack gap={2}>
      <Text c="dimmed" fz="xs" tt="uppercase" fw={600} style={{ letterSpacing: '0.04em' }}>{label}</Text>
      <Text fz="xl" fw={700}>{value}</Text>
      {hint && <Text c="dimmed" fz="xs">{hint}</Text>}
    </Stack></Card>
  );
}

/** Settings › Storage (0.25.0): the space files take, by kind and by record type, and the largest files. CMX has no storage limit. */
export function StorageSettings() {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const [debounced] = useDebouncedValue(search, 300);
  const [page, setPage] = useState(1);
  const { data } = useQuery({ queryKey: ['storage'], queryFn: () => get<StorageOverview>('/storage') });
  const { data: files, isFetching } = useQuery({
    queryKey: ['storage-files', debounced, page],
    queryFn: () => get<{ items: StorageFile[]; total: number }>('/storage/files', { search: debounced.trim() || undefined, page }),
    placeholderData: (prev) => prev,
  });
  if (!data) return null;
  const typeLabel = (type: string) => t(`activity.types.${type}`, { defaultValue: humanizeField(type) });
  return (
    <Stack>
      <SimpleGrid cols={{ base: 1, sm: 3 }}>
        <Stat label={t('storage.total')} value={formatBytes(data.totalBytes)} hint={t('storage.files', { count: data.files })} />
        <Stat label={t('storage.deleted')} value={formatBytes(data.deletedBytes)} hint={t('storage.deletedHint')} />
        <Stat label={t('storage.limit')} value={t('storage.noLimit')} />
      </SimpleGrid>
      <SimpleGrid cols={{ base: 1, md: 2 }}>
        <Card><Stack gap="xs">
          <Title order={5}>{t('storage.byKind')}</Title>
          <Table><Table.Tbody>
            {data.byKind.map((k) => (
              <Table.Tr key={k.kind}>
                <Table.Td>{t(`storage.kinds.${k.kind}`)}</Table.Td>
                <Table.Td ta="end" c="dimmed">{t('storage.files', { count: k.files })}</Table.Td>
                <Table.Td ta="end" fw={500}>{formatBytes(k.bytes)}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody></Table>
        </Stack></Card>
        <Card><Stack gap="xs">
          <Title order={5}>{t('storage.byRecord')}</Title>
          <Table><Table.Tbody>
            {data.byRecord.map((r) => (
              <Table.Tr key={r.entityType}>
                <Table.Td>{typeLabel(r.entityType)}</Table.Td>
                <Table.Td ta="end" c="dimmed">{t('storage.files', { count: r.files })}</Table.Td>
                <Table.Td ta="end" fw={500}>{formatBytes(r.bytes)}</Table.Td>
              </Table.Tr>
            ))}
            {data.byRecord.length === 0 && <Table.Tr><Table.Td c="dimmed">{t('common.noRecords')}</Table.Td></Table.Tr>}
          </Table.Tbody></Table>
        </Stack></Card>
      </SimpleGrid>
      <Card><Stack>
        <Group justify="space-between" align="flex-end">
          <Title order={5}>{t('storage.largest')}</Title>
          <TextInput w={280} placeholder={t('storage.search')} leftSection={<IconSearch size={14} />} value={search}
            onChange={(e) => { setSearch(e.currentTarget.value); setPage(1); }} />
        </Group>
        <DataTable minHeight={150} fetching={isFetching} records={files?.items ?? []} noRecordsText={t('common.noRecords')}
          totalRecords={files?.total ?? 0} recordsPerPage={PAGE_SIZE} page={page} onPageChange={setPage}
          columns={[
            { accessor: 'fileName', title: t('storage.file'), render: (f) => <Stack gap={0}><Text fz="sm">{f.fileName}</Text><Text fz="xs" c="dimmed">{t(`storage.kinds.${f.kind}`)}</Text></Stack> },
            {
              accessor: 'recordLabel', title: t('activity.record'), render: (f) => {
                const path = recordPath(f.entityType, f.entityId);
                const label = f.recordLabel ?? typeLabel(f.entityType);
                return path ? <Anchor component={Link} to={path} fz="sm">{label}</Anchor> : <Text fz="sm">{label}</Text>;
              },
            },
            { accessor: 'uploadedBy', title: t('storage.uploadedBy'), render: (f) => <Stack gap={0}><Text fz="sm">{f.uploadedBy ?? '—'}</Text><Text fz="xs" c="dimmed">{fmtDateTime(f.uploadedAt)}</Text></Stack> },
            { accessor: 'size', title: t('storage.size'), textAlign: 'right', render: (f) => formatBytes(f.size) },
          ]} />
      </Stack></Card>
    </Stack>
  );
}
