import { useMemo, useState } from 'react';
import { Anchor, Box, Button, Card, Group, Loader, Modal, NumberInput, Paper, ScrollArea, Select, Stack, Text, TextInput, Textarea } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { notifications } from '@mantine/notifications';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { DndContext, PointerSensor, useDraggable, useDroppable, useSensor, useSensors, type DragEndEvent } from '@dnd-kit/core';
import { get, post, put } from '@/api/client';
import type { ContractorCategory, ContractorLine, SupplierLookup, WorkOrderDetail } from '@/api/types';
import { fmtQty, notifyError, toIsoDate } from '@/components/utils';
import { cellsByContractor, planVisitMove, recordVisitBody, TRAY, type ContractorBoard as Board, type Visit } from './board';

let lastDragEnd = 0;

/**
 * Contractor visits by company (0.19.0): drag a visit to another day of its company; click it to record the hours done, the date and
 * the person. The company itself is changed on the work order.
 */
export function ContractorBoard({ from, to, days, siteId, companyId, categoryId, onFilter, canEdit, canExec }: {
  from: string; to: string; days: string[]; siteId: string | null; companyId: string | null; categoryId: string | null;
  onFilter: (patch: Record<string, string | null>) => void; canEdit: boolean; canExec: boolean;
}) {
  const { t } = useTranslation();
  const { data: companies } = useQuery({ queryKey: ['suppliers', 'lookup', 'contractor'], queryFn: () => get<SupplierLookup[]>('/suppliers/lookup', { role: 'contractor' }) });
  const { data: categories } = useQuery({ queryKey: ['contractor-categories'], queryFn: () => get<ContractorCategory[]>('/contractor-categories') });
  const { data, refetch, isFetching } = useQuery({
    queryKey: ['planning-contractors', from, to, siteId, companyId, categoryId], placeholderData: (prev) => prev,
    queryFn: () => get<Board>('/planning/contractors', { from, to, siteId, companyId, categoryId }),
  });
  const cells = useMemo(() => cellsByContractor(data?.visits ?? []), [data]);
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }));
  const [recording, setRecording] = useState<Visit | null>(null);
  const colWidth = days.length > 7 ? 110 : 150;

  const onDragEnd = async (e: DragEndEvent) => {
    lastDragEnd = Date.now();
    const v = e.active.data.current as Visit | undefined;
    const target = e.over?.id ? String(e.over.id).split('|') : null; // "company|day"
    if (!v || !target) return;
    const plan = planVisitMove(v, target[0], target[1]);
    if (plan === 'other-company') { notifications.show({ color: 'orange', message: t('planning.otherCompany') }); return; }
    if (plan === 'work-order-day') { notifications.show({ color: 'orange', message: t('planning.followsWorkOrder') }); return; }
    if (!plan) return;
    try { await post('/planning/visit', plan); notifications.show({ color: 'teal', message: t('planning.visitMoved') }); } catch (err) { notifyError(err); }
    await refetch();
  };

  return (
    <>
      <Card mb="sm" p="sm">
        <Group gap="sm" wrap="wrap">
          <Select size="xs" w={200} placeholder={t('contracts.company')} clearable searchable data={(companies ?? []).map((c) => ({ value: c.id, label: c.name }))}
            value={companyId} onChange={(v) => onFilter({ companyId: v })} />
          <Select size="xs" w={180} placeholder={t('companies.category')} clearable data={(categories ?? []).map((c) => ({ value: c.id, label: c.name }))}
            value={categoryId} onChange={(v) => onFilter({ categoryId: v })} />
          {isFetching && <Loader size="xs" />}
        </Group>
      </Card>
      {!data ? <Loader /> : (
        <DndContext sensors={sensors} onDragEnd={(e) => void onDragEnd(e)}>
          <Group align="flex-start" gap="sm" wrap="nowrap">
            <ScrollArea type="auto" style={{ flex: 1, minWidth: 0 }}>
              <Box style={{ display: 'grid', gridTemplateColumns: `180px repeat(${days.length}, ${colWidth}px)`, width: 180 + days.length * colWidth }}>
                <Box />
                {days.map((d) => (
                  <Text key={d} fz="xs" fw={600} ta="center" py={4} c={d === dayjs().format('YYYY-MM-DD') ? 'brand' : 'dimmed'}
                    style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}>{dayjs(d).format('ddd D MMM')}</Text>
                ))}
                {data.companies.length === 0 && <Text fz="sm" c="dimmed" p="sm" style={{ gridColumn: `1 / span ${days.length + 1}` }}>{t('planning.noVisits')}</Text>}
                {data.companies.map((c) => (
                  <Box key={c.id} style={{ display: 'contents' }}>
                    <Text fz="sm" fw={500} py={6} pe="xs" lineClamp={2} style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}>{c.name}</Text>
                    {days.map((d) => (
                      <VisitCell key={d} id={`${c.id}|${d}`} visits={cells.get(c.id)?.get(d) ?? []} canEdit={canEdit} onOpen={setRecording} />
                    ))}
                  </Box>
                ))}
              </Box>
            </ScrollArea>
            <Card p="xs" w={260} style={{ flex: 'none' }}>
              <Text fw={600} fz="sm">{t('planning.undatedVisits')} · {data.undated.length}</Text>
              <Text fz="xs" c="dimmed" mb={6}>{t('planning.undatedVisitsHint')}</Text>
              <VisitTray visits={data.undated} canEdit={canEdit} onOpen={setRecording} />
            </Card>
          </Group>
        </DndContext>
      )}
      {recording && <RecordVisit visit={recording} canEdit={canEdit} canExec={canExec} onClose={() => setRecording(null)} onSaved={() => { setRecording(null); void refetch(); }} />}
    </>
  );
}

