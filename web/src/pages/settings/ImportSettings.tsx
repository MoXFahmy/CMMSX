import { useEffect, useState } from 'react';
import { Accordion, Alert, Badge, Button, Card, FileInput, Group, List, Progress, Stack, Table, Text, Title } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import { DataTable } from 'mantine-datatable';
import { IconAlertTriangle, IconCheck, IconDownload, IconFileSpreadsheet, IconUpload } from '@tabler/icons-react';
import { api, apiBlob, get } from '@/api/client';
import type { ImportJob, ImportResult, RowResult, SheetResult } from '@/api/types';
import { fmtDateTime, fromNow, notifyError } from '@/components/utils';
import { canImport, fileKey, jobLabel, totals } from './importView';

/** At most this many rows of a sheet are listed in the preview; the rest are counted. */
const SHOWN = 200;

const OUTCOME_COLOR: Record<RowResult['outcome'], string> = { New: 'teal', Changed: 'blue', Unchanged: 'gray', Problem: 'red' };
const JOB_COLOR: Record<ImportJob['status'], string> = { Queued: 'gray', Running: 'blue', Done: 'teal', Failed: 'red' };

function saveBlob(blob: Blob, name: string) {
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob); a.download = name; a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}

/** "2 min" — how long a finished job took. */
const took = (j: ImportJob) => (j.startedAt && j.finishedAt ? `${Math.max(1, Math.round((Date.parse(j.finishedAt) - Date.parse(j.startedAt)) / 1000))} s` : '—');

/**
 * Settings › Import: download the template or the current data, choose a filled workbook, check it (nothing is saved),
 * then import it. Checks and imports run in the background (0.26.0): the page follows the job, which goes on if the page is
 * left, and lists past ones. Rows with problems can be downloaded with the reason beside each.
 */
