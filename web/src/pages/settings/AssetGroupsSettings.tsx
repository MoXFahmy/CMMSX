import { useState } from 'react';
import { ActionIcon, Badge, Button, Card, Group, Modal, Select, Stack, Switch, Table, Text, TextInput, Title } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { IconEdit, IconPlus, IconTrash, IconUsersGroup, IconX } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import type { AssetGroupDetail, AssetGroupItem } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { AssetPicker } from '@/components/AssetPicker';
import { confirmDelete } from '@/components/confirm';
import { useHierarchy } from '@/components/LocationPicker';
import { useAssetGroups } from '@/components/ReferencePickers';
import { notifyError, notifySuccess } from '@/components/utils';

type Editing = { id?: string; code: string; name: string; siteId: string | null; isActive: boolean };
const blank: Editing = { code: '', name: '', siteId: null, isActive: true };

/**
 * Settings › Asset groups (0.21.0): named sets of assets anywhere in the tree ("Kitchen equipment", "Guest-facing"), for one site
 * or all. Used to filter lists and reports, and as a PM target that follows its members.
 */
export function AssetGroupsSettings({ editable }: { editable: boolean }) {
  const { t } = useTranslation();
  const { terms } = useAuth();
  const qc = useQueryClient();
  const { data } = useAssetGroups();
  const { data: h } = useHierarchy();
  const [opened, m] = useDisclosure();
  const [editing, setEditing] = useState<Editing>(blank);
  const [members, setMembers] = useState<AssetGroupItem | null>(null);
  const [busy, setBusy] = useState(false);
  const refresh = () => qc.invalidateQueries({ queryKey: ['asset-groups'] });
  const open = (g: AssetGroupItem | null) => { setEditing(g ? { id: g.id, code: g.code, name: g.name, siteId: g.siteId ?? null, isActive: g.isActive } : blank); m.open(); };
  const save = async () => {
    setBusy(true);
    try {
      const body = { code: editing.code, name: editing.name, siteId: editing.siteId, isActive: editing.isActive };
      if (editing.id) await put(`/asset-groups/${editing.id}`, body); else await post('/asset-groups', body);
      notifySuccess(t('common.saved')); await refresh(); m.close();
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Card>
      <Group justify="space-between" mb="xs" wrap="wrap">
        <div><Title order={5}>{t('assetGroups.title')}</Title><Text fz="sm" c="dimmed">{t('assetGroups.hint')}</Text></div>
        {editable && <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('assetGroups.new')}</Button>}
      </Group>
      <Table.ScrollContainer minWidth={520}>
        <Table striped>
          <Table.Thead><Table.Tr><Table.Th>{t('common.code')}</Table.Th><Table.Th>{t('common.name')}</Table.Th><Table.Th>{terms.site}</Table.Th><Table.Th ta="right">{t('assetGroups.members')}</Table.Th><Table.Th /></Table.Tr></Table.Thead>
          <Table.Tbody>
            {(data ?? []).length === 0 && <Table.Tr><Table.Td colSpan={5}><Text fz="sm" c="dimmed">{t('assetGroups.none')}</Text></Table.Td></Table.Tr>}
            {(data ?? []).map((g) => (
              <Table.Tr key={g.id}>
                <Table.Td><Text fz="sm" fw={600}>{g.code}</Text></Table.Td>
                <Table.Td><Group gap={6}><Text fz="sm">{g.name}</Text>{!g.isActive && <Badge size="xs" color="gray">{t('common.inactive')}</Badge>}</Group></Table.Td>
                <Table.Td><Text fz="sm" c={g.siteName ? undefined : 'dimmed'}>{g.siteName ?? t('costCentres.allSites')}</Text></Table.Td>
                <Table.Td ta="right">{g.memberCount}</Table.Td>
                <Table.Td><Group gap={4} justify="flex-end" wrap="nowrap">
                  <ActionIcon variant="subtle" aria-label={t('assetGroups.members')} onClick={() => setMembers(g)}><IconUsersGroup size={16} /></ActionIcon>
                  {editable && <>
                    <ActionIcon variant="subtle" aria-label={t('common.edit')} onClick={() => open(g)}><IconEdit size={16} /></ActionIcon>
                    <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => confirmDelete(g.name, async () => {
                      try { await del(`/asset-groups/${g.id}`); await refresh(); } catch (e) { notifyError(e); } })}><IconTrash size={16} /></ActionIcon>
                  </>}
                </Group></Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      <Modal opened={opened} onClose={m.close} title={editing.id ? t('assetGroups.edit') : t('assetGroups.new')}>
        <Stack>
          <TextInput label={t('common.code')} required maxLength={50} value={editing.code} onChange={(e) => setEditing({ ...editing, code: e.currentTarget.value })} data-autofocus />
          <TextInput label={t('common.name')} required maxLength={200} value={editing.name} onChange={(e) => setEditing({ ...editing, name: e.currentTarget.value })} />
          <Select label={terms.site} placeholder={t('costCentres.allSites')} description={t('assetGroups.siteHint')} clearable
            data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} value={editing.siteId} onChange={(v) => setEditing({ ...editing, siteId: v })} />
          <Switch label={t('common.active')} checked={editing.isActive} onChange={(e) => setEditing({ ...editing, isActive: e.currentTarget.checked })} />
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button>
            <Button loading={busy} disabled={!editing.code.trim() || !editing.name.trim()} onClick={save}>{t('common.save')}</Button></Group>
        </Stack>
      </Modal>
      {members && <MembersModal group={members} editable={editable} onClose={() => setMembers(null)} />}
    </Card>
  );
}

/** A group's members (those the person can see): add one with the asset picker, remove with the cross. */
function MembersModal({ group, editable, onClose }: { group: AssetGroupItem; editable: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data, refetch } = useQuery({ queryKey: ['asset-group', group.id], queryFn: () => get<AssetGroupDetail>(`/asset-groups/${group.id}`) });
  const [asset, setAsset] = useState<string | null>(null);
  const change = async (add: string[], remove: string[]) => {
    try {
      await post(`/asset-groups/${group.id}/members`, { add, remove });
      setAsset(null);
      await refetch(); await qc.invalidateQueries({ queryKey: ['asset-groups'] }); await qc.invalidateQueries({ queryKey: ['assets'] });
    } catch (e) { notifyError(e); }
  };
  return (
    <Modal opened onClose={onClose} size="lg" title={`${group.code} · ${group.name}`}>
      <Stack>
        {editable && <Group align="flex-end" wrap="nowrap">
          <div style={{ flex: 1 }}><AssetPicker label={t('assetGroups.addAsset')} value={asset} onChange={setAsset} siteId={group.siteId ?? undefined} /></div>
          <Button disabled={!asset} onClick={() => asset && change([asset], [])}>{t('common.add')}</Button>
        </Group>}
        <Table striped>
          <Table.Tbody>
            {(data?.members ?? []).length === 0 && <Table.Tr><Table.Td><Text fz="sm" c="dimmed">{t('assetGroups.noMembers')}</Text></Table.Td></Table.Tr>}
            {(data?.members ?? []).map((a) => (
              <Table.Tr key={a.id}>
                <Table.Td><Text fz="sm" fw={600}>{a.assetNumber}</Text></Table.Td>
                <Table.Td><Text fz="sm">{a.name}</Text></Table.Td>
                <Table.Td>{editable && <ActionIcon variant="subtle" color="red" aria-label={t('assetGroups.remove')} onClick={() => change([], [a.id])}><IconX size={16} /></ActionIcon>}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Stack>
    </Modal>
  );
}
