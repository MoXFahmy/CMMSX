import { ActionIcon, Button, Card, Group, MultiSelect, NumberInput, SegmentedControl, Select, Stack, Switch, Table, Text, Title } from '@mantine/core';
import type { UseFormReturnType } from '@mantine/form';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { IconPlus, IconTrash } from '@tabler/icons-react';
import { get } from '@/api/client';
import { ASSET_STATUSES, DAY_NAMES, type MeterItem, type ScheduleCalendarItem } from '@/api/types';
import { spreadBlocks } from './spread';
import type { V } from './PmScheduleFormPage';

type Form = UseFormReturnType<V>;
type Option = { value: string; label: string };

/** Shared calendars and extra meters (0.12.0): more triggers, whichever comes first. */
export function TriggersCard({ form, meters }: { form: Form; meters: MeterItem[] }) {
  const { t } = useTranslation();
  const v = form.values;
  const { data: calendars } = useQuery({ queryKey: ['pm-calendars'], queryFn: () => get<ScheduleCalendarItem[]>('/pm-calendars') });
  const single = v.target === 'asset' && !!v.assetId;
  return (
    <Card><Stack gap="xs">
      <div><Title order={5}>{t('pm12.moreTriggers')}</Title><Text fz="sm" c="dimmed">{t('pm12.moreTriggersHint')}</Text></div>
      <MultiSelect label={t('pm12.calendars')} required={v.type === 'Calendar'} searchable clearable error={form.errors.calendarIds}
        data={(calendars ?? []).filter((c) => c.isActive || v.calendarIds.includes(c.id)).map((c) => ({ value: c.id, label: `${c.code} · ${c.name}` }))}
        value={v.calendarIds} onChange={(x) => form.setFieldValue('calendarIds', x)} description={v.basis === 'Actual' ? t('pm12.calendarsNeedScheduled') : undefined} />
      {single && <>
        {v.extraMeters.map((_, i) => (
          <Group key={i} gap="xs" wrap="nowrap" align="flex-end">
            <Select style={{ flex: 1 }} label={i === 0 ? t('pm12.extraMeter') : undefined} data={meters.filter((x) => x.id !== v.meterId).map((x) => ({ value: x.id, label: `${x.code} · ${x.name} (${x.unit})` }))}
              {...form.getInputProps(`extraMeters.${i}.meterId`)} />
            <NumberInput w={160} label={i === 0 ? t('pm.meterInterval') : undefined} min={0} decimalScale={3} {...form.getInputProps(`extraMeters.${i}.interval`)} />
            <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => form.removeListItem('extraMeters', i)}><IconTrash size={16} /></ActionIcon>
          </Group>
        ))}
        {form.errors.extraMeters && <Text c="red" fz="sm">{form.errors.extraMeters}</Text>}
        {v.extraMeters.length < 5 && meters.length > 0 && (
          <Button variant="subtle" w="fit-content" leftSection={<IconPlus size={14} />} onClick={() => form.insertListItem('extraMeters', { meterId: null, interval: '' })}>{t('pm12.addMeter')}</Button>
        )}
      </>}
    </Stack></Card>
  );
}

