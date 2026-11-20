import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Card, Center, Group, Loader, Stack, Text, ThemeIcon, Title } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { IconCamera, IconCircleCheck, IconRefresh, IconWifiOff } from '@tabler/icons-react';

/** Keeps the app's files on the phone for /tech (see public/tech-sw.js). Office pages are outside its scope. */
export function registerTechWorker(): Promise<ServiceWorkerRegistration | undefined> {
  if (!('serviceWorker' in navigator)) return Promise.resolve(undefined);
  return navigator.serviceWorker.register('/tech-sw.js', { scope: '/tech' }).catch(() => undefined);
}

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

/** Whether the server answers within a few seconds (no signal, or too slow to wait for, counts as offline). */
async function reachable(): Promise<boolean> {
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), 4000);
  try { return (await fetch('/api/health', { cache: 'no-store', signal: ctrl.signal })).ok; } catch { return false; } finally { clearTimeout(timer); }
}

/**
 * The start of the technician view (0.3.1): the Android app opens here. With a connection it keeps the app on the phone
 * and goes straight on to the full app; with none, it opens from the phone's copy and says so, with a camera test.
 * Add ?check to the address to stay on this page while online.
 */
export function TechCheckPage() {
  const { t } = useTranslation();
  const [state, setState] = useState<'checking' | 'online' | 'offline'>('checking');
  const [camera, setCamera] = useState<{ ok: boolean; message: string } | null>(null);
  const video = useRef<HTMLVideoElement>(null);
  const stream = useRef<MediaStream | null>(null);

  useEffect(() => {
    let alive = true;
    (async () => {
      const registered = registerTechWorker();
      const online = await reachable();
      if (!alive) return;
      if (online && !new URLSearchParams(window.location.search).has('check')) {
        // Give the phone a moment to keep its copy (it carries on in the background), then open the full app.
        await Promise.race([registered.then(() => navigator.serviceWorker?.ready), sleep(5000)]);
        window.location.replace('/');
        return;
      }
      setState(online ? 'online' : 'offline');
    })();
    return () => { alive = false; stream.current?.getTracks().forEach((x) => x.stop()); };
  }, []);

  const testCamera = async () => {
    try {
      stream.current?.getTracks().forEach((x) => x.stop());
      stream.current = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } } });
      if (video.current) { video.current.srcObject = stream.current; await video.current.play(); }
      setCamera({ ok: true, message: t('techCheck.cameraWorks') });
    } catch (e) {
      const err = e as { name?: string; message?: string };
      setCamera({ ok: false, message: `${err.name ?? 'Error'}: ${err.message ?? ''}` });
    }
  };

  if (state === 'checking') return <Center h="100vh"><Loader /></Center>;
  return (
    <Center mih="100vh" p="md">
      <Card withBorder maw={440} w="100%">
        <Stack>
          <Group gap="sm" wrap="nowrap">
            <ThemeIcon size={40} radius="xl" color="teal" variant="light">{state === 'offline' ? <IconWifiOff size={22} /> : <IconCircleCheck size={22} />}</ThemeIcon>
            <div>
              <Title order={4}>{t('techCheck.title')}</Title>
              <Text fz="xs" c="dimmed">{t('techCheck.version', { version: __APP_VERSION__ })}</Text>
            </div>
          </Group>
          {state === 'offline'
            ? <Alert color="teal" title={t('techCheck.passed')}>{t('techCheck.passedBody')}</Alert>
            : <Alert color="blue">{t('techCheck.onlineBody')}</Alert>}
          <Button variant="light" leftSection={<IconCamera size={16} />} onClick={testCamera}>{t('techCheck.testCamera')}</Button>
          <video ref={video} playsInline muted style={{ width: '100%', borderRadius: 8, display: camera?.ok ? 'block' : 'none', background: '#000' }} />
          {camera && <Alert color={camera.ok ? 'teal' : 'red'} p="xs">{camera.message}</Alert>}
          <Group grow>
            <Button variant="default" leftSection={<IconRefresh size={16} />} onClick={() => window.location.reload()}>{t('techCheck.tryAgain')}</Button>
            <Button onClick={() => window.location.replace('/')}>{t('techCheck.openApp')}</Button>
          </Group>
        </Stack>
      </Card>
    </Center>
  );
}
