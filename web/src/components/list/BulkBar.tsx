import type { ReactNode } from 'react';
import { Button, Card, Group, List, Text } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import type { BulkResult } from './types';

/** Rows a bulk action can take at once (the server's limit). */
export const BULK_MAX = 500;

/** The bar above a list while rows are ticked: the count, the actions and "Clear selection". */
export function BulkBar({ count, onClear, children }: { count: number; onClear: () => void; children: ReactNode }) {
  const { t } = useTranslation();
  if (count === 0) return null;
  return (
    <Card p="xs" mb="sm" bg="var(--mantine-color-brand-light)">
      <Group gap="xs">
        <Text fz="sm" fw={600} ml="xs">{t('common.selected', { count })}</Text>
        {count > BULK_MAX ? <Text fz="sm" c="red">{t('list.tooMany', { max: BULK_MAX })}</Text> : children}
        <Button size="xs" variant="subtle" onClick={onClear}>{t('list.clearSelection')}</Button>
      </Group>
    </Card>
  );
}

/** Says how many rows changed and, when some were skipped, which and why (the first ten). */
export function showBulkResult(t: TFunction, r: BulkResult) {
  if (r.skipped.length === 0) {
    notifications.show({ color: 'teal', message: t('list.bulkUpdated', { count: r.updated }), autoClose: 3000 });
    return;
  }
  notifications.show({
    color: r.updated > 0 ? 'yellow' : 'red', autoClose: 12000,
    title: t('list.bulkPartly', { updated: r.updated, skipped: r.skipped.length }),
    message: (
      <List size="xs" spacing={2}>
        {r.skipped.slice(0, 10).map((s) => <List.Item key={s.id}>{s.number ? `${s.number}: ` : ''}{s.reason}</List.Item>)}
        {r.skipped.length > 10 && <List.Item>{t('list.andMore', { count: r.skipped.length - 10 })}</List.Item>}
      </List>
    ),
  });
}
