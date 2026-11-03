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
