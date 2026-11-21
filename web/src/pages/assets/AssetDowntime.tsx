import { useEffect, useState } from 'react';
import { ActionIcon, Alert, Anchor, Badge, Button, Card, Checkbox, Group, Modal, SimpleGrid, Stack, Table, Text, Textarea, Title, Tooltip } from '@mantine/core';
import { DateTimePicker } from '@mantine/dates';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { IconAlertTriangle, IconEdit, IconPlayerPlay, IconPlus } from '@tabler/icons-react';
import { get, post, put } from '@/api/client';
import { DAY_NAMES } from '@/api/types';
import type { AssetDowntime as Downtime, DowntimeStop } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { downFor } from '@/components/downtime';
import { fmtDateTime, fmtNumber, handleFormError, hoursText, notifyError, notifySuccess } from '@/components/utils';

/** Invalidates everything that shows whether an asset is down. */
export async function refreshDowntime(qc: ReturnType<typeof useQueryClient>, assetId: string) {
  await Promise.all([qc.invalidateQueries({ queryKey: ['asset', assetId] }), qc.invalidateQueries({ queryKey: ['assets'] }),
    qc.invalidateQueries({ queryKey: ['dashboard'] }), qc.invalidateQueries({ queryKey: ['work-order'] })]);
}

/** "Back running": ends the asset's open stop now. */
export function BackRunningButton({ assetId, size = 'sm' }: { assetId: string; size?: 'xs' | 'sm' }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [busy, setBusy] = useState(false);
  const go = async () => {
    setBusy(true);
    try { await post(`/assets/${assetId}/back-running`); notifySuccess(t('downtime.backRunningDone')); await refreshDowntime(qc, assetId); }
    catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return <Button size={size} color="teal" leftSection={<IconPlayerPlay size={14} />} loading={busy} onClick={go}>{t('downtime.backRunning')}</Button>;
}

/** The asset's Downtime tab: down since, running time, availability over 30 days, and its stops (newest first). */
export function AssetDowntimeTab({ assetId }: { assetId: string }) {
  const { t } = useTranslation();
  const { can } = useAuth();
  const { data } = useQuery({ queryKey: ['asset', assetId, 'downtime'], queryFn: () => get<Downtime>(`/assets/${assetId}/downtime`) });
  const [editing, setEditing] = useState<DowntimeStop | 'new' | null>(null);
  const [now, setNow] = useState(() => new Date());
  useEffect(() => { const timer = setInterval(() => setNow(new Date()), 60_000); return () => clearInterval(timer); }, []);
  if (!data) return null;
  const days = DAY_NAMES.filter((d) => data.runsOn.includes(d));
  const runsOn = days.length === 7 ? t('downtime.everyDay') : days.map((d) => t(`pm.days.${d}`)).join(t('common.listSeparator', ', '));

  return (
    <Stack>
      {data.downSince
        ? <Alert color="red" icon={<IconAlertTriangle size={16} />} title={t('downtime.downSince', { when: fmtDateTime(data.downSince), for: downFor(data.downSince, now) })}>
            {can(P.woExecute) && <BackRunningButton assetId={assetId} />}
          </Alert>
        : <Alert color="teal" variant="light">{t('downtime.running')}</Alert>}
      <SimpleGrid cols={{ base: 1, sm: 3 }}>
        <Card><Text fz="xs" c="dimmed">{t('downtime.availability30')}</Text>
          <Title order={3}>{data.availability30 == null ? '—' : `${fmtNumber(data.availability30)}%`}</Title></Card>
        <Card><Text fz="xs" c="dimmed">{t('downtime.downHours30')}</Text>
          <Title order={3}>{hoursText(fmtNumber(data.downHours30))}</Title>
          <Text fz="xs" c="dimmed">{t('downtime.ofPlanned', { hours: hoursText(fmtNumber(data.plannedHours30)) })}</Text></Card>
        <Card><Text fz="xs" c="dimmed">{t('downtime.runningTime')}</Text>
          <Text fw={600}>{t('downtime.runsHours', { hours: fmtNumber(data.runsHoursPerDay) })}</Text>
          <Text fz="sm" c="dimmed">{runsOn}</Text></Card>
      </SimpleGrid>
      <Card p={0}>
        <Group justify="space-between" p="md"><Title order={5}>{t('downtime.stops')}</Title>
          {can(P.assetsEdit) && <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => setEditing('new')}>{t('downtime.addStop')}</Button>}</Group>
        {data.stops.length === 0 ? <Text c="dimmed" fz="sm" px="md" pb="md">{t('downtime.noStops')}</Text> : (
          <Table.ScrollContainer minWidth={720}>
            <Table verticalSpacing="xs">
              <Table.Thead><Table.Tr><Table.Th>{t('downtime.start')}</Table.Th><Table.Th>{t('downtime.end')}</Table.Th><Table.Th>{t('downtime.duration')}</Table.Th>
                <Table.Th>{t('downtime.reason')}</Table.Th><Table.Th>{t('downtime.from')}</Table.Th><Table.Th /></Table.Tr></Table.Thead>
              <Table.Tbody>
                {data.stops.map((s) => (
                  <Table.Tr key={s.id}>
                    <Table.Td>{fmtDateTime(s.startedAt)}</Table.Td>
                    <Table.Td>{s.endedAt ? fmtDateTime(s.endedAt) : <Badge color="red" variant="light">{t('downtime.stillDown')}</Badge>}</Table.Td>
                    <Table.Td>{hoursText(fmtNumber(s.hours))}</Table.Td>
                    <Table.Td style={{ whiteSpace: 'pre-wrap' }}>{s.reason}</Table.Td>
                    <Table.Td>
                      {s.links.length === 0 ? <Text fz="sm" c="dimmed">{s.source === 'Manual' ? t('downtime.byHand') : s.sourceNumber}</Text> : (
                        <Group gap={6}>{s.links.map((l) => (
                          <Anchor key={l.id} component={Link} fz="sm" to={l.kind === 'workOrder' ? `/work-orders/${l.id}` : `/work-requests?open=${l.id}`}>{l.number}</Anchor>
                        ))}</Group>
                      )}
                    </Table.Td>
                    <Table.Td>{can(P.assetsEdit) && <Tooltip label={t('common.edit')}><ActionIcon variant="subtle" onClick={() => setEditing(s)} aria-label={t('common.edit')}><IconEdit size={16} /></ActionIcon></Tooltip>}</Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        )}
      </Card>
      <StopModal assetId={assetId} stop={editing} onClose={() => setEditing(null)} />
    </Stack>
  );
}

/** Adds a stop by hand, or corrects a stop's start, end or reason. */
function StopModal({ assetId, stop, onClose }: { assetId: string; stop: DowntimeStop | 'new' | null; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const form = useForm({ initialValues: { startedAt: null as Date | null, stillDown: false, endedAt: null as Date | null, reason: '' } });
  useEffect(() => {
    if (!stop) return;
    form.setValues(stop === 'new'
      ? { startedAt: new Date(), stillDown: true, endedAt: null, reason: '' }
      : { startedAt: new Date(stop.startedAt), stillDown: !stop.endedAt, endedAt: stop.endedAt ? new Date(stop.endedAt) : null, reason: stop.reason ?? '' });
    form.clearErrors();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [stop]);
  const submit = form.onSubmit(async (v) => {
    if (!v.startedAt) { form.setFieldError('startedAt', t('downtime.startRequired')); return; }
    if (!v.stillDown && !v.endedAt) { form.setFieldError('endedAt', t('downtime.endRequired')); return; }
    const body = { startedAt: dayjs(v.startedAt).toISOString(), endedAt: v.stillDown || !v.endedAt ? null : dayjs(v.endedAt).toISOString(), reason: v.reason };
    try {
      if (stop === 'new') await post(`/assets/${assetId}/downtime`, body);
      else if (stop) await put(`/downtime/${stop.id}`, body);
      notifySuccess(t('common.saved'));
      await refreshDowntime(qc, assetId);
      onClose();
    } catch (e) { handleFormError(form, e); }
  });
  return (
    <Modal opened={stop !== null} onClose={onClose} title={stop === 'new' ? t('downtime.addStop') : t('downtime.editStop')}>
      <form onSubmit={submit}><Stack>
        <DateTimePicker label={t('downtime.start')} required valueFormat="DD MMM YYYY HH:mm" maxDate={new Date()} {...form.getInputProps('startedAt')} />
        <Checkbox label={t('downtime.stillDown')} {...form.getInputProps('stillDown', { type: 'checkbox' })} />
        {!form.values.stillDown && <DateTimePicker label={t('downtime.end')} required valueFormat="DD MMM YYYY HH:mm" maxDate={new Date()} {...form.getInputProps('endedAt')} />}
        <Textarea label={t('downtime.reason')} autosize minRows={2} maxLength={500} {...form.getInputProps('reason')} />
        <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
      </Stack></form>
    </Modal>
  );
}
