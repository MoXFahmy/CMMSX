import { useRef, useState } from 'react';
import { ActionIcon, Alert, Badge, Button, Card, Checkbox, Group, NumberInput, SegmentedControl, Select, Stack, Text, TextInput } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { IconCamera, IconChecks, IconLock, IconShieldCheck } from '@tabler/icons-react';
import { answered, failed, levelColor, outOfRange, roundProgress } from '@/components/measure';
import { notifyError } from '@/components/utils';
import { safetyOpen } from './complete';
import type { TechBundle, TechJob, TechPlaces, TechStep } from './types';

type Payload = { isDone?: boolean; answer?: string; value?: number; text?: string; meterId?: string; reading?: string; remarks?: string };

/** What answered() needs from a phone step (a step from an older server has no type: a Check step whose tick is its answer). */
const state = (s: TechStep) => ({
  type: s.type ?? 'Check', answer: s.answer ?? ((s.type ?? 'Check') === 'Check' && s.isDone ? 'Ok' as const : null), value: s.value, textValue: s.textValue, level: s.level,
});

/**
 * The job's checklist on the phone (0.13.0), offline: an answer that fits each step, safety steps first, All OK, a photo on a step, the level of a
 * reading as it is typed, and the follow-up the server raised (after a refresh).
 */
export function Checklist({ job, bundle, meters, stepPhotos, onChange, onPhoto }: {
  job: TechJob; bundle?: TechBundle; meters: TechPlaces['meters']; stepPhotos: Record<string, number>; onChange: (taskId: string, p: Payload) => void; onPhoto: (taskId: string, file: File) => void;
}) {
  const { t } = useTranslation();
  const groups = job.roundAssets.length > 0
    ? job.roundAssets.map((a) => ({ key: a.id, assetId: a.id as string | null, title: `${a.assetNumber} · ${a.name}` as string | null, tasks: job.tasks.filter((x) => x.assetId === a.id) }))
      .concat([{ key: 'other', assetId: null, title: null, tasks: job.tasks.filter((x) => !job.roundAssets.some((a) => a.id === x.assetId)) }]).filter((g) => g.tasks.length > 0)
    : [{ key: 'all', assetId: null as string | null, title: null as string | null, tasks: job.tasks }];
  const allOk = (list: TechStep[]) => list.filter((s) => (s.type ?? 'Check') === 'Check' && !s.beforeStart && !s.photoRequired && !answered(state(s)))
    .forEach((s) => onChange(s.id, { answer: 'Ok' }));
  const done = job.tasks.filter((s) => answered(state(s))).length;
  return (
    <Card withBorder p="sm">
      <Group justify="space-between" mb={6}>
        <Text fw={600}>{t('tech.checklist')} · {done}/{job.tasks.length}</Text>
      </Group>
      {safetyOpen(job.tasks, null).some((s) => !s.assetId) && <Alert color="orange" icon={<IconLock size={16} />} mb="xs" p="xs">{t('checklist.safetyFirstBody')}</Alert>}
      {groups.map((g) => {
        const progress = roundProgress(g.tasks.map(state));
        const blocked = safetyOpen(job.tasks, g.assetId).length > 0;
        const plain = g.tasks.some((s) => (s.type ?? 'Check') === 'Check' && !s.beforeStart && !s.photoRequired && !answered(state(s)));
        return (
          <Stack key={g.key} gap={8} mt={6}>
            {(g.title || plain) && (
              <Group justify="space-between" gap="xs" px={4} py={2} bg={progress === 'red' ? 'var(--mantine-color-red-light)' : progress === 'green' ? 'var(--mantine-color-teal-light)' : undefined}>
                <Text fz="xs" fw={700} c="dimmed">{g.title ?? ''}</Text>
                {plain && !blocked && <Button size="compact-xs" variant="subtle" leftSection={<IconChecks size={12} />} onClick={() => allOk(g.tasks)}>{t('checklist.allOk')}</Button>}
              </Group>
            )}
            {[...g.tasks].sort((a, b) => Number(!!b.beforeStart) - Number(!!a.beforeStart) || a.sequence - b.sequence).map((s) => (
              <PhoneStep key={s.id} s={s} locked={!s.beforeStart && blocked} bundle={bundle} meters={meters} job={job} photos={(stepPhotos[s.id] ?? 0) + job.files.filter((f) => f.workOrderTaskId === s.id).length}
                onChange={(p) => onChange(s.id, p)} onPhoto={(f) => onPhoto(s.id, f)} />
            ))}
          </Stack>
        );
      })}
    </Card>
  );
}

