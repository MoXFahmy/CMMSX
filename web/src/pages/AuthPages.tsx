import { Alert, Anchor, Button, Center, Checkbox, Paper, PasswordInput, Stack, Text, TextInput, Title, Group, Loader } from '@mantine/core';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { LanguageSwitch } from '@/components/LanguageSwitch';
import { useEffect, useRef, useState } from 'react';
import { ApiError, get, post } from '@/api/client';
import { appVersionLabel, fromNow, handleFormError, notifyError } from '@/components/utils';
import { PASSWORD_EXPIRED_FLAG, signInStep } from './auth/signIn';

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
        <Group justify="center"><LanguageSwitch saveToAccount={false} /></Group>
        <Text c="dimmed" fz="xs" ta="center">{appVersionLabel(t)}</Text>
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

/** Whether a signed-in page sent the person here because their password expired (read once). */
function takeExpiredNotice(): boolean {
  try { const v = sessionStorage.getItem(PASSWORD_EXPIRED_FLAG) === '1'; sessionStorage.removeItem(PASSWORD_EXPIRED_FLAG); return v; } catch { return false; }
}

/** Asks for the confirmation link again (the server always answers ok, so nobody learns which addresses exist). */
export async function resendConfirmation(email: string): Promise<void> {
  await post('/auth/resend-confirmation', { email });
}

export type ConfirmResult = { ok: true } | { ok: false; invalid: boolean; message: string };

/** Spends the e-mailed token. A 400 means the link expired or was used; anything else (too many attempts, no signal) keeps the server's own words. */
export async function confirmToken(token: string): Promise<ConfirmResult> {
  try { await post('/auth/confirm-email', { token }); return { ok: true }; }
  catch (e) {
    const message = e instanceof Error ? e.message : String(e);
    return { ok: false, invalid: e instanceof ApiError && e.status === 400, message };
  }
}

/** "Send the link again", then "We sent a new link." Doesn't navigate; a refusal (too many attempts) shows as the usual toast. */
function ResendLink({ email }: { email: string }) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState(false);
  const resend = async () => {
    setBusy(true);
    try { await resendConfirmation(email); setSent(true); } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Stack gap="xs">
      {sent && <Alert color="teal" variant="light">{t('auth.linkSent')}</Alert>}
      <Button variant="default" loading={busy} disabled={!email} onClick={resend} fullWidth>{t('auth.resendLink')}</Button>
    </Stack>
  );
}

/** After registering in an organization that must confirm its e-mail first: no sign-in yet, just where the link went. */
export function CheckEmailPanel({ email }: { email: string }) {
  const { t } = useTranslation();
  return (
    <AuthFrame title={t('auth.checkEmailTitle')} subtitle={email}>
      <Stack>
        <Text>{t('auth.checkEmailText', { email })}</Text>
        <ResendLink email={email} />
        <Text fz="sm" ta="center"><Anchor component={Link} to="/login">{t('auth.signIn')}</Anchor></Text>
      </Stack>
    </AuthFrame>
  );
}

