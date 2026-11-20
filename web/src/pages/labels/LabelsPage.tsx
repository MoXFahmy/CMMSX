import { useEffect, useState } from 'react';
import { Alert, Button, Center, Group, Loader, Text } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import QRCode from 'qrcode';
import { IconPrinter } from '@tabler/icons-react';
import { ApiError, post } from '@/api/client';
import { SHEET, sheets } from './sheet';

type LabelType = 'asset' | 'location' | 'part';
type Label = { id: string; number: string; name: string; detail?: string | null; url: string };
const KEY = 'cmms.labels';

/** Opens the label sheet for the chosen records (kept in this tab's storage: hundreds of ids don't fit an address). */
export function openLabels(type: LabelType, ids: string[], includeChildren = false) {
  try { sessionStorage.setItem(KEY, JSON.stringify({ type, ids, includeChildren })); } catch { /* private mode */ }
  window.open('/labels', '_blank');
}

/** A4 sheets of 3 × 8 labels (70 × 37 mm, edge to edge): QR code, number and name (assets also show their location). */
export function LabelsPage() {
  const { t } = useTranslation();
  const [labels, setLabels] = useState<(Label & { qr: string })[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    (async () => {
      try {
        const raw = sessionStorage.getItem(KEY);
        if (!raw) { setError(t('labels.nothing')); return; }
        const rows = await post<Label[]>('/labels', JSON.parse(raw));
        setLabels(await Promise.all(rows.map(async (l) => ({ ...l, qr: await QRCode.toDataURL(l.url, { margin: 0, width: 240, errorCorrectionLevel: 'M' }) }))));
        document.title = t('labels.title');
      } catch (e) { setError(e instanceof ApiError ? e.message : String(e)); }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (error) return <Center h="60vh"><Alert color="orange">{error}</Alert></Center>;
  if (!labels) return <Center h="60vh"><Loader /></Center>;
  return (
    <div>
      <style>{`
        @page { size: A4; margin: ${SHEET.marginTopMm}mm 0 0 0; }
        .label-sheet { display: grid; grid-template-columns: repeat(${SHEET.columns}, ${SHEET.labelWidthMm}mm); grid-auto-rows: ${SHEET.labelHeightMm}mm; width: 210mm; margin: 0 auto 8mm; }
        @media print { .label-sheet { margin: 0; break-after: page; } .label-sheet:last-child { break-after: auto; } }
        .label { box-sizing: border-box; padding: 3mm; display: flex; gap: 3mm; align-items: center; overflow: hidden; break-inside: avoid; border: 0.2mm dashed #ccc; }
        .label img { width: 27mm; height: 27mm; flex: none; }
        @media print { .no-print { display: none !important; } .label { border: none; } }
      `}</style>
      <Group justify="center" my="md" className="no-print">
        <Text fz="sm" c="dimmed">{t('labels.count', { count: labels.length })}</Text>
        <Button leftSection={<IconPrinter size={16} />} onClick={() => window.print()}>{t('labels.print')}</Button>
      </Group>
      {sheets(labels).map((page, n) => (
      <div className="label-sheet" key={n}>
        {page.map((l) => (
          <div className="label" key={l.id}>
            <img src={l.qr} alt="" />
            <div style={{ minWidth: 0 }}>
              <div style={{ fontWeight: 700, fontSize: '11pt', lineHeight: 1.1 }}>{l.number}</div>
              <div style={{ fontSize: '8.5pt', lineHeight: 1.2, maxHeight: '3.6em', overflow: 'hidden' }}>{l.name}</div>
              {l.detail && <div style={{ fontSize: '7pt', color: '#555', lineHeight: 1.2, maxHeight: '2.4em', overflow: 'hidden' }}>{l.detail}</div>}
            </div>
          </div>
        ))}
      </div>
      ))}
    </div>
  );
}