function PhoneStep({ s, locked, bundle, meters: allMeters, job, photos, onChange, onPhoto }: {
  s: TechStep; locked: boolean; bundle?: TechBundle; meters: TechPlaces['meters']; job: TechJob; photos: number; onChange: (p: Payload) => void; onPhoto: (f: File) => void;
}) {
  const { t } = useTranslation();
  const camera = useRef<HTMLInputElement>(null);
  const [remarks, setRemarks] = useState(s.remarks ?? '');
  const [text, setText] = useState(s.textValue ?? '');
  const [meterId, setMeterId] = useState<string | null>(s.meterId ?? null);
  const point = bundle?.points?.find((p) => p.id === s.measurementPointId);
  const type = s.type ?? 'Check';
  const st = state(s);
  const assetId = s.assetId ?? job.asset?.id;
  const meters = allMeters.filter((m) => m.assetId === assetId);
  const unit = s.unit ?? point?.unit ?? (type === 'Meter' ? meters.find((m) => m.id === (s.meterId ?? meterId))?.unit : undefined) ?? '';
  const saveValue = (raw: string | number) => {
    const v = typeof raw === 'number' ? raw : raw.trim() === '' ? null : Number(raw.replace(',', '.'));
    if (v === null) { if (s.value !== null && s.value !== undefined) onChange({ isDone: false }); return; }
    if (Number.isNaN(v) || v === s.value) return;
    if (point && outOfRange(point, v)) { notifyError(new Error(t('checklist.outOfRange', { min: point.validMin ?? '', max: point.validMax ?? '', unit: point.unit ?? '' }))); return; }
    if (type === 'Meter' && !s.meterId && !meterId) { notifyError(new Error(t('checklist.chooseMeter'))); return; }
    onChange({ value: v, meterId: type === 'Meter' && !s.meterId && meterId ? meterId : undefined });
  };
  return (
    <div style={{ opacity: locked ? 0.55 : 1 }}>
      <Group gap={6} wrap="nowrap" align="flex-start">
        {s.beforeStart && <IconShieldCheck size={16} color="var(--mantine-color-orange-6)" style={{ marginTop: 2 }} />}
        {locked && <IconLock size={14} style={{ marginTop: 3 }} />}
        <Text fz="sm" style={{ flex: 1 }}>{s.description}</Text>
        {s.followUpNumber && <Badge size="xs" color="orange">{t('checklist.followUp', { number: s.followUpNumber })}</Badge>}
      </Group>
      <Stack gap={4} mt={4} pl={22}>
        {type === 'Check' && (s.type === undefined
          ? <Checkbox label={t('checklist.ok')} checked={s.isDone} disabled={locked} onChange={(e) => onChange({ isDone: e.currentTarget.checked })} />
          : <SegmentedControl size="xs" fullWidth disabled={locked} value={st.answer ?? ''} color={st.answer === 'NotOk' ? 'red' : st.answer === 'Ok' ? 'teal' : 'gray'}
              data={[{ value: 'Ok', label: t('checklist.ok') }, { value: 'NotOk', label: t('checklist.notOk') }, { value: 'NotApplicable', label: t('checklist.na') }]}
              onChange={(a) => onChange(a === st.answer ? { isDone: false } : { answer: a })} />)}
        {(type === 'Number' || type === 'Meter') && (
          <Group gap="xs" wrap="nowrap">
            {type === 'Meter' && !s.meterId && (
              <Select size="xs" w={150} placeholder={t('checklist.chooseMeter')} data={meters.map((m) => ({ value: m.id, label: `${m.name} (${m.unit})` }))} value={meterId} onChange={setMeterId} disabled={locked} />
            )}
            <NumberInput key={`v:${s.value ?? ''}`} size="xs" style={{ flex: 1 }} inputMode="decimal" disabled={locked} defaultValue={s.value ?? ''} decimalScale={s.decimals ?? point?.decimals ?? 2}
              suffix={unit ? ` ${unit}` : undefined} placeholder={t('checklist.value')} onBlur={(e) => saveValue(e.currentTarget.value.replace(unit, ''))} />
            {st.level && st.level !== 'Normal' && <Badge size="sm" color={levelColor(st.level)}>{t(`checklist.levels.${st.level}`)}</Badge>}
          </Group>
        )}
        {type === 'Text' && (
          <TextInput size="xs" disabled={locked} maxLength={1000} placeholder={t('checklist.answer')} value={text} onChange={(e) => setText(e.currentTarget.value)}
            onBlur={() => { const v = text.trim(); if (v !== (s.textValue ?? '')) onChange(v ? { text: v } : { isDone: false }); }} />
        )}
        <Group gap="xs" wrap="nowrap">
          <TextInput size="xs" style={{ flex: 1 }} placeholder={t('tech.remarks')} maxLength={2000} value={remarks} onChange={(e) => setRemarks(e.currentTarget.value)}
            onBlur={() => { if (remarks !== (s.remarks ?? '')) onChange({ remarks }); }} />
          <ActionIcon variant={s.photoRequired && photos === 0 ? 'filled' : 'light'} color={s.photoRequired && photos === 0 ? 'orange' : 'gray'} aria-label={t('checklist.addPhoto')}
            onClick={() => camera.current?.click()}><IconCamera size={16} /></ActionIcon>
          {photos > 0 && <Badge size="xs" variant="light">{photos}</Badge>}
          <input ref={camera} type="file" accept="image/*" capture="environment" hidden onChange={(e) => { const f = e.currentTarget.files?.[0]; if (f) onPhoto(f); e.currentTarget.value = ''; }} />
        </Group>
        {failed(st) && !s.followUpNumber && <Text fz="xs" c="orange">{t('checklist.followUpOnSync')}</Text>}
      </Stack>
    </div>
  );
}
