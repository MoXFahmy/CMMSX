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
export function ServiceAccountModal({ account, opened, onClose, roles, sites, places }: {
  account: User | null; opened: boolean; onClose: () => void; roles: Role[]; sites: { value: string; label: string }[];
  /** Every place the editor can see, labelled with its site and path (branches, as for a person; 0.26.1). */
  places: { value: string; label: string }[];
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const form = useForm({ initialValues: { fullName: '', roleIds: [] as string[], siteIds: [] as string[], locationIds: [] as string[], isActive: true } });
  useEffect(() => {
    if (!opened) return;
    form.setValues(account
      ? { fullName: account.fullName, roleIds: account.roles.map((r) => r.roleId), siteIds: account.siteIds ?? [], locationIds: account.locationIds ?? [], isActive: account.isActive }
      : { fullName: '', roleIds: [], siteIds: [], locationIds: [], isActive: true });
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
        <MultiSelect label={t('serviceAccounts.sites')} description={t('serviceAccounts.sitesHint')} data={sites} clearable disabled={form.values.locationIds.length > 0}
          {...form.getInputProps('siteIds')} />
        <MultiSelect label={t('access.userBranches')} description={t('access.userBranchesHint')} searchable clearable
          // A place deleted since it was given still shows, so it can be taken off.
          data={[...places, ...form.values.locationIds.filter((id) => places.length > 0 && !places.some((o) => o.value === id)).map((id) => ({ value: id, label: t('access.deletedPlace') }))]}
          {...form.getInputProps('locationIds')} />
        <Switch label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
        <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button type="submit">{t('common.save')}</Button></Group>
      </Stack></form>
    </Modal>
  );
}
