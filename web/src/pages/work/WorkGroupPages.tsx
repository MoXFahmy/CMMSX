import { useState } from 'react';
import { Anchor, Button, Card, Center, Checkbox, Grid, Group, Loader, Modal, Progress, Select, SimpleGrid, Stack, Table, Text, TextInput, Textarea, Title } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { DataTable } from 'mantine-datatable';
import { IconSearch } from '@tabler/icons-react';
import { get, post, put } from '@/api/client';
import { HOLD_REASONS, type Lookup, type WorkGroupDetail, type WorkGroupListItem, type WorkOrderStatus } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { EmployeePicker } from '@/components/EmployeePicker';
import { WoStatusBadge } from '@/components/WorkBadges';
import { fmtDate, fmtDateTime, fmtMoney, fmtNumber, notifyError, notifySuccess, notifyWarning, toIsoDate } from '@/components/utils';
import { CodeSelect } from '@/components/CodeSelect';
import { pick } from '@/components/bilingual';

const asStatus = (s: string) => s as WorkOrderStatus;

/** Work › Groups (0.12.0): groups made by group PMs, with their progress. */
export function WorkGroupsPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { terms } = useAuth();
  const [search, setSearch] = useState('');
  const { data, isFetching } = useQuery({ queryKey: ['work-groups', search], queryFn: () => get<WorkGroupListItem[]>('/work-groups', { search: search || undefined }) });
  return (
    <>
      <PageHeader title={t('groups.title')} subtitle={t('groups.subtitle')} />
      <Card p={0}>
        <Group p="md"><TextInput leftSection={<IconSearch size={16} />} placeholder={t('groups.search')} value={search} onChange={(e) => setSearch(e.currentTarget.value)} w={280} /></Group>
        <DataTable minHeight={200} fetching={isFetching} records={data ?? []} noRecordsText={t('common.noRecords')} highlightOnHover rowClassName={() => 'clickable-row'}
          onRowClick={({ record }) => navigate(`/work-groups/${record.id}`)}
          columns={[
            { accessor: 'number', title: t('groups.number'), noWrap: true, render: (g) => <Text fw={600} fz="sm">{g.number}</Text> },
            { accessor: 'title', title: t('common.name') },
            { accessor: 'siteName', title: terms.site },
            { accessor: 'pmCode', title: t('groups.pm'), render: (g) => g.pmScheduleId ? <Anchor component={Link} to={`/pm/schedules/${g.pmScheduleId}`} fz="sm" onClick={(e) => e.stopPropagation()}>{g.pmCode}</Anchor> : '—' },
            { accessor: 'dueDate', title: t('wo.due'), render: (g) => fmtDate(g.dueDate) },
            { accessor: 'done', title: t('groups.progress'), width: 160, render: (g) => <Stack gap={2}><Progress value={g.total ? (g.done * 100) / g.total : 0} size="sm" /><Text fz="xs" c="dimmed">{g.done} / {g.total}</Text></Stack> },
            { accessor: 'status', title: t('common.status'), render: (g) => <WoStatusBadge status={asStatus(g.status)} /> },
          ]} />
      </Card>
    </>
  );
}

