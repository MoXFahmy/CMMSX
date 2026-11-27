import { useState } from 'react';
import { ActionIcon, Anchor, Badge, Button, Card, Group, Loader, Modal, NumberInput, ScrollArea, Select, Stack, Table, Tabs, Text, Textarea, Tooltip } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { IconChevronLeft, IconChevronRight, IconEdit, IconPlus, IconTrash } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { confirmDelete } from '@/components/confirm';
import { teamOptions, useTeams } from '@/components/teams';
import { WorkOrderPicker } from '@/components/WorkOrderPicker';
import { fmtQty, notifyError, notifySuccess } from '@/components/utils';
import { dayTotals, weekCell, type TimesheetDay, type TimesheetRow, type TimesheetWeek } from './timesheet';

type Editing = { workOrderId: string; number: string; entry?: TimesheetRow['entries'][number] };

/** The daily timesheet (0.19.0): one person's day over the work orders' labour records, and each person's week for supervisors. */
export function TimesheetPage() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const [params, setParams] = useSearchParams();
  const others = can(P.woEdit) || can(P.woLogForOthers);
  const tab = others && params.get('tab') === 'week' ? 'week' : 'day';
  const date = params.get('date') ?? dayjs().format('YYYY-MM-DD');
  const employeeId = params.get('employeeId');
  const set = (patch: Record<string, string | null>) => {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(patch)) { if (v) next.set(k, v); else next.delete(k); }
    setParams(next, { replace: true });
  };
  return (
    <>
      <PageHeader title={t('timesheet.title')} subtitle={t('timesheet.subtitle')} />
      <Tabs value={tab} onChange={(v) => set({ tab: v === 'week' ? 'week' : null })}>
        {others && <Tabs.List mb="sm"><Tabs.Tab value="day">{t('timesheet.day')}</Tabs.Tab><Tabs.Tab value="week">{t('timesheet.week')}</Tabs.Tab></Tabs.List>}
        <Tabs.Panel value="day"><Day date={date} employeeId={employeeId} others={others} set={set} /></Tabs.Panel>
        {others && <Tabs.Panel value="week"><Week date={date} set={set} /></Tabs.Panel>}
      </Tabs>
    </>
  );
}

