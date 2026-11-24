import { Fragment, type ReactNode } from 'react';
import { Group, SimpleGrid, Stack, Text, Title } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import type { WorkRequestDetail } from '@/api/types';
import { fmtDate, fmtDateTime } from '@/components/utils';
import { blocksInOrder, layoutText, shows, type PrintBlock, type PrintLayout } from './layouts';
import { FooterText, PlainText, TermsBlock, type PrintContext } from './PrintTexts';

/** A request receipt (0.23.0): what was asked, by whom, when it may be done, and the work order that followed. */
export function RequestBody({ r, layout, orgName, terms, logoUrl, language }: PrintContext & { r: WorkRequestDetail; layout: PrintLayout }) {
  const { t } = useTranslation();
  const has = (block: string, field?: string) => shows(layout, block, field);
  const field = (label: string, value?: string | null) => (
    <div key={label}><Text fz={10} c="dimmed" tt="uppercase" fw={600}>{label}</Text><Text fz="sm">{value || '—'}</Text></div>
  );
  const details: [string, string, string | null | undefined][] = [
    ['status', t('common.status'), t(`wrStatus.${r.status}`)],
    ['submitted', t('print.submitted'), fmtDateTime(r.createdAt)],
    ['requester', t('print.requester'), r.requesterName],
    ['phone', t('print.phone'), r.requesterPhone],
    ['requestType', t('print.requestType'), r.requestType?.name],
    ['priority', t('priority.label'), t(`priority.${r.priority}`)],
    ['workType', t('workType.label'), t(`workType.${r.type}`)],
    ['site', terms.site, r.site.name],
    ['location', terms.location, r.locationName],
    ['asset', terms.asset, r.asset ? `${r.asset.assetNumber} · ${r.asset.name}` : null],
    ['requiredBy', t('print.requiredBy'), r.requiredBy ? fmtDate(r.requiredBy) : null],
    ['symptom', t('codes.symptom'), r.symptom ? `${r.symptom.code} · ${r.symptom.name}` : null],
  ];
  const render = (b: PrintBlock): ReactNode => {
    switch (b.key) {
      case 'details': {
        const shown = details.filter(([k]) => has('details', k));
        return shown.length > 0 && <SimpleGrid cols={3} spacing="sm" mb="md">{shown.map(([, label, value]) => field(label, value))}</SimpleGrid>;
      }
      case 'description': return r.description && <Text fz="sm" mb="md" style={{ whiteSpace: 'pre-wrap' }}>{r.description}</Text>;
      case 'access-windows': return r.accessWindows.length > 0 && (
        <div style={{ marginBottom: 16 }}>
          <Title order={5} mb={6}>{t('print.accessWindows')}</Title>
          <Stack gap={2}>{r.accessWindows.map((w, i) => <Text key={i} fz="sm">{fmtDateTime(w.start)} – {fmtDateTime(w.end)}{w.note ? ` · ${w.note}` : ''}</Text>)}</Stack>
        </div>
      );
      case 'progress': return r.workOrder && (
        <div style={{ marginBottom: 16 }}>
          <Title order={5} mb={6}>{t('print.progress')}</Title>
          <Text fz="sm">{t('print.workOrderLine', { number: r.workOrder.number, status: t(`woStatus.${r.workOrder.status}`) })}
            {(r.workOrder.dueAt || r.workOrder.dueDate) && ` · ${t('print.dueOn', { date: r.workOrder.dueAt ? fmtDateTime(r.workOrder.dueAt) : fmtDate(r.workOrder.dueDate!) })}`}</Text>
        </div>
      );
      case 'terms': return <TermsBlock title={t('print.terms')} text={layoutText(layout, 'terms', language)} />;
      default: return null;
    }
  };
  const headerText = layoutText(layout, 'header', language);
  return (
    <>
      {has('header') && (
        <Group justify="space-between" align="flex-start" mb="lg">
          <Group gap="md" wrap="nowrap" align="flex-start">
            {layout.showLogo && logoUrl && <img src={logoUrl} alt={orgName} style={{ height: 56, maxWidth: 224, objectFit: 'contain' }} />}
            <Stack gap={2}>{has('header', 'orgName') && <Title order={2}>{orgName}</Title>}{has('header', 'site') && <Text c="dimmed">{r.site.name}</Text>}</Stack>
          </Group>
          <Stack gap={2} align="flex-end">
            {has('header', 'title') && <><Title order={3}>{t('print.requestTitle')}</Title><Text fw={700} fz="lg">{r.number}</Text></>}
            {has('header', 'printedDate') && <Text fz="xs" c="dimmed">{t('jobCard.printed', { date: fmtDate(new Date().toISOString()) })}</Text>}
          </Stack>
        </Group>
      )}
      <Title order={4} mb="sm">{has('header', 'title') ? r.title : `${r.number} · ${r.title}`}</Title>
      {headerText && <div style={{ marginBottom: 12 }}><PlainText text={headerText} /></div>}
      {blocksInOrder(layout).filter((b) => b.key !== 'header').map((b) => <Fragment key={b.key}>{render(b)}</Fragment>)}
      <FooterText text={layoutText(layout, 'footer', language)} />
    </>
  );
}
