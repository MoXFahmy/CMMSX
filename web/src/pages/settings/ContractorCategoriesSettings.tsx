import { ActionIcon, Badge, Button, Card, Group, Modal, NumberInput, Stack, Switch, Table, Text, TextInput, Title } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { IconEdit, IconPlus, IconTrash } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import type { ContractorCategory } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { confirmDelete } from '@/components/confirm';
import { fmtPrice, handleFormError, notifyError, notifySuccess } from '@/components/utils';

const blank = { code: '', name: '', defaultRate: 0 as number | string, isActive: true };

/** Settings › Contractor categories (0.17.0): kinds of contractor work with a default hourly rate in base currency. */
export function ContractorCategoriesSettings({ editable }: { editable: boolean }) {
  const { t } = useTranslation();
  const { me } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const qc = useQueryClient();
  const { data } = useQuery({ queryKey: ['contractor-categories'], queryFn: () => get<ContractorCategory[]>('/contractor-categories') });
  const [opened, m] = useDisclosure();
  const [editing, setEditing] = useState<ContractorCategory | null>(null);
  const form = useForm({ initialValues: blank });
  const open = (c: ContractorCategory | null) => {
    setEditing(c); form.clearErrors();
    form.setValues(c ? { code: c.code, name: c.name, defaultRate: c.defaultRate, isActive: c.isActive } : blank);
    m.open();
  };
  const submit = form.onSubmit(async (v) => {
    try {
      const body = { ...v, defaultRate: Number(v.defaultRate) || 0 };
      if (editing) await put(`/contractor-categories/${editing.id}`, body); else await post('/contractor-categories', body);
      notifySuccess(t('common.saved')); await qc.invalidateQueries({ queryKey: ['contractor-categories'] }); m.close();
    } catch (e) { handleFormError(form, e); }
  });
  return (
    <Card>
      <Group justify="space-between" mb="xs">
        <div><Title order={5}>{t('contractorCategories.title')}</Title><Text fz="sm" c="dimmed">{t('contractorCategories.hint')}</Text></div>
        {editable && <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('contractorCategories.new')}</Button>}
      </Group>
      <Table striped>
        <Table.Thead><Table.Tr><Table.Th>{t('common.code')}</Table.Th><Table.Th>{t('common.name')}</Table.Th><Table.Th ta="end">{t('contractorCategories.defaultRate')}</Table.Th><Table.Th /></Table.Tr></Table.Thead>
        <Table.Tbody>
          {(data ?? []).length === 0 && <Table.Tr><Table.Td colSpan={4}><Text fz="sm" c="dimmed">{t('contractorCategories.none')}</Text></Table.Td></Table.Tr>}
          {(data ?? []).map((c) => (
            <Table.Tr key={c.id}>
              <Table.Td><Text fz="sm" fw={600}>{c.code}</Text></Table.Td>
              <Table.Td><Group gap={6}><Text fz="sm">{c.name}</Text>{!c.isActive && <Badge size="xs" color="gray">{t('common.inactive')}</Badge>}</Group></Table.Td>
              <Table.Td ta="end">{fmtPrice(c.defaultRate, currency)}</Table.Td>
              <Table.Td>{editable && <Group gap={4} justify="flex-end" wrap="nowrap">
                <ActionIcon variant="subtle" aria-label={t('common.edit')} onClick={() => open(c)}><IconEdit size={16} /></ActionIcon>
                <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => confirmDelete(c.name, async () => {
                  try { await del(`/contractor-categories/${c.id}`); await qc.invalidateQueries({ queryKey: ['contractor-categories'] }); } catch (e) { notifyError(e); } })}><IconTrash size={16} /></ActionIcon>
              </Group>}</Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
      <Modal opened={opened} onClose={m.close} title={editing ? t('contractorCategories.edit') : t('contractorCategories.new')}>
        <form onSubmit={submit}><Stack>
          <TextInput label={t('common.code')} required {...form.getInputProps('code')} />
          <TextInput label={t('common.name')} required {...form.getInputProps('name')} />
          <NumberInput label={t('contractorCategories.defaultRate')} description={t('contractorCategories.rateHint', { currency })} min={0} decimalScale={2} {...form.getInputProps('defaultRate')} />
          <Switch label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack></form>
      </Modal>
    </Card>
  );
}
