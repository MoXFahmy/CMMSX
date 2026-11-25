import { useState } from 'react';
import { Button, Checkbox, Group, Modal, ScrollArea, Stack, Table, Text, TextInput } from '@mantine/core';
import { IconLanguage } from '@tabler/icons-react';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { post } from '@/api/client';
import { P, useAuth } from '@/auth/AuthContext';
import { aiOn, useAiStatus } from './aiStatus';
import { notifyError, notifySuccess } from './utils';
import { toRows, toSaveBody, type ReviewRow, type TranslationDraft, type TranslationList } from './translations';

/** The permission that edits each list's texts (the server checks the same). */
const EDIT: Record<TranslationList, string> = {
  assets: P.assetsEdit, locations: P.locationsManage, parts: P.inventoryManage, categories: P.settingsManage, symptoms: P.settingsManage,
  causes: P.settingsManage, remedies: P.settingsManage, 'cancel-reasons': P.settingsManage, 'request-types': P.settingsManage,
  templates: P.pmManage, 'pm-schedules': P.pmManage,
};

/**
 * "Fill in missing Arabic" (0.28.0): the AI drafts a batch of the list's missing Arabic texts; the person checks, edits, unticks and saves.
 * Shown to people who can edit the list, when the AI assistant is on. <c>refresh</c>: the query keys to reload after saving.
 */
export function TranslateButton({ list, refresh = [] }: { list: TranslationList; refresh?: unknown[][] }) {
  const { t } = useTranslation();
  const { can } = useAuth();
  const { data: ai } = useAiStatus();
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [draft, setDraft] = useState<TranslationDraft | null>(null);
  const [rows, setRows] = useState<ReviewRow[]>([]);
  if (!aiOn(ai) || !can(EDIT[list])) return null;

  const next = async () => {
    setBusy(true);
    try {
      const d = await post<TranslationDraft>('/translations/draft', { list });
      setDraft(d); setRows(toRows(d)); setOpen(true);
      void qc.invalidateQueries({ queryKey: ['ai-status'] });
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };

  const save = async () => {
    if (!draft?.field) return;
    setBusy(true);
    try {
      const r = await post<{ saved: number; skipped: number }>('/translations/save', toSaveBody(list, draft.field, rows));
      notifySuccess(t('bilingual.saved', { n: r.saved }));
      for (const key of refresh) void qc.invalidateQueries({ queryKey: key });
      if (draft.remaining > 0) await next(); else setOpen(false);
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };

  const change = (id: string, patch: Partial<ReviewRow>) => setRows((rs) => rs.map((r) => (r.id === id ? { ...r, ...patch } : r)));

  return (
    <>
      <Button variant="default" leftSection={<IconLanguage size={16} />} loading={busy && !open} onClick={() => void next()}>{t('bilingual.fill')}</Button>
      <Modal opened={open} onClose={() => setOpen(false)} title={t('bilingual.review')} size="xl">
        {draft && draft.items.length === 0 && draft.field === null ? <Text>{t('bilingual.none')}</Text> : (
          <Stack>
            {draft && draft.couldNot > 0 && <Text c="orange" fz="sm">{t('bilingual.couldNot', { n: draft.couldNot })}</Text>}
            <ScrollArea.Autosize mah="60vh">
              <Table>
                <Table.Thead><Table.Tr><Table.Th w={40} /><Table.Th>{t('bilingual.main')}</Table.Th><Table.Th>{t('bilingual.arabic')}</Table.Th></Table.Tr></Table.Thead>
                <Table.Tbody>
                  {rows.map((r) => (
                    <Table.Tr key={r.id}>
                      <Table.Td><Checkbox checked={r.keep} onChange={(e) => change(r.id, { keep: e.currentTarget.checked })} aria-label={t('bilingual.keep')} /></Table.Td>
                      <Table.Td><Text fz="sm">{r.text}</Text></Table.Td>
                      <Table.Td><TextInput dir="rtl" value={r.ar} onChange={(e) => change(r.id, { ar: e.currentTarget.value })} aria-label={t('bilingual.arabic')} /></Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </ScrollArea.Autosize>
            <Group justify="space-between">
              <Text fz="sm" c="dimmed">{draft && draft.remaining > 0 ? t('bilingual.remaining', { n: draft.remaining }) : ''}</Text>
              <Group>
                <Button variant="default" onClick={() => setOpen(false)}>{t('common.cancel')}</Button>
                <Button loading={busy} onClick={() => void save()} disabled={!rows.some((r) => r.keep && r.ar.trim())}>
                  {draft && draft.remaining > 0 ? t('bilingual.saveNext') : t('bilingual.save')}
                </Button>
              </Group>
            </Group>
          </Stack>
        )}
      </Modal>
    </>
  );
}
