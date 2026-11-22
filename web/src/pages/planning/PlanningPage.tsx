import { useMemo } from 'react';
import { ActionIcon, Box, Button, Card, Group, Loader, Paper, ScrollArea, SegmentedControl, Select, Stack, Text, Tooltip } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { DndContext, PointerSensor, useDraggable, useDroppable, useSensor, useSensors, type DragEndEvent } from '@dnd-kit/core';
import { IconChevronLeft, IconChevronRight, IconPlayerPlay } from '@tabler/icons-react';
import { get, post } from '@/api/client';
import type { Lookup } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { useHierarchy } from '@/components/LocationPicker';
import { priorityColors } from '@/components/WorkBadges';
import { SlaText } from '@/components/SlaBadge';
import { notifyError } from '@/components/utils';
import { generateNow } from '@/pages/pm/generateNow';
import { useTeams, teamOptions } from '@/components/teams';
import { capacityOn, cellsByAsset, cellsByPerson, days as dayList, isPm, load, NO_ASSET, planMove, TRAY, UNASSIGNED, type Board, type BoardJob, type ForecastRow, type Move } from './board';

/** When the last drag ended: the click that ends a drag must not also open the work order. */
let lastDragEnd = 0;
/** What a card being dragged carries: the job and the row it was dragged from (a person, UNASSIGNED, or a tray). */
type Dragged = { job: BoardJob; row: string };

