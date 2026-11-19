import { Fragment, useEffect } from 'react';
import { Button, Center, Group, Loader, SimpleGrid, Stack, Table, Text, Title } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconPrinter } from '@tabler/icons-react';
import { get } from '@/api/client';
import type { WorkOrderDetail } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { fmtDate, fmtQty } from '@/components/utils';

/** Blank rows for the technician to fill in by hand. */
const BLANK_ROWS = 4;

const line = { borderBottom: '1px solid #000', minHeight: 26 } as const;

/** A paper job card for a work order: what to do, where, the checklist to tick, and room to record labour, parts and sign-off. */
export function WorkOrderPrintPage() {
  const { t } = useTranslation();
  const { id = '' } = useParams();
  const { me, terms } = useAuth();
  const { data: w } = useQuery({ queryKey: ['work-order', id], queryFn: () => get<WorkOrderDetail>(`/work-orders/${id}`) });
  useEffect(() => { if (w) { document.title = w.number; const h = setTimeout(() => window.print(), 400); return () => clearTimeout(h); } }, [w]);
  if (!w) return <Center h={300}><Loader /></Center>;

  const location = w.locationTrail.map((l) => l.name).join(' › ');
  const tasks = [...w.tasks].sort((a, b) => a.sequence - b.sequence);
  // PM rounds list their assets in order; every other work order has one group.
  const groups = w.assets.length > 0
    ? w.assets.map((a) => ({ key: a.id, title: `${a.assetNumber} · ${a.name}`, tasks: tasks.filter((x) => x.assetId === a.id) }))
    : [{ key: 'all', title: null as string | null, tasks }];
  // Net quantity per part, as on the work order's parts tab (returns subtract).
  const parts = Object.values(w.parts.reduce<Record<string, { id: string; label: string; unit: string; qty: number }>>((a, p) => {
    const x = (a[p.partId] ??= { id: p.partId, label: `${p.partNumber} · ${p.partName}`, unit: p.unitOfMeasure, qty: 0 });
    x.qty += p.quantity; return a;
  }, {})).filter((x) => x.qty !== 0);
  const field = (label: string, value?: string | null) => (
    <div><Text fz={10} c="dimmed" tt="uppercase" fw={600}>{label}</Text><Text fz="sm">{value || '—'}</Text></div>
  );
  const blanks = (cols: number) => Array.from({ length: BLANK_ROWS }, (_, i) => (
    <Table.Tr key={`blank-${i}`}>{Array.from({ length: cols }, (__, c) => <Table.Td key={c} h={30} />)}</Table.Tr>
  ));

  return (
    <div className="print-doc">
      <Group justify="space-between" align="flex-start" mb="lg">
        <Stack gap={2}><Title order={2}>{me?.tenant.name}</Title><Text c="dimmed">{w.site.name}</Text></Stack>
        <Stack gap={2} align="flex-end">
          <Title order={3}>{t('jobCard.title')}</Title><Text fw={700} fz="lg">{w.number}</Text>
          <Text fz="xs" c="dimmed">{t('jobCard.printed', { date: fmtDate(new Date().toISOString()) })}</Text>
        </Stack>
      </Group>

      <Title order={4} mb="sm">{w.title}</Title>
      <SimpleGrid cols={4} spacing="sm" mb="md">
        {field(t('common.status'), t(`woStatus.${w.status}`))}
        {field(t('priority.label'), t(`priority.${w.priority}`))}
        {field(t('workType.label'), t(`workType.${w.type}`))}
        {field(t('wo.trade'), w.tradeName)}
        {field(terms.site, w.site.name)}
        {field(terms.location, location)}
        {field(terms.asset, w.asset ? `${w.asset.assetNumber} · ${w.asset.name}` : w.assets.length ? t('jobCard.assetCount', { count: w.assets.length }) : null)}
        {field(t('jobCard.serial'), w.asset?.serialNumber)}
        {field(t('wo.plannedStart'), w.plannedStart ? fmtDate(w.plannedStart) : null)}
        {field(t('wo.dueDate'), w.dueDate ? fmtDate(w.dueDate) : null)}
        {field(t('wo.assignees'), w.assignees.map((a) => a.fullName).join(', '))}
        {field(t('wo.requestedBy'), w.requestedBy ?? w.request?.requesterName)}
      </SimpleGrid>

      {w.description && <Text fz="sm" mb="md" style={{ whiteSpace: 'pre-wrap' }}>{w.description}</Text>}
      {(w.asset?.notesToTechnician || w.locationNotesToTechnician) && (
        <Stack gap={2} mb="md" p="xs" style={{ border: '1px solid #000' }}>
          <Text fz="xs" fw={700} tt="uppercase">{t('wo.notesForTech')}</Text>
          {w.asset?.notesToTechnician && <Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{w.asset.notesToTechnician}</Text>}
          {w.locationNotesToTechnician && <Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{w.locationNotesToTechnician}</Text>}
        </Stack>
      )}
      {w.asset?.underWarranty && (
        <Text fz="sm" mb="md" fw={600}>{t('wo.underWarrantyTitle', { type: t(`warrantyType.${w.asset.warrantyType}`) })} — {t('wo.underWarrantyBody', { date: fmtDate(w.asset.warrantyExpiresOn) })}</Text>
      )}

      <Title order={5} mb={6}>{t('wo.tasks')}</Title>
      <Table withTableBorder withColumnBorders verticalSpacing={6} mb="md">
        <Table.Thead><Table.Tr>
          <Table.Th w={36}>#</Table.Th><Table.Th>{t('jobCard.step')}</Table.Th><Table.Th w={44} ta="center">✓</Table.Th>
          <Table.Th w={110}>{t('wo.reading')}</Table.Th><Table.Th w={200}>{t('wo.remarks')}</Table.Th>
        </Table.Tr></Table.Thead>
        <Table.Tbody>
          {groups.map((g) => (
            <Fragment key={g.key}>
              {g.title && <Table.Tr><Table.Td colSpan={5} fw={700} bg="var(--mantine-color-gray-1)">{g.title}</Table.Td></Table.Tr>}
              {g.tasks.map((x, i) => (
                <Table.Tr key={x.id}>
                  <Table.Td>{i + 1}</Table.Td><Table.Td>{x.description}</Table.Td>
                  <Table.Td ta="center" fz="lg">{x.isDone ? '☑' : '☐'}</Table.Td>
                  <Table.Td>{x.reading ?? ''}</Table.Td><Table.Td>{x.remarks ?? ''}</Table.Td>
                </Table.Tr>
              ))}
            </Fragment>
          ))}
          {tasks.length === 0 && blanks(5)}
        </Table.Tbody>
      </Table>

      <SimpleGrid cols={2} spacing="md" mb="md">
        <div>
          <Title order={5} mb={6}>{t('wo.labor')}</Title>
          <Table withTableBorder withColumnBorders verticalSpacing={4}>
            <Table.Thead><Table.Tr><Table.Th>{t('wo.date')}</Table.Th><Table.Th>{t('wo.who')}</Table.Th><Table.Th ta="right">{t('wo.hours')}</Table.Th><Table.Th ta="right">{t('wo.overtime')}</Table.Th></Table.Tr></Table.Thead>
            <Table.Tbody>
              {w.labor.map((l) => <Table.Tr key={l.id}><Table.Td>{fmtDate(l.workDate)}</Table.Td><Table.Td>{l.employeeName}</Table.Td><Table.Td ta="right">{fmtQty(l.hours)}</Table.Td><Table.Td ta="right">{l.overtimeHours ? fmtQty(l.overtimeHours) : ''}</Table.Td></Table.Tr>)}
              {blanks(4)}
            </Table.Tbody>
          </Table>
        </div>
        <div>
          <Title order={5} mb={6}>{t('wo.parts')}</Title>
          <Table withTableBorder withColumnBorders verticalSpacing={4}>
            <Table.Thead><Table.Tr><Table.Th>{t('jobCard.part')}</Table.Th><Table.Th ta="right">{t('wo.qty')}</Table.Th></Table.Tr></Table.Thead>
            <Table.Tbody>
              {parts.map((p) => <Table.Tr key={p.id}><Table.Td>{p.label}</Table.Td><Table.Td ta="right">{fmtQty(p.qty)} {p.unit}</Table.Td></Table.Tr>)}
              {blanks(2)}
            </Table.Tbody>
          </Table>
        </div>
      </SimpleGrid>

      <Title order={5} mb={6}>{t('jobCard.completion')}</Title>
      <Stack gap="sm" mb="md">
        {[[t('wo.completionNotes'), w.completionNotes], [t('wo.causeNotes'), w.causeNotes], [t('wo.preventionNotes'), w.preventionNotes]].map(([label, value]) => (
          <div key={label as string}><Text fz="xs" fw={600}>{label}</Text><Text fz="sm" style={line}>{value ?? ''}</Text>{!value && <div style={line} />}</div>
        ))}
        <SimpleGrid cols={3}>
          <div><Text fz="xs" fw={600}>{t('wo.failureCode')}</Text><Text fz="sm" style={line}>{w.failureCode ? `${w.failureCode.code} · ${w.failureCode.name}` : ''}</Text></div>
          <div><Text fz="xs" fw={600}>{t('wo.assetWasDown')}</Text><Text fz="sm" style={line}>{w.completedAt ? (w.assetWasDown ? t('common.yes') : t('common.no')) : `${t('common.yes')} / ${t('common.no')}`}</Text></div>
          <div><Text fz="xs" fw={600}>{t('wo.downtimeHours')}</Text><Text fz="sm" style={line}>{w.downtimeHours != null ? fmtQty(w.downtimeHours) : ''}</Text></div>
        </SimpleGrid>
      </Stack>

      <Group mt={48} grow>
        {[t('jobCard.technician'), t('jobCard.supervisor'), t('jobCard.signDate')].map((label) => (
          <Stack key={label} gap={2}><div style={{ borderTop: '1px solid #000' }} /><Text fz="xs">{label}</Text></Stack>
        ))}
      </Group>
      <Group justify="center" mt="xl" className="no-print"><Button onClick={() => window.print()} leftSection={<IconPrinter size={16} />}>{t('wo.printJobCard')}</Button></Group>
    </div>
  );
}
