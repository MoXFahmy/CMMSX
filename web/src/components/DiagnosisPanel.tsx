import { useState } from 'react';
import { Accordion, ActionIcon, Alert, Anchor, Badge, Button, Card, Group, List, Stack, Text, Title, Tooltip } from '@mantine/core';
import { IconSparkles, IconThumbDown, IconThumbUp } from '@tabler/icons-react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { get, post, put } from '@/api/client';
import { aiOn, useAiStatus } from './aiStatus';
import { confidenceColor, nextVote, toPlan, type Diagnosis } from './diagnosis';
import { fmtDateTime, fmtQty, notifyError, notifySuccess } from './utils';

/** One diagnosis: what is known and seen, the probable cause, ranked actions with their sources, and the thumbs. */
export function DiagnosisView({ d, canPlan, plannedPartIds, onVote, onPlan }: {
  d: Diagnosis; canPlan: boolean; plannedPartIds: string[];
  onVote?: (up: boolean | null) => void; onPlan?: (action: Diagnosis['actions'][number]) => void;
}) {
  const { t } = useTranslation();
  if (d.historyTooThin) return <Alert color="gray" variant="light">{d.message ?? t('ai.tooThin')}</Alert>;
  return (
    <Stack gap="sm">
      {d.context && <Text fz="sm"><b>{t('ai.context')}:</b> {d.context}</Text>}
      {d.observed && <Text fz="sm"><b>{t('ai.observed')}:</b> {d.observed}</Text>}
      {d.probableCause && (
        <Group gap="xs" wrap="nowrap" align="flex-start">
          <Text fz="sm"><b>{t('ai.probableCause')}:</b> {d.probableCause}</Text>
          {d.confidence && <Badge variant="light" color={confidenceColor(d.confidence)}>{t(`ai.confidence.${d.confidence}`)}</Badge>}
        </Group>
      )}
      {d.actions.map((a, i) => {
        const plan = toPlan(a, plannedPartIds);
        return (
          <Card key={`${i}-${a.title}`} withBorder padding="sm"><Stack gap={4}>
            <Text fw={600} fz="sm">{i + 1}. {a.title}</Text>
            {a.why && <Text fz="sm" c="dimmed">{a.why}</Text>}
            {a.parts.length > 0 && <List size="sm">{a.parts.map((p) => <List.Item key={p.partId}>{fmtQty(p.quantity)} {p.unitOfMeasure ?? ''} × {p.partNumber} · {p.name}</List.Item>)}</List>}
            {a.hours ? <Text fz="sm">{t('ai.estimatedHours', { hours: a.hours })}</Text> : null}
            {a.sources.length > 0 && (
              <Group gap={6}><Text fz="xs" c="dimmed">{t('ai.sources')}:</Text>
                {a.sources.map((s) => <Tooltip key={s.id} label={s.title}><Anchor component={Link} to={`/work-orders/${s.id}`} fz="xs">{s.number}</Anchor></Tooltip>)}
              </Group>
            )}
            {canPlan && plan.length > 0 && onPlan && (
              <Group justify="flex-end"><Button size="compact-xs" variant="light" onClick={() => onPlan(a)}>{t('ai.addToPlanned')}</Button></Group>
            )}
          </Stack></Card>
        );
      })}
      {d.id && (
        <Group gap="xs">
          <Text fz="xs" c="dimmed">{t('ai.wasHelpful')}</Text>
          <ActionIcon variant={d.myVote === true ? 'filled' : 'subtle'} color="teal" aria-label={t('ai.helpful')} aria-pressed={d.myVote === true}
            onClick={() => onVote?.(nextVote(d.myVote, true))}><IconThumbUp size={16} /></ActionIcon>
          <Text fz="xs">{d.up}</Text>
          <ActionIcon variant={d.myVote === false ? 'filled' : 'subtle'} color="red" aria-label={t('ai.notHelpful')} aria-pressed={d.myVote === false}
            onClick={() => onVote?.(nextVote(d.myVote, false))}><IconThumbDown size={16} /></ActionIcon>
          <Text fz="xs">{d.down}</Text>
        </Group>
      )}
    </Stack>
  );
}

