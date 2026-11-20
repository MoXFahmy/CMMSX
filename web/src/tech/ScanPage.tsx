import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Card, Group, Stack, Text, TextInput } from '@mantine/core';
import { useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconCamera, IconCameraOff } from '@tabler/icons-react';
import { lookupCode, parseCode } from './scan';
import { useTech } from './useTech';

/** Scan a QR label or barcode with the camera, or type an asset number; opens the asset or location from the phone's copy. */
export function ScanPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { places, online } = useTech();
  const video = useRef<HTMLVideoElement>(null);
  const stopper = useRef<(() => void) | null>(null);
  const [scanning, setScanning] = useState(false);
  const [typed, setTyped] = useState('');
  const [problem, setProblem] = useState<string | null>(null);

  const stop = () => { stopper.current?.(); stopper.current = null; setScanning(false); };
  useEffect(() => stop, []);

  const open = (text: string) => {
    const s = parseCode(text);
    setProblem(null);
    if (s.kind === 'asset' || s.kind === 'location') {
      const known = s.kind === 'asset' ? places?.assets.some((a) => a.id === s.id) : places?.locations.some((l) => l.id === s.id);
      if (known || online) { navigate(`/tech/place/${s.kind}/${s.id}`); return; }
      setProblem(t('tech.notOnPhone'));
      return;
    }
    if (s.kind === 'part') {
      if (online) window.location.href = `/inventory/parts/${s.id}`;
      else setProblem(t('tech.partOnline'));
      return;
    }
    if (s.kind !== 'code') return;
    const hit = lookupCode(places, s.code);
    if (hit) navigate(`/tech/place/asset/${hit.id}`);
    else setProblem(t('tech.unknownCode', { code: s.code }));
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
        open(result.getText());
      });
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
        <form onSubmit={(e) => { e.preventDefault(); if (typed.trim()) open(typed); }}>
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
