import { useState } from 'react';
import { ActionIcon, Badge, Button, Card, Group, NumberInput, Select, Stack, Switch, Text, TextInput, Title } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { IconArrowDown, IconArrowUp, IconCamera, IconChevronDown, IconChevronUp, IconPlus, IconShieldCheck, IconTrash } from '@tabler/icons-react';
import { get } from '@/api/client';
import { FOLLOW_UP_KINDS, STEP_TYPES, type FollowUpKind, type MeasurementPointItem, type PmTaskListItem, type StepDetail } from '@/api/types';
import { UnitSelect } from '@/components/ReferencePickers';

export const plainStep = (description: string): StepDetail => ({ description, type: 'Check', photoRequired: false, beforeStart: false, followUp: 'Default' });

/**
 * A work template's steps (0.13.0): each says what it asks for (check, number, meter reading, text), whether it needs a photo or must be answered
 * before the work starts, and what a failed answer raises.
 */
export function TemplateSteps({ value, onChange, editable, error, selfId }: {
  value: StepDetail[]; onChange: (next: StepDetail[]) => void; editable: boolean; error?: React.ReactNode; selfId?: string;
}) {
  const { t } = useTranslation();
  const [text, setText] = useState('');
  const [open, setOpen] = useState<number | null>(null);
  const { data: points } = useQuery({ queryKey: ['measurement-points'], queryFn: () => get<MeasurementPointItem[]>('/measurement-points') });
  const { data: templates } = useQuery({ queryKey: ['pm-tasks'], queryFn: () => get<PmTaskListItem[]>('/pm-tasks') });
  const set = (i: number, patch: Partial<StepDetail>) => onChange(value.map((s, k) => (k === i ? { ...s, ...patch } : s)));
  const move = (i: number, by: number) => {
    const j = i + by;
    if (j < 0 || j >= value.length) return;
    const next = [...value]; [next[i], next[j]] = [next[j], next[i]]; onChange(next);
  };
  const add = () => { if (text.trim()) { onChange([...value, plainStep(text.trim())]); setText(''); } };
  const pointOf = (id?: string | null) => points?.find((p) => p.id === id);
  return (
    <Card><Stack gap={6}>
      <Title order={5}>{t('pm.steps')} ({value.length})</Title>
      <Text fz="xs" c="dimmed">{t('steps.hint')}</Text>
      {value.map((s, i) => (
        <Stack key={i} gap={4} p={6} style={{ border: '1px solid var(--mantine-color-default-border)', borderRadius: 6 }}>
          <Group gap={6} wrap="nowrap">
            <Text fz="sm" c="dimmed" w={24} ta="right">{i + 1}.</Text>
            <TextInput style={{ flex: 1 }} size="xs" value={s.description} disabled={!editable} onChange={(e) => set(i, { description: e.currentTarget.value })} />
            <Badge size="sm" variant="light">{t(`steps.types.${s.type}`)}</Badge>
            {s.beforeStart && <IconShieldCheck size={16} color="var(--mantine-color-orange-6)" aria-label={t('steps.beforeStart')} />}
            {s.photoRequired && <IconCamera size={16} aria-label={t('steps.photoRequired')} />}
            <ActionIcon variant="subtle" color="gray" size="sm" aria-label={t('steps.details')} onClick={() => setOpen(open === i ? null : i)}>
              {open === i ? <IconChevronUp size={14} /> : <IconChevronDown size={14} />}</ActionIcon>
            {editable && <>
              <ActionIcon variant="subtle" color="gray" size="sm" aria-label={t('pm.moveUp')} onClick={() => move(i, -1)}><IconArrowUp size={14} /></ActionIcon>
              <ActionIcon variant="subtle" color="gray" size="sm" aria-label={t('pm.moveDown')} onClick={() => move(i, 1)}><IconArrowDown size={14} /></ActionIcon>
              <ActionIcon variant="subtle" color="red" size="sm" aria-label={t('common.delete')} onClick={() => onChange(value.filter((_, k) => k !== i))}><IconTrash size={14} /></ActionIcon>
            </>}
          </Group>
          {open === i && (
            <Stack gap={6} pl={30}>
              <Group gap="xs" align="flex-end" wrap="wrap">
                <Select w={150} size="xs" label={t('steps.type')} disabled={!editable} data={STEP_TYPES.map((x) => ({ value: x, label: t(`steps.types.${x}`) }))} value={s.type}
                  onChange={(x) => set(i, { type: (x ?? 'Check') as StepDetail['type'], measurementPointId: x === 'Number' ? s.measurementPointId : null })} />
                {s.type === 'Number' && (
                  <Select w={240} size="xs" clearable searchable label={t('steps.point')} placeholder={t('steps.noPoint')} disabled={!editable}
                    data={(points ?? []).filter((p) => p.isActive || p.id === s.measurementPointId).map((p) => ({ value: p.id, label: `${p.code} · ${p.name}` }))}
                    value={s.measurementPointId ?? null} onChange={(x) => set(i, x ? { measurementPointId: x, unit: null, decimals: null } : { measurementPointId: null })} />
                )}
                {s.type === 'Meter' && (
                  <TextInput w={110} size="xs" label={t('steps.meterUnit')} maxLength={20} disabled={!editable}
                    value={s.unit ?? ''} onChange={(e) => set(i, { unit: e.currentTarget.value || null })} />
                )}
                {s.type === 'Number' && !s.measurementPointId && (
                  <UnitSelect w={130} size="xs" label={t('points.unit')} clearable disabled={!editable} value={s.unit ?? null} onChange={(v) => set(i, { unit: v })} />
                )}
                {s.type === 'Number' && !s.measurementPointId && (
                  <NumberInput w={100} size="xs" label={t('points.decimals')} min={0} max={4} disabled={!editable} value={s.decimals ?? ''}
                    onChange={(x) => set(i, { decimals: typeof x === 'number' ? x : null })} />
                )}
                {s.type === 'Number' && s.measurementPointId && (
                  <Text fz="xs" c="dimmed" pb={6}>{t('steps.pointLimits', { unit: pointOf(s.measurementPointId)?.unit ?? '' })}</Text>
                )}
              </Group>
              <Group gap="lg">
                <Switch size="xs" label={t('steps.beforeStart')} disabled={!editable} checked={s.beforeStart} onChange={(e) => set(i, { beforeStart: e.currentTarget.checked })} />
                <Switch size="xs" label={t('steps.photoRequired')} disabled={!editable} checked={s.photoRequired} onChange={(e) => set(i, { photoRequired: e.currentTarget.checked })} />
              </Group>
              <Group gap="xs" align="flex-end" wrap="wrap">
                <Select w={200} size="xs" label={t('steps.followUp')} disabled={!editable} data={FOLLOW_UP_KINDS.map((x) => ({ value: x, label: t(`steps.followUps.${x}`) }))}
                  value={s.followUp} onChange={(x) => set(i, { followUp: (x ?? 'Default') as FollowUpKind, followUpTemplateId: x === 'WorkOrder' ? s.followUpTemplateId : null })} />
                {s.followUp === 'WorkOrder' && (
                  <Select style={{ flex: 1, minWidth: 220 }} size="xs" searchable label={t('steps.followUpTemplate')} disabled={!editable}
                    data={(templates ?? []).filter((x) => x.id !== selfId && (x.isActive || x.id === s.followUpTemplateId)).map((x) => ({ value: x.id, label: `${x.code} · ${x.name}` }))}
                    value={s.followUpTemplateId ?? null} onChange={(x) => set(i, { followUpTemplateId: x })} />
                )}
              </Group>
              <Text fz="xs" c="dimmed">{t(s.type === 'Check' ? 'steps.failsCheck' : s.type === 'Number' ? 'steps.failsNumber' : 'steps.failsNever')}</Text>
            </Stack>
          )}
        </Stack>
      ))}
      {error && <Text c="red" fz="sm">{error}</Text>}
      {editable && <Group gap="xs"><TextInput style={{ flex: 1 }} placeholder={t('pm.addStep')} value={text} onChange={(e) => setText(e.currentTarget.value)}
        onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); add(); } }} />
        <Button variant="light" leftSection={<IconPlus size={14} />} onClick={add}>{t('common.create')}</Button></Group>}
    </Stack></Card>
  );
}
