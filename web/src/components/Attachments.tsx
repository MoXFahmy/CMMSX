import { ActionIcon, Anchor, Group, Image, Menu, SimpleGrid, Stack, Text, Card, Badge, Tooltip } from '@mantine/core';
import { Dropzone } from '@mantine/dropzone';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { IconCloudUpload, IconDots, IconDownload, IconFile, IconPhotoStar, IconTrash } from '@tabler/icons-react';
import { useState } from 'react';
import { api, del, get, put } from '@/api/client';
import type { Attachment } from '@/api/types';
import { confirmDelete } from './confirm';
import { fmtBytes, fromNow, notifyError, notifySuccess } from './utils';

export function Attachments({ entityType, entityId, canEdit, onChanged }: { entityType: string; entityId: string; canEdit: boolean; onChanged?: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [busy, setBusy] = useState(false);
  const key = ['attachments', entityType, entityId];
  const { data } = useQuery({ queryKey: key, queryFn: () => get<Attachment[]>('/attachments', { entityType, entityId }) });
  const refresh = async () => { await qc.invalidateQueries({ queryKey: key }); onChanged?.(); };
  const upload = async (files: File[]) => {
    setBusy(true);
    try {
      const fd = new FormData();
      fd.set('entityType', entityType); fd.set('entityId', entityId);
      files.forEach((f) => fd.append('files', f));
      await api('/attachments', { method: 'POST', body: fd });
      notifySuccess(`${files.length} ✓`);
      await refresh();
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  const images = (data ?? []).filter((a) => a.contentType.startsWith('image/'));
  const docs = (data ?? []).filter((a) => !a.contentType.startsWith('image/'));
  const actions = (a: Attachment) => (
    <Menu position="bottom-end">
      <Menu.Target><ActionIcon variant="subtle" color="gray" aria-label={t('common.actions')}><IconDots size={16} /></ActionIcon></Menu.Target>
      <Menu.Dropdown>
        <Menu.Item leftSection={<IconDownload size={16} />} component="a" href={`/api/attachments/${a.id}/content?download=true`}>{t('common.download')}</Menu.Item>
        {canEdit && a.contentType.startsWith('image/') && !a.isPrimaryImage && (
          <Menu.Item leftSection={<IconPhotoStar size={16} />} onClick={async () => { try { await put(`/attachments/${a.id}/primary`, {}); await refresh(); } catch (e) { notifyError(e); } }}>{t('assets.setPrimary')}</Menu.Item>
        )}
        {canEdit && <Menu.Item color="red" leftSection={<IconTrash size={16} />} onClick={() => confirmDelete(a.fileName, async () => { try { await del(`/attachments/${a.id}`); await refresh(); } catch (e) { notifyError(e); } })}>{t('common.delete')}</Menu.Item>}
      </Menu.Dropdown>
    </Menu>
  );
  return (
    <Stack>
      {canEdit && (
        <Dropzone onDrop={upload} loading={busy} maxSize={25 * 1024 * 1024} onReject={() => notifyError(new Error('File too large (max 25 MB)'))}>
          <Group justify="center" gap="md" mih={90} style={{ pointerEvents: 'none' }}>
            <IconCloudUpload size={36} stroke={1.4} color="var(--mantine-color-brand-6)" />
            <Stack gap={0}><Text fw={500}>{t('assets.dropFiles')}</Text><Text fz="sm" c="dimmed">{t('assets.dropHint')}</Text></Stack>
          </Group>
        </Dropzone>
      )}
      {data && data.length === 0 && <Text c="dimmed" fz="sm">{t('assets.noFiles')}</Text>}
      {images.length > 0 && (
        <SimpleGrid cols={{ base: 2, sm: 3, md: 4 }}>
          {images.map((a) => (
            <Card key={a.id} p={0} style={{ overflow: 'hidden' }}>
              <Anchor href={`/api/attachments/${a.id}/content`} target="_blank"><Image src={`/api/attachments/${a.id}/content`} h={140} fit="cover" alt={a.fileName} /></Anchor>
              <Group justify="space-between" px="xs" py={4} wrap="nowrap">
                <Text fz="xs" lineClamp={1} style={{ flex: 1 }}>{a.fileName}</Text>
                {a.isPrimaryImage && <Tooltip label="Main photo"><Badge size="xs" color="yellow">★</Badge></Tooltip>}
                {actions(a)}
              </Group>
            </Card>
          ))}
        </SimpleGrid>
      )}
      {docs.map((a) => (
        <Group key={a.id} justify="space-between" wrap="nowrap" p="xs" style={{ border: '1px solid var(--mantine-color-default-border)', borderRadius: 8 }}>
          <Group gap="sm" wrap="nowrap" style={{ minWidth: 0 }}>
            <IconFile size={22} stroke={1.5} />
            <Stack gap={0} style={{ minWidth: 0 }}>
              <Anchor href={`/api/attachments/${a.id}/content`} target="_blank" fz="sm" lineClamp={1}>{a.fileName}</Anchor>
              <Text fz="xs" c="dimmed">{fmtBytes(a.size)} · {a.createdBy ?? ''} · {fromNow(a.createdAt)}</Text>
            </Stack>
          </Group>
          {actions(a)}
        </Group>
      ))}
    </Stack>
  );
}