/** Who hears about the PM's work, and the PM's own generation rules (0.12.0). */
export function PeopleRulesCard({ form }: { form: Form }) {
  const { t } = useTranslation();
  const v = form.values;
  const { data: people } = useQuery({ queryKey: ['users-lookup'], queryFn: () => get<{ id: string; fullName: string }[]>('/users/lookup'), retry: false });
  return (
    <Card><Stack gap="xs">
      <Title order={5}>{t('pm12.peopleAndRules')}</Title>
      <MultiSelect label={t('pm12.recipients')} description={t('pm12.recipientsHint')} searchable clearable maxValues={20} error={form.errors.recipientIds}
        data={(people ?? []).map((p) => ({ value: p.id, label: p.fullName }))} value={v.recipientIds} onChange={(x) => form.setFieldValue('recipientIds', x)} />
      <Switch mt="xs" label={t('pm12.useOrgRules')} checked={v.useOrgRules} onChange={(e) => form.setFieldValue('useOrgRules', e.currentTarget.checked)} />
      {!v.useOrgRules && <>
        <MultiSelect label={t('pm12.skipStatuses')} data={ASSET_STATUSES.map((x) => ({ value: x, label: t(`status.${x}`) }))} value={v.skipStatusesOverride}
          onChange={(x) => form.setFieldValue('skipStatusesOverride', x)} clearable />
        <MultiSelect label={t('pm12.nonWorkingDays')} data={DAY_NAMES.map((x) => ({ value: x, label: t(`pm.days.${x}`) }))} value={v.nonWorkingDaysOverride}
          onChange={(x) => form.setFieldValue('nonWorkingDaysOverride', x)} error={form.errors.nonWorkingDaysOverride} clearable />
        <Switch label={t('pm12.noStacking')} checked={v.noStackingOverride} onChange={(e) => form.setFieldValue('noStackingOverride', e.currentTarget.checked)} />
      </>}
    </Stack></Card>
  );
}

/** A multi-asset PM (0.12.0): round or group, how the plan reaches a group, even spread, and each asset's own template. */
export function MultiAssetCard({ form, templates, locked }: { form: Form; templates: Option[]; locked: boolean }) {
  const { t } = useTranslation();
  const v = form.values;
  const parts = typeof v.spreadParts === 'number' && v.spreadParts >= 2 ? v.spreadParts : 0;
  const spreadable = v.type === 'FixedInterval' && v.basis === 'Scheduled' && v.calendarIds.length === 0 && v.roundAssets.length >= 2;
  return (
    <Card><Stack gap="sm">
      <Group justify="space-between" wrap="wrap" gap="xs">
        <div><Title order={5}>{t('pm12.multiAsset')}</Title><Text fz="sm" c="dimmed">{t(v.multiAssetMode === 'Group' ? 'pm12.groupHint' : 'pm12.roundHint')}</Text></div>
        <SegmentedControl size="xs" disabled={locked} value={v.multiAssetMode} onChange={(x) => form.setFieldValue('multiAssetMode', x as V['multiAssetMode'])}
          data={[{ value: 'Round', label: t('pm12.round') }, { value: 'Group', label: t('pm12.group') }]} />
      </Group>
      {locked && <Text fz="xs" c="dimmed">{t('pm12.modeLocked')}</Text>}
      {form.errors.multiAssetMode && <Text c="red" fz="sm">{form.errors.multiAssetMode}</Text>}
      {v.multiAssetMode === 'Group' && (
        <SegmentedControl w="fit-content" size="xs" value={v.groupPlan} onChange={(x) => form.setFieldValue('groupPlan', x as V['groupPlan'])}
          data={[{ value: 'Copy', label: t('pm12.planCopy') }, { value: 'Split', label: t('pm12.planSplit') }]} />
      )}
      <Group gap="xs" align="flex-end">
        <NumberInput w={200} label={t('pm12.spread')} min={0} max={v.roundAssets.length} disabled={!spreadable && !parts} placeholder={t('pm12.spreadOff')}
          value={v.spreadParts} onChange={(x) => form.setFieldValue('spreadParts', typeof x === 'number' ? x : '')} error={form.errors.spreadParts} />
        <Text fz="xs" c="dimmed" pb={8}>{parts ? t('pm12.spreadPreview', { sizes: spreadBlocks(v.roundAssets.length, parts).join(' · ') }) : t('pm12.spreadHint')}</Text>
      </Group>
      {v.roundAssets.length > 0 && (
        <Table verticalSpacing={4}><Table.Tbody>
          {v.roundAssets.map((a) => (
            <Table.Tr key={a.id}>
              <Table.Td><Text fz="sm">{a.assetNumber} · {a.name}</Text></Table.Td>
              <Table.Td w={280}><Select size="xs" clearable searchable placeholder={t('pm12.pmTemplate')} data={templates}
                value={v.assetTemplates[a.id] ?? null} onChange={(x) => form.setFieldValue('assetTemplates', { ...v.assetTemplates, [a.id]: x })} /></Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody></Table>
      )}
    </Stack></Card>
  );
}
