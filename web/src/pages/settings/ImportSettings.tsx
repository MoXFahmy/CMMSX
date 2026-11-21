import { useState } from 'react';
import { Accordion, Alert, Badge, Button, Card, FileInput, Group, List, Stack, Table, Text, Title } from '@mantine/core';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { IconAlertTriangle, IconCheck, IconDownload, IconFileSpreadsheet, IconUpload } from '@tabler/icons-react';
import { api, apiBlob } from '@/api/client';
import type { ImportResult, RowResult, SheetResult } from '@/api/types';
import { notifyError } from '@/components/utils';
import { canImport, fileKey, totals } from './importView';

/** At most this many rows of a sheet are listed in the preview; the rest are counted. */
const SHOWN = 200;

const OUTCOME_COLOR: Record<RowResult['outcome'], string> = { New: 'teal', Changed: 'blue', Unchanged: 'gray', Problem: 'red' };

function saveBlob(blob: Blob, name: string) {
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob); a.download = name; a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}

/**
 * Settings › Import: download the template or the current data, choose a filled workbook, check it (nothing is saved),
 * then import it. Rows with problems can be downloaded with the reason beside each.
 */
export function ImportSettings() {
  const { t, i18n } = useTranslation();
  const qc = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<ImportResult | null>(null);
  const [checkedKey, setCheckedKey] = useState<string | null>(null);
  const [busy, setBusy] = useState<'check' | 'run' | 'problems' | null>(null);

  const form = () => { const fd = new FormData(); fd.append('file', file!); return fd; };
  const send = async (step: 'check' | 'run') => {
    if (!file) return;
    setBusy(step);
    try {
      const r = await api<ImportResult>(`/import/${step}`, { method: 'POST', body: form() });
      setResult(r);
      setCheckedKey(fileKey(file));
      if (r.saved) await qc.invalidateQueries();
    } catch (e) { notifyError(e); } finally { setBusy(null); }
  };
  const problems = async () => {
    if (!file) return;
    setBusy('problems');
    try { saveBlob(await apiBlob('/import/problems', { method: 'POST', body: form() }), 'CMX-import-problems.xlsx'); }
    catch (e) { notifyError(e); } finally { setBusy(null); }
  };

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
          <Button onClick={() => send('check')} loading={busy === 'check'} disabled={!file || busy !== null}>{t('import.check')}</Button>
        </Group>
      </Card>

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
              <Button onClick={() => send('run')} loading={busy === 'run'} disabled={!canImport(result, checkedKey, file) || busy !== null}>
                {t('import.run')}
              </Button>
            )}
            {!result.saved && <Text fz="sm" c="dimmed">{t('import.willSave', { count: sum.new + sum.changed })}</Text>}
            {sum.problems > 0 && file && (
              <Button variant="default" onClick={problems} loading={busy === 'problems'} disabled={busy !== null} leftSection={<IconDownload size={16} />}>
                {t('import.problemRows')}
              </Button>
            )}
          </Group>
        </Card>
      )}
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
