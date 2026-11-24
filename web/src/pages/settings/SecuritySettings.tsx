import { useEffect } from 'react';
import { Button, Card, Group, NumberInput, Stack, Switch, Text, Title } from '@mantine/core';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api, get, put } from '@/api/client';
import type { SecuritySettings as Security } from '@/api/types';
import { fromNow, handleFormError, notifySuccess } from '@/components/utils';
import { DEFAULT_SECURITY, securityProblems } from '@/pages/auth/signIn';

type Key = 'passwordMinLength' | 'passwordExpiryDays' | 'passwordHistory' | 'oneSessionPerUser' | 'activityRetentionMonths';
const KEYS: Key[] = ['passwordMinLength', 'passwordExpiryDays', 'passwordHistory', 'oneSessionPerUser', 'activityRetentionMonths'];

type LastChanged = { key: string; userName: string | null; timestamp: string };

/** Settings › Security (0.25.0): password rules, one session per person and how long the activity log keeps changes. */
export function SecuritySettings({ editable }: { editable: boolean }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data } = useQuery({ queryKey: ['security-settings'], queryFn: () => get<Security>('/settings/security') });
  const { data: last } = useQuery({
    queryKey: ['security-last-changed'],
    queryFn: () => api<LastChanged[]>(`/activity/last-changed?entityType=Tenant&${KEYS.map((k) => `keys=security.${k}`).join('&')}`),
  });
  const form = useForm<Security>({
    initialValues: DEFAULT_SECURITY,
    validate: (v: Security) => Object.fromEntries(Object.entries(securityProblems(v)).map(([k, key]) => [k, t(key ?? '')])),
  });
  useEffect(() => { if (data) { form.setValues(data); form.resetDirty(data); } }, [data]); // eslint-disable-line react-hooks/exhaustive-deps

  /** "Last changed by Admin, 3 days ago" under a setting someone changed. */
  const changed = (key: Key) => {
    const row = last?.find((r) => r.key === `security.${key}`);
    return row ? t('security.lastChanged', { name: row.userName ?? '—', when: fromNow(row.timestamp) }) : undefined;
  };

  const submit = form.onSubmit(async (v) => {
    try {
      await put('/settings/security', v);
      notifySuccess(t('common.saved'));
      await qc.invalidateQueries({ queryKey: ['security-settings'] });
      await qc.invalidateQueries({ queryKey: ['security-last-changed'] });
    } catch (e) { handleFormError(form, e); }
  });

  if (!data) return null;
  const hint = (text: string, key: Key) => <>{text}{changed(key) && <><br />{changed(key)}</>}</>;
  return (
    <form onSubmit={submit}>
      <Stack maw={640}>
        <Card><Stack>
          <Title order={5}>{t('security.passwords')}</Title>
          <NumberInput w={260} label={t('security.minLength')} description={hint(t('security.minLengthHint'), 'passwordMinLength')} min={8} max={64} allowDecimal={false}
            disabled={!editable} {...form.getInputProps('passwordMinLength')} />
          <NumberInput w={260} label={t('security.expiryDays')} description={hint(t('security.expiryHint'), 'passwordExpiryDays')} min={0} max={730} allowDecimal={false}
            disabled={!editable} {...form.getInputProps('passwordExpiryDays')} />
          <NumberInput w={260} label={t('security.history')} description={hint(t('security.historyHint'), 'passwordHistory')} min={0} max={24} allowDecimal={false}
            disabled={!editable} {...form.getInputProps('passwordHistory')} />
        </Stack></Card>
        <Card><Stack>
          <Title order={5}>{t('security.sessions')}</Title>
          <Switch label={t('security.oneSession')} description={hint(t('security.oneSessionHint'), 'oneSessionPerUser')} disabled={!editable}
            {...form.getInputProps('oneSessionPerUser', { type: 'checkbox' })} />
        </Stack></Card>
        <Card><Stack>
          <Title order={5}>{t('security.activityLog')}</Title>
          <NumberInput w={260} label={t('security.retention')} description={hint(t('security.retentionHint'), 'activityRetentionMonths')} min={12} max={120} allowDecimal={false}
            placeholder={t('security.keepEverything')} disabled={!editable} value={form.values.activityRetentionMonths ?? ''} error={form.errors.activityRetentionMonths}
            onChange={(v) => form.setFieldValue('activityRetentionMonths', v === '' ? null : Number(v))} />
          <Text c="dimmed" fz="sm">{t('security.retentionNote')}</Text>
        </Stack></Card>
        {editable && <Group><Button type="submit" disabled={!form.isDirty()}>{t('common.save')}</Button></Group>}
      </Stack>
    </form>
  );
}
