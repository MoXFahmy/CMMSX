import { useState } from 'react';
import { Anchor, Badge, Button, Card, Group, Modal, NumberInput, Stack, Table, Text, Textarea, Title } from '@mantine/core';
import { LineChart } from '@mantine/charts';
import { useChartAxes } from '@/components/chartDirection';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconPlus } from '@tabler/icons-react';
import { get, post } from '@/api/client';
import type { AssetMeasurement } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { levelColor, outOfRange, rangeMessage } from '@/components/measure';
import { fmtDate, fmtDateTime, notifyError, notifySuccess } from '@/components/utils';

/** An asset's measurements (0.13.0): a chart per measurement point with its levels, the latest readings, and Add reading. */
export function AssetMeasurements({ assetId }: { assetId: string }) {
  const { t } = useTranslation();
  const axes = useChartAxes();
  const { can } = useAuth();
  const qc = useQueryClient();
  const key = ['asset-measurements', assetId];
  const { data } = useQuery({ queryKey: key, queryFn: () => get<AssetMeasurement[]>(`/assets/${assetId}/measurements`) });
  const [adding, setAdding] = useState<AssetMeasurement | null>(null);
  const [value, setValue] = useState<number | ''>('');
  const [note, setNote] = useState('');
  const save = async () => {
    if (!adding || value === '') return;
    if (outOfRange(adding, value)) { notifyError(new Error(rangeMessage(adding, t))); return; }
    try {
      const r = await post<{ level: string; followUpWorkOrderId?: string | null; followUpRequestId?: string | null }>(`/measurement-points/${adding.id}/readings`, { assetId, value, note: note || null });
      notifySuccess(r.followUpWorkOrderId || r.followUpRequestId ? t('measurements.savedWithFollowUp') : t('common.saved'));
      setAdding(null); setValue(''); setNote(''); await qc.invalidateQueries({ queryKey: key });
    } catch (e) { notifyError(e); }
  };
  if (!data) return null;
  if (data.length === 0) return <Card><Text c="dimmed" fz="sm">{t('measurements.none')}</Text></Card>;
  return (
    <Stack>
      {data.map((p) => {
        const levels = ([['alarmHigh', 'red'], ['alertHigh', 'orange'], ['target', 'teal'], ['alertLow', 'orange'], ['alarmLow', 'red']] as const)
          .filter(([k]) => p[k] !== null && p[k] !== undefined).map(([k, color]) => ({ y: p[k] as number, label: t(`points.level.${k}`), color: `${color}.6` }));
        const series = p.readings.map((r) => ({ date: fmtDate(r.readAt), value: r.value }));
        return (
          <Card key={p.id}><Stack gap="xs">
            <Group justify="space-between">
              <div><Title order={5}>{p.code} · {p.name}</Title><Text fz="xs" c="dimmed">{t(`points.kinds.${p.kind}`)}{p.unit ? ` · ${p.unit}` : ''}</Text></div>
              {can(P.metersRecord) && p.isActive && <Button size="xs" variant="light" leftSection={<IconPlus size={14} />} onClick={() => setAdding(p)}>{t('measurements.add')}</Button>}
            </Group>
            {series.length > 1
              ? <LineChart h={220} data={series} dataKey="date" {...axes} series={[{ name: 'value', label: p.unit ?? t('checklist.value'), color: 'blue.6' }]} referenceLines={levels} curveType="linear" withDots />
              : <Text fz="sm" c="dimmed">{t('measurements.needTwo')}</Text>}
            {p.readings.length > 0 && (
              <Table verticalSpacing={4} fz="sm"><Table.Tbody>
                {[...p.readings].reverse().slice(0, 8).map((r) => (
                  <Table.Tr key={r.id}>
                    <Table.Td>{fmtDateTime(r.readAt)}</Table.Td>
                    <Table.Td ta="end">{r.value} {p.unit ?? ''}</Table.Td>
                    <Table.Td>{r.level !== 'Normal' && <Badge size="xs" color={levelColor(r.level)}>{t(`checklist.levels.${r.level}`)}</Badge>}</Table.Td>
                    <Table.Td>{r.workOrderId && <Anchor component={Link} to={`/work-orders/${r.workOrderId}`} fz="xs">{t('measurements.onWork')}</Anchor>}</Table.Td>
                    <Table.Td>{(r.followUpWorkOrderId || r.followUpRequestId) && (
                      <Anchor component={Link} to={r.followUpWorkOrderId ? `/work-orders/${r.followUpWorkOrderId}` : `/work-requests/${r.followUpRequestId}`} fz="xs">{t('measurements.followUp')}</Anchor>)}</Table.Td>
                    <Table.Td><Text fz="xs" c="dimmed">{r.byName}</Text></Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody></Table>
            )}
          </Stack></Card>
        );
      })}
      <Modal opened={!!adding} onClose={() => setAdding(null)} title={adding ? `${t('measurements.add')} · ${adding.code}` : ''}>
        <Stack>
          <NumberInput label={t('checklist.value')} suffix={adding?.unit ? ` ${adding.unit}` : undefined} decimalScale={adding?.decimals ?? 2} value={value}
            onChange={(x) => setValue(typeof x === 'number' ? x : '')} data-autofocus />
          <Textarea label={t('common.notes')} autosize value={note} onChange={(e) => setNote(e.currentTarget.value)} maxLength={1000} />
          <Group justify="flex-end"><Button variant="default" onClick={() => setAdding(null)}>{t('common.cancel')}</Button><Button disabled={value === ''} onClick={save}>{t('common.save')}</Button></Group>
        </Stack>
      </Modal>
    </Stack>
  );
}