function VisitCell({ id, visits, canEdit, onOpen }: { id: string; visits: Visit[]; canEdit: boolean; onOpen: (v: Visit) => void }) {
  const { setNodeRef, isOver } = useDroppable({ id, disabled: !canEdit });
  return (
    <Stack ref={setNodeRef} gap={4} p={4} mih={64}
      style={{ borderBottom: '1px solid var(--mantine-color-default-border)', borderInlineStart: '1px solid var(--mantine-color-default-border)',
        background: isOver ? 'var(--mantine-color-brand-light)' : undefined }}>
      {visits.map((v) => <VisitCard key={v.lineId} visit={v} canEdit={canEdit} onOpen={onOpen} />)}
    </Stack>
  );
}

function VisitTray({ visits, canEdit, onOpen }: { visits: Visit[]; canEdit: boolean; onOpen: (v: Visit) => void }) {
  const { t } = useTranslation();
  const { setNodeRef } = useDroppable({ id: `any|${TRAY}`, disabled: !canEdit });
  return (
    <ScrollArea.Autosize mah={360}>
      <Stack ref={setNodeRef} gap={4} mih={40}>
        {visits.length === 0 && <Text fz="xs" c="dimmed">{t('planning.trayEmpty')}</Text>}
        {visits.map((v) => <VisitCard key={v.lineId} visit={v} canEdit={canEdit} onOpen={onOpen} />)}
      </Stack>
    </ScrollArea.Autosize>
  );
}

