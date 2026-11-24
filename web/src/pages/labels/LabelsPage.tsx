import { useEffect, useState } from 'react';
import { LayoutChooser, usePrintLayout } from '@/print/usePrintLayout';
import { Alert, Button, Center, Group, Loader, Text } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import QRCode from 'qrcode';
import { IconPrinter } from '@tabler/icons-react';
import { ApiError, post } from '@/api/client';
import { sheetFor, sheets } from './sheet';

type LabelType = 'asset' | 'location' | 'part';
type Label = { id: string; number: string; name: string; detail?: string | null; url: string; site?: string | null };
const KEY = 'cmms.labels';

/** Opens the label sheet for the chosen records (kept in this tab's storage: hundreds of ids don't fit an address). */
export function openLabels(type: LabelType, ids: string[], includeChildren = false) {
  try { sessionStorage.setItem(KEY, JSON.stringify({ type, ids, includeChildren })); } catch { /* private mode */ }
  window.open('/labels', '_blank');
}

/**
 * The label sheet: a QR code on every label, and the details and size of the label layout that applies (0.23.0); with none,
 * today's A4 sheet of 3 × 8 labels (70 × 37 mm) with the number, name and the third line.
 */
export function LabelsPage() {
  const { t } = useTranslation();
  const [labels, setLabels] = useState<(Label & { qr: string })[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [type, setType] = useState<LabelType | null>(null);

  useEffect(() => {
    (async () => {
      try {
        const raw = sessionStorage.getItem(KEY);
        if (!raw) { setError(t('labels.nothing')); return; }
        const input = JSON.parse(raw) as { type: LabelType };
        setType(input.type);
        const rows = await post<Label[]>('/labels', input);
        setLabels(await Promise.all(rows.map(async (l) => ({ ...l, qr: await QRCode.toDataURL(l.url, { margin: 0, width: 240, errorCorrectionLevel: 'M' }) }))));
        document.title = t('labels.title');
      } catch (e) { setError(e instanceof ApiError ? e.message : String(e)); }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const { layout, matched, layouts, ready, choose } = usePrintLayout('labels', labels?.[0]?.id, type ? { labelKind: type } : {});
  if (error) return <Center h="60vh"><Alert color="orange">{error}</Alert></Center>;
  if (!labels || !layout || !ready) return <Center h="60vh"><Loader /></Center>;
  const s = sheetFor(layout.labelSize);
  const show = (f: string) => layout.labelFields.includes(f);
  // One sheet has one size: when the labels are at several sites and some layout is for a site, the first label's layout decides.
  const mixed = new Set(labels.map((l) => l.site ?? '')).size > 1 && layouts.some((l) => !!l.siteId);
  return (
    <div>
      <style>{`
        @page { size: ${s.pageWidthMm}mm ${s.pageHeightMm}mm; margin: ${s.marginTopMm}mm 0 0 0; }
        .label-sheet { display: grid; grid-template-columns: repeat(${s.columns}, ${s.labelWidthMm}mm); column-gap: ${s.gapMm}mm; grid-auto-rows: ${s.labelHeightMm}mm; width: ${s.pageWidthMm}mm; justify-content: center; margin: 0 auto 8mm; }
        @media print { .label-sheet { margin: 0; break-after: page; } .label-sheet:last-child { break-after: auto; } }
        .label { box-sizing: border-box; padding: 3mm; display: flex; gap: 3mm; align-items: center; overflow: hidden; break-inside: avoid; border: 0.2mm dashed #ccc; }
        .label img { width: ${s.qrMm}mm; height: ${s.qrMm}mm; flex: none; }
        @media print { .no-print { display: none !important; } .label { border: none; } }
      `}</style>
      <LayoutChooser layout={layout} matched={matched} layouts={layouts} onChange={choose} />
      {mixed && <Group justify="center" className="no-print"><Text fz="sm" c="orange">{t('print.firstLabelDecides')}</Text></Group>}
      <Group justify="center" my="md" className="no-print">
        <Text fz="sm" c="dimmed">{t('labels.count', { count: labels.length })}</Text>
        <Button leftSection={<IconPrinter size={16} />} onClick={() => window.print()}>{t('labels.print')}</Button>
      </Group>
      {sheets(labels, layout.labelSize).map((page, n) => (
      <div className="label-sheet" key={n}>
        {page.map((l) => (
          <div className="label" key={l.id}>
            <img src={l.qr} alt="" />
            <div style={{ minWidth: 0 }}>
              {show('number') && <div style={{ fontWeight: 700, fontSize: '11pt', lineHeight: 1.1 }}>{l.number}</div>}
              {show('name') && <div style={{ fontSize: '8.5pt', lineHeight: 1.2, maxHeight: '3.6em', overflow: 'hidden' }}>{l.name}</div>}
              {show('detail') && l.detail && <div style={{ fontSize: '7pt', color: '#555', lineHeight: 1.2, maxHeight: '2.4em', overflow: 'hidden' }}>{l.detail}</div>}
              {show('site') && l.site && <div style={{ fontSize: '7pt', color: '#555', lineHeight: 1.2 }}>{l.site}</div>}
            </div>
          </div>
        ))}
      </div>
      ))}
    </div>
  );
}
