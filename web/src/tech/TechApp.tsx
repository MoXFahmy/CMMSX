import { useEffect, useMemo, useRef, useState } from 'react';
import { ActionIcon, Alert, Anchor, Badge, Box, Button, Card, Center, Group, Indicator, Loader, Stack, Switch, Text, TextInput, Title, UnstyledButton } from '@mantine/core';
import { modals } from '@mantine/modals';
import { Link, Route, Routes, useLocation, useNavigate } from 'react-router';
import { changeLabelKey } from './changeLabel';
import { CountPage, PartPage, ReceivePage, StockSearchPage } from './StockPages';
import { NewWorkOrderPage } from './JobExtras';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { notifications } from '@mantine/notifications';
import { IconClock, IconCloudOff, IconCloudCheck, IconFingerprint, IconList, IconLogout, IconPackage, IconRefresh, IconScan, IconSearch, IconTransferVertical } from '@tabler/icons-react';
import { BIOMETRIC_KEY, biometricAvailable, registerPush, shouldLock, verifyBiometric } from './native';
import { sendPushToken } from './api';
import { PriorityBadge, WoStatusBadge } from '@/components/WorkBadges';
import { fmtDateTime, fromNow } from '@/components/utils';
import { LanguageSwitch } from '@/components/LanguageSwitch';
import { dueLabel } from './due';
import { JobPage } from './JobPage';
import { DayPage } from './DayPage';
import { PlacePage } from './PlacePage';
import { ScanPage } from './ScanPage';
import { MODE_KEY, TechProvider, useTech, type Phase } from './useTech';
import { FILE_TRIES, type QueuedChange } from './outbox';
import type { TechJob } from './types';

/** Fingerprint/face lock (0.11.0): locked when the app opens or comes back after 5 minutes, if switched on and the phone can. */
function useLock(ready: boolean) {
  const [locked, setLocked] = useState(false);
  const hiddenAt = useRef<number | null>(null);
  const enabled = () => { try { return localStorage.getItem(BIOMETRIC_KEY) === '1'; } catch { return false; } };
  useEffect(() => {
    if (!ready || !enabled()) return;
    void biometricAvailable().then((ok) => { if (ok && shouldLock(true, null, Date.now())) setLocked(true); });
    const onVisibility = () => {
      if (document.hidden) { hiddenAt.current = Date.now(); return; }
      if (enabled() && shouldLock(true, hiddenAt.current, Date.now())) void biometricAvailable().then((ok) => { if (ok) setLocked(true); });
    };
    document.addEventListener('visibilitychange', onVisibility);
    return () => document.removeEventListener('visibilitychange', onVisibility);
  }, [ready]);
  return { locked, unlock: () => { hiddenAt.current = null; setLocked(false); } };
}

/** Push notifications (0.11.0): register once per app start inside the new Android app; a tap opens the job. */
function usePush(ready: boolean) {
  const navigate = useNavigate();
  const done = useRef(false);
  useEffect(() => {
    if (!ready || done.current) return;
    done.current = true;
    void registerPush((token) => void sendPushToken(token), (link) => navigate(link));
  }, [ready, navigate]);
}

function LockScreen({ onUnlock }: { onUnlock: () => void }) {
  const { t } = useTranslation();
  const { signOut } = useTech();
  const tryUnlock = async () => { if (await verifyBiometric(t('techLock.reason'))) onUnlock(); };
  useEffect(() => { void tryUnlock(); }, []); // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <Center mih="100vh" p="md">
      <Stack align="center" maw={320}>
        <IconFingerprint size={56} />
        <Title order={4} ta="center">{t('techLock.title')}</Title>
        <Button fullWidth onClick={() => void tryUnlock()}>{t('techLock.unlock')}</Button>
        <Button fullWidth variant="subtle" onClick={() => void signOut()}>{t('tech.signOut')}</Button>
      </Stack>
    </Center>
  );
}

/** "Unlock with fingerprint or face" (0.11.0): only shown inside the new app on a phone that can. */
function BiometricSetting() {
  const { t } = useTranslation();
  const [can, setCan] = useState(false);
  const [on, setOn] = useState(() => { try { return localStorage.getItem(BIOMETRIC_KEY) === '1'; } catch { return false; } });
  useEffect(() => { void biometricAvailable().then(setCan); }, []);
  if (!can) return null;
  const change = async (next: boolean) => {
    // Turning it on asks once, so nobody locks themselves out with a sensor that doesn't work.
    if (next && !(await verifyBiometric(t('techLock.reason')))) return;
    try { localStorage.setItem(BIOMETRIC_KEY, next ? '1' : '0'); } catch { /* the setting just won't stick */ }
    setOn(next);
  };
  return <Switch label={t('techLock.setting')} description={t('techLock.settingHint')} checked={on} onChange={(e) => void change(e.currentTarget.checked)} />;
}

