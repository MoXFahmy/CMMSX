import { useState } from 'react';
import { ActionIcon, Alert, Badge, Button, Card, Code, CopyButton, Group, Modal, Select, Stack, Switch, Text, TextInput, Title, Tooltip } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { IconCheck, IconCopy, IconPlus } from '@tabler/icons-react';
import dayjs from 'dayjs';
import { DataTable } from 'mantine-datatable';
import { useTranslation } from 'react-i18next';
import { get, post } from '@/api/client';
import type { ApiKeyRow, User } from '@/api/types';
import { confirmAction } from '@/components/confirm';
import { fmtDateTime, fromNow, handleFormError, notifyError } from '@/components/utils';
import { keyLabel, keyStatusColor } from './apiKeys';

/** Settings › API keys (0.26.0, Manage users): keys for service accounts, shown once, revocable. */
export function ApiKeysSettings({ account }: { account?: string | null }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data: keys, isFetching } = useQuery({ queryKey: ['api-keys'], queryFn: () => get<ApiKeyRow[]>('/api-keys') });
  const { data: users } = useQuery({ queryKey: ['users'], queryFn: () => get<User[]>('/users') });
  const accounts = (users ?? []).filter((u) => u.isServiceAccount && u.isActive);
  const [opened, setOpened] = useState(false);
  const [made, setMade] = useState<string | null>(null);
  const form = useForm({ initialValues: { serviceAccountId: account ?? '', name: '', readOnly: false, expiresAt: null as string | null } });
  const shown = (keys ?? []).filter((k) => !account || k.serviceAccountId === account);

  const open = () => { form.setValues({ serviceAccountId: account ?? accounts[0]?.id ?? '', name: '', readOnly: false, expiresAt: null }); form.clearErrors(); setMade(null); setOpened(true); };
  const submit = form.onSubmit(async (v) => {
    try {
      const r = await post<{ id: string; key: string }>('/api-keys', { ...v, expiresAt: v.expiresAt ? dayjs(v.expiresAt).endOf('day').toISOString() : null });
      setMade(r.key);
      await qc.invalidateQueries({ queryKey: ['api-keys'] });
    } catch (e) { handleFormError(form, e); }
  });
  const revoke = (k: ApiKeyRow) => confirmAction(t('apiKeys.revokeQuestion', { name: k.name }), async () => {
    try { await post(`/api-keys/${k.id}/revoke`); await qc.invalidateQueries({ queryKey: ['api-keys'] }); } catch (e) { notifyError(e); }
  });

  return (
    <Card p={0}>
      <Group justify="space-between" p="md">
        <div><Title order={5}>{t('apiKeys.title')}</Title><Text c="dimmed" fz="sm">{t('apiKeys.intro')}</Text></div>
        <Button size="xs" leftSection={<IconPlus size={14} />} onClick={open} disabled={accounts.length === 0}>{t('apiKeys.new')}</Button>
      </Group>
      {accounts.length === 0 && <Alert mx="md" mb="md" color="gray" variant="light">{t('apiKeys.noAccounts')}</Alert>}
      <DataTable minHeight={150} fetching={isFetching} records={shown} noRecordsText={t('common.noRecords')} columns={[
        { accessor: 'name', title: t('common.name'), render: (k) => <Stack gap={0}><Text fz="sm" fw={500}>{k.name}</Text><Code fz="xs">{keyLabel(k.prefix)}</Code></Stack> },
        { accessor: 'serviceAccountName', title: t('apiKeys.account') },
        { accessor: 'readOnly', title: t('apiKeys.access'), render: (k) => (k.readOnly ? t('apiKeys.readOnly') : t('apiKeys.readWrite')) },
        { accessor: 'createdAt', title: t('apiKeys.created'), render: (k) => <Stack gap={0}><Text fz="sm">{k.createdByName ?? '—'}</Text><Text fz="xs" c="dimmed">{fmtDateTime(k.createdAt)}</Text></Stack> },
        { accessor: 'expiresAt', title: t('apiKeys.expires'), render: (k) => (k.expiresAt ? fmtDateTime(k.expiresAt) : t('apiKeys.never')) },
        { accessor: 'lastUsedAt', title: t('apiKeys.lastUsed'), render: (k) => (k.lastUsedAt ? fromNow(k.lastUsedAt) : t('apiKeys.notYet')) },
        { accessor: 'status', title: t('common.status'), render: (k) => <Badge variant="light" color={keyStatusColor(k.status)}>{t(`apiKeys.status.${k.status}`)}</Badge> },
        { accessor: 'actions', title: '', width: 110, render: (k) => (k.status === 'Revoked' ? null
          : <Button size="compact-xs" variant="subtle" color="red" onClick={() => revoke(k)}>{t('apiKeys.revoke')}</Button>) },
      ]} />
      <Modal opened={opened} onClose={() => setOpened(false)} title={t('apiKeys.new')}>
        {made ? (
          <Stack>
            <Alert color="yellow" variant="light">{t('apiKeys.onlyOnce')}</Alert>
            <Group gap="xs" wrap="nowrap">
              <Code block style={{ flex: 1, wordBreak: 'break-all' }}>{made}</Code>
              <CopyButton value={made}>{({ copied, copy }) => (
                <Tooltip label={copied ? t('apiKeys.copied') : t('apiKeys.copy')}><ActionIcon variant="light" onClick={copy}>{copied ? <IconCheck size={16} /> : <IconCopy size={16} />}</ActionIcon></Tooltip>
              )}</CopyButton>
            </Group>
            <Group justify="flex-end"><Button onClick={() => setOpened(false)}>{t('common.close')}</Button></Group>
          </Stack>
        ) : (
          <form onSubmit={submit}><Stack>
            <Select label={t('apiKeys.account')} required data={accounts.map((a) => ({ value: a.id, label: a.fullName }))} {...form.getInputProps('serviceAccountId')} />
            <TextInput label={t('common.name')} placeholder={t('apiKeys.namePlaceholder')} required {...form.getInputProps('name')} />
            <Switch label={t('apiKeys.readOnly')} description={t('apiKeys.readOnlyHint')} {...form.getInputProps('readOnly', { type: 'checkbox' })} />
            <DateInput label={t('apiKeys.expires')} placeholder={t('apiKeys.never')} clearable minDate={dayjs().add(1, 'day').toDate()} valueFormat="DD MMM YYYY"
              {...form.getInputProps('expiresAt')} />
            <Group justify="flex-end"><Button variant="default" onClick={() => setOpened(false)}>{t('common.cancel')}</Button><Button type="submit">{t('apiKeys.make')}</Button></Group>
          </Stack></form>
        )}
      </Modal>
    </Card>
  );
}