/** The planning board: open work by day and person (or asset); drag a card to another day or person. */
export function PlanningPage() {
  const { t } = useTranslation();
  const { can, terms } = useAuth();
  const [params, setParams] = useSearchParams();
  const view = params.get('view') === 'asset' ? 'asset' : 'person';
  const span = params.get('span') === 'month' ? 'month' : 'week';
  const anchor = params.get('from') ?? dayjs().format('YYYY-MM-DD');
  const from = span === 'month' ? dayjs(anchor).startOf('month') : dayjs(anchor);
  const to = span === 'month' ? from.endOf('month') : from.add(6, 'day');
  const [fromIso, toIso] = [from.format('YYYY-MM-DD'), to.format('YYYY-MM-DD')];
  const siteId = params.get('siteId');
  const tradeId = params.get('tradeId');
  const teamId = params.get('teamId');
  const set = (patch: Record<string, string | null>) => {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(patch)) { if (v) next.set(k, v); else next.delete(k); }
    setParams(next, { replace: true });
  };

  const { data: h } = useHierarchy();
  const { data: trades } = useQuery({ queryKey: ['trades'], queryFn: () => get<Lookup[]>('/trades') });
  const { data: teams } = useTeams(siteId);
  const { data, refetch, isFetching } = useQuery({
    queryKey: ['planning-board', fromIso, toIso, siteId, tradeId, teamId], placeholderData: (prev) => prev,
    queryFn: () => get<Board>('/planning/board', { from: fromIso, to: toIso, siteId, tradeId, teamId }),
  });
  const canEdit = can(P.woEdit);
  const days = useMemo(() => dayList(fromIso, toIso), [fromIso, toIso]);
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }));

  const send = async (m: Move, undo?: Move) => {
    try {
      const r = await post<{ warning?: string | null }>('/planning/move', m);
      await refetch();
      const id = `move-${Date.now()}`;
      notifications.show({ id,
        color: r.warning ? 'orange' : 'teal', message: (
          <Group gap="xs" justify="space-between" wrap="nowrap">
            <Text fz="sm">{r.warning ?? t('planning.moved')}</Text>
            {undo && <Button size="compact-xs" variant="subtle" onClick={() => { notifications.hide(id); void send(undo); }}>{t('planning.undo')}</Button>}
          </Group>
        ), autoClose: 6000,
      });
    } catch (e) { notifyError(e); await refetch(); }
  };

  const onDragEnd = (e: DragEndEvent) => {
    lastDragEnd = Date.now();
    const dragged = e.active.data.current as Dragged | undefined;
    const target = e.over?.id ? String(e.over.id).split('|') : null; // "row|day"
    if (!dragged || !target) return;
    const [row, day] = target;
    const plan = planMove(dragged.job, dragged.row, row, day, view);
    if (plan) void send(plan.move, plan.undo);
  };

  const byPerson = useMemo(() => (data ? cellsByPerson(data) : new Map()), [data]);
  const byAsset = useMemo(() => (data ? cellsByAsset(data) : new Map()), [data]);
  const hours = useMemo(() => (data ? load(data) : new Map<string, Map<string, number>>()), [data]);
  const rows: { key: string; label: string }[] = view === 'person'
    ? [{ key: UNASSIGNED, label: t('planning.unassigned') }, ...(data?.people ?? []).map((p) => ({ key: p.id, label: p.name }))]
    : assetRows(data, t('planning.noAsset'));
  const cells = view === 'person' ? byPerson : byAsset;
  const colWidth = span === 'week' ? 150 : 110;
  const shift = (n: number) => set({ from: (span === 'month' ? from.add(n, 'month') : from.add(n * 7, 'day')).format('YYYY-MM-DD') });

  return (
    <>
      <PageHeader title={t('planning.title')} subtitle={t('planning.subtitle')} />
      <Card mb="sm" p="sm">
        <Group gap="sm" wrap="wrap">
          <SegmentedControl size="xs" value={view} onChange={(v) => set({ view: v === 'asset' ? 'asset' : null })}
            data={[{ value: 'person', label: t('planning.byPerson') }, { value: 'asset', label: t('planning.byAsset', { asset: terms.asset }) }]} />
          <SegmentedControl size="xs" value={span} onChange={(v) => set({ span: v === 'month' ? 'month' : null })}
            data={[{ value: 'week', label: t('planning.week') }, { value: 'month', label: t('planning.month') }]} />
          <Group gap={4}>
            <ActionIcon variant="default" onClick={() => shift(-1)} aria-label={t('planning.previous')}><IconChevronLeft size={16} className="mirror-rtl" /></ActionIcon>
            <Button size="xs" variant="default" onClick={() => set({ from: null })}>{t('planning.today')}</Button>
            <ActionIcon variant="default" onClick={() => shift(1)} aria-label={t('planning.next')}><IconChevronRight size={16} className="mirror-rtl" /></ActionIcon>
          </Group>
          <Text fw={600} fz="sm">{span === 'month' ? from.format('MMMM YYYY') : `${from.format('D MMM')} – ${to.format('D MMM YYYY')}`}</Text>
          <Select size="xs" w={180} placeholder={terms.site} clearable searchable data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))}
            value={siteId} onChange={(v) => set({ siteId: v })} />
          <Select size="xs" w={160} placeholder={t('wo.trade')} clearable data={(trades ?? []).filter((x) => x.isActive).map((x) => ({ value: x.id, label: x.name }))}
            value={tradeId} onChange={(v) => set({ tradeId: v })} />
          {(teams?.length ?? 0) > 0 && (
            <Select size="xs" w={160} placeholder={t('teams.team')} clearable data={teamOptions(teams, teamId)} value={teamId} onChange={(v) => set({ teamId: v })} />
          )}
          {isFetching && <Loader size="xs" />}
        </Group>
      </Card>
      {!data ? <Loader /> : (
        <DndContext sensors={sensors} onDragEnd={onDragEnd}>
          <Group align="flex-start" gap="sm" wrap="nowrap">
            <ScrollArea type="auto" style={{ flex: 1, minWidth: 0 }}>
              <Box style={{ display: 'grid', gridTemplateColumns: `180px repeat(${days.length}, ${colWidth}px)`, width: 180 + days.length * colWidth }}>
                <Box />
                {days.map((d) => (
                  <Text key={d} fz="xs" fw={600} ta="center" py={4} c={d === dayjs().format('YYYY-MM-DD') ? 'brand' : 'dimmed'}
                    style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}>{dayjs(d).format('ddd D MMM')}</Text>
                ))}
                {rows.map((r) => (
                  <Row key={r.key} label={r.label} rowKey={r.key} days={days} cells={cells.get(r.key)} capacity={view === 'person' && r.key !== UNASSIGNED ? (d: string) => capacityOn(data, r.key, d) : undefined}
                    hours={hours.get(r.key)} canEdit={canEdit} onGenerated={() => void refetch()} canGenerate={can(P.pmGenerate)} />
                ))}
              </Box>
            </ScrollArea>
            <Stack w={260} gap="sm" style={{ flex: 'none' }}>
              <Tray title={t('planning.earlier')} hint={t('planning.earlierHint')} jobs={data.earlier} canEdit={canEdit} />
              <Tray title={t('planning.unscheduled')} hint={t('planning.unscheduledHint')} jobs={data.unscheduled} canEdit={canEdit} />
            </Stack>
          </Group>
        </DndContext>
      )}
    </>
  );
}