/** The technician view (/tech): works from the phone's copy and sends changes when there is signal. */
export function TechApp() {
  return (
    <TechProvider>
      <TechShell />
    </TechProvider>
  );
}

function TechShell() {
  const { phase } = useTech();
  const locked = useLock(phase === 'ready');
  usePush(phase === 'ready');
  if (phase !== 'ready') return <PhaseScreen phase={phase} />;
  if (locked.locked) return <LockScreen onUnlock={locked.unlock} />;
  return (
    <Box mih="100vh" pb={72} bg="var(--mantine-color-gray-0)">
      <Header />
      <Box p="sm" maw={720} mx="auto">
        <Routes>
          <Route path="/tech" element={<MyJobsPage />} />
          <Route path="/tech/jobs/:id" element={<JobPage />} />
          <Route path="/tech/sync" element={<SyncPage />} />
          <Route path="/tech/day" element={<DayPage />} />
          <Route path="/tech/scan" element={<ScanPage />} />
          <Route path="/tech/place/:kind/:id" element={<PlacePage />} />
          <Route path="/tech/stock" element={<StockSearchPage />} />
          <Route path="/tech/parts/:id" element={<PartPage />} />
          <Route path="/tech/counts/:id" element={<CountPage />} />
          <Route path="/tech/receive" element={<ReceivePage />} />
          <Route path="/tech/new-work-order" element={<NewWorkOrderPage />} />
          <Route path="*" element={<MyJobsPage />} />
        </Routes>
      </Box>
      <BottomNav />
    </Box>
  );
}

function Header() {
  const { t } = useTranslation();
  const { fullName, online, queue, signOut } = useTech();
  const waiting = queue.length;
  const confirmSignOut = () => modals.openConfirmModal({
    title: t('tech.signOut'),
    children: <Text fz="sm">{waiting > 0 ? t('tech.signOutWaiting', { count: waiting }) : t('tech.signOutHint')}</Text>,
    labels: { confirm: t('tech.signOut'), cancel: t('common.cancel') },
    confirmProps: { color: 'red' },
    onConfirm: () => { void signOut().then((ok) => { if (!ok) notifications.show({ color: 'orange', message: t('tech.signOutNeedsSignal') }); }); },
  });
  return (
    <Group justify="space-between" px="sm" py={8} bg="white" style={{ borderBottom: '1px solid var(--mantine-color-gray-3)', position: 'sticky', top: 0, zIndex: 10 }}>
      <div>
        <Text fw={700} fz="sm">{t('app.name')}</Text>
        <Text fz="xs" c="dimmed">{fullName}</Text>
      </div>
      <Group gap="xs">
        <Badge variant="light" color={online ? 'teal' : 'gray'} leftSection={online ? <IconCloudCheck size={12} /> : <IconCloudOff size={12} />}>
          {online ? t('tech.online') : t('tech.offline')}
        </Badge>
        <Indicator label={waiting} size={16} disabled={waiting === 0} color={queue.some((c) => c.state === 'failed') ? 'red' : 'orange'}>
          <ActionIcon component={Link} to="/tech/sync" variant="default" aria-label={t('tech.sync')}><IconTransferVertical size={16} /></ActionIcon>
        </Indicator>
        {/* The full app needs a connection; without one it would only fail. */}
        {online && <Anchor fz="xs" href="/" onClick={() => { try { localStorage.setItem(MODE_KEY, 'full'); } catch { /* private mode */ } }}>{t('tech.fullApp')}</Anchor>}
        <ActionIcon variant="subtle" color="gray" onClick={confirmSignOut} aria-label={t('tech.signOut')}><IconLogout size={16} /></ActionIcon>
      </Group>
    </Group>
  );
}

function BottomNav() {
  const { t } = useTranslation();
  const { pathname } = useLocation();
  const items = [
    { to: '/tech', icon: IconList, label: t('tech.myJobs'), active: pathname === '/tech' || pathname === '/tech/' || pathname.startsWith('/tech/jobs') },
    { to: '/tech/scan', icon: IconScan, label: t('tech.scan'), active: pathname.startsWith('/tech/scan') || pathname.startsWith('/tech/place') },
    { to: '/tech/sync', icon: IconTransferVertical, label: t('tech.sync'), active: pathname.startsWith('/tech/sync') },
  ];
  return (
    <Group grow gap={0} bg="white" style={{ position: 'fixed', bottom: 0, left: 0, right: 0, borderTop: '1px solid var(--mantine-color-gray-3)', zIndex: 10 }}>
      {items.map((i) => (
        <UnstyledButton key={i.to} component={Link} to={i.to} py={8}>
          <Stack gap={0} align="center"><i.icon size={20} color={i.active ? 'var(--mantine-color-blue-6)' : undefined} /><Text fz={11} c={i.active ? 'blue' : 'dimmed'}>{i.label}</Text></Stack>
        </UnstyledButton>
      ))}
    </Group>
  );
}