function Day({ date, employeeId, others, set }: { date: string; employeeId: string | null; others: boolean; set: (p: Record<string, string | null>) => void }) {
  const { t } = useTranslation();
  const { can } = useAuth();
  const qc = useQueryClient();
  const { data: people } = useQuery({ queryKey: ['employees', 'lookup'], queryFn: () => get<{ id: string; fullName: string }[]>('/employees/lookup'), enabled: others });
  const { data, isFetching, error } = useQuery({
    queryKey: ['timesheet-day', date, employeeId], placeholderData: (prev) => prev, retry: false,
    queryFn: () => get<TimesheetDay>('/timesheet/day', { date, employeeId }),
  });
  const [editing, setEditing] = useState<Editing | null>(null);
  const [other, setOther] = useState<string | null>(null);
  const canLog = can(P.woExecute);
  const refresh = () => qc.invalidateQueries({ queryKey: ['timesheet-day'] });
  const totals = data ? dayTotals(data.rows) : { hours: 0, overtimeHours: 0 };
  const shift = (n: number) => set({ date: dayjs(date).add(n, 'day').format('YYYY-MM-DD') });
  return (
    <Stack gap="sm">
      <Card p="sm">
        <Group gap="sm" wrap="wrap">
          {others && <Select size="xs" w={220} placeholder={t('timesheet.me')} clearable searchable data={(people ?? []).map((p) => ({ value: p.id, label: p.fullName }))}
            value={employeeId} onChange={(v) => set({ employeeId: v })} />}
          <Group gap={4}>
            <ActionIcon variant="default" onClick={() => shift(-1)} aria-label={t('planning.previous')}><IconChevronLeft size={16} className="mirror-rtl" /></ActionIcon>
            <Button size="xs" variant="default" onClick={() => set({ date: null })}>{t('planning.today')}</Button>
            <ActionIcon variant="default" onClick={() => shift(1)} aria-label={t('planning.next')}><IconChevronRight size={16} className="mirror-rtl" /></ActionIcon>
          </Group>
          <Text fw={600} fz="sm">{dayjs(date).format('dddd D MMMM YYYY')}</Text>
          {data && <Text fz="sm" c="dimmed">{data.employee.name}</Text>}
          {isFetching && <Loader size="xs" />}
        </Group>
      </Card>
      {error ? <Text c="red">{(error as Error).message}</Text> : !data ? <Loader /> : (
        <Card p={0}>
          <ScrollArea type="auto">
            <Table verticalSpacing={6} miw={640}>
              <Table.Thead><Table.Tr><Table.Th>{t('timesheet.workOrder')}</Table.Th><Table.Th ta="end">{t('timesheet.hours')}</Table.Th>
                <Table.Th ta="end">{t('timesheet.overtime')}</Table.Th><Table.Th>{t('common.notes')}</Table.Th><Table.Th w={110} /></Table.Tr></Table.Thead>
              <Table.Tbody>
                {data.rows.length === 0 && <Table.Tr><Table.Td colSpan={5}><Text c="dimmed" fz="sm">{t('timesheet.nothing')}</Text></Table.Td></Table.Tr>}
                {data.rows.map((r) => (r.entries.length === 0 ? [undefined] : r.entries).map((e, i) => (
                  <Table.Tr key={`${r.workOrderId}-${e?.id ?? 'none'}`}>
                    <Table.Td>{i === 0 && <><Anchor component={Link} to={`/work-orders/${r.workOrderId}`} fz="sm" fw={600}>{r.number}</Anchor>
                      <Text fz="xs" c="dimmed" lineClamp={1}>{r.title}</Text>{r.closed && <Badge size="xs" color="gray">{t('timesheet.closed')}</Badge>}</>}</Table.Td>
                    <Table.Td ta="end">{e ? fmtQty(e.hours) : '—'}</Table.Td>
                    <Table.Td ta="end">{e?.overtimeHours ? fmtQty(e.overtimeHours) : ''}</Table.Td>
                    <Table.Td><Text fz="sm" lineClamp={1}>{e?.notes}</Text></Table.Td>
                    <Table.Td>{canLog && !r.closed && (
                      <Group gap={2} justify="flex-end" wrap="nowrap">
                        {e && <ActionIcon variant="subtle" aria-label={t('common.edit')} onClick={() => setEditing({ workOrderId: r.workOrderId, number: r.number, entry: e })}><IconEdit size={14} /></ActionIcon>}
                        {e && <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => confirmDelete(`${r.number} · ${fmtQty(e.hours)} h`, async () => {
                          try { await del(`/work-orders/${r.workOrderId}/labor/${e.id}`); await refresh(); } catch (err) { notifyError(err); }
                        })}><IconTrash size={14} /></ActionIcon>}
                        {i === 0 && <Tooltip label={t('timesheet.addHours')}><ActionIcon variant="subtle" aria-label={t('timesheet.addHours')}
                          onClick={() => setEditing({ workOrderId: r.workOrderId, number: r.number })}><IconPlus size={14} /></ActionIcon></Tooltip>}
                      </Group>)}</Table.Td>
                  </Table.Tr>
                )))}
              </Table.Tbody>
              <Table.Tfoot><Table.Tr>
                <Table.Th>{t('timesheet.total')}{data.available && (
                  <Text fz="xs" c="dimmed" fw={400}>{t('timesheet.available', { hours: fmtQty(data.available.available) })}
                    {data.available.slots.length > 0 && ` · ${data.available.slots.map((s) => (s.hours ? `${s.name} ${s.hours} h` : s.name)).join(', ')}`}</Text>)}</Table.Th>
                <Table.Th ta="end">{fmtQty(totals.hours)}</Table.Th><Table.Th ta="end">{totals.overtimeHours ? fmtQty(totals.overtimeHours) : ''}</Table.Th><Table.Th /><Table.Th />
              </Table.Tr></Table.Tfoot>
            </Table>
          </ScrollArea>
          {canLog && (
            <Group p="sm" gap="xs" align="flex-end">
              <div style={{ flex: 1, maxWidth: 360 }}><WorkOrderPicker value={other} onChange={setOther} label={t('timesheet.otherWorkOrder')} /></div>
              <Button size="xs" variant="light" disabled={!other} onClick={() => other && setEditing({ workOrderId: other, number: '' })}>{t('timesheet.addHours')}</Button>
            </Group>
          )}
        </Card>
      )}
      {editing && data && <EntryModal key={editing.entry?.id ?? editing.workOrderId} editing={editing} employeeId={data.employee.id} date={date}
        onClose={() => setEditing(null)} onSaved={async () => { setEditing(null); setOther(null); await refresh(); }} />}
    </Stack>
  );
}

