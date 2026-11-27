import { Fragment, type ReactNode } from 'react';
import { Group, SimpleGrid, Stack, Table, Text, Title } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import type { WorkOrderDetail } from '@/api/types';
import { printedStep } from '@/components/measure';
import { fmtDate, fmtDateTime, fmtQty } from '@/components/utils';
import { blocksInOrder, layoutText, pairRows, shows, type PrintBlock, type PrintLayout } from './layouts';
import { FooterText, PlainText, TermsBlock, type PrintContext } from './PrintTexts';
import { pick, safetyText } from '@/components/bilingual';

/** Blank rows for the technician to fill in by hand. */
const BLANK_ROWS = 4;
const line = { borderBottom: '1px solid #000', minHeight: 26 } as const;

/** An asset custom field to print: its label and the asset's value. */
export type PrintedField = { key: string; label: string; value?: string | null };

/**
 * The job card's content (0.23.0): today's paper split into blocks, printed in the layout's order with its fields. The header
 * always comes first; the work order's title and the layout's header text follow it.
 */
export function JobCardBody({ w, layout, orgName, terms, logoUrl, language, assetFields }: PrintContext & {
  w: WorkOrderDetail; layout: PrintLayout; assetFields?: PrintedField[] | null;
}) {
  const { t } = useTranslation();
  const has = (block: string, field?: string) => shows(layout, block, field);
  const location = w.locationTrail.map((l) => l.name).join(' › ');
  const safety = safetyText(w.safetyNotes, w.safetyNotesAr); // never hidden: the other language when the reader's has none (0.29.2)
  const tasks = [...w.tasks].sort((a, b) => a.sequence - b.sequence);
  // PM rounds list their assets in order, then any step not tied to one of them (as on the work order page); other work orders have one group.
  const groups = w.assets.length > 0
    ? [...w.assets.map((a) => ({ key: a.id, title: `${a.assetNumber} · ${a.name}` as string | null, tasks: tasks.filter((x) => x.assetId === a.id) })),
      { key: 'other', title: null as string | null, tasks: tasks.filter((x) => !w.assets.some((a) => a.id === x.assetId)) }].filter((g) => g.title || g.tasks.length > 0)
    : [{ key: 'all', title: null as string | null, tasks }];
  // Net quantity per part, as on the work order's parts tab (returns subtract).
  const parts = Object.values(w.parts.reduce<Record<string, { id: string; label: string; unit: string; qty: number }>>((a, p) => {
    const x = (a[p.partId] ??= { id: p.partId, label: `${p.partNumber} · ${p.partName}`, unit: p.unitOfMeasure, qty: 0 });
    x.qty += p.quantity; return a;
  }, {})).filter((x) => x.qty !== 0);
  const field = (label: string, value?: string | null) => (
    <div key={label}><Text fz={10} c="dimmed" tt="uppercase" fw={600}>{label}</Text><Text fz="sm">{value || '—'}</Text></div>
  );
  const blanks = (cols: number) => Array.from({ length: BLANK_ROWS }, (_, i) => (
    <Table.Tr key={`blank-${i}`}>{Array.from({ length: cols }, (__, c) => <Table.Td key={c} h={30} />)}</Table.Tr>
  ));

  const details: [string, string, string | null | undefined][] = [
    ['status', t('common.status'), t(`woStatus.${w.status}`)],
    ['priority', t('priority.label'), t(`priority.${w.priority}`)],
    ['workType', t('workType.label'), t(`workType.${w.type}`)],
    ['trade', t('wo.trade'), w.tradeName],
    ['team', t('print.team'), w.team?.name],
    ['site', terms.site, w.site.name],
    ['location', terms.location, location],
    ['asset', terms.asset, w.asset ? `${w.asset.assetNumber} · ${w.asset.name}` : w.assets.length ? t('jobCard.assetCount', { count: w.assets.length }) : null],
    ['serialNumber', t('jobCard.serial'), w.asset?.serialNumber],
    ['plannedStart', t('wo.plannedStart'), w.plannedStart ? fmtDate(w.plannedStart) : null],
    ['due', t('wo.due'), w.dueAt ? fmtDateTime(w.dueAt) : w.dueDate ? fmtDate(w.dueDate) : null],
    ['assignees', t('wo.assignees'), w.assignees.map((a) => a.fullName).join(', ')],
    ['requestedBy', t('wo.requestedBy'), w.requestedBy ?? w.request?.requesterName],
    ['costCentre', t('print.costCentre'), w.costCentrePath],
  ];

  const labour = (
    <div>
      <Title order={5} mb={6}>{t('wo.labor')}</Title>
      <Table withTableBorder withColumnBorders verticalSpacing={4}>
        <Table.Thead><Table.Tr><Table.Th>{t('wo.date')}</Table.Th><Table.Th>{t('wo.who')}</Table.Th><Table.Th ta="end">{t('wo.hours')}</Table.Th><Table.Th ta="end">{t('wo.overtime')}</Table.Th></Table.Tr></Table.Thead>
        <Table.Tbody>
          {w.labor.map((l) => <Table.Tr key={l.id}><Table.Td>{fmtDate(l.workDate)}</Table.Td><Table.Td>{l.employeeName}</Table.Td><Table.Td ta="end">{fmtQty(l.hours)}</Table.Td><Table.Td ta="end">{l.overtimeHours ? fmtQty(l.overtimeHours) : ''}</Table.Td></Table.Tr>)}
          {blanks(4)}
        </Table.Tbody>
      </Table>
    </div>
  );
  const partsTable = (
    <div>
      <Title order={5} mb={6}>{t('wo.parts')}</Title>
      <Table withTableBorder withColumnBorders verticalSpacing={4}>
        <Table.Thead><Table.Tr><Table.Th>{t('jobCard.part')}</Table.Th><Table.Th ta="end">{t('plan.planned')}</Table.Th><Table.Th ta="end">{t('plan.used')}</Table.Th></Table.Tr></Table.Thead>
        <Table.Tbody>
          {/* Planned parts first, with room to write what was used; then parts issued without a plan. */}
          {w.plannedParts.map((p) => <Table.Tr key={p.id}><Table.Td>{p.partNumber} · {p.partName}</Table.Td><Table.Td ta="end">{fmtQty(p.quantity)} {p.unitOfMeasure}</Table.Td><Table.Td /></Table.Tr>)}
          {parts.filter((p) => !w.plannedParts.some((x) => x.partId === p.id)).map((p) => (
            <Table.Tr key={p.id}><Table.Td>{p.label}</Table.Td><Table.Td ta="end">—</Table.Td><Table.Td ta="end">{fmtQty(p.qty)} {p.unit}</Table.Td></Table.Tr>
          ))}
          {blanks(3)}
        </Table.Tbody>
      </Table>
    </div>
  );

  const render = (b: PrintBlock): ReactNode => {
    switch (b.key) {
      case 'details': {
        const shown = details.filter(([key]) => has('details', key));
        return shown.length > 0 && <SimpleGrid cols={4} spacing="sm" mb="md">{shown.map(([, label, value]) => field(label, value))}</SimpleGrid>;
      }
      // 0.28.0: the safety notes print with the description, in the reader's language.
      case 'description': return (w.description || safety) && <>
        {w.description && <Text fz="sm" mb="md" style={{ whiteSpace: 'pre-wrap' }}>{w.description}</Text>}
        {safety && <Text fz="sm" mb="md" style={{ whiteSpace: 'pre-wrap' }}><b>{t('wo.safety')}:</b> {safety}</Text>}</>;
      case 'technician-notes': return (w.asset?.notesToTechnician || w.locationNotesToTechnician) && (
        <Stack gap={2} mb="md" p="xs" style={{ border: '1px solid #000' }}>
          <Text fz="xs" fw={700} tt="uppercase">{t('wo.notesForTech')}</Text>
          {w.asset?.notesToTechnician && <Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{w.asset.notesToTechnician}</Text>}
          {w.locationNotesToTechnician && <Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{w.locationNotesToTechnician}</Text>}
        </Stack>
      );
      case 'warranty': return w.asset?.underWarranty && (
        <Text fz="sm" mb="md" fw={600}>{t('wo.underWarrantyTitle', { type: t(`warrantyType.${w.asset.warrantyType}`) })} — {t('wo.underWarrantyBody', { date: fmtDate(w.asset.warrantyExpiresOn) })}</Text>
      );
      case 'asset-fields': {
        const chosen = (assetFields ?? []).filter((f) => b.fields.includes(f.key));
        return chosen.length > 0 && <SimpleGrid cols={4} spacing="sm" mb="md">{chosen.map((f) => field(f.label, f.value))}</SimpleGrid>;
      }
      case 'checklist': return (
        <>
          <Title order={5} mb={6}>{t('wo.tasks')}</Title>
          <Table withTableBorder withColumnBorders verticalSpacing={6} mb="md">
            <Table.Thead><Table.Tr>
              <Table.Th w={36}>#</Table.Th><Table.Th>{t('jobCard.step')}</Table.Th><Table.Th w={44} ta="center">✓</Table.Th>
              <Table.Th w={170}>{t('jobCard.result')}</Table.Th><Table.Th w={180}>{t('wo.remarks')}</Table.Th>
            </Table.Tr></Table.Thead>
            <Table.Tbody>
              {groups.map((g) => (
                <Fragment key={g.key}>
                  {g.title && <Table.Tr><Table.Td colSpan={5} fw={700} bg="var(--mantine-color-gray-1)">{g.title}</Table.Td></Table.Tr>}
                  {g.tasks.map((x, i) => (
                    <Table.Tr key={x.id}>
                      <Table.Td>{i + 1}</Table.Td><Table.Td>{pick(x.description, x.descriptionAr)}</Table.Td>
                      <Table.Td ta="center" fz="lg">{printedStep(x, t).mark}</Table.Td>
                      <Table.Td>{printedStep(x, t).result}</Table.Td><Table.Td>{x.remarks ?? ''}</Table.Td>
                    </Table.Tr>
                  ))}
                </Fragment>
              ))}
              {tasks.length === 0 && blanks(5)}
            </Table.Tbody>
          </Table>
        </>
      );
      case 'contractors': return (w.contractors?.length ?? 0) > 0 && (
        <div style={{ marginBottom: 16 }}>
          <Title order={5} mb={6}>{t('contractors.title')}</Title>
          <Table withTableBorder withColumnBorders verticalSpacing={4}>
            <Table.Thead><Table.Tr><Table.Th>{t('contracts.company')}</Table.Th><Table.Th>{t('companies.category')}</Table.Th><Table.Th>{t('contractors.person')}</Table.Th>
              <Table.Th ta="end">{t('contractors.planned')}</Table.Th><Table.Th ta="end">{t('contractors.done')}</Table.Th><Table.Th>{t('wo.date')}</Table.Th></Table.Tr></Table.Thead>
            <Table.Tbody>
              {w.contractors.map((l) => <Table.Tr key={l.id}><Table.Td>{l.companyName}</Table.Td><Table.Td>{l.categoryName ?? ''}</Table.Td><Table.Td>{l.personName ?? ''}</Table.Td>
                <Table.Td ta="end">{l.plannedHours ? fmtQty(l.plannedHours) : ''}</Table.Td><Table.Td ta="end">{l.hoursDone ? fmtQty(l.hoursDone) : ''}</Table.Td><Table.Td>{l.doneOn ? fmtDate(l.doneOn) : ''}</Table.Td></Table.Tr>)}
            </Table.Tbody>
          </Table>
        </div>
      );
      case 'labour': return <div style={{ marginBottom: 16 }}>{labour}</div>;
      case 'parts': return <div style={{ marginBottom: 16 }}>{partsTable}</div>;
      case 'completion': {
        const notes = ([['completionNotes', t('wo.completionNotes'), w.completionNotes], ['causeNotes', t('wo.causeNotes'), w.causeNotes],
          ['preventionNotes', t('wo.preventionNotes'), w.preventionNotes]] as const).filter(([k]) => has('completion', k));
        const codes = ([['symptom', t('codes.symptom'), w.symptom], ['cause', t('codes.cause'), w.failureCode], ['remedy', t('codes.remedy'), w.remedy]] as const)
          .filter(([k]) => has('completion', k));
        const down = has('completion', 'assetWasDown'), hours = has('completion', 'downtimeHours');
        if (notes.length === 0 && codes.length === 0 && !down && !hours) return null;
        return (
          <>
            <Title order={5} mb={6}>{t('jobCard.completion')}</Title>
            <Stack gap="sm" mb="md">
              {notes.map(([k, label, value]) => (
                <div key={k}><Text fz="xs" fw={600}>{label}</Text><Text fz="sm" style={line}>{value ?? ''}</Text>{!value && <div style={line} />}</div>
              ))}
              {codes.length > 0 && <SimpleGrid cols={3}>
                {codes.map(([k, label, c]) => <div key={k}><Text fz="xs" fw={600}>{label}</Text><Text fz="sm" style={line}>{c ? `${c.code} · ${c.name}` : ''}</Text></div>)}
              </SimpleGrid>}
              {(down || hours) && <SimpleGrid cols={3}>
                {down && <div><Text fz="xs" fw={600}>{t('wo.assetWasDown')}</Text><Text fz="sm" style={line}>{w.completedAt ? (w.assetWasDown ? t('common.yes') : t('common.no')) : `${t('common.yes')} / ${t('common.no')}`}</Text></div>}
                {hours && <div><Text fz="xs" fw={600}>{t('wo.downtimeHours')}</Text><Text fz="sm" style={line}>{w.downtimeHours != null ? fmtQty(w.downtimeHours) : ''}</Text></div>}
              </SimpleGrid>}
            </Stack>
          </>
        );
      }
      case 'signatures': return (
        <>
          {w.signatures.length > 0 && (
            <Group mt="lg" gap="xl" align="flex-end">
              {w.signatures.map((s) => (
                <Stack key={s.id} gap={2}>
                  <img src={`/api/attachments/${s.id}/content`} alt="" style={{ height: 60, maxWidth: 200, objectFit: 'contain' }} />
                  <Text fz="xs">{s.signedBy ?? ''} · {fmtDate(s.createdAt)}</Text>
                </Stack>
              ))}
            </Group>
          )}
          <Group mt={48} mb="md" grow>
            {[t('jobCard.technician'), t('jobCard.supervisor'), t('jobCard.signDate')].map((label) => (
              <Stack key={label} gap={2}><div style={{ borderTop: '1px solid #000' }} /><Text fz="xs">{label}</Text></Stack>
            ))}
          </Group>
        </>
      );
      case 'terms': return <TermsBlock title={t('print.terms')} text={layoutText(layout, 'terms', language)} />;
      default: return null;
    }
  };

  const rest = blocksInOrder(layout).filter((b) => b.key !== 'header');
  return (
    <>
      {has('header') && (
        <Group justify="space-between" align="flex-start" mb="lg">
          <Group gap="md" wrap="nowrap" align="flex-start">
            {layout.showLogo && logoUrl && <img src={logoUrl} alt={orgName} style={{ height: 56, maxWidth: 224, objectFit: 'contain' }} />}
            <Stack gap={2}>{has('header', 'orgName') && <Title order={2}>{orgName}</Title>}{has('header', 'site') && <Text c="dimmed">{w.site.name}</Text>}</Stack>
          </Group>
          <Stack gap={2} align="flex-end">
            {has('header', 'title') && <><Title order={3}>{t('jobCard.title')}</Title><Text fw={700} fz="lg">{w.number}</Text></>}
            {has('header', 'printedDate') && <Text fz="xs" c="dimmed">{t('jobCard.printed', { date: fmtDate(new Date().toISOString()) })}</Text>}
          </Stack>
        </Group>
      )}
      <Title order={4} mb="sm">{has('header', 'title') ? pick(w.title, w.titleAr) : `${w.number} · ${pick(w.title, w.titleAr)}`}</Title>
      {layoutText(layout, 'header', language) && <div style={{ marginBottom: 12 }}><PlainText text={layoutText(layout, 'header', language)} /></div>}
      {pairRows(rest, [['labour', 'parts']]).map((row) => row.length === 2
        ? <SimpleGrid key={row.map((x) => x.key).join('+')} cols={2} spacing="md" mb="md">{row[0].key === 'labour' ? <>{labour}{partsTable}</> : <>{partsTable}{labour}</>}</SimpleGrid>
        : <Fragment key={row[0].key}>{render(row[0])}</Fragment>)}
      <FooterText text={layoutText(layout, 'footer', language)} />
    </>
  );
}