function PhaseScreen({ phase }: { phase: Phase }) {
  const { t } = useTranslation();
  const { queue } = useTech();
  if (phase === 'starting') return <Center h="100vh"><Loader /></Center>;
  const text: Record<Exclude<Phase, 'starting' | 'ready'>, string> = {
    noCopy: t('tech.noCopy'), locked: t('tech.locked'), revoked: t('tech.revoked'), signedOut: t('tech.signedOutBody'), passwordExpired: t('tech.passwordExpired'),
  };
  return (
    <Center mih="100vh" p="md">
      <Card withBorder maw={420}>
        <Stack>
          <Title order={4}>{t('tech.title')}</Title>
          <Text fz="sm">{text[phase as keyof typeof text]}</Text>
          {queue.length > 0 && <Alert color="orange">{t('tech.queueKept', { count: queue.length })}</Alert>}
          <Group grow>
            <Button variant="default" onClick={() => window.location.reload()}>{t('tech.tryAgain')}</Button>
            <Button component="a" href="/login?next=%2Ftech%2F">{t('tech.signIn')}</Button>
          </Group>
        </Stack>
      </Card>
    </Center>
  );
}

/** Overdue, today, the next 7 days, then the rest (later or no due date). */
function groupOf(j: TechJob): 'overdue' | 'today' | 'week' | 'later' {
  if (!j.dueDate) return 'later';
  const d = dayjs(j.dueDate).startOf('day');
  const today = dayjs().startOf('day');
  if (d.isBefore(today)) return 'overdue';
  if (d.isSame(today)) return 'today';
  return d.isBefore(today.add(8, 'day')) ? 'week' : 'later';
}

function MyJobsPage() {
  const { t } = useTranslation();
  const { bundle, queue, lastRefresh, problem } = useTech();
  const [search, setSearch] = useState('');
  const navigate = useNavigate();
  const pending = useMemo(() => new Set(queue.map((c) => c.workOrderId).filter(Boolean)), [queue]);
  const jobs = useMemo(() => {
    const s = search.trim().toLowerCase();
    return (bundle?.jobs ?? []).filter((j) => !s || [j.number, j.title, j.asset?.assetNumber, j.asset?.name].some((x) => x?.toLowerCase().includes(s)));
  }, [bundle, search]);
  const groups = (['overdue', 'today', 'week', 'later'] as const).map((g) => ({ g, items: jobs.filter((j) => groupOf(j) === g) })).filter((x) => x.items.length > 0);
  return (
    <Stack gap="sm">
      {problem && <Alert color="orange" p="xs">{problem === 'server' ? t('tech.refreshServerProblem') : problem}</Alert>}
      <Group gap="xs" wrap="nowrap">
        <TextInput style={{ flex: 1 }} leftSection={<IconSearch size={16} />} placeholder={t('tech.searchJobs')} value={search} onChange={(e) => setSearch(e.currentTarget.value)} />
        <Button variant="light" leftSection={<IconClock size={16} />} onClick={() => navigate('/tech/day')}>{t('techDay.title')}</Button>
        <Button variant="light" leftSection={<IconPackage size={16} />} onClick={() => navigate('/tech/stock')}>{t('techStock.stock')}</Button>
      </Group>
      {groups.length === 0 && <Text c="dimmed" ta="center" py="xl">{t('tech.noJobs')}</Text>}
      {groups.map(({ g, items }) => (
        <Stack key={g} gap={6}>
          <Text fz="xs" fw={700} tt="uppercase" c={g === 'overdue' ? 'red' : 'dimmed'}>{t(`tech.group.${g}`)} · {items.length}</Text>
          {items.map((j) => (
            <Card key={j.id} withBorder p="sm" onClick={() => navigate(`/tech/jobs/${j.id}`)} style={{ cursor: 'pointer' }}>
              <Group justify="space-between" wrap="nowrap" align="flex-start">
                <div style={{ minWidth: 0 }}>
                  <Group gap={6}><Text fz="xs" c="dimmed">{j.number}</Text>{pending.has(j.id) && <Badge size="xs" color="orange" variant="light">{t('tech.unsent')}</Badge>}</Group>
                  <Text fw={600} lineClamp={2}>{j.title}</Text>
                  {j.asset && <Text fz="xs" c="dimmed" lineClamp={1}>{j.asset.assetNumber} · {j.asset.name}</Text>}
                </div>
                <Stack gap={4} align="flex-end">
                  <PriorityBadge value={j.priority} />
                  <WoStatusBadge status={j.status} />
                  <Text fz="xs" c={g === 'overdue' ? 'red' : 'dimmed'}>{dueLabel(j.dueDate, j.dueAt) ?? '—'}</Text>
                </Stack>
              </Group>
            </Card>
          ))}
        </Stack>
      ))}
      {bundle?.can?.createWorkOrders && <Anchor fz="sm" ta="center" onClick={() => navigate('/tech/new-work-order')}>{t('techJob.newWorkOrder')}</Anchor>}
      {lastRefresh && <Text fz="xs" c="dimmed" ta="center">{t('tech.updated', { when: fromNow(new Date(lastRefresh).toISOString()) })}</Text>}
    </Stack>
  );
}

