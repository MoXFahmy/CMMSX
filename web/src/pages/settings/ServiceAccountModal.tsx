import { useEffect } from 'react';
import { Button, Group, Modal, MultiSelect, Stack, Switch, Text, TextInput } from '@mantine/core';
import { useForm } from '@mantine/form';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { post, put } from '@/api/client';
import type { Role, User } from '@/api/types';
import { handleFormError, notifySuccess } from '@/components/utils';

/**
 * A service account (0.26.0): an account for a system, which API keys act as. It has roles and sites like a person, but no
 * e-mail, password or phone, and can't sign in.
 */
export function ServiceAccountModal({ account, opened, onClose, roles, sites }: {
  account: User | null; opened: boolean; onClose: () => void; roles: Role[]; sites: { value: string; label: string }[];
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const form = useForm({ initialValues: { fullName: '', roleIds: [] as string[], siteIds: [] as string[], isActive: true } });
  useEffect(() => {
    if (!opened) return;
    form.setValues(account
      ? { fullName: account.fullName, roleIds: account.roles.map((r) => r.roleId), siteIds: account.siteIds ?? [], isActive: account.isActive }
      : { fullName: '', roleIds: [], siteIds: [], isActive: true });
    form.clearErrors();
  }, [opened, account?.id]); // eslint-disable-line react-hooks/exhaustive-deps
  const submit = form.onSubmit(async (v) => {
    try {
      if (account) await put(`/service-accounts/${account.id}`, v); else await post('/service-accounts', v);
      notifySuccess(t('common.saved'));
      await qc.invalidateQueries({ queryKey: ['users'] });
      onClose();
    } catch (e) { handleFormError(form, e); }
  });
  return (
    <Modal opened={opened} onClose={onClose} title={account ? t('serviceAccounts.edit') : t('serviceAccounts.new')}>
      <form onSubmit={submit}><Stack>
        <Text fz="sm" c="dimmed">{t('serviceAccounts.intro')}</Text>
        <TextInput label={t('common.name')} placeholder={t('serviceAccounts.namePlaceholder')} required {...form.getInputProps('fullName')} />
        <MultiSelect label={t('settings.rolesLabel')} data={roles.map((r) => ({ value: r.id, label: r.name }))} required {...form.getInputProps('roleIds')} />
        <MultiSelect label={t('serviceAccounts.sites')} description={t('serviceAccounts.sitesHint')} data={sites} clearable {...form.getInputProps('siteIds')} />
        <Switch label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
        <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button type="submit">{t('common.save')}</Button></Group>
      </Stack></form>
    </Modal>
  );
}
