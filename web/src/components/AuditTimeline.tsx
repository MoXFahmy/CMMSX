import { Text, Timeline, Code } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { IconEdit, IconPlus, IconTrash } from '@tabler/icons-react';
import { get } from '@/api/client';
import type { AuditItem } from '@/api/types';
import { fmtDateTime } from './utils';

const humanize = (k: string) => k.replace(/Id$/, '').replace(/([a-z])([A-Z])/g, '$1 $2');
const show = (v: unknown): string => {
  if (v === null || v === undefined || v === '') return '∅';
  if (typeof v === 'object') return Object.entries(v as Record<string, unknown>).map(([k, x]) => `${k}: ${x ?? '∅'}`).join(', ') || '∅';
  const s = String(v);
  return /^[0-9a-f]{8}-[0-9a-f]{4}-/.test(s) ? '…' : s;
};

export function AuditTimeline({ entityType, entityId }: { entityType: string; entityId: string }) {
  const { t } = useTranslation();
  const { data } = useQuery({ queryKey: ['audit', entityType, entityId], queryFn: () => get<AuditItem[]>('/audit', { entityType, entityId }) });
  if (data && data.length === 0) return <Text c="dimmed" fz="sm">{t('assets.noHistory')}</Text>;
  return (
    <Timeline bulletSize={26} lineWidth={2}>
      {(data ?? []).map((a) => {
        const changes = a.changes ? (JSON.parse(a.changes) as Record<string, { from: unknown; to: unknown }>) : {};
        const Icon = a.action === 'Created' ? IconPlus : a.action === 'Deleted' ? IconTrash : IconEdit;
        return (
          <Timeline.Item key={a.id} bullet={<Icon size={14} />} title={<Text fz="sm"><b>{a.userName ?? 'System'}</b> {t(`audit.${a.action}`)}</Text>}>
            {Object.entries(changes).map(([field, c]) => (
              <Text key={field} fz="sm" c="dimmed">{humanize(field)}: <Code>{show(c.from)}</Code> → <Code>{show(c.to)}</Code></Text>
            ))}
            <Text fz="xs" c="dimmed" mt={2}>{fmtDateTime(a.timestamp)}</Text>
          </Timeline.Item>
        );
      })}
    </Timeline>
  );
}
