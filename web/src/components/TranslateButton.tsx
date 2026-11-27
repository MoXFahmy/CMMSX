import { useRef, useState } from 'react';
import { Button, Checkbox, Group, Modal, ScrollArea, Stack, Table, Text, TextInput } from '@mantine/core';
import { IconLanguage } from '@tabler/icons-react';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { post } from '@/api/client';
import { P, useAuth } from '@/auth/AuthContext';
import { aiOn, useAiStatus } from './aiStatus';
import { notifyError, notifySuccess } from './utils';
import { draftState, leftOutCount, nextStep, primaryAction, toRows, type ReviewRow, type Skipped, type TranslationDraft, type TranslationList } from './translations';

/** The permission that edits each list's texts (the server checks the same). */
const EDIT: Record<TranslationList, string> = {
  assets: P.assetsEdit, locations: P.locationsManage, parts: P.inventoryManage, categories: P.settingsManage, symptoms: P.settingsManage,
  causes: P.settingsManage, remedies: P.settingsManage, 'cancel-reasons': P.settingsManage, 'request-types': P.settingsManage,
  templates: P.pmManage, 'pm-schedules': P.pmManage,
};

/** The review's main button text for each action. */
const PRIMARY = { save: 'bilingual.save', saveNext: 'bilingual.saveNext', skip: 'bilingual.skipAndGo' } as const;

/**
 * "Fill in missing Arabic" (0.28.0): the AI drafts a batch of the list's missing Arabic texts; the person checks, edits, unticks and saves.
 * Shown to people who can edit the list, when the AI assistant is on. <c>refresh</c>: the query keys to reload after saving.
 * 0.29.2: while the window is open it remembers the texts the person unticked and the ones the assistant couldn't draft, and doesn't
 * ask for them again (no charge twice); closing it forgets them. A batch that drafts nothing offers Try again / Skip these and go on / Close.
 * After a save or a skip it always asks for the next batch, so a field's last batch goes on to the list's next field; a batch with no
 * row kept is skipped, not stuck behind a disabled Save. An answer that arrives after the window was closed is ignored.
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
  const [passed, setPassed] = useState<Skipped[]>([]);
  // Bumped on every close: an answer to a request sent before it belongs to a window that is gone and must not reopen it.
  const session = useRef(0);
  if (!aiOn(ai) || !can(EDIT[list])) return null;

  /** Closing forgets what was passed: opening the window again offers everything. */
  const close = () => { session.current++; setOpen(false); setPassed([]); setDraft(null); setRows([]); };
  /** The next batch, leaving out <c>skip</c> (texts the person passed on while this window has been open, each with its field). */
  const next = async (skip: Skipped[] = []) => {
    const mine = session.current;
    setBusy(true);
    try {
      const d = await post<TranslationDraft>('/translations/draft', { list, skip });
      void qc.invalidateQueries({ queryKey: ['ai-status'] });
      if (session.current !== mine) return;
      setPassed(skip); setDraft(d); setRows(toRows(d)); setOpen(true);
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };

  /** Save (or skip) this batch, then go on to the next one. */
  const goOn = async () => {
    const step = nextStep(list, draft, rows, passed);
    if (step.kind === 'close') { close(); return; }
    const mine = session.current;
    setBusy(true);
    try {
      if (step.save) {
        const r = await post<{ saved: number; skipped: number }>('/translations/save', step.save);
        notifySuccess(t('bilingual.saved', { n: r.saved }));
        for (const key of refresh) void qc.invalidateQueries({ queryKey: key });
      }
      if (session.current === mine) await next(step.skip);
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };

  const state = draft ? draftState(draft) : 'done';
  // Texts left out so far: the earlier passes plus this batch's unticked or blank rows and the ones not drafted.
  const leftOut = leftOutCount(list, draft, rows, passed);
  const change = (id: string, patch: Partial<ReviewRow>) => setRows((rs) => rs.map((r) => (r.id === id ? { ...r, ...patch } : r)));

  return (
    <>
      <Button variant="default" leftSection={<IconLanguage size={16} />} loading={busy && !open} onClick={() => void next()}>{t('bilingual.fill')}</Button>
      <Modal opened={open} onClose={close} title={t('bilingual.review')} size="xl">
        {draft && state === 'done' ? (
          <Stack>
            <Text>{passed.length > 0 ? t('bilingual.nothingMore') : t('bilingual.none')}</Text>
            {passed.length > 0 && <Text fz="sm" c="dimmed">{t('bilingual.leftOut', { n: passed.length })}</Text>}
            <Group justify="flex-end"><Button variant="default" onClick={close}>{t('bilingual.close')}</Button></Group>
          </Stack>
        ) : draft && state === 'nothing' ? (
          <Stack>
            <Text c="orange">{t('bilingual.nothingDrafted', { n: draft.couldNot })}</Text>
            {passed.length > 0 && <Text fz="sm" c="dimmed">{t('bilingual.leftOut', { n: passed.length })}</Text>}
            <Group justify="flex-end">
              <Button variant="default" onClick={close}>{t('bilingual.close')}</Button>
              <Button variant="default" loading={busy} onClick={() => void next(passed)}>{t('bilingual.tryAgain')}</Button>
              <Button loading={busy} onClick={() => void goOn()}>{t('bilingual.skipAndGo')}</Button>
            </Group>
          </Stack>
        ) : (
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
              <Stack gap={2}>
                <Text fz="sm" c="dimmed">{draft && draft.remaining > 0 ? t('bilingual.remaining', { n: draft.remaining }) : ''}</Text>
                {leftOut > 0 && <Text fz="sm" c="dimmed">{t('bilingual.leftOut', { n: leftOut })}</Text>}
              </Stack>
              <Group>
                <Button variant="default" onClick={close}>{t('common.cancel')}</Button>
                <Button loading={busy} onClick={() => void goOn()}>{t(PRIMARY[primaryAction(rows, draft)])}</Button>
              </Group>
            </Group>
          </Stack>
        )}
      </Modal>
    </>
  );
}
