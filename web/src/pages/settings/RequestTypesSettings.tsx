import { useState } from 'react';
import { ActionIcon, Badge, Button, Group, Modal, MultiSelect, Select, Stack, Switch, Text, TextInput } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { DataTable } from 'mantine-datatable';
import { IconEdit, IconPlus, IconTrash } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import { WORK_PRIORITIES, type Lookup, type RequestType } from '@/api/types';
import { confirmDelete } from '@/components/confirm';
import { teamOptions, useTeams } from '@/components/teams';
import { handleFormError, notifyError, notifySuccess } from '@/components/utils';

const blank = { code: '', name: '', isActive: true, defaultTradeId: null as string | null, defaultPriority: null as string | null, recipientIds: [] as string[], autoApprove: false, teamId: null as string | null };

/** Settings › Request types (0.10.0): default trade and priority, who is told, and optional automatic approval. */
export function RequestTypesSettings({ editable }: { editable: boolean }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [opened, m] = useDisclosure(false);
  const [editing, setEditing] = useState<RequestType | null>(null);
  const { data, isFetching } = useQuery({ queryKey: ['request-types'], queryFn: () => get<RequestType[]>('/request-types') });
  const { data: trades } = useQuery({ queryKey: ['trades'], queryFn: () => get<Lookup[]>('/trades') });
  const { data: people } = useQuery({ queryKey: ['users-lookup'], queryFn: () => get<{ id: string; fullName: string }[]>('/users/lookup'), enabled: opened, retry: false });
  const { data: teams } = useTeams();
  const form = useForm({ initialValues: blank });
  const open = (x: RequestType | null) => {
    setEditing(x);
    form.setValues(x ? { code: x.code, name: x.name, isActive: x.isActive, defaultTradeId: x.defaultTradeId ?? null, defaultPriority: x.defaultPriority ?? null,
      recipientIds: x.recipientIds ?? [], autoApprove: x.autoApprove, teamId: x.teamId ?? null } : blank);
    form.resetDirty(); form.clearErrors(); m.open();
  };
  const submit = form.onSubmit(async (v) => {
    try {
      if (editing) await put(`/request-types/${editing.id}`, v); else await post('/request-types', v);
      notifySuccess(t('common.saved')); await qc.invalidateQueries({ queryKey: ['request-types'] }); m.close();
    } catch (e) { handleFormError(form, e); }
  });
  const tradeName = (id?: string | null) => (trades ?? []).find((x) => x.id === id)?.name;

  return (
    <Stack maw={1000}>
      <Group justify="space-between">
        <Text c="dimmed" fz="sm">{t('requestTypes.hint')}</Text>
        {editable && <Button leftSection={<IconPlus size={16} />} onClick={() => open(null)}>{t('requestTypes.new')}</Button>}
      </Group>
      <DataTable withTableBorder borderRadius="sm" minHeight={120} fetching={isFetching} records={data ?? []} noRecordsText={t('common.noRecords')}
        columns={[
          { accessor: 'code', title: t('common.code'), width: 110 },
          { accessor: 'name', title: t('common.name') },
          { accessor: 'defaultTradeId', title: t('requestTypes.defaultTrade'), render: (x) => tradeName(x.defaultTradeId) ?? '—' },
          { accessor: 'defaultPriority', title: t('requestTypes.defaultPriority'), render: (x) => (x.defaultPriority ? t(`priority.${x.defaultPriority}`) : '—') },
          { accessor: 'recipientIds', title: t('requestTypes.recipients'), render: (x) => x.recipientIds.length || '—' },
          { accessor: 'autoApprove', title: t('requestTypes.autoApprove'), render: (x) => (x.autoApprove ? <Badge variant="light" color="teal">{t('common.yes')}</Badge> : '—') },
          { accessor: 'isActive', title: t('common.status'), render: (x) => <Badge variant="light" color={x.isActive ? 'teal' : 'gray'}>{x.isActive ? t('common.active') : t('common.inactive')}</Badge> },
          ...(editable ? [{
            accessor: 'actions', title: '', width: 90, render: (x: RequestType) => (
              <Group gap={4} wrap="nowrap">
                <ActionIcon variant="subtle" aria-label={t('common.edit')} onClick={() => open(x)}><IconEdit size={16} /></ActionIcon>
                <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => confirmDelete(x.name, async () => {
                  try { await del(`/request-types/${x.id}`); await qc.invalidateQueries({ queryKey: ['request-types'] }); } catch (e) { notifyError(e); }
                })}><IconTrash size={16} /></ActionIcon>
              </Group>
            ),
          }] : []),
        ]} />
      <Modal opened={opened} onClose={m.close} title={editing ? editing.name : t('requestTypes.new')} size="lg">
        <form onSubmit={submit}><Stack>
          <Group grow>
            <TextInput label={t('common.code')} required {...form.getInputProps('code')} />
            <TextInput label={t('common.name')} required {...form.getInputProps('name')} />
          </Group>
          <Group grow>
            <Select label={t('requestTypes.defaultTrade')} clearable searchable data={(trades ?? []).filter((x) => x.isActive || x.id === form.values.defaultTradeId).map((x) => ({ value: x.id, label: x.name }))}
              {...form.getInputProps('defaultTradeId')} />
            <Select label={t('requestTypes.defaultPriority')} clearable data={WORK_PRIORITIES.map((p) => ({ value: p, label: t(`priority.${p}`) }))} {...form.getInputProps('defaultPriority')} />
          </Group>
          <MultiSelect label={t('requestTypes.recipients')} description={t('requestTypes.recipientsHint')} searchable clearable
            data={(people ?? []).map((u) => ({ value: u.id, label: u.fullName }))} {...form.getInputProps('recipientIds')} />
          <Switch label={t('requestTypes.autoApprove')} description={t('requestTypes.autoApproveHint')} {...form.getInputProps('autoApprove', { type: 'checkbox' })} />
          {form.values.autoApprove && (
            <Select label={t('requestTypes.team')} description={t('requestTypes.teamHint')} clearable data={teamOptions(teams, form.values.teamId)} {...form.getInputProps('teamId')} />
          )}
          {form.errors.autoApprove && <Text c="red" fz="sm">{form.errors.autoApprove}</Text>}
          <Switch label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button type="submit">{t('common.save')}</Button></Group>
        </Stack></form>
      </Modal>
    </Stack>
  );
}
