import { useEffect, useState } from 'react';
import { Alert, Button, Group, List, Modal, Stack, Text, TextInput } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { get, post } from '@/api/client';
import type { AnonymisePreview } from '@/api/types';
import { notifyError, notifySuccess } from './utils';

/**
 * Anonymising a person who has left (0.25.0): what will change, who shares the name, and the code typed to confirm. For an
 * inactive employee (with their sign-in) or an inactive sign-in with no employee record.
 */
export function AnonymiseModal({ who, onClose, onDone }: { who: { employeeId?: string; userId?: string } | null; onClose: () => void; onDone?: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [typed, setTyped] = useState('');
  const [busy, setBusy] = useState(false);
  useEffect(() => setTyped(''), [who]);
  const { data: p } = useQuery({
    queryKey: ['anonymise-preview', who],
    queryFn: () => get<AnonymisePreview>('/anonymise/preview', { employeeId: who?.employeeId, userId: who?.userId }),
    enabled: !!who,
  });
  const matches = !!p && [p.confirmWith, p.placeholder].some((x) => x.toLowerCase() === typed.trim().toLowerCase());
  const run = async () => {
    setBusy(true);
    try {
      await post('/anonymise', { ...who, confirm: typed.trim() });
      notifySuccess(t('anonymise.done'));
      await Promise.all([qc.invalidateQueries({ queryKey: ['employees'] }), qc.invalidateQueries({ queryKey: ['users'] })]);
      onDone?.();
      onClose();
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Modal opened={!!who} onClose={onClose} title={t('anonymise.title')}>
      {p && (p.blocked ? (
        <Stack><Alert color="yellow" variant="light">{p.blocked}</Alert><Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.close')}</Button></Group></Stack>
      ) : (
        <Stack>
          <Text fz="sm">{t('anonymise.what', { placeholder: p.placeholder, count: p.records })}</Text>
          {p.sameNameAs.length > 0 && (
            <Alert color="blue" variant="light" title={t('anonymise.sameName')}>
              <List size="sm">{p.sameNameAs.map((n) => <List.Item key={n}>{n}</List.Item>)}</List>
              <Text fz="sm" mt="xs">{t('anonymise.sameNameKept')}</Text>
            </Alert>
          )}
          <Text fz="sm" c="dimmed">{t('anonymise.freeText')}</Text>
          <TextInput label={t('anonymise.typeToConfirm', { code: p.confirmWith })} value={typed} onChange={(e) => setTyped(e.currentTarget.value)} data-autofocus />
          <Group justify="flex-end">
            <Button variant="default" onClick={onClose}>{t('common.cancel')}</Button>
            <Button color="red" disabled={!matches} loading={busy} onClick={run}>{t('anonymise.action')}</Button>
          </Group>
        </Stack>
      ))}
    </Modal>
  );
}
