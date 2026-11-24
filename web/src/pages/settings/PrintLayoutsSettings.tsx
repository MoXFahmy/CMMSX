import { useState } from 'react';
import { ActionIcon, Badge, Button, Card, Group, Tabs, Text, Title } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { DataTable } from 'mantine-datatable';
import { IconCopy, IconEdit, IconTrash } from '@tabler/icons-react';
import { del, get } from '@/api/client';
import { confirmDelete } from '@/components/confirm';
import { notifyError } from '@/components/utils';
import { copyLayout } from '@/print/editor';
import { builtInLayout, type Paper, type PrintLayout } from '@/print/layouts';
import { PrintLayoutEditor } from './PrintLayoutEditor';

const PAPERS: Paper[] = ['job-card', 'purchase-order', 'request', 'labels'];

function PaperLayouts({ paper, onEdit }: { paper: Paper; onEdit: (l: PrintLayout) => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data, isFetching } = useQuery({ queryKey: ['print-layouts', paper], queryFn: () => get<PrintLayout[]>('/print-layouts', { paper }) });
  const builtIn = builtInLayout(paper);
  const rows = [builtIn, ...(data ?? [])];
  const kindText = (l: PrintLayout) => (l.kindKey ? (paper === 'job-card' ? t(`workType.${l.kindKey}`) : paper === 'labels' ? t(`print.labelKinds.${l.kindKey}`) : l.kindName ?? '') : '');
  return (
    <DataTable minHeight={150} fetching={isFetching} records={rows} idAccessor="id" columns={[
      { accessor: 'name', title: t('common.name'), render: (l) => (
        <Group gap={6}><Text fz="sm" fw={500}>{l.id === builtIn.id ? t('print.builtIn') : l.name}</Text>
          {l.isDefault && <Badge size="xs" variant="light">{t('print.default')}</Badge>}
          {l.id === builtIn.id && <Badge size="xs" variant="outline">{t('print.todaysPaper')}</Badge>}</Group>) },
      { accessor: 'siteName', title: t('print.site'), render: (l) => l.siteName ?? '' },
      { accessor: 'kindKey', title: t(`print.kindLabel.${paper}`), render: kindText },
      { accessor: 'paperSize', title: paper === 'labels' ? t('print.labelSize') : t('print.paperSize'), render: (l) => (paper === 'labels' ? t(`print.sizes.${l.labelSize ?? 'a4-3x8'}`) : l.paperSize ?? 'A4') },
      { accessor: 'actions', title: '', width: 150, render: (l) => (
        <Group gap={4} justify="flex-end" wrap="nowrap">
          <Button size="compact-xs" variant="subtle" leftSection={<IconCopy size={12} />} onClick={() => onEdit(copyLayout(l, l.id === builtIn.id ? t('print.newLayoutName') : `${l.name} (2)`))}>{t('print.copy')}</Button>
          {l.id !== builtIn.id && <ActionIcon variant="subtle" onClick={() => onEdit(l)} aria-label={t('common.edit')}><IconEdit size={16} /></ActionIcon>}
          {l.id !== builtIn.id && <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')}
            onClick={() => confirmDelete(l.name, async () => { try { await del(`/print-layouts/${l.id}`); await qc.invalidateQueries({ queryKey: ['print-layouts'] }); } catch (e) { notifyError(e); } })}>
            <IconTrash size={16} /></ActionIcon>}
        </Group>) },
    ]} />
  );
}

/** Settings › Print layouts (0.23.0): the organization's versions of the job card, purchase order, request receipt and labels. */
export function PrintLayoutsSettings() {
  const { t } = useTranslation();
  const [editing, setEditing] = useState<PrintLayout | null>(null);
  return (
    <Card p={0}>
      <Group justify="space-between" p="md"><Title order={5}>{t('print.title')}</Title></Group>
      <Text fz="sm" c="dimmed" px="md" pb="sm">{t('print.intro')}</Text>
      <Tabs defaultValue="job-card" keepMounted={false} px="md" pb="md">
        <Tabs.List mb="sm">{PAPERS.map((p) => <Tabs.Tab key={p} value={p}>{t(`print.papers.${p}`)}</Tabs.Tab>)}</Tabs.List>
        {PAPERS.map((p) => <Tabs.Panel key={p} value={p}><PaperLayouts paper={p} onEdit={setEditing} /></Tabs.Panel>)}
      </Tabs>
      {editing && <PrintLayoutEditor initial={editing} onClose={() => setEditing(null)} />}
    </Card>
  );
}