/** Asset rows: every asset with work or upcoming PMs in the range, then work without an asset. */
function assetRows(data: Board | undefined, noAsset: string) {
  const seen = new Map<string, string>();
  for (const j of data?.jobs ?? []) if (j.day) seen.set(j.assetId ?? NO_ASSET, j.assetId ? `${j.assetNumber} · ${j.assetName}` : noAsset);
  // A PM only names a row nobody else has named (its rows carry the asset number only).
  for (const p of data?.pms ?? []) if (!seen.has(p.assetId ?? NO_ASSET)) seen.set(p.assetId ?? NO_ASSET, p.assetId ? `${p.assetNumber ?? ''}` : noAsset);
  return [...seen.entries()].map(([key, label]) => ({ key, label })).sort((a, b) => (a.key === NO_ASSET ? 1 : b.key === NO_ASSET ? -1 : a.label.localeCompare(b.label)));
}

function Row({ label, rowKey, days, cells, capacity, hours, canEdit, canGenerate, onGenerated }: {
  label: string; rowKey: string; days: string[]; cells?: Map<string, (BoardJob | ForecastRow)[]>; capacity?: (day: string) => number; hours?: Map<string, number>;
  canEdit: boolean; canGenerate: boolean; onGenerated: () => void;
}) {
  return (
    <>
      <Text fz="sm" fw={500} py={6} pr="xs" lineClamp={2} style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}>{label}</Text>
      {days.map((d) => (
        <Cell key={d} id={`${rowKey}|${d}`} items={cells?.get(d) ?? []} rowKey={rowKey} capacity={capacity?.(d)} hours={hours?.get(d)}
          canEdit={canEdit} canGenerate={canGenerate} onGenerated={onGenerated} />
      ))}
    </>
  );
}

function Cell({ id, items, rowKey, capacity, hours, canEdit, canGenerate, onGenerated }: {
  id: string; items: (BoardJob | ForecastRow)[]; rowKey: string; capacity?: number; hours?: number; canEdit: boolean; canGenerate: boolean; onGenerated: () => void;
}) {
  const { t } = useTranslation();
  const { setNodeRef, isOver } = useDroppable({ id, disabled: !canEdit });
  const over = capacity !== undefined && (hours ?? 0) > capacity;
  return (
    <Stack ref={setNodeRef} gap={4} p={4} mih={64}
      style={{ borderBottom: '1px solid var(--mantine-color-default-border)', borderLeft: '1px solid var(--mantine-color-default-border)',
        background: isOver ? 'var(--mantine-color-brand-light)' : over ? 'var(--mantine-color-red-light)' : undefined }}>
      {capacity !== undefined && (hours ?? 0) > 0 && (
        <Text fz={10} c={over ? 'red' : 'dimmed'} fw={over ? 700 : undefined} ta="right">{t('planning.load', { hours: Math.round((hours ?? 0) * 10) / 10, capacity })}</Text>
      )}
      {items.map((x) => (isPm(x)
        ? <PmCard key={`pm-${x.id}-${x.date}`} pm={x} canGenerate={canGenerate} onGenerated={onGenerated} />
        : <JobCard key={x.id} job={x} row={rowKey} canEdit={canEdit} />))}
    </Stack>
  );
}

