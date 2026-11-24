import { useState } from 'react';
import { Anchor, Badge, Button, Card, Checkbox, Group, Modal, NumberInput, Select, SimpleGrid, Stack, Text, TextInput, Title } from '@mantine/core';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { DataTable } from 'mantine-datatable';
import { IconPlus } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import { MEASUREMENT_KINDS, type MeasurementKind, type MeasurementPointDetail, type MeasurementPointItem, type PmTaskListItem } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { AssetListEditor, type AssetRef } from '@/components/AssetListEditor';
import { confirmDelete } from '@/components/confirm';
import { handleFormError, notifyError, notifySuccess } from '@/components/utils';
import { UnitSelect } from '@/components/ReferencePickers';

type N = number | '';
type F = {
  code: string; name: string; kind: MeasurementKind; unit: string; decimals: N; validMin: N; validMax: N; alarmLow: N; alertLow: N; target: N; alertHigh: N; alarmHigh: N;
  alarmLowTemplateId: string | null; alertLowTemplateId: string | null; alertHighTemplateId: string | null; alarmHighTemplateId: string | null; assets: AssetRef[]; isActive: boolean;
};
const blank: F = {
  code: '', name: '', kind: 'Temperature', unit: '°C', decimals: 1, validMin: '', validMax: '', alarmLow: '', alertLow: '', target: '', alertHigh: '', alarmHigh: '',
  alarmLowTemplateId: null, alertLowTemplateId: null, alertHighTemplateId: null, alarmHighTemplateId: null, assets: [], isActive: true,
};
const num = (x: N) => (x === '' ? null : x);
const LEVELS = ['alarmHigh', 'alertHigh', 'target', 'alertLow', 'alarmLow'] as const;
const show = (x?: number | null) => (x === null || x === undefined ? '—' : String(x));

