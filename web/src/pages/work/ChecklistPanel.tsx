import { useState } from 'react';
import { ActionIcon, Alert, Anchor, Badge, Button, Card, FileButton, Group, NumberInput, Progress, Select, Stack, Text, TextInput, Tooltip } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconCamera, IconChecks, IconLock, IconPhoto, IconPlus, IconShieldCheck, IconTrash } from '@tabler/icons-react';
import { ApiError, api, del, get, post, put } from '@/api/client';
import type { MeterItem, WorkOrderDetail, WorkOrderStep } from '@/api/types';
import { AnswerButtons } from '@/components/AnswerButtons';
import { answered, failed, levelColor, levelOf, outOfRange, rangeMessage, roundProgress, tooBig } from '@/components/measure';
import { confirmAction } from '@/components/confirm';
import { fromNow, notifyError, notifySuccess } from '@/components/utils';
import { pick } from '@/components/bilingual';

type Step = WorkOrderStep;

/** The first message a refused answer carries (field errors first). */
const message = (e: unknown) => (e instanceof ApiError ? Object.values(e.fieldErrors)[0] ?? e.message : e instanceof Error ? e.message : String(e));

/**
 * A work order's checklist (0.13.0): safety steps first (the others wait for them), an answer that fits each step's type, photos on a step,
 * All OK, each round asset's progress in green or red, and the follow-up a failed step raised.
 */
export function ChecklistPanel({ w, canExec, canEdit = false, onChange }: { w: WorkOrderDetail; canExec: boolean; canEdit?: boolean; onChange: () => Promise<void> }) {
  const { t } = useTranslation();
  const [text, setText] = useState('');
  const [textAr, setTextAr] = useState(''); // 0.28.0: the step in Arabic (optional)
  const steps = w.tasks;
  const done = steps.filter(answered).length;
  // Safety steps for a step: those without an asset, and its own asset's on a round.
  const safetyOpen = (assetId?: string | null) => steps.filter((s) => s.beforeStart && (!s.assetId || s.assetId === assetId) && (!answered(s) || failed(s)));
  const add = async () => {
    if (!text.trim()) return;
    try { await post(`/work-orders/${w.id}/tasks`, { description: text, descriptionAr: textAr }); setText(''); setTextAr(''); await onChange(); } catch (e) { notifyError(e); }
  };
  const allOk = async (assetId?: string) => {
    try {
      const r = await post<{ answered: number }>(`/work-orders/${w.id}/tasks/all-ok`, { assetId });
      notifySuccess(t('checklist.allOkDone', { count: r.answered })); await onChange();
    } catch (e) { notifyError(new Error(message(e))); }
  };
  const groups = w.assets.length > 0
    ? [...w.assets.map((a) => ({ asset: a as WorkOrderDetail['assets'][number] | null, steps: steps.filter((x) => x.assetId === a.id) })),
      { asset: null, steps: steps.filter((x) => !w.assets.some((a) => a.id === x.assetId)) }].filter((g) => g.asset || g.steps.length > 0)
    : [{ asset: null, steps }];
  const plainOpen = (list: Step[]) => list.some((s) => s.type === 'Check' && !s.beforeStart && !s.photoRequired && !s.answer);
  const blockedAtStart = safetyOpen(null).length > 0 && steps.some((s) => s.beforeStart && !s.assetId);
  return (
    <Card maw={900}>
      {steps.length > 0 && (
        <Group mb="md" gap="sm">
          <Progress value={(done / steps.length) * 100} style={{ flex: 1 }} color={steps.some(failed) ? 'red' : 'teal'} />
          <Text fz="sm" c="dimmed">{t('wo.tasksDone', { done, total: steps.length })}</Text>
          {canExec && w.assets.length === 0 && plainOpen(steps) && <Button size="xs" variant="light" leftSection={<IconChecks size={14} />} onClick={() => void allOk()}>{t('checklist.allOk')}</Button>}
        </Group>
      )}
      {blockedAtStart && <Alert mb="sm" color="orange" icon={<IconLock size={16} />} title={t('checklist.safetyFirst')}>{t('checklist.safetyFirstBody')}</Alert>}
      <Stack gap={6}>
        {steps.length === 0 && <Text c="dimmed" fz="sm">{t('wo.noTasks')}</Text>}
        {groups.map((g) => {
          const progress = roundProgress(g.steps);
          return (
            <Stack key={g.asset?.id ?? 'other'} gap={0}>
              {g.asset && (
                <Group justify="space-between" wrap="nowrap" px={6} py={8} bg={progress === 'red' ? 'var(--mantine-color-red-light)' : progress === 'green' ? 'var(--mantine-color-teal-light)' : 'var(--mantine-color-default-hover)'}>
                  <Anchor component={Link} to={`/assets/${g.asset.id}`} fz="sm" fw={600} style={{ minWidth: 0 }} truncate>{g.asset.assetNumber} · {g.asset.name}</Anchor>
                  <Group gap="xs" wrap="nowrap">
                    <Text fz="xs" c="dimmed">{t('wo.assetProgress', { done: g.steps.filter(answered).length, total: g.steps.length })}</Text>
                    {canExec && plainOpen(g.steps) && <Button size="compact-xs" variant="subtle" onClick={() => void allOk(g.asset!.id)}>{t('checklist.allOk')}</Button>}
                  </Group>
                </Group>
              )}
              {[...g.steps].sort((a, b) => Number(b.beforeStart) - Number(a.beforeStart) || a.sequence - b.sequence).map((x) => (
                <StepRow key={x.id} w={w} x={x} canExec={canExec} canEdit={canEdit} locked={!x.beforeStart && safetyOpen(x.assetId).length > 0} onChange={onChange} />
              ))}
            </Stack>
          );
        })}
      </Stack>
      {canExec && (
        <Group mt="md" gap="xs"><TextInput style={{ flex: 1 }} placeholder={t('wo.addTask')} value={text} onChange={(e) => setText(e.currentTarget.value)} onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); void add(); } }} />
          <TextInput style={{ flex: 1 }} dir="rtl" placeholder={t('bilingual.stepAr')} aria-label={t('bilingual.stepAr')} value={textAr} onChange={(e) => setTextAr(e.currentTarget.value)} />
          <Button leftSection={<IconPlus size={14} />} onClick={add}>{t('common.create')}</Button></Group>
      )}
    </Card>
  );
}

