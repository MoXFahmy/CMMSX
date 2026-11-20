import { useEffect, useImperativeHandle, useRef, useState, type Ref } from 'react';
import { Box, Button, Group, Text } from '@mantine/core';
import { useTranslation } from 'react-i18next';

export type SignatureHandle = { isEmpty(): boolean; toBlob(): Promise<Blob | null> };

/** A box to sign in with a finger or a mouse. */
export function SignaturePad({ label, ref }: { label: string; ref?: Ref<SignatureHandle> }) {
  const { t } = useTranslation();
  const canvas = useRef<HTMLCanvasElement>(null);
  const drawing = useRef(false);
  const [empty, setEmpty] = useState(true);

  useEffect(() => {
    const c = canvas.current!;
    const scale = window.devicePixelRatio || 1;
    c.width = c.offsetWidth * scale;
    c.height = c.offsetHeight * scale;
    const g = c.getContext('2d')!;
    g.scale(scale, scale);
    g.lineWidth = 2.2; g.lineCap = 'round'; g.lineJoin = 'round'; g.strokeStyle = '#111';
  }, []);

  const point = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const r = e.currentTarget.getBoundingClientRect();
    return { x: e.clientX - r.left, y: e.clientY - r.top };
  };
  const clear = () => {
    const c = canvas.current!;
    c.getContext('2d')!.clearRect(0, 0, c.width, c.height);
    setEmpty(true);
  };

  useImperativeHandle(ref, () => ({
    isEmpty: () => empty,
    toBlob: () => new Promise((resolve) => (empty ? resolve(null) : canvas.current!.toBlob((b) => resolve(b), 'image/png'))),
  }), [empty]);

  return (
    <div>
      <Group justify="space-between" mb={4}><Text fz="sm" fw={500}>{label}</Text><Button size="compact-xs" variant="subtle" onClick={clear} disabled={empty}>{t('tech.clear')}</Button></Group>
      <Box style={{ border: '1px dashed var(--mantine-color-gray-5)', borderRadius: 8, background: 'white' }}>
        <canvas
          ref={canvas}
          style={{ width: '100%', height: 140, touchAction: 'none', display: 'block' }}
          onPointerDown={(e) => { drawing.current = true; e.currentTarget.setPointerCapture(e.pointerId); const p = point(e); const g = e.currentTarget.getContext('2d')!; g.beginPath(); g.moveTo(p.x, p.y); }}
          onPointerMove={(e) => { if (!drawing.current) return; const p = point(e); const g = e.currentTarget.getContext('2d')!; g.lineTo(p.x, p.y); g.stroke(); setEmpty(false); }}
          onPointerUp={() => { drawing.current = false; }}
          onPointerCancel={() => { drawing.current = false; }}
        />
      </Box>
    </div>
  );
}