function JobCard({ job, row, canEdit }: { job: BoardJob; row: string; canEdit: boolean }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({ id: `${job.id}|${row}`, data: { job, row } satisfies Dragged, disabled: !canEdit });
  const late = job.sla.completion === 'Late' || job.sla.response === 'Late';
  return (
    <Paper ref={setNodeRef} {...listeners} {...attributes} withBorder p={4} radius="sm" shadow={isDragging ? 'md' : undefined}
      onClick={() => { if (Date.now() - lastDragEnd > 300) navigate(`/work-orders/${job.id}`); }}
      // touch-action: a finger on a card drags it instead of scrolling the page (tablets).
      style={{ borderLeft: `3px solid var(--mantine-color-${priorityColors[job.priority]}-6)`, cursor: canEdit ? 'grab' : 'pointer', opacity: isDragging ? 0.8 : 1,
        touchAction: canEdit ? 'none' : undefined,
        transform: transform ? `translate(${transform.x}px, ${transform.y}px)` : undefined, zIndex: isDragging ? 10 : undefined, background: 'var(--mantine-color-body)' }}>
      <Group justify="space-between" gap={4} wrap="nowrap">
        <Text fz={11} fw={700} c={late ? 'red' : 'brand'}>{job.number}</Text>
        <Text fz={9} c="dimmed" tt="uppercase">{t(`woStatus.${job.status}`)}</Text>
      </Group>
      <Text fz={11} lineClamp={2}>{job.title}</Text>
      {(job.sla.completion === 'Late' || job.sla.completion === 'DueSoon') && <SlaText fz="10px" deadline={job.dueAt} state={job.sla.completion} />}
    </Paper>
  );
}

function PmCard({ pm, canGenerate, onGenerated }: { pm: ForecastRow; canGenerate: boolean; onGenerated: () => void }) {
  const { t } = useTranslation();
  return (
    <Paper p={4} radius="sm" style={{ border: '1px dashed var(--mantine-color-gray-5)' }}>
      <Text fz={11} fw={600} lineClamp={1}>{pm.code} · {pm.name}</Text>
      {pm.taskName && <Text fz={10} c="dimmed" lineClamp={1}>{pm.taskName}</Text>}
      {canGenerate && (
        <Tooltip label={t('pm.generateNow')}>
          <ActionIcon size="xs" variant="subtle" onClick={() => generateNow(pm.id, pm.date, onGenerated)} aria-label={t('pm.generateNow')}><IconPlayerPlay size={12} /></ActionIcon>
        </Tooltip>
      )}
    </Paper>
  );
}

function Tray({ title, hint, jobs, canEdit }: { title: string; hint: string; jobs: BoardJob[]; canEdit: boolean }) {
  const { t } = useTranslation();
  return (
    <Card p="xs">
      <Text fw={600} fz="sm">{title} · {jobs.length}</Text>
      <Text fz="xs" c="dimmed" mb={6}>{hint}</Text>
      <ScrollArea.Autosize mah={360}>
        <Stack gap={4}>
          {jobs.length === 0 && <Text fz="xs" c="dimmed">{t('planning.trayEmpty')}</Text>}
          {jobs.map((j) => (
            <Box key={j.id}>
              <JobCard job={j} row={TRAY} canEdit={canEdit} />
              {j.day && <Text fz={10} c="dimmed" pl={4}>{t('planning.wasPlanned', { date: dayjs(j.day).format('D MMM') })}</Text>}
            </Box>
          ))}
        </Stack>
      </ScrollArea.Autosize>
    </Card>
  );
}
