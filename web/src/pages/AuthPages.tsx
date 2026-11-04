import { Alert, Anchor, Button, Center, Checkbox, Paper, PasswordInput, Stack, Text, TextInput, Title, Group } from '@mantine/core';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { get, post } from '@/api/client';
import { handleFormError, notifyError } from '@/components/utils';

function AuthFrame({ title, subtitle, children }: { title: string; subtitle: string; children: React.ReactNode }) {
  const { t } = useTranslation();
  return (
    <Center mih="100vh" p="md" style={{ background: 'linear-gradient(135deg, var(--mantine-color-brand-0), var(--mantine-color-gray-0))' }}>
      <Stack w="100%" maw={420} gap="lg">
        <Group gap="sm" justify="center"><img src="/favicon.svg" width={40} height={40} alt="" /><Title order={2}>{t('app.name')}</Title></Group>
        <Paper withBorder shadow="sm" p="xl">
          <Stack gap={4} mb="lg"><Title order={3}>{title}</Title><Text c="dimmed" fz="sm">{subtitle}</Text></Stack>
          {children}
        </Paper>
      </Stack>
    </Center>
  );
}

/** Whether this server lets visitors create a new organization (operators can switch it off). */
function useRegistrationOpen() {
  const q = useQuery({ queryKey: ['auth-options'], queryFn: () => get<{ allowRegistration: boolean }>('/auth/options'), staleTime: Infinity });
  return q.data?.allowRegistration;
}

function useAfterLogin() {
  const qc = useQueryClient();
  // Re-fetching the profile switches the app to the signed-in routes; <AfterLogin> then honours ?next=.
  return async () => { await qc.invalidateQueries({ queryKey: ['me'] }); };
}

export function LoginPage() {
  const { t } = useTranslation();
  const after = useAfterLogin();
  const registrationOpen = useRegistrationOpen();
  const [busy, setBusy] = useState(false);
  const form = useForm({ initialValues: { email: '', password: '', rememberMe: true } });
  const submit = form.onSubmit(async (v) => {
    setBusy(true);
    try { await post('/auth/login', v); await after(); } catch (e) { notifyError(e); } finally { setBusy(false); }
  });
  return (
    <AuthFrame title={t('auth.signInTitle')} subtitle={t('auth.signInSubtitle')}>
      <form onSubmit={submit}>
        <Stack>
          <TextInput label={t('auth.email')} type="email" autoComplete="username" required {...form.getInputProps('email')} />
          <PasswordInput label={t('auth.password')} autoComplete="current-password" required {...form.getInputProps('password')} />
          <Checkbox label={t('auth.rememberMe')} {...form.getInputProps('rememberMe', { type: 'checkbox' })} />
          <Button type="submit" loading={busy} fullWidth>{t('auth.signIn')}</Button>
          {registrationOpen && <Text fz="sm" ta="center">{t('auth.noAccount')} <Anchor component={Link} to="/register">{t('auth.createOrg')}</Anchor></Text>}
        </Stack>
      </form>
    </AuthFrame>
  );
}

export function RegisterPage() {
  const { t } = useTranslation();
  const after = useAfterLogin();
  const registrationOpen = useRegistrationOpen();
  const [busy, setBusy] = useState(false);
  const form = useForm({ initialValues: { organizationName: '', fullName: '', email: '', password: '' } });
  const submit = form.onSubmit(async (v) => {
    setBusy(true);
    try { await post('/auth/register', v); await after(); } catch (e) { handleFormError(form, e); } finally { setBusy(false); }
  });
  return (
    <AuthFrame title={t('auth.registerTitle')} subtitle={t('auth.registerSubtitle')}>
      {registrationOpen === false ? (
        <Stack>
          <Alert color="gray">{t('auth.registrationClosed')}</Alert>
          <Text fz="sm" ta="center">{t('auth.haveAccount')} <Anchor component={Link} to="/login">{t('auth.signIn')}</Anchor></Text>
        </Stack>
      ) : (
      <form onSubmit={submit}>
        <Stack>
          <TextInput label={t('auth.organizationName')} required {...form.getInputProps('organizationName')} />
          <TextInput label={t('auth.fullName')} autoComplete="name" required {...form.getInputProps('fullName')} />
          <TextInput label={t('auth.email')} type="email" autoComplete="username" required {...form.getInputProps('email')} />
          <PasswordInput label={t('auth.password')} autoComplete="new-password" required description="8+ characters with upper, lower case and a digit" {...form.getInputProps('password')} />
          <Button type="submit" loading={busy} fullWidth>{t('auth.register')}</Button>
          <Text fz="sm" ta="center">{t('auth.haveAccount')} <Anchor component={Link} to="/login">{t('auth.signIn')}</Anchor></Text>
        </Stack>
      </form>
      )}
    </AuthFrame>
  );
}
