import { Badge, Code, Group, Text, Timeline } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { IconEdit, IconPlus, IconTrash } from '@tabler/icons-react';
import { get } from '@/api/client';
import type { AuditItem } from '@/api/types';
import { humanizeField, parseChanges, showValue } from '@/pages/settings/activity';
import { fmtDateTime } from './utils';

const sourceColor = (s?: string) => (s === 'Phone' ? 'grape' : s === 'Import' ? 'orange' : 'gray');

/**
 * A record's History tab: its own change entries and its child lines' (a work order's labour, a purchase order's lines…),
 * each with where it came from (0.25.0) when that wasn't the web app.
 */
export function AuditTimeline({ entityType, entityId }: { entityType: string; entityId: string }) {
  const { t } = useTranslation();
  const { data } = useQuery({ queryKey: ['audit', entityType, entityId], queryFn: () => get<AuditItem[]>('/audit', { entityType, entityId }) });
  if (data && data.length === 0) return <Text c="dimmed" fz="sm">{t('assets.noHistory')}</Text>;
  return (
    <Timeline bulletSize={26} lineWidth={2}>
      {(data ?? []).map((a) => {
        const changes = parseChanges(a.changes) ?? {};
        const Icon = a.action === 'Created' ? IconPlus : a.action === 'Deleted' ? IconTrash : IconEdit;
        // A child line's entry names the line ("Labour") beside the action.
        const child = a.entityType && a.entityType !== entityType ? t(`activity.types.${a.entityType}`, { defaultValue: humanizeField(a.entityType) }) : null;
        const verb = child
          ? t(`activity.actions.${a.action}`, { defaultValue: a.action })
          : t(`audit.${a.action}`, { defaultValue: t(`activity.actions.${a.action}`, { defaultValue: a.action }) });
        return (
          <Timeline.Item key={a.id} bullet={<Icon size={14} />} title={
            <Group gap={6} wrap="wrap">
              <Text fz="sm"><b>{a.userName ?? t('activity.system')}</b> {verb}{child && <> · {child}</>}</Text>
              {a.source && a.source !== 'Web' && <Badge size="xs" variant="light" color={sourceColor(a.source)}>{t(`activity.sources.${a.source}`)}</Badge>}
            </Group>}>
            {Object.entries(changes).map(([field, c]) => (
              <Text key={field} fz="sm" c="dimmed">{humanizeField(field)}: <Code>{showValue(c?.from)}</Code> → <Code>{showValue(c?.to)}</Code></Text>
            ))}
            <Text fz="xs" c="dimmed" mt={2}>{fmtDateTime(a.timestamp)}</Text>
          </Timeline.Item>
        );
      })}
    </Timeline>
  );
}