export function ImportSettings() {
  const { t, i18n } = useTranslation();
  const qc = useQueryClient();
  const [params, setParams] = useSearchParams();
  const [file, setFile] = useState<File | null>(null);
  const [checkedKey, setCheckedKey] = useState<string | null>(null);
  const [jobId, setJobId] = useState<string | null>(params.get('job'));
  const [busy, setBusy] = useState<'check' | 'run' | 'problems' | null>(null);

  const { data: job } = useQuery({
    queryKey: ['import-job', jobId],
    queryFn: () => get<ImportJob>(`/import/jobs/${jobId}`),
    enabled: !!jobId,
    refetchInterval: (q) => (q.state.data && (q.state.data.status === 'Queued' || q.state.data.status === 'Running') ? 1500 : false),
  });
  const { data: history, isFetching: historyLoading } = useQuery({ queryKey: ['import-jobs'], queryFn: () => get<ImportJob[]>('/import/jobs') });
  const finished = job?.status === 'Done' || job?.status === 'Failed';
  const result: ImportResult | null = job?.status === 'Done' ? job.result ?? null : null;
  // When a job finishes: the history shows it, and a saved import refreshes every screen.
  useEffect(() => {
    if (!finished) return;
    void qc.invalidateQueries({ queryKey: ['import-jobs'] });
    if (job?.save && job.status === 'Done') void qc.invalidateQueries({ predicate: (q) => q.queryKey[0] !== 'import-job' });
  }, [finished, job?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const form = () => { const fd = new FormData(); fd.append('file', file!); return fd; };
  const send = async (step: 'check' | 'run') => {
    if (!file) return;
    setBusy(step);
    try {
      const r = await api<{ id: string }>(`/import/${step}`, { method: 'POST', body: form() });
      setJobId(r.id);
      setCheckedKey(fileKey(file));
      if (params.has('job')) { params.delete('job'); setParams(params, { replace: true }); }
      await qc.invalidateQueries({ queryKey: ['import-jobs'] });
    } catch (e) { notifyError(e); } finally { setBusy(null); }
  };
  const problems = async () => {
    if (!jobId) return;
    setBusy('problems');
    try { saveBlob(await apiBlob(`/import/jobs/${jobId}/problems`), 'CMX-import-problems.xlsx'); }
    catch (e) { notifyError(e); } finally { setBusy(null); }
  };
  const running = job && !finished;
  const waiting = running ? jobLabel(job, t) : '';

  const sum = result ? totals(result) : null;
  const lang = `?ui-culture=${encodeURIComponent(i18n.language || 'en')}`;

  return (
    <Stack>
      <Card withBorder>
        <Title order={4} mb={4}>{t('import.title')}</Title>
        <Text c="dimmed" fz="sm" mb="md">{t('import.intro')}</Text>
        <Text fw={600} mb="xs">{t('import.step1')}</Text>
        <Group>
          <Button component="a" href={`/api/import/template${lang}`} variant="default" leftSection={<IconFileSpreadsheet size={16} />}>{t('import.template')}</Button>
          <Button component="a" href={`/api/import/current${lang}`} variant="default" leftSection={<IconDownload size={16} />}>{t('import.current')}</Button>
        </Group>
        <Text fw={600} mt="lg" mb="xs">{t('import.step2')}</Text>
        <Group align="end">
          <FileInput w={320} accept=".xlsx,.csv" placeholder={t('import.choose')} value={file} onChange={setFile} clearable leftSection={<IconUpload size={16} />} />
          <Button onClick={() => send('check')} loading={busy === 'check'} disabled={!file || busy !== null || !!running}>{t('import.check')}</Button>
        </Group>
      </Card>

      {running && (
        <Card withBorder>
          <Stack gap="xs">
            <Group justify="space-between"><Text fw={600}>{job.fileName}</Text><Text fz="sm" c="dimmed">{waiting}</Text></Group>
            <Progress value={job.status === 'Running' && job.rowsTotal > 0 ? Math.min(100, (job.rowsDone / job.rowsTotal) * 100) : 0}
              animated striped={job.status === 'Queued'} />
            <Text fz="xs" c="dimmed">{t('import.goesOn')}</Text>
          </Stack>
        </Card>
      )}

      {job?.status === 'Failed' && <Alert color="orange" icon={<IconAlertTriangle size={18} />}>{job.error}</Alert>}
      {result?.stopped && <Alert color="orange" icon={<IconAlertTriangle size={18} />}>{result.stopped}</Alert>}

      {result && !result.stopped && sum && (
        <Card withBorder>
          {result.saved
            ? <Alert color="teal" icon={<IconCheck size={18} />} mb="md">{t('import.done', { saved: sum.new + sum.changed, skipped: sum.problems })}</Alert>
            : <Title order={5} mb="sm">{t('import.step3')}</Title>}
          <Table striped withTableBorder mb="md">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('import.sheet')}</Table.Th><Table.Th ta="end">{t('import.new')}</Table.Th><Table.Th ta="end">{t('import.changed')}</Table.Th>
                <Table.Th ta="end">{t('import.unchanged')}</Table.Th><Table.Th ta="end">{t('import.problems')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {result.sheets.map((s) => (
                <Table.Tr key={s.sheet}>
                  <Table.Td>{t(`import.sheets.${s.sheet}`)}</Table.Td><Table.Td ta="end">{s.new}</Table.Td><Table.Td ta="end">{s.changed}</Table.Td>
                  <Table.Td ta="end">{s.unchanged}</Table.Td><Table.Td ta="end" c={s.problems ? 'red' : undefined}>{s.problems}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
          {result.sheets.length === 0 && <Text c="dimmed" mb="md">{t('import.nothing')}</Text>}

          <Accordion variant="separated" multiple mb="md">
            {result.sheets.filter((s) => s.rows.length > 0 || s.ignoredColumns.length > 0).map((s) => <SheetRows key={s.sheet} sheet={s} />)}
          </Accordion>

          {result.newLookups.length > 0 && (
            <Alert color="blue" mb="sm" title={t('import.newLookups')}><Text fz="sm">{result.newLookups.join(', ')}</Text></Alert>
          )}
          {result.notes.map((n) => <Alert key={n} color="gray" mb="sm">{n}</Alert>)}

          <Group>
            {!result.saved && (
              <Button onClick={() => send('run')} loading={busy === 'run'} disabled={!canImport(result, checkedKey, file) || busy !== null || !!running}>
                {t('import.run')}
              </Button>
            )}
            {!result.saved && <Text fz="sm" c="dimmed">{t('import.willSave', { count: sum.new + sum.changed })}</Text>}
            {sum.problems > 0 && (
              <Button variant="default" onClick={problems} loading={busy === 'problems'} disabled={busy !== null} leftSection={<IconDownload size={16} />}>
                {t('import.problemRows')}
              </Button>
            )}
          </Group>
        </Card>
      )}
      <Card withBorder p={0}>
        <Title order={5} p="md">{t('import.history')}</Title>
        <DataTable minHeight={120} fetching={historyLoading} records={history ?? []} noRecordsText={t('import.noHistory')}
          onRowClick={({ record }) => setJobId(record.id)} rowStyle={() => ({ cursor: 'pointer' })}
          columns={[
            { accessor: 'fileName', title: t('import.file'), render: (j) => <Stack gap={0}><Text fz="sm">{j.fileName}</Text><Text fz="xs" c="dimmed">{j.save ? t('import.anImport') : t('import.aCheck')}</Text></Stack> },
            { accessor: 'userName', title: t('import.by'), render: (j) => <Stack gap={0}><Text fz="sm">{j.userName ?? '—'}</Text><Text fz="xs" c="dimmed" title={fmtDateTime(j.createdAt)}>{fromNow(j.createdAt)}</Text></Stack> },
            { accessor: 'finishedAt', title: t('import.took'), render: (j) => took(j) },
            { accessor: 'status', title: t('common.status'), render: (j) => <Badge variant="light" color={JOB_COLOR[j.status]}>{t(`import.jobStatus.${j.status}`)}</Badge> },
          ]} />
      </Card>
    </Stack>
  );
}

function SheetRows({ sheet }: { sheet: SheetResult }) {
  const { t, i18n } = useTranslation();
  const arrow = i18n.dir() === 'rtl' ? '←' : '→';
  const shown = sheet.rows.slice(0, SHOWN);
  return (
    <Accordion.Item value={sheet.sheet}>
      <Accordion.Control>{t(`import.sheets.${sheet.sheet}`)}</Accordion.Control>
      <Accordion.Panel>
        {sheet.ignoredColumns.length > 0 && <Text fz="sm" c="dimmed" mb="xs">{t('import.ignored', { list: sheet.ignoredColumns.join(', ') })}</Text>}
        <Stack gap={6}>
          {shown.map((r) => (
            <div key={r.row}>
              <Group gap="xs">
                <Badge size="sm" variant="light" color={OUTCOME_COLOR[r.outcome]}>{t(`import.outcome.${r.outcome}`)}</Badge>
                <Text fz="sm" fw={500}>{t('import.row', { row: r.row })}{r.key ? ` · ${r.key}` : ''}</Text>
              </Group>
              {r.problems.length > 0 && <List size="sm" c="red" ms="md">{r.problems.map((p) => <List.Item key={p}>{p}</List.Item>)}</List>}
              {r.changes.length > 0 && (
                <List size="sm" ms="md">
                  {r.changes.map((ch) => <List.Item key={ch.column}>{ch.column}: {ch.old ?? '—'} {arrow} {ch.new ?? '—'}</List.Item>)}
                </List>
              )}
            </div>
          ))}
          {sheet.rows.length > SHOWN && <Text fz="sm" c="dimmed">{t('import.more', { count: sheet.rows.length - SHOWN })}</Text>}
        </Stack>
      </Accordion.Panel>
    </Accordion.Item>
  );
}