function VisitCard({ visit, canEdit, onOpen }: { visit: Visit; canEdit: boolean; onOpen: (v: Visit) => void }) {
  const { t } = useTranslation();
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({ id: visit.lineId, data: visit, disabled: !canEdit });
  return (
    <Paper ref={setNodeRef} {...listeners} {...attributes} withBorder p={4} radius="sm" shadow={isDragging ? 'md' : undefined}
      onClick={() => { if (Date.now() - lastDragEnd > 300) onOpen(visit); }}
      style={{ cursor: canEdit ? 'grab' : 'pointer', touchAction: canEdit ? 'none' : undefined, background: 'var(--mantine-color-body)', zIndex: isDragging ? 10 : undefined,
        transform: transform ? `translate(${transform.x}px, ${transform.y}px)` : undefined }}>
      <Text fz={11} fw={700} c="brand">{visit.number}</Text>
      <Text fz={11} lineClamp={2}>{visit.title}</Text>
      {(visit.categoryName || visit.person) && <Text fz={10} c="dimmed" lineClamp={1}>{[visit.categoryName, visit.person].filter(Boolean).join(' · ')}</Text>}
      <Text fz={10} c="dimmed">{t('planning.visitHours', { done: fmtQty(visit.hoursDone), planned: fmtQty(visit.plannedHours) })}</Text>
    </Paper>
  );
}

/** Record the visit: hours done, the date and the person (0.17.0 rules: notes only for planners). */
function RecordVisit({ visit, canEdit, canExec, onClose, onSaved }: { visit: Visit; canEdit: boolean; canExec: boolean; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation();
  const { data: w } = useQuery({ queryKey: ['work-order', visit.workOrderId], queryFn: () => get<WorkOrderDetail>(`/work-orders/${visit.workOrderId}`) });
  const line: ContractorLine | undefined = w?.contractors.find((l) => l.id === visit.lineId);
  const [hours, setHours] = useState<number | ''>(visit.hoursDone || '');
  // Empty until hours are done: a done date on a visit with no hours would read as done (the server dates hours done today when empty).
  const [doneOn, setDoneOn] = useState<Date | string | null>(visit.doneOn ?? null);
  const [person, setPerson] = useState<string | null>(null);
  const [notes, setNotes] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const save = async () => {
    if (!line) return;
    setBusy(true);
    try {
      await put(`/work-orders/${visit.workOrderId}/contractors/${line.id}`,
        recordVisitBody(line, { hours: hours === '' ? 0 : hours, doneOn: toIsoDate(doneOn), person, notes }));
      notifications.show({ color: 'teal', message: t('planning.visitRecorded') });
      onSaved();
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Modal opened onClose={onClose} title={t('planning.recordVisit')}>
      {!line ? <Loader /> : (
        <Stack gap="sm">
          <Text fz="sm"><Anchor component={Link} to={`/work-orders/${visit.workOrderId}`}>{visit.number}</Anchor> · {visit.title}</Text>
          <Text fz="sm" c="dimmed">{[visit.companyName, visit.categoryName].filter(Boolean).join(' · ')}</Text>
          <Group grow>
            <NumberInput label={t('contractors.hoursDone')} min={0} decimalScale={2} value={hours} onChange={(v) => setHours(v === '' ? '' : Number(v))} disabled={!canExec && !canEdit} />
            <DateInput label={t('contractors.doneOn')} clearable valueFormat="DD MMM YYYY" maxDate={dayjs().add(1, 'day').toDate()} value={doneOn ? new Date(doneOn) : null}
              onChange={(v) => setDoneOn(v)} disabled={!canExec && !canEdit} />
          </Group>
          {line.contactId ? <TextInput label={t('contractors.person')} value={line.personName ?? ''} disabled />
            : <TextInput label={t('contractors.personName')} value={person ?? line.personName ?? ''} onChange={(e) => setPerson(e.currentTarget.value)} disabled={!canExec && !canEdit} />}
          <Textarea label={t('common.notes')} autosize value={notes ?? line.notes ?? ''} onChange={(e) => setNotes(e.currentTarget.value)} disabled={!canEdit}
            description={!canEdit ? t('planning.notesForPlanners') : undefined} />
          <Group justify="flex-end">
            <Button variant="default" onClick={onClose}>{t('common.cancel')}</Button>
            <Button onClick={() => void save()} loading={busy} disabled={!canExec && !canEdit}>{t('common.save')}</Button>
          </Group>
        </Stack>
      )}
    </Modal>
  );
}
