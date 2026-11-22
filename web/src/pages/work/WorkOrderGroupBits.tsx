import { useState } from 'react';
import { Anchor, Button, Card, Group, Stack, Text, Textarea, Title } from '@mantine/core';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconFileText, IconStack2 } from '@tabler/icons-react';
import { post } from '@/api/client';
import type { WorkOrderDetail } from '@/api/types';
import { fmtBytes, fmtDateTime, notifyError } from '@/components/utils';

type Wo = WorkOrderDetail;

/** A group's work order (0.12.0): the group it belongs to and the comments shared by all its work orders. */
export function GroupCard({ group, canComment, onChange }: { group: NonNullable<Wo['group']>; canComment: boolean; onChange: () => Promise<unknown> }) {
  const { t } = useTranslation();
  const [body, setBody] = useState('');
  const [saving, setSaving] = useState(false);
  const send = async () => {
    setSaving(true);
    try { await post(`/work-groups/${group.id}/comments`, { body }); setBody(''); await onChange(); } catch (e) { notifyError(e); } finally { setSaving(false); }
  };
  return (
    <Card><Stack gap="xs">
      <Group gap="xs" wrap="nowrap"><IconStack2 size={18} />
        <Text fz="sm">{t('groups.partOf')}{' '}<Anchor component={Link} to={`/work-groups/${group.id}`} fw={600}>{group.number}</Anchor> · {group.title}</Text></Group>
      <Text fz="xs" c="dimmed">{t('groups.commentsHint')}</Text>
      {canComment && <Group gap="xs" align="flex-end" wrap="nowrap">
        <Textarea style={{ flex: 1 }} autosize minRows={1} value={body} onChange={(e) => setBody(e.currentTarget.value)} placeholder={t('groups.addComment')} />
        <Button loading={saving} disabled={!body.trim()} onClick={send}>{t('tech.send')}</Button>
      </Group>}
      {group.comments.map((c) => <div key={c.id}><Text fz="xs" c="dimmed">{c.userName} · {fmtDateTime(c.createdAt)}</Text><Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{c.body}</Text></div>)}
    </Stack></Card>
  );
}

/** The work template the work order came from (0.12.0): its documents and expected stop. */
export function TemplateCard({ template }: { template: NonNullable<Wo['template']> }) {
  const { t } = useTranslation();
  return (
    <Card><Stack gap={6}>
      <Title order={6}>{t('pm12.template')}</Title>
      <Anchor component={Link} to={`/pm/templates/${template.id}`} fz="sm" fw={500}>{template.code} · {template.name}</Anchor>
      {template.expectedStopHours != null && template.expectedStopHours > 0 && <Text fz="sm" c="dimmed">{t('pm12.expectedStop')}: {template.expectedStopHours}</Text>}
      {template.documents.map((d) => (
        <Group key={d.id} gap={6} wrap="nowrap"><IconFileText size={16} />
          <Anchor href={`/api/attachments/${d.id}/content?download=true`} fz="sm" style={{ overflowWrap: 'anywhere' }}>{d.fileName}</Anchor>
          <Text fz="xs" c="dimmed">{fmtBytes(d.size)}</Text></Group>
      ))}
    </Stack></Card>
  );
}