/**
 * Diagnose (0.15.0) on a work order or a request with an asset: Claude reads the asset's history; the latest diagnosis shows,
 * earlier ones fold away. Work orders can copy an action's parts onto their planned parts.
 */
export function DiagnosisPanel({ kind, id, hasAsset, canRun, canPlan, plannedPartIds = [], onPlanned }: {
  kind: 'work-order' | 'request'; id: string; hasAsset: boolean; canRun: boolean; canPlan: boolean; plannedPartIds?: string[]; onPlanned?: () => Promise<void>;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data: status } = useAiStatus();
  const base = kind === 'work-order' ? `/work-orders/${id}` : `/work-requests/${id}`;
  const key = ['diagnoses', kind, id];
  const { data: list } = useQuery({ queryKey: key, queryFn: () => get<Diagnosis[]>(`${base}/diagnoses`) });
  const [running, setRunning] = useState(false);
  const [thin, setThin] = useState<Diagnosis | null>(null);
  if (!aiOn(status) && !list?.length) return null;
  const [latest, ...earlier] = list ?? [];
  const run = async () => {
    setRunning(true);
    try {
      const d = await post<Diagnosis>(`${base}/diagnose`);
      setThin(d.id ? null : d);
      await qc.invalidateQueries({ queryKey: key });
      await qc.invalidateQueries({ queryKey: ['ai-status'] });
    } catch (e) { notifyError(e); } finally { setRunning(false); }
  };
  const vote = async (d: Diagnosis, up: boolean | null) => {
    try { await put(`/ai/diagnoses/${d.id}/vote`, { up }); await qc.invalidateQueries({ queryKey: key }); } catch (e) { notifyError(e); }
  };
  const plan = async (a: Diagnosis['actions'][number]) => {
    let added = 0;
    for (const p of toPlan(a, plannedPartIds)) {
      try { await post(`/work-orders/${id}/planned-parts`, { partId: p.partId, quantity: p.quantity }); added++; } catch (e) { notifyError(e); }
    }
    if (added > 0) { notifySuccess(t('ai.planned', { count: added })); await onPlanned?.(); }
  };
  const view = (d: Diagnosis) => (
    <DiagnosisView d={d} canPlan={canPlan && kind === 'work-order'} plannedPartIds={plannedPartIds} onVote={(up) => void vote(d, up)} onPlan={(a) => void plan(a)} />
  );
  return (
    <Card><Stack>
      <Group justify="space-between">
        <Title order={5}>{t('ai.diagnosis')}</Title>
        {aiOn(status) && canRun && (
          <Tooltip label={t('ai.needsAsset')} disabled={hasAsset}>
            <Button size="xs" variant="light" color="violet" leftSection={<IconSparkles size={14} />} loading={running} disabled={!hasAsset} onClick={() => void run()}>
              {latest ? t('ai.runAgain') : t('ai.diagnose')}
            </Button>
          </Tooltip>
        )}
      </Group>
      {running && <Text fz="sm" c="dimmed">{t('ai.reading')}</Text>}
      {thin && view(thin)}
      {latest && <><Text fz="xs" c="dimmed">{latest.createdByName} · {fmtDateTime(latest.createdAt)} · {t('ai.sent', { count: latest.sentCount })}</Text>{view(latest)}</>}
      {earlier.length > 0 && (
        <Accordion variant="contained">
          <Accordion.Item value="earlier">
            <Accordion.Control>{t('ai.earlier', { count: earlier.length })}</Accordion.Control>
            <Accordion.Panel><Stack>{earlier.map((d) => (
              <Stack key={d.id} gap={4}><Text fz="xs" c="dimmed">{d.createdByName} · {fmtDateTime(d.createdAt)}</Text>{view(d)}</Stack>
            ))}</Stack></Accordion.Panel>
          </Accordion.Item>
        </Accordion>
      )}
    </Stack></Card>
  );
}