function EntryModal({ editing, employeeId, date, onClose, onSaved }: { editing: Editing; employeeId: string; date: string; onClose: () => void; onSaved: () => Promise<void> }) {
  const { t } = useTranslation();
  const [hours, setHours] = useState<number | ''>(editing.entry?.hours ?? 1);
  const [ot, setOt] = useState<number | ''>(editing.entry?.overtimeHours ?? 0);
  const [notes, setNotes] = useState(editing.entry?.notes ?? '');
  const [busy, setBusy] = useState(false);
  const save = async () => {
    setBusy(true);
    const body = { employeeId, workDate: date, hours: hours === '' ? 0 : hours, overtimeHours: ot === '' ? 0 : ot, notes: notes.trim() || null };
    try {
      if (editing.entry) await put(`/work-orders/${editing.workOrderId}/labor/${editing.entry.id}`, body);
      else await post(`/work-orders/${editing.workOrderId}/labor`, body);
      notifySuccess(t('common.saved'));
      await onSaved();
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Modal opened onClose={onClose} title={`${t('timesheet.addHours')}${editing.number ? ` · ${editing.number}` : ''}`}>
      <Stack>
        <Group grow>
          <NumberInput label={t('timesheet.hours')} min={0} max={24} step={0.25} decimalScale={2} value={hours} onChange={(v) => setHours(v === '' ? '' : Number(v))} data-autofocus />
          <NumberInput label={t('timesheet.overtime')} min={0} max={24} step={0.25} decimalScale={2} value={ot} onChange={(v) => setOt(v === '' ? '' : Number(v))} />
        </Group>
        <Textarea label={t('common.notes')} autosize value={notes} onChange={(e) => setNotes(e.currentTarget.value)} />
        <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button onClick={() => void save()} loading={busy}>{t('common.save')}</Button></Group>
      </Stack>
    </Modal>
  );
}

function Week({ date, set }: { date: string; set: (p: Record<string, string | null>) => void }) {
  const { t } = useTranslation();
  const from = dayjs(date).subtract(dayjs(date).day(), 'day').format('YYYY-MM-DD');
  const [teamId, setTeamId] = useState<string | null>(null);
  const { data: teams } = useTeams(null);
  const { data, isFetching } = useQuery({
    queryKey: ['timesheet-week', from, teamId], placeholderData: (prev) => prev,
    queryFn: () => get<TimesheetWeek>('/timesheet/week', { from, teamId }),
  });
  const colour = { over: 'red', under: 'orange', ok: undefined } as const;
  return (
    <Stack gap="sm">
      <Card p="sm">
        <Group gap="sm">
          <Group gap={4}>
            <ActionIcon variant="default" onClick={() => set({ date: dayjs(from).subtract(7, 'day').format('YYYY-MM-DD') })} aria-label={t('planning.previous')}><IconChevronLeft size={16} className="mirror-rtl" /></ActionIcon>
            <ActionIcon variant="default" onClick={() => set({ date: dayjs(from).add(7, 'day').format('YYYY-MM-DD') })} aria-label={t('planning.next')}><IconChevronRight size={16} className="mirror-rtl" /></ActionIcon>
          </Group>
          <Text fw={600} fz="sm">{`${dayjs(from).format('D MMM')} – ${dayjs(from).add(6, 'day').format('D MMM YYYY')}`}</Text>
          <Select size="xs" w={180} placeholder={t('teams.team')} clearable data={teamOptions(teams, teamId)} value={teamId} onChange={setTeamId} />
          {isFetching && <Loader size="xs" />}
        </Group>
      </Card>
      {!data ? <Loader /> : (
        <Card p={0}>
          <ScrollArea type="auto">
            <Table verticalSpacing={6} miw={760}>
              <Table.Thead><Table.Tr><Table.Th>{t('availability.person')}</Table.Th>
                {data.days.map((d) => <Table.Th key={d} ta="center">{dayjs(d).format('ddd D')}</Table.Th>)}<Table.Th ta="end">{t('timesheet.total')}</Table.Th></Table.Tr></Table.Thead>
              <Table.Tbody>
                {data.people.map((p) => (
                  <Table.Tr key={p.id}>
                    <Table.Td fw={500}>{p.name}</Table.Td>
                    {p.days.map((c, i) => (
                      <Table.Td key={i} ta="center" style={{ cursor: 'pointer' }} onClick={() => set({ tab: null, employeeId: p.id, date: data.days[i] })}>
                        <Text fz="sm" c={colour[weekCell(c)]} fw={weekCell(c) === 'ok' ? undefined : 600}>
                          {fmtQty(c.hours + c.overtimeHours)}<Text span fz="xs" c="dimmed"> / {fmtQty(c.available)}</Text></Text>
                      </Table.Td>
                    ))}
                    <Table.Td ta="end">{fmtQty(p.days.reduce((a, c) => a + c.hours + c.overtimeHours, 0))}</Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </ScrollArea>
          <Text fz="xs" c="dimmed" p="sm">{t('timesheet.weekHint')}</Text>
        </Card>
      )}
    </Stack>
  );
}
