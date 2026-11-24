import { useEffect, useRef } from 'react';
import { Button, Center, Group, Loader } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconPrinter } from '@tabler/icons-react';
import { get } from '@/api/client';
import type { WorkRequestDetail } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { PageSize } from '@/print/PrintTexts';
import { RequestBody } from '@/print/RequestBody';
import { LayoutChooser, usePrintLayout } from '@/print/usePrintLayout';

/** A printed receipt for a request (0.23.0), for the requester or the noticeboard, in the layout that applies to it. */
export function RequestPrintPage() {
  const { t, i18n } = useTranslation();
  const { id = '' } = useParams();
  const { me, terms } = useAuth();
  const { data: r } = useQuery({ queryKey: ['work-request', id], queryFn: () => get<WorkRequestDetail>(`/work-requests/${id}`) });
  const { layout, matched, layouts, ready, choose } = usePrintLayout('request', id);
  const printed = useRef(false);
  useEffect(() => {
    if (!r || !ready || printed.current) return;
    printed.current = true;
    document.title = r.number;
    const h = setTimeout(() => window.print(), 400);
    return () => clearTimeout(h);
  }, [r, ready]);
  if (!r || !layout) return <Center h={300}><Loader /></Center>;
  const logoUrl = me?.tenant.settings.logoVersion ? `/api/settings/logo?v=${me.tenant.settings.logoVersion}` : null;
  return (
    <div className="print-doc">
      <PageSize size={layout.paperSize} />
      <LayoutChooser layout={layout} matched={matched} layouts={layouts} onChange={choose} />
      <RequestBody r={r} layout={layout} orgName={me?.tenant.name ?? ''} terms={terms} logoUrl={logoUrl} language={i18n.language} />
      <Group justify="center" mt="xl" className="no-print"><Button onClick={() => window.print()} leftSection={<IconPrinter size={16} />}>{t('print.printReceipt')}</Button></Group>
    </div>
  );
}
