import { useState } from 'react';
import { ActionIcon, Badge, Button, Card, Group, Modal, Stack, Switch, Table, Text, TextInput, Title } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { IconEdit, IconPlus, IconTrash } from '@tabler/icons-react';
import { del, post, put } from '@/api/client';
import type { UnitItem } from '@/api/types';
import { confirmAction, confirmDelete } from '@/components/confirm';
import { isMergeConflict } from '@/components/merge';
import { useUnits } from '@/components/ReferencePickers';
import { notifyError, notifySuccess } from '@/components/utils';

type Editing = { id?: string; symbol: string; name: string; nameAr: string; isActive: boolean };
const blank: Editing = { symbol: '', name: '', nameAr: '', isActive: true };

/**
 * Settings › Units (0.21.0): picked for parts, measurement points and template steps, which keep the symbol as their text.
 * Case matters ("mW" isn't "MW"); a new symbol is written to everything using the old one, and onto another unit's symbol it merges.
 */
export function UnitsSettings({ editable }: { editable: boolean }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data } = useUnits();
  const [opened, m] = useDisclosure();
  const [editing, setEditing] = useState<Editing>(blank);
  const [busy, setBusy] = useState(false);
  const refresh = () => qc.invalidateQueries({ queryKey: ['units'] });
  const open = (u: UnitItem | null) => { setEditing(u ? { id: u.id, symbol: u.symbol, name: u.name, nameAr: u.nameAr ?? '', isActive: u.isActive } : blank); m.open(); };
  const send = (merge: boolean) => {
    const body = { symbol: editing.symbol, name: editing.name || null, nameAr: editing.nameAr || null, isActive: editing.isActive, merge };
    return editing.id ? put(`/units/${editing.id}`, body) : post('/units', body);
  };
  const save = async () => {
    setBusy(true);
    try { await send(false); notifySuccess(t('common.saved')); await refresh(); m.close(); }
    catch (e) {
      if (!isMergeConflict(e)) notifyError(e);
      else confirmAction(t('units.mergeQuestion', { symbol: editing.symbol }), async () => {
        try { await send(true); notifySuccess(t('brands.merged')); await refresh(); m.close(); } catch (x) { notifyError(x); }
      });
    } finally { setBusy(false); }
  };
  return (
    <Card>
      <Group justify="space-between" mb="xs" wrap="wrap">
        <div><Title order={5}>{t('units.title')}</Title><Text fz="sm" c="dimmed">{t('units.hint')}</Text></div>
        {editable && <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('units.new')}</Button>}
      </Group>
      <Table striped>
        <Table.Thead><Table.Tr><Table.Th>{t('units.symbol')}</Table.Th><Table.Th>{t('lists.nameEn')}</Table.Th><Table.Th>{t('lists.nameAr')}</Table.Th><Table.Th /></Table.Tr></Table.Thead>
        <Table.Tbody>
          {(data ?? []).map((u) => (
            <Table.Tr key={u.id}>
              <Table.Td><Group gap={6}><Text fz="sm" fw={600}>{u.symbol}</Text>{!u.isActive && <Badge size="xs" color="gray">{t('common.inactive')}</Badge>}</Group></Table.Td>
              <Table.Td><Text fz="sm">{u.name}</Text></Table.Td>
              <Table.Td><Text fz="sm" dir="rtl">{u.nameAr}</Text></Table.Td>
              <Table.Td>{editable && <Group gap={4} justify="flex-end" wrap="nowrap">
                <ActionIcon variant="subtle" aria-label={t('common.edit')} onClick={() => open(u)}><IconEdit size={16} /></ActionIcon>
                <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => confirmDelete(u.symbol, async () => {
                  try { await del(`/units/${u.id}`); await refresh(); } catch (e) { notifyError(e); } })}><IconTrash size={16} /></ActionIcon>
              </Group>}</Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
      <Modal opened={opened} onClose={m.close} title={editing.id ? t('units.edit') : t('units.new')}>
        <Stack>
          <TextInput label={t('units.symbol')} required maxLength={30} value={editing.symbol} onChange={(e) => setEditing({ ...editing, symbol: e.currentTarget.value })} data-autofocus />
          <TextInput label={t('lists.nameEn')} maxLength={100} placeholder={editing.symbol} value={editing.name} onChange={(e) => setEditing({ ...editing, name: e.currentTarget.value })} />
          <TextInput label={t('lists.nameAr')} maxLength={100} dir="rtl" value={editing.nameAr} onChange={(e) => setEditing({ ...editing, nameAr: e.currentTarget.value })} />
          <Switch label={t('common.active')} checked={editing.isActive} onChange={(e) => setEditing({ ...editing, isActive: e.currentTarget.checked })} />
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button loading={busy} disabled={!editing.symbol.trim()} onClick={save}>{t('common.save')}</Button></Group>
        </Stack>
      </Modal>
    </Card>
  );
}
