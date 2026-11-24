import { useEffect, useRef } from 'react';
import { imagesSettled } from './printWait';
import { Button, Center, Group, Loader } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconPrinter } from '@tabler/icons-react';
import { get } from '@/api/client';
import type { AssetDetail, CustomField, WorkOrderDetail } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { JobCardBody } from '@/print/JobCardBody';
import { shows } from '@/print/layouts';
import { PageSize } from '@/print/PrintTexts';
import { LayoutChooser, usePrintLayout } from '@/print/usePrintLayout';

/** A paper job card for a work order, in the layout that applies to it (0.23.0; today's card when none is set up). */
export function WorkOrderPrintPage() {
  const { t, i18n } = useTranslation();
  const { id = '' } = useParams();
  const { me, terms } = useAuth();
  const { data: w } = useQuery({ queryKey: ['work-order', id], queryFn: () => get<WorkOrderDetail>(`/work-orders/${id}`) });
  const { layout, matched, layouts, ready, choose } = usePrintLayout('job-card', id);
  const wantsFields = !!layout && shows(layout, 'asset-fields') && !!w?.asset;
  const { data: asset } = useQuery({ queryKey: ['asset', w?.asset?.id], queryFn: () => get<AssetDetail>(`/assets/${w!.asset!.id}`), enabled: wantsFields });
  const { data: defs } = useQuery({ queryKey: ['custom-fields', 'Asset'], queryFn: () => get<CustomField[]>('/custom-fields', { entityType: 'Asset' }), enabled: wantsFields });
  const printed = useRef(false);
  useEffect(() => {
    if (!w || !ready || printed.current) return;
    printed.current = true;
    document.title = w.number;
    // Signature images must be loaded before the print dialog takes its snapshot (but a stalled one mustn't hold it up).
    const h = setTimeout(() => { void imagesSettled(Array.from(document.images), 5000).then(() => window.print()); }, 400);
    return () => clearTimeout(h);
  }, [w, ready]);
  if (!w || !layout) return <Center h={300}><Loader /></Center>;
  const logoUrl = me?.tenant.settings.logoVersion ? `/api/settings/logo?v=${me.tenant.settings.logoVersion}` : null;
  const assetFields = (defs ?? []).map((d) => ({ key: d.key, label: d.label, value: asset?.customFields?.[d.key] ?? null }));
  return (
    <div className="print-doc">
      <PageSize size={layout.paperSize} />
      <LayoutChooser layout={layout} matched={matched} layouts={layouts} onChange={choose} />
      <JobCardBody w={w} layout={layout} orgName={me?.tenant.name ?? ''} terms={terms} logoUrl={logoUrl} language={i18n.language} assetFields={assetFields} />
      <Group justify="center" mt="xl" className="no-print"><Button onClick={() => window.print()} leftSection={<IconPrinter size={16} />}>{t('wo.printJobCard')}</Button></Group>
    </div>
  );
}