function SyncPage() {
  const { t } = useTranslation();
  const { queue, bundle, online, lastRefresh, notes, busy, problem, refresh, drainNow, retry, discard } = useTech();
  const jobNo = (id?: string) => bundle?.jobs.find((j) => j.id === id)?.number ?? '';
  const sorted = [...queue].sort((a, b) => a.seq - b.seq);
  const confirmDiscard = (c: QueuedChange) => modals.openConfirmModal({
    title: t('tech.discardTitle'), children: <Text fz="sm">{t('tech.discardBody')}</Text>,
    labels: { confirm: t('tech.discard'), cancel: t('common.cancel') }, confirmProps: { color: 'red' }, onConfirm: () => { void discard(c); },
  });
  return (
    <Stack gap="sm">
      <BiometricSetting />
      <Card withBorder p="sm">
        <Group justify="space-between">
          <div>
            <Text fw={600}>{online ? t('tech.online') : t('tech.offline')}</Text>
            <Text fz="xs" c="dimmed">{lastRefresh ? t('tech.lastRefresh', { when: fmtDateTime(new Date(lastRefresh).toISOString()) }) : t('tech.neverRefreshed')}</Text>
          </div>
          <Button size="xs" variant="default" leftSection={<IconRefresh size={14} />} loading={busy} onClick={() => { void drainNow(true); void refresh(); }}>{t('tech.refreshNow')}</Button>
        </Group>
        {online && <Group mt="xs" justify="space-between"><Text fz="sm">{t('common.language')}</Text><LanguageSwitch onChanged={() => void refresh()} /></Group>}
        {problem && <Alert color="orange" mt="xs" p="xs">{problem === 'server' ? t('tech.refreshServerProblem') : problem}</Alert>}
      </Card>
      <Text fz="xs" fw={700} tt="uppercase" c="dimmed">{t('tech.waiting')} · {sorted.length}</Text>
      {sorted.length === 0 && <Text c="dimmed" fz="sm" ta="center" py="md">{t('tech.allSent')}</Text>}
      {sorted.map((c) => (
        <Card key={c.clientId} withBorder p="sm">
          <Group justify="space-between" wrap="nowrap" align="flex-start">
            <div style={{ minWidth: 0 }}>
              <Text fz="sm" fw={600}>{t(changeLabelKey(c), { defaultValue: c.type })} {jobNo(c.workOrderId)}</Text>
              <Text fz="xs" c="dimmed">{fmtDateTime(c.at)}</Text>
              {c.state === 'failed' && <Text fz="xs" c="red">{c.code === 'tooSlow' ? t('tech.tooSlow', { tries: FILE_TRIES }) : c.message}</Text>}
            </div>
            <Badge color={c.state === 'failed' ? 'red' : c.state === 'sending' ? 'blue' : 'orange'} variant="light">{t(`tech.state.${c.state}`)}</Badge>
          </Group>
          {c.state === 'failed' && (
            <Group gap="xs" mt="xs">
              {c.code === 'conflict'
                ? <Button size="xs" onClick={() => void retry(c, true)}>{t('tech.applyMine')}</Button>
                : <Button size="xs" variant="light" onClick={() => void retry(c)}>{t('tech.retry')}</Button>}
              <Button size="xs" variant="subtle" color="red" onClick={() => confirmDiscard(c)}>{t('tech.discard')}</Button>
            </Group>
          )}
        </Card>
      ))}
      {notes.length > 0 && (
        <>
          <Text fz="xs" fw={700} tt="uppercase" c="dimmed">{t('tech.notes')}</Text>
          {notes.map((n, i) => <Alert key={i} p="xs" color="blue"><Text fz="xs">{fmtDateTime(n.at)} · {n.text}</Text></Alert>)}
        </>
      )}
    </Stack>
  );
}
