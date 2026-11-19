import { Alert, Badge, Button, Card, Group, Stack, Switch, Table, Text, Title, Tooltip } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { IconMail, IconRefresh, IconSend } from '@tabler/icons-react';
import { get, post, put } from '@/api/client';
import type { EmailLogItem, EmailStatusInfo, TenantSettings } from '@/api/types';
import { fmtDateTime, notifyError, notifySuccess } from '@/components/utils';

/** i18n key for an e-mail event ("request.submitted" → "request_submitted"; dots separate i18n keys). */
export const eventKey = (key: string) => key.replace('.', '_');

const STATUS_COLOR: Record<EmailLogItem['status'], string> = { Pending: 'blue', Sent: 'teal', Failed: 'red' };

/** Settings › Notifications: switch e-mail on, choose which e-mails go out, send a test and read the log. */
export function NotificationSettings() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data: status } = useQuery({ queryKey: ['email-status'], queryFn: () => get<EmailStatusInfo>('/email/status') });
  const { data: org } = useQuery({ queryKey: ['settings'], queryFn: () => get<{ name: string; slug: string; settings: TenantSettings }>('/settings') });
  const { data: log, refetch, isFetching } = useQuery({ queryKey: ['email-log'], queryFn: () => get<EmailLogItem[]>('/email/log') });
  if (!status || !org) return null;
  const s = org.settings;

  const save = async (patch: Partial<TenantSettings>) => {
    try {
      await put('/settings', { name: org.name, settings: { ...s, ...patch } });
      notifySuccess(t('common.saved'));
      await qc.invalidateQueries({ queryKey: ['settings'] });
      await qc.invalidateQueries({ queryKey: ['email-status'] });
    } catch (e) { notifyError(e); }
  };
  const toggleEvent = (key: string, on: boolean) =>
    save({ disabledEmails: on ? (s.disabledEmails ?? []).filter((k) => k !== key) : [...(s.disabledEmails ?? []), key] });
  const sendTest = async () => {
    try { await post('/email/test'); notifySuccess(t('email.testQueued')); setTimeout(() => void refetch(), 3000); } catch (e) { notifyError(e); }
  };

  return (
    <Stack maw={900}>
      {!status.available
        ? <Alert color="yellow" icon={<IconMail size={18} />} title={t('email.notSetUpTitle')}>{t('email.notSetUpBody')}</Alert>
        : !status.allowed
          ? <Alert color="yellow" icon={<IconMail size={18} />} title={t('email.notAllowedTitle')}>{t('email.notAllowedBody', { slug: status.slug })}</Alert>
          : <Alert color="teal" icon={<IconMail size={18} />}>{t('email.sendingFrom', { from: status.from })}</Alert>}
      <Card><Stack>
        <Group justify="space-between" align="flex-start">
          <div><Title order={5}>{t('email.title')}</Title><Text c="dimmed" fz="sm">{t('email.hint')}</Text></div>
          <Button variant="light" leftSection={<IconSend size={16} />} onClick={sendTest} disabled={!status.allowed}>{t('email.sendTest')}</Button>
        </Group>
        <Switch label={t('email.enabled')} description={t('email.enabledHint')} checked={s.emailEnabled} disabled={!status.allowed}
          onChange={(e) => void save({ emailEnabled: e.currentTarget.checked })} />
      </Stack></Card>
      <Card><Stack gap="sm">
        <Title order={5}>{t('email.which')}</Title>
        {/* "Send to supplier" is a button someone presses, not an automatic e-mail, so it has no switch. */}
        {status.events.filter((key) => key !== 'po.sent').map((key) => (
          <Switch key={key} label={t(`email.events.${eventKey(key)}.label`)} description={t(`email.events.${eventKey(key)}.description`)}
            checked={!(s.disabledEmails ?? []).includes(key)} disabled={!s.emailEnabled} onChange={(e) => void toggleEvent(key, e.currentTarget.checked)} />
        ))}
      </Stack></Card>
      <Card p={0}>
        <Group justify="space-between" px="md" py="sm">
          <Title order={5}>{t('email.log')}</Title>
          <Tooltip label={t('common.refresh')}><Button variant="subtle" size="xs" leftSection={<IconRefresh size={14} />} loading={isFetching} onClick={() => void refetch()}>{t('common.refresh')}</Button></Tooltip>
        </Group>
        <Table.ScrollContainer minWidth={700}>
          <Table verticalSpacing={6} striped>
            <Table.Thead><Table.Tr><Table.Th>{t('email.when')}</Table.Th><Table.Th>{t('email.to')}</Table.Th><Table.Th>{t('email.subject')}</Table.Th><Table.Th>{t('common.status')}</Table.Th></Table.Tr></Table.Thead>
            <Table.Tbody>
              {(log ?? []).length === 0 && <Table.Tr><Table.Td colSpan={4}><Text c="dimmed" ta="center" py="md">{t('email.logEmpty')}</Text></Table.Td></Table.Tr>}
              {(log ?? []).map((e) => (
                <Table.Tr key={e.id}>
                  <Table.Td><Text fz="sm">{fmtDateTime(e.sentAt ?? e.createdAt)}</Text></Table.Td>
                  <Table.Td><Text fz="sm">{e.toAddress}</Text></Table.Td>
                  <Table.Td><Text fz="sm" lineClamp={1}>{e.subject}</Text></Table.Td>
                  <Table.Td>
                    <Badge variant="light" color={STATUS_COLOR[e.status]}>{t(`email.status.${e.status}`)}</Badge>
                    {e.lastError && <Text fz="xs" c={e.status === 'Failed' ? 'red' : 'dimmed'} lineClamp={2}>{t('email.attempts', { count: e.attempts })} · {e.lastError}</Text>}
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      </Card>
    </Stack>
  );
}