/** One group (0.12.0): its work orders, totals and comments, and actions that apply to all of them (or the ticked ones). */
export function WorkGroupDetailPage() {
  const { t } = useTranslation();
  const { id = '' } = useParams();
  const qc = useQueryClient();
  const { can, me, terms } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const { data: g, isLoading } = useQuery({ queryKey: ['work-group', id], queryFn: () => get<WorkGroupDetail>(`/work-groups/${id}`) });
  const { data: cancelReasons } = useQuery({ queryKey: ['cancel-reasons'], queryFn: () => get<Lookup[]>('/cancel-reasons') });
  const [picked, setPicked] = useState<string[]>([]);
  const [modal, setModal] = useState<'assign' | 'report' | 'status' | null>(null);
  const [people, setPeople] = useState<string[]>([]);
  const [report, setReport] = useState({ completionNotes: '', causeNotes: '', preventionNotes: '', failureCodeId: null as string | null, remedyId: null as string | null });
  const [status, setStatus] = useState({ status: 'InProgress', holdReason: null as string | null, cancelReasonId: null as string | null, note: '', completionNotes: '' });
  const [comment, setComment] = useState('');
  const [due, setDue] = useState<string | null | undefined>(undefined);
  if (isLoading || !g) return <Center h={300}><Loader /></Center>;
  const childIds = picked.length ? picked : undefined;
  const refresh = () => Promise.all([qc.invalidateQueries({ queryKey: ['work-group', id] }), qc.invalidateQueries({ queryKey: ['work-groups'] }), qc.invalidateQueries({ queryKey: ['work-orders'] })]);
  const run = async (action: () => Promise<unknown>) => { try { await action(); setModal(null); await refresh(); } catch (e) { notifyError(e); } };
  const assign = () => run(async () => {
    const r = await post<{ assigned: number; message?: string | null }>(`/work-groups/${id}/assign`, { employeeIds: people, childIds });
    notifySuccess(t('groups.assigned', { count: r.assigned })); if (r.message) notifyWarning(r.message); setPeople([]);
  });
  const sendReport = () => run(async () => {
    const r = await post<{ updated: number }>(`/work-groups/${id}/report`, { ...report, childIds });
    notifySuccess(t('groups.reported', { count: r.updated }));
  });
  const changeStatus = () => run(async () => {
    const r = await post<{ changed: number; refused: { number: string; reason: string }[] }>(`/work-groups/${id}/status`, {
      status: status.status, holdReason: status.status === 'OnHold' ? status.holdReason : null, cancelReasonId: status.status === 'Cancelled' ? status.cancelReasonId : null,
      note: status.note || null, completionNotes: status.status === 'Completed' ? status.completionNotes || null : null, childIds,
    });
    notifySuccess(t('groups.changed', { count: r.changed }));
    for (const x of r.refused) notifyWarning(`${x.number}: ${x.reason}`);
  });
  const saveDue = () => run(async () => { await put(`/work-groups/${id}/due`, { dueDate: due ?? null }); notifySuccess(t('common.saved')); setDue(undefined); });
  const addComment = () => run(async () => { await post(`/work-groups/${id}/comments`, { body: comment }); setComment(''); });
  const editable = can(P.woEdit);
  const execute = can(P.woExecute);
  const all = picked.length === g.children.length && g.children.length > 0;

  return (
    <>
      <PageHeader title={`${g.number} · ${g.title}`} crumbs={[{ label: t('groups.title'), to: '/work-groups' }, { label: g.number }]} badge={<WoStatusBadge status={asStatus(g.status)} />}
        subtitle={[g.siteName, g.pm ? `${t('groups.pm')} ${g.pm.code}` : null].filter(Boolean).join(' · ')}
        actions={<>
          {editable && <Button variant="light" onClick={() => setModal('assign')}>{t('groups.assign')}</Button>}
          {execute && <Button variant="light" onClick={() => setModal('report')}>{t('groups.report')}</Button>}
          {execute && <Button onClick={() => setModal('status')}>{t('groups.status')}</Button>}
        </>} />
      <Grid gutter="lg">
        <Grid.Col span={{ base: 12, lg: 8 }}>
          <Card p={0}>
            <Group justify="space-between" p="md">
              <Title order={5}>{t('groups.workOrders', { count: g.children.length })}</Title>
              <Group gap="sm"><Text fz="xs" c="dimmed">{picked.length ? t('groups.picked', { count: picked.length }) : t('groups.allApply')}</Text>
                <Anchor component={Link} to={`/work-orders?tab=all&groupId=${g.id}`} fz="xs">{t('groups.inList')}</Anchor></Group>
            </Group>
            <Table verticalSpacing={6} striped>
              <Table.Thead><Table.Tr>
                <Table.Th w={36}><Checkbox checked={all} indeterminate={picked.length > 0 && !all} onChange={() => setPicked(all ? [] : g.children.map((c) => c.id))} aria-label={t('groups.pickAll')} /></Table.Th>
                <Table.Th>{t('wo.number')}</Table.Th><Table.Th>{terms.asset}</Table.Th><Table.Th>{t('wo.assignees')}</Table.Th><Table.Th>{t('wo.tasks')}</Table.Th>
                <Table.Th ta="end">{t('wo.hours')}</Table.Th><Table.Th ta="end">{t('wo.cost')}</Table.Th><Table.Th>{t('common.status')}</Table.Th>
              </Table.Tr></Table.Thead>
              <Table.Tbody>
                {g.children.map((c) => (
                  <Table.Tr key={c.id}>
                    <Table.Td><Checkbox checked={picked.includes(c.id)} onChange={() => setPicked(picked.includes(c.id) ? picked.filter((x) => x !== c.id) : [...picked, c.id])} aria-label={c.number} /></Table.Td>
                    <Table.Td><Anchor component={Link} to={`/work-orders/${c.id}`} fz="sm" fw={600}>{c.number}</Anchor></Table.Td>
                    <Table.Td><Text fz="sm">{c.assetNumber}</Text><Text fz="xs" c="dimmed">{c.assetName}</Text></Table.Td>
                    <Table.Td><Text fz="sm" c={c.assignees.length ? undefined : 'dimmed'}>{c.assignees.map((a) => a.fullName).join(', ') || t('wo.noAssignees')}</Text></Table.Td>
                    <Table.Td><Text fz="sm">{c.stepsDone} / {c.stepsTotal}</Text></Table.Td>
                    <Table.Td ta="end"><Text fz="sm">{fmtNumber(c.laborHours)}</Text></Table.Td>
                    <Table.Td ta="end"><Text fz="sm">{fmtMoney(c.cost, currency)}</Text></Table.Td>
                    <Table.Td><WoStatusBadge status={c.status} /></Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Card>
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 4 }}>
          <Stack gap="lg">
            <Card><Stack gap="xs">
              <SimpleGrid cols={2}>
                <div><Text fz="xs" c="dimmed">{t('wo.plannedStart')}</Text><Text fz="sm">{fmtDate(g.plannedStart)}</Text></div>
                <div><Text fz="xs" c="dimmed">{t('wo.due')}</Text><Text fz="sm">{fmtDate(g.dueDate)}</Text></div>
              </SimpleGrid>
              {editable && <Group gap="xs" align="flex-end">
                <DateInput label={t('groups.newDue')} description={t('groups.dueHint')} valueFormat="DD MMM YYYY" clearable value={due === undefined ? g.dueDate ?? null : due} onChange={(d) => setDue(d ? toIsoDate(d) : null)} style={{ flex: 1 }} />
                <Button variant="light" disabled={due === undefined} onClick={saveDue}>{t('common.save')}</Button>
              </Group>}
            </Stack></Card>
            <Card p={0}>
              <Title order={5} p="md" pb="xs">{t('wo.totals')}</Title>
              <Table verticalSpacing={6}><Table.Tbody>
                <Table.Tr><Table.Td>{t('wo.laborHours')}</Table.Td><Table.Td ta="end">{fmtNumber(g.totals.laborHours)}</Table.Td></Table.Tr>
                <Table.Tr><Table.Td>{t('wo.laborCost')}</Table.Td><Table.Td ta="end">{fmtMoney(g.totals.laborCost, currency)}</Table.Td></Table.Tr>
                <Table.Tr><Table.Td>{t('wo.partsCost')}</Table.Td><Table.Td ta="end">{fmtMoney(g.totals.partsCost, currency)}</Table.Td></Table.Tr>
                <Table.Tr><Table.Td>{t('wo.otherCost')}</Table.Td><Table.Td ta="end">{fmtMoney(g.totals.otherCost, currency)}</Table.Td></Table.Tr>
                {(g.totals.contractorCost ?? 0) > 0 && <Table.Tr><Table.Td>{t('contractors.cost')}</Table.Td><Table.Td ta="end">{fmtMoney(g.totals.contractorCost ?? 0, currency)}</Table.Td></Table.Tr>}
                <Table.Tr><Table.Td fw={700}>{t('wo.totalCost')}</Table.Td><Table.Td ta="end" fw={700}>{fmtMoney(g.totals.totalCost, currency)}</Table.Td></Table.Tr>
              </Table.Tbody></Table>
            </Card>
            <Card><Stack gap="xs">
              <Title order={5}>{t('groups.comments')}</Title>
              <Text fz="xs" c="dimmed">{t('groups.commentsHint')}</Text>
              {execute && <Group gap="xs" align="flex-end" wrap="nowrap">
                <Textarea style={{ flex: 1 }} autosize minRows={1} value={comment} onChange={(e) => setComment(e.currentTarget.value)} placeholder={t('groups.addComment')} />
                <Button disabled={!comment.trim()} onClick={addComment}>{t('tech.send')}</Button>
              </Group>}
              {g.comments.map((c) => <div key={c.id}><Text fz="xs" c="dimmed">{c.userName} · {fmtDateTime(c.createdAt)}</Text><Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{c.body}</Text></div>)}
            </Stack></Card>
          </Stack>
        </Grid.Col>
      </Grid>
      <Modal opened={modal === 'assign'} onClose={() => setModal(null)} title={t('groups.assign')}>
        <Stack>
          <Text fz="sm" c="dimmed">{t('groups.assignHint')}</Text>
          <EmployeePicker label={t('wo.assignees')} value={people} onChange={setPeople} siteId={g.siteId} />
          <Button disabled={people.length === 0} onClick={assign}>{t('groups.assign')}</Button>
        </Stack>
      </Modal>
      <Modal opened={modal === 'report'} onClose={() => setModal(null)} title={t('groups.report')} size="lg">
        <Stack>
          <Text fz="sm" c="dimmed">{t('groups.reportHint')}</Text>
          <Textarea label={t('wo.completionNotes')} autosize minRows={2} value={report.completionNotes} onChange={(e) => setReport({ ...report, completionNotes: e.currentTarget.value })} />
          <Textarea label={t('wo.causeNotes')} autosize value={report.causeNotes} onChange={(e) => setReport({ ...report, causeNotes: e.currentTarget.value })} />
          <Textarea label={t('wo.preventionNotes')} autosize value={report.preventionNotes} onChange={(e) => setReport({ ...report, preventionNotes: e.currentTarget.value })} />
          <CodeSelect path="/failure-codes" label={t('codes.cause')} value={report.failureCodeId} onChange={(x) => setReport({ ...report, failureCodeId: x })} />
          <CodeSelect path="/remedies" label={t('codes.remedy')} value={report.remedyId} onChange={(x) => setReport({ ...report, remedyId: x })}
            pairing="causeRemedy" pairedWith={report.failureCodeId} />
          <Button onClick={sendReport}>{t('groups.report')}</Button>
        </Stack>
      </Modal>
      <Modal opened={modal === 'status'} onClose={() => setModal(null)} title={t('groups.status')}>
        <Stack>
          <Select label={t('common.status')} data={['InProgress', 'OnHold', 'Completed', 'Closed', 'Cancelled', 'Open'].map((s) => ({ value: s, label: t(`woStatus.${s}`) }))}
            value={status.status} onChange={(x) => setStatus({ ...status, status: x ?? 'InProgress' })} />
          {status.status === 'OnHold' && <Select label={t('wo.holdReason')} required data={HOLD_REASONS.map((h) => ({ value: h, label: t(`holdReason.${h}`) }))} value={status.holdReason}
            onChange={(x) => setStatus({ ...status, holdReason: x })} />}
          {status.status === 'Cancelled' && (cancelReasons?.length ?? 0) > 0 && <Select label={t('wo.cancelReason')} required data={cancelReasons!.map((r) => ({ value: r.id, label: pick(r.name, r.nameAr) }))}
            value={status.cancelReasonId} onChange={(x) => setStatus({ ...status, cancelReasonId: x })} />}
          {status.status === 'Completed' && <Textarea label={t('wo.completionNotes')} autosize value={status.completionNotes} onChange={(e) => setStatus({ ...status, completionNotes: e.currentTarget.value })} />}
          <Textarea label={t('common.notes')} autosize value={status.note} onChange={(e) => setStatus({ ...status, note: e.currentTarget.value })} />
          <Text fz="xs" c="dimmed">{t('groups.statusHint')}</Text>
          <Button onClick={changeStatus}>{t('groups.status')}</Button>
        </Stack>
      </Modal>
    </>
  );
}