/** One step: the answer its type asks for, remarks, photos and its follow-up. */
function StepRow({ w, x, canExec, canEdit, locked, onChange }: { w: WorkOrderDetail; x: Step; canExec: boolean; canEdit: boolean; locked: boolean; onChange: () => Promise<void> }) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState(false);
  const point = w.points?.find((p) => p.id === x.measurementPointId);
  const meterAsset = x.assetId ?? w.asset?.id;
  const { data: meters } = useQuery({ queryKey: ['meters', meterAsset ?? 'none'], queryFn: () => get<MeterItem[]>('/meters', { assetId: meterAsset }),
    enabled: x.type === 'Meter' && !x.meterId && !!meterAsset && canExec });
  const [meterId, setMeterId] = useState<string | null>(null);
  const editable = canExec && !locked;
  const send = async (body: Record<string, unknown>) => {
    setBusy(true);
    try {
      const r = await put<{ level?: string | null; followUp?: { kind: string; number: string } | null }>(`/work-orders/${w.id}/tasks/${x.id}`, body);
      if (r.followUp && !x.followUp) notifySuccess(t('checklist.followUpRaised', { number: r.followUp.number }));
      await onChange();
    } catch (e) {
      const m = message(e);
      // A meter's lower or much higher reading asks first, as on the meter form.
      if (x.type === 'Meter' && e instanceof ApiError && e.fieldErrors.value) confirmAction(m, () => send({ ...body, confirm: true }));
      else notifyError(new Error(m));
    } finally { setBusy(false); }
  };
  const saveValue = (raw: string) => {
    const v = raw.trim() === '' ? null : Number(raw.replace(',', '.'));
    if (v === null) { if (x.value !== null && x.value !== undefined) void send({ isDone: false }); return; }
    if (Number.isNaN(v) || v === x.value) return;
    if (tooBig(v)) { notifyError(new Error(t('checklist.valueTooBig'))); return; }
    if (point && outOfRange(point, v)) { notifyError(new Error(rangeMessage(point, t))); return; }
    void send({ value: v, meterId: x.type === 'Meter' && !x.meterId ? meterId : undefined });
  };
  const upload = async (file: File | null) => {
    if (!file) return;
    const fd = new FormData();
    fd.set('entityType', 'WorkOrder'); fd.set('entityId', w.id); fd.set('stepId', x.id); fd.append('files', file);
    try { await api('/attachments', { method: 'POST', body: fd }); await onChange(); } catch (e) { notifyError(e); }
  };
  const unit = x.unit ?? point?.unit ?? '';
  const preview = point && x.value !== null && x.value !== undefined ? levelOf(point, x.value) : null;
  const level = x.level ?? preview;
  return (
    <Stack gap={4} p={6} style={{ borderBottom: '1px solid var(--mantine-color-default-border)', opacity: locked ? 0.6 : 1 }}>
      <Group justify="space-between" wrap="nowrap" align="flex-start">
        <Group gap={6} wrap="nowrap" style={{ minWidth: 0 }}>
          {x.beforeStart && <Tooltip label={t('steps.beforeStart')}><IconShieldCheck size={16} color="var(--mantine-color-orange-6)" /></Tooltip>}
          {locked && <Tooltip label={t('checklist.lockedHint')}><IconLock size={14} /></Tooltip>}
          <Text fz="sm" fw={500}>{x.sequence}. {pick(x.description, x.descriptionAr)}</Text>
          {x.photoRequired && <Badge size="xs" variant="light" color={x.photos.length ? 'teal' : 'gray'} leftSection={<IconCamera size={10} />}>{t('steps.photoRequired')}</Badge>}
        </Group>
        <Group gap={6} wrap="nowrap">
          {x.followUp && <Badge size="sm" color="orange" component={Link} to={x.followUp.kind === 'workOrder' ? `/work-orders/${x.followUp.id}` : `/work-requests/${x.followUp.id}`}
            style={{ cursor: 'pointer' }}>{t('checklist.followUp', { number: x.followUp.number })}</Badge>}
          {x.isDone && <Text fz="xs" c="dimmed" visibleFrom="xs">{x.doneBy} · {fromNow(x.doneAt)}</Text>}
          {canExec && (canEdit || !(x.beforeStart || x.photoRequired)) && <ActionIcon variant="subtle" color="gray" size="sm" aria-label={t('common.delete')} onClick={async () => { try { await del(`/work-orders/${w.id}/tasks/${x.id}`); await onChange(); } catch (e) { notifyError(e); } }}><IconTrash size={14} /></ActionIcon>}
        </Group>
      </Group>
      <Group gap="xs" ps={24} wrap="wrap" align="center">
        {x.type === 'Check' && <AnswerButtons value={x.answer} disabled={!editable || busy} onChange={(c) => void send(c)} />}
        {(x.type === 'Number' || x.type === 'Meter') && (
          <>
            {x.type === 'Meter' && !x.meterId && editable && (
              <Select size="xs" w={200} placeholder={t('checklist.chooseMeter')} data={(meters ?? []).map((m) => ({ value: m.id, label: `${m.code} · ${m.name} (${m.unit})` }))}
                value={meterId} onChange={setMeterId} />
            )}
            {x.type === 'Meter' && x.meterName && <Text fz="xs" c="dimmed">{x.meterName}</Text>}
            <NumberInput key={`v:${x.value ?? ''}`} size="xs" w={150} disabled={!editable || busy} defaultValue={x.value ?? ''} decimalScale={x.decimals ?? point?.decimals ?? 2}
              suffix={unit ? ` ${unit}` : undefined} placeholder={t('checklist.value')} aria-label={t('checklist.value')}
              onBlur={(e) => saveValue(e.currentTarget.value.replace(unit, ''))} onKeyDown={(e) => { if (e.key === 'Enter') (e.currentTarget as HTMLInputElement).blur(); }} />
            {level && <Badge size="sm" color={levelColor(level)}>{t(`checklist.levels.${level}`)}</Badge>}
            {point && <Text fz="xs" c="dimmed">{point.code}</Text>}
          </>
        )}
        {x.type === 'Text' && (
          <TextInput key={`t:${x.textValue ?? ''}`} size="xs" style={{ flex: 1, minWidth: 220 }} disabled={!editable || busy} defaultValue={x.textValue ?? ''} maxLength={1000}
            placeholder={t('checklist.answer')} onBlur={(e) => { const v = e.currentTarget.value.trim(); if (v !== (x.textValue ?? '')) void send(v ? { text: v } : { isDone: false }); }} />
        )}
        <TextInput key={`r:${x.reading ?? ''}`} size="xs" w={130} disabled={!canExec} maxLength={100} placeholder={t('wo.reading')} aria-label={t('wo.reading')}
          defaultValue={x.reading ?? ''} onBlur={(e) => { const v = e.currentTarget.value.trim(); if (v !== (x.reading ?? '')) void send({ reading: v }); }} />
        <TextInput key={`m:${x.remarks ?? ''}`} size="xs" style={{ flex: 1, minWidth: 180 }} disabled={!canExec} placeholder={t('wo.remarks')} aria-label={t('wo.remarks')}
          defaultValue={x.remarks ?? ''} onBlur={(e) => { if ((x.remarks ?? '') !== e.currentTarget.value.trim()) void send({ remarks: e.currentTarget.value }); }} />
        {canExec && <FileButton onChange={(f) => void upload(f)} accept="image/png,image/jpeg,image/webp">
          {(props) => <Tooltip label={t('checklist.addPhoto')}><ActionIcon {...props} variant="subtle" aria-label={t('checklist.addPhoto')}><IconPhoto size={16} /></ActionIcon></Tooltip>}
        </FileButton>}
        {x.photos.map((p) => <Anchor key={p.id} href={`/api/attachments/${p.id}/content`} target="_blank" fz="xs">{p.fileName}</Anchor>)}
      </Group>
    </Stack>
  );
}
