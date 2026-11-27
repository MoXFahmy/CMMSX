import { Anchor, Badge, Button, Drawer, Stack, Table, Text } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { get, post } from '@/api/client';
import type { OccurrenceList, PmRunResult } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { confirmAction } from '@/components/confirm';
import { fmtDate, fmtQty, notifyError, notifySuccess } from '@/components/utils';

/** A PM's next 24 occurrences (0.12.0): dates with "Generate this one now", or projected meter readings with an estimated date. */
export function OccurrencesDrawer({ scheduleId, opened, onClose }: { scheduleId: string; opened: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const { can } = useAuth();
  const qc = useQueryClient();
  const { data, isFetching } = useQuery({ queryKey: ['pm-occurrences', scheduleId], queryFn: () => get<OccurrenceList>(`/pm-schedules/${scheduleId}/occurrences`), enabled: opened });
  const generate = (date: string) => confirmAction(t('pm12.generateOneConfirm', { date: fmtDate(date) }), async () => {
    try {
      const r = await post<PmRunResult>(`/pm-schedules/${scheduleId}/occurrences/generate`, { date });
      notifySuccess(t('pm12.generatedOne', { number: r.workOrderNumber ?? r.groupNumber ?? '' }));
      await Promise.all([qc.invalidateQueries({ queryKey: ['pm-occurrences', scheduleId] }), qc.invalidateQueries({ queryKey: ['pm-schedule', scheduleId] }),
        qc.invalidateQueries({ queryKey: ['work-orders'] })]);
    } catch (e) { notifyError(e); }
  });
  return (
    <Drawer opened={opened} onClose={onClose} position="right" size="lg" title={t('pm12.occurrences')}>
      <Stack>
        {!isFetching && data && data.dates.length === 0 && data.readings.length === 0 && <Text c="dimmed">{t('pm12.noOccurrences')}</Text>}
        {(data?.dates.length ?? 0) > 0 && (
          <Table verticalSpacing={6} striped>
            <Table.Thead><Table.Tr><Table.Th>{t('pm12.occurrence')}</Table.Th><Table.Th>{t('wo.due')}</Table.Th><Table.Th>{t('pm12.template')}</Table.Th><Table.Th /></Table.Tr></Table.Thead>
            <Table.Tbody>
              {data!.dates.map((d) => (
                <Table.Tr key={d.date}>
                  <Table.Td><Text fz="sm">{fmtDate(d.date)}</Text></Table.Td>
                  <Table.Td><Text fz="sm" c="dimmed">{fmtDate(d.dueDate)}</Text></Table.Td>
                  <Table.Td><Text fz="sm">{d.templateName}</Text></Table.Td>
                  <Table.Td ta="end">
                    {d.status === 'Generated'
                      ? (d.workOrderId ? <Anchor component={Link} to={`/work-orders/${d.workOrderId}`} fz="sm">{d.workOrderNumber}</Anchor>
                        : d.groupId ? <Anchor component={Link} to={`/work-groups/${d.groupId}`} fz="sm">{d.groupNumber}</Anchor> : <Badge variant="light">{t('pm12.generated')}</Badge>)
                      : can(P.pmGenerate) && <Button size="compact-xs" variant="light" onClick={() => generate(d.date)}>{t('pm12.generateOne')}</Button>}
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
        {(data?.readings.length ?? 0) > 0 && (
          <Table verticalSpacing={6} striped>
            <Table.Thead><Table.Tr><Table.Th>{t('pm12.dueReading')}</Table.Th><Table.Th>{t('pm12.estimatedDate')}</Table.Th></Table.Tr></Table.Thead>
            <Table.Tbody>
              {data!.readings.map((r) => (
                <Table.Tr key={r.reading}><Table.Td><Text fz="sm">{fmtQty(r.reading)}</Text></Table.Td><Table.Td><Text fz="sm" c="dimmed">{r.estimatedDate ? fmtDate(r.estimatedDate) : '—'}</Text></Table.Td></Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Stack>
    </Drawer>
  );
}
