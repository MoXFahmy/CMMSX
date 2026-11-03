import { useState } from 'react';
import { ActionIcon, Badge, Button, Card, Checkbox, ColorInput, Group, Modal, SimpleGrid, Stack, TextInput, Title } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { DataTable } from 'mantine-datatable';
import { IconEdit, IconPlus, IconTrash } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import type { Lookup } from '@/api/types';
import { confirmDelete } from './confirm';
import { handleFormError, notifyError, notifySuccess } from './utils';

/** CRUD table for simple code/name lookups (trades, failure codes). */
export function LookupTable({ path, title, newLabel, editable, withColor }: { path: string; title: string; newLabel: string; editable: boolean; withColor?: boolean }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const key = [path.replace('/', '')];
  const { data, isFetching } = useQuery({ queryKey: key, queryFn: () => get<Lookup[]>(path) });
  const [opened, m] = useDisclosure();
  const [editing, setEditing] = useState<Lookup | null>(null);
  const form = useForm({ initialValues: { code: '', name: '', color: '#228be6', isActive: true } });
  const open = (x: Lookup | null) => { setEditing(x); form.setValues(x ? { code: x.code, name: x.name, color: x.color ?? '#228be6', isActive: x.isActive } : { code: '', name: '', color: '#228be6', isActive: true }); form.clearErrors(); m.open(); };
  const submit = form.onSubmit(async (v) => {
    try { if (editing) await put(`${path}/${editing.id}`, v); else await post(path, v); notifySuccess(t('common.saved')); await qc.invalidateQueries({ queryKey: key }); m.close(); }
    catch (e) { handleFormError(form, e); }
  });
  return (
    <Card p={0}>
      <Group justify="space-between" p="md"><Title order={5}>{title}</Title>{editable && <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{newLabel}</Button>}</Group>
      <DataTable minHeight={150} fetching={isFetching} records={data ?? []} noRecordsText={t('common.noRecords')} columns={[
        { accessor: 'code', title: t('common.code'), width: 120 },
        { accessor: 'name', title: t('common.name'), render: (x) => <Group gap={8}>{withColor && <span style={{ width: 10, height: 10, borderRadius: 5, background: x.color ?? '#adb5bd', display: 'inline-block' }} />}{x.name}</Group> },
        { accessor: 'isActive', title: t('common.status'), width: 110, render: (x) => <Badge color={x.isActive ? 'teal' : 'gray'}>{x.isActive ? t('common.active') : t('common.inactive')}</Badge> },
        { accessor: 'actions', title: '', width: 90, hidden: !editable, render: (x) => (
          <Group gap={4} justify="flex-end" wrap="nowrap">
            <ActionIcon variant="subtle" onClick={() => open(x)} aria-label={t('common.edit')}><IconEdit size={16} /></ActionIcon>
            <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => confirmDelete(x.name, async () => { try { await del(`${path}/${x.id}`); await qc.invalidateQueries({ queryKey: key }); } catch (e) { notifyError(e); } })}><IconTrash size={16} /></ActionIcon>
          </Group>) },
      ]} />
      <Modal opened={opened} onClose={m.close} title={editing ? t('common.edit') : newLabel}>
        <form onSubmit={submit}><Stack>
          <SimpleGrid cols={3}><TextInput label={t('common.code')} required {...form.getInputProps('code')} /><TextInput label={t('common.name')} required style={{ gridColumn: 'span 2' }} {...form.getInputProps('name')} /></SimpleGrid>
          {withColor && <ColorInput label={t('settings.color')} {...form.getInputProps('color')} />}
          <Checkbox label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack></form>
      </Modal>
    </Card>
  );
}