/** The e-mailed link's page: spends the token once, signs the person in and goes home. */
export function ConfirmEmailPage() {
  const { t } = useTranslation();
  const [params] = useSearchParams();
  const token = params.get('token') ?? '';
  const after = useAfterLogin();
  const navigate = useNavigate();
  const [failed, setFailed] = useState<string | null>(null);
  // React StrictMode runs effects twice in development; the token is single-use, so only the first run spends it.
  const started = useRef(false);
  useEffect(() => {
    if (started.current) return;
    started.current = true;
    if (!token) { setFailed(t('auth.linkInvalid')); return; }
    void confirmToken(token).then(async (r) => {
      if (r.ok) { await after(); navigate('/', { replace: true }); }
      else setFailed(r.invalid ? t('auth.linkInvalid') : r.message);
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  return (
    <AuthFrame title={t('auth.checkEmailTitle')} subtitle="">
      {failed === null ? (
        <Group justify="center" gap="sm"><Loader size="sm" /><Text>{t('auth.confirming')}</Text></Group>
      ) : (
        <Stack>
          <Alert color="red" variant="light">{failed}</Alert>
          <Text fz="sm" ta="center"><Anchor component={Link} to="/login">{t('auth.signIn')}</Anchor></Text>
        </Stack>
      )}
    </AuthFrame>
  );
}

type Credentials = { email: string; password: string; rememberMe: boolean };

export function LoginPage() {
  const { t } = useTranslation();
  const after = useAfterLogin();
  const registrationOpen = useRegistrationOpen();
  const [busy, setBusy] = useState(false);
  const [expired] = useState(takeExpiredNotice);
  // 0.25.0: a new password first (expired, or the organization's rules changed), or confirming another device's session.
  const [renew, setRenew] = useState<{ creds: Credentials; message: string } | null>(null);
  const [other, setOther] = useState<{ creds: Credentials; device: string; lastSeenAt?: string } | null>(null);
  // 0.29.0: the organization's e-mail isn't confirmed yet; no navigation, just a way to ask for the link again.
  const [unconfirmed, setUnconfirmed] = useState<string | null>(null);
  const form = useForm({ initialValues: { email: '', password: '', rememberMe: true } });
  const renewForm = useForm({
    initialValues: { newPassword: '', repeat: '' },
    validate: { repeat: (v, all) => (v === all.newPassword ? null : t('auth.passwordsDiffer')) },
  });
  // Signing in for the technician view: "Keep me signed in" then lasts 30 days, like the Android app.
  const technician = new URLSearchParams(window.location.search).get('next')?.startsWith('/tech') ?? false;

  /** What the server asked for next; true when it was handled here. */
  const next = (e: unknown, creds: Credentials): boolean => {
    if (!(e instanceof ApiError)) return false;
    const step = signInStep(e.status, e.body);
    const body = (e.body ?? {}) as { title?: string; device?: string; lastSeenAt?: string };
    if (step === 'renew') { setRenew({ creds, message: body.title ?? e.message }); return true; }
    if (step === 'unconfirmed') { setUnconfirmed(body.title ?? e.message); return true; }
    if (step === 'other-session') { setOther({ creds, device: body.device ?? '', lastSeenAt: body.lastSeenAt }); return true; }
    return false;
  };

  const signIn = async (creds: Credentials, endOtherSessions = false) => {
    setBusy(true);
    setUnconfirmed(null);
    try { await post('/auth/login', { ...creds, technician, endOtherSessions }); await after(); }
    catch (e) { if (!next(e, creds)) notifyError(e); }
    finally { setBusy(false); }
  };

  const submit = form.onSubmit((v) => signIn(v));
  const submitRenew = renewForm.onSubmit(async (v) => {
    if (!renew) return;
    setBusy(true);
    try {
      await post('/auth/renew-password', { ...renew.creds, newPassword: v.newPassword, technician, endOtherSessions: false });
      await after();
    } catch (e) {
      // The new password is saved before the one-session question, so confirming signs in with it.
      if (e instanceof ApiError && signInStep(e.status, e.body) === 'other-session') { setRenew(null); next(e, { ...renew.creds, password: v.newPassword }); }
      else handleFormError(renewForm, e);
    } finally { setBusy(false); }
  });

  if (other) {
    return (
      <AuthFrame title={t('auth.otherSessionTitle')} subtitle={t('auth.signInSubtitle')}>
        <Stack>
          <Text>{t('auth.otherSession', { device: other.device, when: fromNow(other.lastSeenAt) })}</Text>
          <Group grow>
            <Button variant="default" onClick={() => setOther(null)}>{t('common.cancel')}</Button>
            <Button loading={busy} onClick={() => signIn(other.creds, true)}>{t('auth.signInHere')}</Button>
          </Group>
        </Stack>
      </AuthFrame>
    );
  }

  if (renew) {
    return (
      <AuthFrame title={t('auth.renewTitle')} subtitle={renew.creds.email}>
        <form onSubmit={submitRenew}>
          <Stack>
            <Alert color="yellow" variant="light">{renew.message}</Alert>
            <PasswordInput label={t('auth.newPassword')} autoComplete="new-password" required data-autofocus {...renewForm.getInputProps('newPassword')} />
            <PasswordInput label={t('auth.repeatPassword')} autoComplete="new-password" required {...renewForm.getInputProps('repeat')} />
            <Group grow>
              <Button variant="default" onClick={() => { setRenew(null); renewForm.reset(); }}>{t('common.cancel')}</Button>
              <Button type="submit" loading={busy}>{t('auth.saveAndSignIn')}</Button>
            </Group>
          </Stack>
        </form>
      </AuthFrame>
    );
  }

  return (
    <AuthFrame title={t('auth.signInTitle')} subtitle={t('auth.signInSubtitle')}>
      <form onSubmit={submit}>
        <Stack>
          {unconfirmed && <Stack gap="xs"><Alert color="yellow" variant="light">{t('auth.unconfirmed')}</Alert><ResendLink email={form.values.email} /></Stack>}
          {expired && <Alert color="yellow" variant="light">{t('auth.passwordExpiredNotice')}</Alert>}
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
  const [checkEmail, setCheckEmail] = useState<string | null>(null);
  const form = useForm({ initialValues: { organizationName: '', fullName: '', email: '', password: '' } });
  const submit = form.onSubmit(async (v) => {
    setBusy(true);
    try {
      const r = await post<{ ok: boolean; confirm?: boolean }>('/auth/register', v);
      if (r?.confirm) setCheckEmail(v.email); else await after();
    } catch (e) { handleFormError(form, e); } finally { setBusy(false); }
  });
  if (checkEmail) return <CheckEmailPanel email={checkEmail} />;
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
          <PasswordInput label={t('auth.password')} autoComplete="new-password" required description={t('auth.passwordRule')} {...form.getInputProps('password')} />
          <Button type="submit" loading={busy} fullWidth>{t('auth.register')}</Button>
          <Text fz="sm" ta="center">{t('auth.haveAccount')} <Anchor component={Link} to="/login">{t('auth.signIn')}</Anchor></Text>
        </Stack>
      </form>
      )}
    </AuthFrame>
  );
}