/** PM › Measurement points (0.13.0): what Number steps measure, the values possible at all, and up to five levels with their follow-up templates. */
export function MeasurementPointsTab() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const editable = can(P.pmManage);
  const qc = useQueryClient();
  const { data, isFetching } = useQuery({ queryKey: ['measurement-points'], queryFn: () => get<MeasurementPointItem[]>('/measurement-points') });
  const { data: templates } = useQuery({ queryKey: ['pm-tasks'], queryFn: () => get<PmTaskListItem[]>('/pm-tasks') });
  const templateOptions = (templates ?? []).map((x) => ({ value: x.id, label: `${x.code} · ${x.name}` }));
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<MeasurementPointDetail | null>(null);
  const form = useForm<F>({ initialValues: blank });
  const show2 = async (id: string | null) => {
    form.clearErrors();
    if (!id) { setEditing(null); form.setValues(blank); setOpen(true); return; }
    try {
      const p = await get<MeasurementPointDetail>(`/measurement-points/${id}`);
      setEditing(p);
      const n = (x?: number | null): N => (x === null || x === undefined ? '' : x);
      form.setValues({
        code: p.code, name: p.name, kind: p.kind, unit: p.unit ?? '', decimals: p.decimals, validMin: n(p.validMin), validMax: n(p.validMax), alarmLow: n(p.alarmLow),
        alertLow: n(p.alertLow), target: n(p.target), alertHigh: n(p.alertHigh), alarmHigh: n(p.alarmHigh), alarmLowTemplateId: p.alarmLowTemplateId ?? null,
        alertLowTemplateId: p.alertLowTemplateId ?? null, alertHighTemplateId: p.alertHighTemplateId ?? null, alarmHighTemplateId: p.alarmHighTemplateId ?? null,
        assets: p.assets.map((a) => ({ id: a.id, assetNumber: a.assetNumber, name: a.name })), isActive: p.isActive,
      });
      setOpen(true);
    } catch (e) { notifyError(e); }
  };
  const submit = form.onSubmit(async (v) => {
    const body = {
      ...v, decimals: v.decimals === '' ? 1 : v.decimals, validMin: num(v.validMin), validMax: num(v.validMax), alarmLow: num(v.alarmLow), alertLow: num(v.alertLow),
      target: num(v.target), alertHigh: num(v.alertHigh), alarmHigh: num(v.alarmHigh), assetIds: v.assets.map((a) => a.id), assets: undefined, rowVersion: editing?.rowVersion,
    };
    try {
      if (editing) await put(`/measurement-points/${editing.id}`, body); else await post('/measurement-points', body);
      notifySuccess(t('common.saved')); setOpen(false); await qc.invalidateQueries({ queryKey: ['measurement-points'] });
    } catch (e) { handleFormError(form, e); }
  });
  const v = form.values;
  return (
    <Card p={0}>
      <Group justify="space-between" p="md">
        <div><Title order={5}>{t('points.title')}</Title><Text fz="sm" c="dimmed">{t('points.hint')}</Text></div>
        {editable && <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => show2(null)}>{t('points.new')}</Button>}
      </Group>
      <DataTable minHeight={160} fetching={isFetching} records={data ?? []} noRecordsText={t('common.noRecords')} highlightOnHover rowClassName={() => 'clickable-row'}
        onRowClick={({ record }) => show2(record.id)}
        columns={[
          { accessor: 'code', title: t('pm.code'), render: (p) => <Text fw={600} fz="sm">{p.code}</Text> },
          { accessor: 'name', title: t('common.name') },
          { accessor: 'kind', title: t('points.kind'), render: (p) => t(`points.kinds.${p.kind}`) },
          { accessor: 'unit', title: t('points.unit'), render: (p) => p.unit ?? '—' },
          { accessor: 'levels', title: t('points.levels'), render: (p) => <Text fz="xs" c="dimmed">{show(p.alarmLow)} · {show(p.alertLow)} · {show(p.target)} · {show(p.alertHigh)} · {show(p.alarmHigh)}</Text> },
          { accessor: 'assetCount', title: t('points.assets'), textAlign: 'right' },
          { accessor: 'readingCount', title: t('points.readings'), textAlign: 'right' },
          { accessor: 'isActive', title: t('common.status'), render: (p) => <Badge color={p.isActive ? 'teal' : 'gray'}>{p.isActive ? t('common.active') : t('common.inactive')}</Badge> },
        ]} />
      <Modal opened={open} onClose={() => setOpen(false)} title={editing ? `${t('points.edit')} · ${editing.code}` : t('points.new')} size="xl">
        <form onSubmit={submit}><Stack>
          <SimpleGrid cols={{ base: 1, sm: 3 }}>
            <TextInput label={t('pm.code')} required {...form.getInputProps('code')} />
            <TextInput label={t('common.name')} required style={{ gridColumn: 'span 2' }} {...form.getInputProps('name')} />
            <Select label={t('points.kind')} data={MEASUREMENT_KINDS.map((k) => ({ value: k, label: t(`points.kinds.${k}`) }))} {...form.getInputProps('kind')} />
            <UnitSelect label={t('points.unit')} clearable value={form.values.unit || null} onChange={(v) => form.setFieldValue('unit', v ?? '')} />
            <NumberInput label={t('points.decimals')} min={0} max={4} {...form.getInputProps('decimals')} />
          </SimpleGrid>
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <NumberInput label={t('points.validMin')} description={t('points.validHint')} decimalScale={4} {...form.getInputProps('validMin')} />
            <NumberInput label={t('points.validMax')} decimalScale={4} {...form.getInputProps('validMax')} error={form.errors.validMax} />
          </SimpleGrid>
          <Stack gap={6}>
            <Text fz="sm" fw={500}>{t('points.levels')}</Text>
            <Text fz="xs" c="dimmed">{t('points.levelsHint')}</Text>
            {LEVELS.map((level) => (
              <Group key={level} gap="xs" align="flex-end" wrap="nowrap">
                <NumberInput w={160} label={t(`points.level.${level}`)} decimalScale={4} {...form.getInputProps(level)} />
                {level !== 'target' && (
                  <Select style={{ flex: 1 }} clearable searchable label={t('points.levelTemplate')} placeholder={t('points.noTemplate')} data={templateOptions}
                    {...form.getInputProps(`${level}TemplateId`)} />
                )}
              </Group>
            ))}
          </Stack>
          <Stack gap={4}>
            <Text fz="sm" fw={500}>{t('points.assets')}</Text>
            <Text fz="xs" c="dimmed">{t('points.assetsHint')}</Text>
            <AssetListEditor value={v.assets} onChange={(x) => form.setFieldValue('assets', x)} siteId={null} error={form.errors.assetIds} />
          </Stack>
          <Checkbox label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
          {editing && editing.usedBy.length > 0 && (
            <Text fz="xs" c="dimmed">{t('points.usedBy')}: {editing.usedBy.map((x, i) => <span key={x.id}>{i > 0 && ', '}<Anchor component={Link} to={`/pm/templates/${x.id}`} fz="xs">{x.code}</Anchor></span>)}</Text>
          )}
          <Group justify="space-between">
            {editing && editable ? <Button variant="subtle" color="red" onClick={() => confirmDelete(editing.code, async () => {
              try { await del(`/measurement-points/${editing.id}`); setOpen(false); await qc.invalidateQueries({ queryKey: ['measurement-points'] }); } catch (e) { notifyError(e); }
            })}>{t('common.delete')}</Button> : <span />}
            <Group><Button variant="default" onClick={() => setOpen(false)}>{t('common.cancel')}</Button>{editable && <Button type="submit" loading={form.submitting}>{t('common.save')}</Button>}</Group>
          </Group>
        </Stack></form>
      </Modal>
    </Card>
  );
}
