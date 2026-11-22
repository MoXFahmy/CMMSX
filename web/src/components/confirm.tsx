import { Text } from '@mantine/core';
import { modals } from '@mantine/modals';
import i18n from '@/i18n';

export function confirmDelete(name: string, onConfirm: () => Promise<unknown> | void, body?: string) {
  modals.openConfirmModal({
    title: i18n.t('common.confirmDeleteTitle', { name }),
    children: <Text size="sm">{body ?? i18n.t('common.confirmDeleteBody')}</Text>,
    labels: { confirm: i18n.t('common.delete'), cancel: i18n.t('common.cancel') },
    confirmProps: { color: 'red' },
    onConfirm: () => void onConfirm(),
  });
}

/** Asks before an action that can't be undone (posting a count, cancelling it). */
export function confirmAction(question: string, onConfirm: () => Promise<unknown> | void) {
  modals.openConfirmModal({
    title: question,
    labels: { confirm: i18n.t('common.confirm'), cancel: i18n.t('common.cancel') },
    onConfirm: () => void onConfirm(),
  });
}
