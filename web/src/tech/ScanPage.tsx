import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Card, Group, Stack, Text, TextInput } from '@mantine/core';
import { useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconCamera, IconCameraOff } from '@tabler/icons-react';
import { get } from '@/api/client';
import { exactPart, lookupCode, parseCode, placeTarget } from './scan';
import { findPart } from './stock';
import { useTech } from './useTech';

/** Scan a QR label or barcode with the camera, or type an asset number; opens the asset or location from the phone's copy. */
export function ScanPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { places, stock, online } = useTech();
  const video = useRef<HTMLVideoElement>(null);
  const stopper = useRef<(() => void) | null>(null);
  const [scanning, setScanning] = useState(false);
  const [typed, setTyped] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  // Leaving while the phone still asks for camera permission: the camera starts after this page is gone, so stop it then.
  const gone = useRef(false);

  const stop = () => { stopper.current?.(); stopper.current = null; setScanning(false); };
  useEffect(() => () => { gone.current = true; stop(); }, []);

  const open = async (text: string) => {
    const s = parseCode(text);
    setProblem(null);
    if (s.kind === 'asset' || s.kind === 'location') {
      const to = placeTarget(s.kind, s.id, places, online);
      if ('tech' in to) navigate(to.tech);
      else if ('full' in to) window.location.href = to.full; // on a site this phone doesn't carry
      else setProblem(t('tech.notOnPhone'));
      return;
    }
    if (s.kind === 'part') {
      // On the phone (0.11.0): stock and bin in each store, offline too.
      if (stock?.parts.some((p) => p.id === s.id)) navigate(`/tech/parts/${s.id}`);
      else if (online) window.location.href = `/inventory/parts/${s.id}`;
      else setProblem(t('tech.partOnline'));
      return;
    }
    if (s.kind !== 'code') return;
    const hit = lookupCode(places, s.code);
    if (hit) { navigate(`/tech/place/asset/${hit.id}`); return; }
    const onPhone = findPart(stock, s.code);
    if (onPhone) { navigate(`/tech/parts/${onPhone.id}`); return; }
    // Not an asset on the phone: with a connection it may be a part's number or barcode.
    if (online) {
      const found = await get<{ id: string; partNumber: string; barcode?: string | null }[]>(`/parts/lookup?search=${encodeURIComponent(s.code)}`).catch(() => []);
      const part = exactPart(found, s.code);
      if (part) { window.location.href = `/inventory/parts/${part}`; return; }
    }
    setProblem(t(online ? 'tech.unknownCodeOnline' : 'tech.unknownCode', { code: s.code }));
  };

  const start = async () => {
    setProblem(null);
    try {
      // Loaded only when needed: the decoder is the biggest part of the technician view.
      const { BrowserMultiFormatReader } = await import('@zxing/browser');
      const reader = new BrowserMultiFormatReader();
      setScanning(true);
      const controls = await reader.decodeFromConstraints({ video: { facingMode: { ideal: 'environment' } } }, video.current!, (result) => {
        if (!result) return;
        stop();
        void open(result.getText());
      });
      if (gone.current) { controls.stop(); return; }
      stopper.current = () => controls.stop();
    } catch (e) {
      setScanning(false);
      setProblem(`${t('tech.cameraProblem')} (${(e as Error).name ?? 'Error'})`);
    }
  };

  return (
    <Stack gap="sm">
      <Card withBorder p="sm">
        <video ref={video} playsInline muted style={{ width: '100%', borderRadius: 8, background: '#000', display: scanning ? 'block' : 'none' }} />
        {!scanning
          ? <Button fullWidth leftSection={<IconCamera size={16} />} onClick={() => void start()}>{t('tech.scanLabel')}</Button>
          : <Button fullWidth mt="xs" variant="default" leftSection={<IconCameraOff size={16} />} onClick={stop}>{t('tech.stopCamera')}</Button>}
      </Card>
      <Card withBorder p="sm">
        <form onSubmit={(e) => { e.preventDefault(); if (typed.trim()) void open(typed); }}>
          <Group align="flex-end" wrap="nowrap">
            <TextInput style={{ flex: 1 }} label={t('tech.typeCode')} placeholder="AST-00012" value={typed} onChange={(e) => setTyped(e.currentTarget.value)} />
            <Button type="submit" disabled={!typed.trim()}>{t('tech.open')}</Button>
          </Group>
        </form>
      </Card>
      {problem && <Alert color="orange">{problem}</Alert>}
      <Text fz="xs" c="dimmed" ta="center">{t('tech.scanHint')}</Text>
    </Stack>
  );
}
