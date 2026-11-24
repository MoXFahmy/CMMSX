import { useState } from 'react';
import { ActionIcon, Badge, Button, Card, Grid, Group, Modal, Stack, Switch, Table, Text, TextInput, Title } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { IconEdit, IconPlus, IconTrash } from '@tabler/icons-react';
import { del, post, put } from '@/api/client';
import { confirmAction, confirmDelete } from '@/components/confirm';
import { isMergeConflict } from '@/components/merge';
import { useBrands } from '@/components/ReferencePickers';
import { notifyError, notifySuccess } from '@/components/utils';

type Editing = { kind: 'brand' | 'model'; id?: string; brandId?: string; name: string; isActive: boolean };

/**
 * Settings › Brands and models (0.21.0): picked on assets, which keep the names as text. Renaming onto a name already in the list
 * merges the two after a confirmation — the way to tidy the starting list built from what assets already said.
 */
export function BrandsSettings({ editable }: { editable: boolean }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data } = useBrands();
  const [chosen, setChosen] = useState<string | null>(null);
  const [opened, m] = useDisclosure();
  const [editing, setEditing] = useState<Editing | null>(null);
  const [busy, setBusy] = useState(false);
  const brand = data?.find((b) => b.id === chosen) ?? data?.[0];
  const refresh = () => qc.invalidateQueries({ queryKey: ['brands'] });
  const open = (e: Editing) => { setEditing(e); m.open(); };

  const send = async (merge: boolean) => {
    if (!editing) return;
    const body = { name: editing.name, isActive: editing.isActive, merge };
    if (editing.id) await put(editing.kind === 'brand' ? `/brands/${editing.id}` : `/brand-models/${editing.id}`, body);
    else await post(editing.kind === 'brand' ? '/brands' : `/brands/${editing.brandId}/models`, body);
  };
  const save = async () => {
    setBusy(true);
    try { await send(false); notifySuccess(t('common.saved')); await refresh(); m.close(); }
    catch (e) {
      const conflict = isMergeConflict(e);
      if (!conflict) notifyError(e);
      else confirmAction(t('brands.mergeQuestion', { name: editing?.name }), async () => {
        try { await send(true); notifySuccess(t('brands.merged')); await refresh(); m.close(); } catch (x) { notifyError(x); }
      });
    } finally { setBusy(false); }
  };

  return (
    <Card>
      <Group justify="space-between" mb="xs" wrap="wrap">
        <div><Title order={5}>{t('brands.title')}</Title><Text fz="sm" c="dimmed">{t('brands.hint')}</Text></div>
        {editable && <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open({ kind: 'brand', name: '', isActive: true })}>{t('brands.new')}</Button>}
      </Group>
      <Grid>
        <Grid.Col span={{ base: 12, md: 6 }}>
          <Table striped highlightOnHover>
            <Table.Thead><Table.Tr><Table.Th>{t('brands.brand')}</Table.Th><Table.Th ta="right">{t('brands.models')}</Table.Th><Table.Th /></Table.Tr></Table.Thead>
            <Table.Tbody>
              {(data ?? []).length === 0 && <Table.Tr><Table.Td colSpan={3}><Text fz="sm" c="dimmed">{t('brands.none')}</Text></Table.Td></Table.Tr>}
              {(data ?? []).map((b) => (
                <Table.Tr key={b.id} onClick={() => setChosen(b.id)} style={{ cursor: 'pointer' }} bg={b.id === brand?.id ? 'var(--mantine-primary-color-light)' : undefined}>
                  <Table.Td><Group gap={6}><Text fz="sm" fw={600}>{b.name}</Text>{!b.isActive && <Badge size="xs" color="gray">{t('common.inactive')}</Badge>}</Group></Table.Td>
                  <Table.Td ta="right">{b.models.length}</Table.Td>
                  <Table.Td>{editable && <Group gap={4} justify="flex-end" wrap="nowrap">
                    <ActionIcon variant="subtle" aria-label={t('common.edit')} onClick={(e) => { e.stopPropagation(); open({ kind: 'brand', id: b.id, name: b.name, isActive: b.isActive }); }}><IconEdit size={16} /></ActionIcon>
                    <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={(e) => { e.stopPropagation(); confirmDelete(b.name, async () => {
                      try { await del(`/brands/${b.id}`); await refresh(); } catch (x) { notifyError(x); } }); }}><IconTrash size={16} /></ActionIcon>
                  </Group>}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Grid.Col>
        <Grid.Col span={{ base: 12, md: 6 }}>
          {brand && <>
            <Group justify="space-between" mb={6}>
              <Text fw={600}>{t('brands.modelsOf', { name: brand.name })}</Text>
              {editable && <Button size="xs" variant="light" leftSection={<IconPlus size={14} />} onClick={() => open({ kind: 'model', brandId: brand.id, name: '', isActive: true })}>{t('brands.newModel')}</Button>}
            </Group>
            <Table striped>
              <Table.Tbody>
                {brand.models.length === 0 && <Table.Tr><Table.Td><Text fz="sm" c="dimmed">{t('brands.noModels')}</Text></Table.Td></Table.Tr>}
                {brand.models.map((x) => (
                  <Table.Tr key={x.id}>
                    <Table.Td><Group gap={6}><Text fz="sm">{x.name}</Text>{!x.isActive && <Badge size="xs" color="gray">{t('common.inactive')}</Badge>}</Group></Table.Td>
                    <Table.Td>{editable && <Group gap={4} justify="flex-end" wrap="nowrap">
                      <ActionIcon variant="subtle" aria-label={t('common.edit')} onClick={() => open({ kind: 'model', id: x.id, brandId: brand.id, name: x.name, isActive: x.isActive })}><IconEdit size={16} /></ActionIcon>
                      <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => confirmDelete(x.name, async () => {
                        try { await del(`/brand-models/${x.id}`); await refresh(); } catch (e) { notifyError(e); } })}><IconTrash size={16} /></ActionIcon>
                    </Group>}</Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </>}
        </Grid.Col>
      </Grid>
      <Modal opened={opened} onClose={m.close} title={editing?.kind === 'model' ? (editing.id ? t('brands.editModel') : t('brands.newModel')) : (editing?.id ? t('brands.edit') : t('brands.new'))}>
        {editing && <Stack>
          <TextInput label={t('common.name')} required maxLength={100} value={editing.name} onChange={(e) => setEditing({ ...editing, name: e.currentTarget.value })} data-autofocus />
          <Switch label={t('common.active')} checked={editing.isActive} onChange={(e) => setEditing({ ...editing, isActive: e.currentTarget.checked })} />
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button loading={busy} disabled={!editing.name.trim()} onClick={save}>{t('common.save')}</Button></Group>
        </Stack>}
      </Modal>
    </Card>
  );
}
