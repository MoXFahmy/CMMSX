import { useEffect, useState } from 'react';
import { ActionIcon, Alert, Anchor, Badge, Breadcrumbs, Button, Card, Center, Checkbox, Grid, Group, Loader, Menu, Modal, NumberInput, Progress, Select, SimpleGrid, Stack, Switch, Table, Tabs, Text, TextInput, Textarea, Timeline, Title } from '@mantine/core';
import { DateInput, DateTimePicker } from '@mantine/dates';
import { useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { IconAlertTriangle, IconCheck, IconDots, IconEdit, IconInfoCircle, IconMessage, IconPlayerPause, IconPlayerPlay, IconPlus, IconRefresh, IconTrash, IconX, IconLock, IconShieldCheck } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import { HOLD_REASONS, type ActivityItem, type Lookup, type WorkOrderDetail, type WorkOrderStatus } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { Attachments } from '@/components/Attachments';
import { useEmployees } from '@/components/EmployeePicker';
import { PriorityBadge, WoStatusBadge } from '@/components/WorkBadges';
import { CriticalityBadge } from '@/components/Badges';
import { confirmDelete } from '@/components/confirm';
import { fmtDate, fmtDateTime, fmtMoney, fromNow, handleFormError, notifyError, notifySuccess, toIsoDate } from '@/components/utils';

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <Stack gap={2}><Text fz="xs" c="dimmed">{label}</Text><Text fz="sm" fw={500} style={{ whiteSpace: 'pre-wrap' }}>{children === undefined || children === null || children === '' ? '—' : children}</Text></Stack>;
}

const transitionIcon: Partial<Record<WorkOrderStatus, typeof IconCheck>> = { InProgress: IconPlayerPlay, OnHold: IconPlayerPause, Completed: IconCheck, Closed: IconLock, Cancelled: IconX, Open: IconRefresh };

export function WorkOrderDetailPage() {
  const { t } = useTranslation();
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { me, can } = useAuth();
  const [tab, setTab] = useState<string | null>('details');
  const [completeOpen, complete] = useDisclosure();
  const [holdOpen, hold] = useDisclosure();
  const { data: w, isLoading, error } = useQuery({ queryKey: ['work-order', id], queryFn: () => get<WorkOrderDetail>(`/work-orders/${id}`) });
  const currency = me?.tenant.settings.currency ?? 'USD';
  if (isLoading) return <Center h={300}><Loader /></Center>;
  if (error || !w) return <Alert color="red">{(error as Error)?.message}</Alert>;

  const refresh = async () => {
    await qc.invalidateQueries({ queryKey: ['work-order', id] });
    await qc.invalidateQueries({ queryKey: ['work-order-activity', id] });
    await qc.invalidateQueries({ queryKey: ['work-orders'] });
    await qc.invalidateQueries({ queryKey: ['dashboard'] });
  };
  const locked = w.status === 'Closed' || w.status === 'Cancelled';
  const canExec = can(P.woExecute) && !locked;
  const move = async (to: WorkOrderStatus) => {
    if (to === 'Completed') return complete.open();
    if (to === 'OnHold') return hold.open();
    try { await post(`/work-orders/${w.id}/status`, { status: to }); notifySuccess(t(`woStatus.${to}`)); await refresh(); } catch (e) { notifyError(e); }
  };
  const label = (to: WorkOrderStatus) =>
    to === 'InProgress' && (w.status === 'OnHold' || w.status === 'Open') ? (w.status === 'OnHold' ? t('transition.resume') : t('transition.InProgress'))
      : to === 'InProgress' ? t('transition.reopen') : t(`transition.${to}`);
  const primary = w.allowedTransitions.filter((s) => s !== 'Cancelled' && s !== 'Open');
  const secondary = w.allowedTransitions.filter((s) => s === 'Cancelled' || s === 'Open');
  const doneTasks = w.tasks.filter((x) => x.isDone).length;

  return (
    <>
      <PageHeader crumbs={[{ label: t('wo.title'), to: '/work-orders' }, { label: w.number }]} title={w.title}
        badge={<Group gap={6}><WoStatusBadge status={w.status} /><PriorityBadge value={w.priority} />{w.isOverdue && <Badge color="red" variant="filled">{t('wo.overdueBadge')}</Badge>}</Group>}
        subtitle={<>{w.number} · {t(`workType.${w.type}`)}{w.tradeName && ` · ${w.tradeName}`}{w.status === 'OnHold' && w.holdReason && ` · ${t(`holdReason.${w.holdReason}`)}`}
          {w.request && <> · <Anchor component={Link} to={`/work-requests?open=${w.request.id}`} fz="sm">{t('wo.fromRequest', { number: w.request.number })}</Anchor></>}
          {w.pm && <> · <Anchor component={Link} to={`/pm/schedules/${w.pm.id}`} fz="sm">{t('pm.fromPm', { code: w.pm.code })}</Anchor></>}</>}
        actions={<>
          {primary.map((s) => { const Icon = transitionIcon[s] ?? IconCheck; return (
            <Button key={s} leftSection={<Icon size={16} />} variant={s === 'Completed' || (s === 'InProgress' && w.status !== 'Completed') ? 'filled' : 'default'}
              color={s === 'Completed' ? 'teal' : undefined} onClick={() => move(s)}>{label(s)}</Button>); })}
          {(can(P.woEdit) || can(P.woDelete) || secondary.length > 0) && (
            <Menu position="bottom-end">
              <Menu.Target><ActionIcon variant="default" size="lg" aria-label={t('common.actions')}><IconDots size={16} /></ActionIcon></Menu.Target>
              <Menu.Dropdown>
                {can(P.woEdit) && !locked && <Menu.Item leftSection={<IconEdit size={16} />} component={Link} to={`/work-orders/${w.id}/edit`}>{t('common.edit')}</Menu.Item>}
                {secondary.map((s) => <Menu.Item key={s} color={s === 'Cancelled' ? 'red' : undefined} leftSection={s === 'Cancelled' ? <IconX size={16} /> : <IconRefresh size={16} />} onClick={() => move(s)}>{label(s)}</Menu.Item>)}
                {can(P.woDelete) && <><Menu.Divider /><Menu.Item color="red" leftSection={<IconTrash size={16} />} onClick={() => confirmDelete(w.number, async () => {
                  try { await del(`/work-orders/${w.id}`); await qc.invalidateQueries({ queryKey: ['work-orders'] }); navigate('/work-orders'); } catch (e) { notifyError(e); } })}>{t('common.delete')}</Menu.Item></>}
              </Menu.Dropdown>
            </Menu>
          )}
        </>} />
      <Tabs value={tab} onChange={setTab} keepMounted={false}>
        <Tabs.List mb="md">
          <Tabs.Tab value="details">{t('wo.details')}</Tabs.Tab>
          <Tabs.Tab value="tasks" rightSection={w.tasks.length ? <Badge size="xs" variant="default">{doneTasks}/{w.tasks.length}</Badge> : null}>{t('wo.tasks')}</Tabs.Tab>
          <Tabs.Tab value="labor" rightSection={w.labor.length ? <Badge size="xs" variant="default">{w.totals.laborHours}h</Badge> : null}>{t('wo.labor')}</Tabs.Tab>
          <Tabs.Tab value="costs">{t('wo.costs')}</Tabs.Tab>
          <Tabs.Tab value="activity">{t('wo.activity')}</Tabs.Tab>
          <Tabs.Tab value="files">{t('wo.files')}</Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="details">
          <Grid gutter="lg">
            <Grid.Col span={{ base: 12, md: 8 }}>
              <Stack gap="lg">
                {w.asset?.underWarranty && !locked && (
                  <Alert variant="light" color="teal" icon={<IconShieldCheck size={18} />} title={t('wo.underWarrantyTitle', { type: t(`warrantyType.${w.asset.warrantyType}`) })}>
                    {t('wo.underWarrantyBody', { date: fmtDate(w.asset.warrantyExpiresOn) })}
                  </Alert>
                )}
                {(w.asset?.notesToTechnician || w.locationNotesToTechnician) && (
                  <Alert variant="light" color="yellow" icon={<IconAlertTriangle size={18} />} title={t('wo.notesForTech')}>
                    <Stack gap={4}>{w.asset?.notesToTechnician && <Text fz="sm">{w.asset.notesToTechnician}</Text>}{w.locationNotesToTechnician && <Text fz="sm">{w.locationNotesToTechnician}</Text>}</Stack>
                  </Alert>
                )}
                <Card>
                  <Text fz="xs" c="dimmed" mb={4}>{t('wo.location')}</Text>
                  <Breadcrumbs separator="›" mb="md">
                    <Anchor component={Link} to={`/locations?site=${w.site.id}`} fz="sm" fw={500}>{w.site.name}</Anchor>
                    {w.locationTrail.map((l) => <Anchor key={l.id} component={Link} to={`/locations?loc=${l.id}`} fz="sm" fw={500}>{l.name}</Anchor>)}
                  </Breadcrumbs>
                  {w.asset && (
                    <Group gap="xs" mb="md"><Text fz="sm" c="dimmed">{t('wo.asset')}:</Text>
                      <Anchor component={Link} to={`/assets/${w.asset.id}`} fz="sm" fw={500}>{w.asset.assetNumber} · {w.asset.name}</Anchor><CriticalityBadge value={w.asset.criticality} /></Group>
                  )}
                  <Field label={t('wo.description')}>{w.description}</Field>
                </Card>
                {(w.completedAt || w.completionNotes) && (
                  <Card>
                    <Title order={5} mb="sm">{t('wo.completed')}</Title>
                    <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="md">
                      <Field label={t('wo.completionNotes')}>{w.completionNotes}</Field>
                      <Field label={t('wo.causeNotes')}>{w.causeNotes}</Field>
                      <Field label={t('wo.preventionNotes')}>{w.preventionNotes}</Field>
                      <Field label={t('wo.failureCode')}>{w.failureCode ? `${w.failureCode.code} · ${w.failureCode.name}` : null}</Field>
                      <Field label={t('wo.downtimeHours')}>{w.assetWasDown ? `${w.downtimeHours ?? 0} h` : t('common.no')}</Field>
                    </SimpleGrid>
                  </Card>
                )}
              </Stack>
            </Grid.Col>
            <Grid.Col span={{ base: 12, md: 4 }}>
              <Stack gap="lg">
                <Card><SimpleGrid cols={2} spacing="sm">
                  <Field label={t('wo.assignees')}>{w.assignees.map((a) => a.fullName).join(', ') || t('wo.noAssignees')}</Field>
                  <Field label={t('wo.trade')}>{w.tradeName}</Field>
                  <Field label={t('wo.requestedBy')}>{w.requestedBy ?? w.request?.requesterName}</Field>
                  <Field label={t('wo.plannedStart')}>{fmtDate(w.plannedStart)}</Field>
                  <Field label={t('wo.dueDate')}><Text span c={w.isOverdue ? 'red' : undefined} fw={500} fz="sm">{fmtDate(w.dueDate)}</Text></Field>
                  <Field label={t('wo.estimatedHours')}>{w.estimatedHours}</Field>
                  <Field label={t('wo.createdOn')}>{fmtDateTime(w.createdAt)}</Field>
                  {w.startedAt && <Field label={t('wo.started')}>{fmtDateTime(w.startedAt)}</Field>}
                  {w.completedAt && <Field label={t('wo.completed')}>{fmtDateTime(w.completedAt)}</Field>}
                  {w.closedAt && <Field label={t('wo.closed')}>{fmtDateTime(w.closedAt)}</Field>}
                </SimpleGrid></Card>
                <Card>
                  <Text fw={600} mb="sm">{t('wo.totals')}</Text>
                  <Table verticalSpacing={4}><Table.Tbody>
                    <Table.Tr><Table.Td>{t('wo.laborHours')}</Table.Td><Table.Td ta="right">{w.totals.laborHours}</Table.Td></Table.Tr>
                    <Table.Tr><Table.Td>{t('wo.laborCost')}</Table.Td><Table.Td ta="right">{fmtMoney(w.totals.laborCost, currency)}</Table.Td></Table.Tr>
                    <Table.Tr><Table.Td>{t('wo.otherCost')}</Table.Td><Table.Td ta="right">{fmtMoney(w.totals.otherCost, currency)}</Table.Td></Table.Tr>
                    <Table.Tr><Table.Td fw={700}>{t('wo.totalCost')}</Table.Td><Table.Td ta="right" fw={700}>{fmtMoney(w.totals.totalCost, currency)}</Table.Td></Table.Tr>
                  </Table.Tbody></Table>
                </Card>
              </Stack>
            </Grid.Col>
          </Grid>
        </Tabs.Panel>
        <Tabs.Panel value="tasks"><TasksPanel w={w} canExec={canExec} onChange={refresh} /></Tabs.Panel>
        <Tabs.Panel value="labor"><LaborPanel w={w} canExec={canExec} onChange={refresh} currency={currency} /></Tabs.Panel>
        <Tabs.Panel value="costs"><CostsPanel w={w} canExec={canExec} onChange={refresh} currency={currency} /></Tabs.Panel>
        <Tabs.Panel value="activity"><ActivityPanel id={w.id} /></Tabs.Panel>
        <Tabs.Panel value="files"><Card><Attachments entityType="WorkOrder" entityId={w.id} canEdit={canExec} /></Card></Tabs.Panel>
      </Tabs>
      <CompleteModal opened={completeOpen} onClose={complete.close} w={w} onDone={refresh} />
      <HoldModal opened={holdOpen} onClose={hold.close} w={w} onDone={refresh} />
    </>
  );
}

function TasksPanel({ w, canExec, onChange }: { w: WorkOrderDetail; canExec: boolean; onChange: () => Promise<void> }) {
  const { t } = useTranslation();
  const [text, setText] = useState('');
  // Optimistic ticks: the checkbox flips immediately and reverts if the save fails.
  const [pending, setPending] = useState<Record<string, boolean>>({});
  const isDone = (x: WorkOrderDetail['tasks'][number]) => pending[x.id] ?? x.isDone;
  const toggle = async (taskId: string, value: boolean) => {
    setPending((p) => ({ ...p, [taskId]: value }));
    try { await put(`/work-orders/${w.id}/tasks/${taskId}`, { isDone: value }); await onChange(); }
    catch (err) { notifyError(err); }
    finally { setPending((p) => { const n = { ...p }; delete n[taskId]; return n; }); }
  };
  const done = w.tasks.filter(isDone).length;
  const add = async () => {
    if (!text.trim()) return;
    try { await post(`/work-orders/${w.id}/tasks`, { description: text }); setText(''); await onChange(); } catch (e) { notifyError(e); }
  };
  return (
    <Card maw={820}>
      {w.tasks.length > 0 && <Group mb="md" gap="sm"><Progress value={(done / w.tasks.length) * 100} style={{ flex: 1 }} color="teal" /><Text fz="sm" c="dimmed">{t('wo.tasksDone', { done, total: w.tasks.length })}</Text></Group>}
      <Stack gap={6}>
        {w.tasks.length === 0 && <Text c="dimmed" fz="sm">{t('wo.noTasks')}</Text>}
        {w.tasks.map((x) => (
          <Group key={x.id} justify="space-between" wrap="nowrap" p={6} style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}>
            <Checkbox checked={isDone(x)} disabled={!canExec} label={<Text fz="sm" td={isDone(x) ? 'line-through' : undefined} c={isDone(x) ? 'dimmed' : undefined}>{x.description}</Text>}
              onChange={(e) => void toggle(x.id, e.currentTarget.checked)} />
            <Group gap={6} wrap="nowrap">
              {x.isDone && <Text fz="xs" c="dimmed">{x.doneBy} · {fromNow(x.doneAt)}</Text>}
              {canExec && <ActionIcon variant="subtle" color="gray" size="sm" aria-label={t('common.delete')} onClick={async () => { try { await del(`/work-orders/${w.id}/tasks/${x.id}`); await onChange(); } catch (e) { notifyError(e); } }}><IconTrash size={14} /></ActionIcon>}
            </Group>
          </Group>
        ))}
      </Stack>
      {canExec && (
        <Group mt="md" gap="xs"><TextInput style={{ flex: 1 }} placeholder={t('wo.addTask')} value={text} onChange={(e) => setText(e.currentTarget.value)} onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); void add(); } }} />
          <Button leftSection={<IconPlus size={14} />} onClick={add}>{t('common.create')}</Button></Group>
      )}
    </Card>
  );
}

function LaborPanel({ w, canExec, onChange, currency }: { w: WorkOrderDetail; canExec: boolean; onChange: () => Promise<void>; currency: string }) {
  const { t } = useTranslation();
  const { data: people } = useEmployees(w.siteId);
  const [opened, m] = useDisclosure();
  const form = useForm({ initialValues: { employeeId: null as string | null, workDate: new Date() as Date | null, hours: 1 as number | '', overtimeHours: 0 as number | '', notes: '' } });
  const open = () => { form.setValues({ employeeId: w.assignees[0]?.employeeId ?? null, workDate: new Date(), hours: 1, overtimeHours: 0, notes: '' }); form.clearErrors(); m.open(); };
  const submit = form.onSubmit(async (v) => {
    try { await post(`/work-orders/${w.id}/labor`, { ...v, workDate: toIsoDate(v.workDate), hours: v.hours || 0, overtimeHours: v.overtimeHours || 0 }); m.close(); await onChange(); }
    catch (e) { handleFormError(form, e); }
  });
  return (
    <Card p={0}>
      <Group justify="space-between" p="md"><Title order={5}>{t('wo.labor')} · {w.totals.laborHours} h · {fmtMoney(w.totals.laborCost, currency)}</Title>
        {canExec && <Button size="xs" leftSection={<IconPlus size={14} />} onClick={open}>{t('wo.logLabor')}</Button>}</Group>
      <Table.ScrollContainer minWidth={640}>
        <Table verticalSpacing="sm" highlightOnHover>
          <Table.Thead><Table.Tr><Table.Th>{t('wo.date')}</Table.Th><Table.Th>{t('wo.who')}</Table.Th><Table.Th ta="right">{t('wo.hours')}</Table.Th><Table.Th ta="right">{t('wo.overtime')}</Table.Th><Table.Th ta="right">{t('wo.cost')}</Table.Th><Table.Th>{t('common.notes')}</Table.Th><Table.Th /></Table.Tr></Table.Thead>
          <Table.Tbody>
            {w.labor.length === 0 && <Table.Tr><Table.Td colSpan={7}><Text c="dimmed" fz="sm" ta="center">{t('common.noRecords')}</Text></Table.Td></Table.Tr>}
            {w.labor.map((l) => (
              <Table.Tr key={l.id}><Table.Td>{fmtDate(l.workDate)}</Table.Td><Table.Td>{l.employeeName}</Table.Td><Table.Td ta="right">{l.hours}</Table.Td><Table.Td ta="right">{l.overtimeHours || '—'}</Table.Td>
                <Table.Td ta="right">{fmtMoney(l.cost, currency)}</Table.Td><Table.Td><Text fz="sm" lineClamp={2}>{l.notes}</Text></Table.Td>
                <Table.Td>{canExec && <ActionIcon variant="subtle" color="gray" size="sm" aria-label={t('common.delete')} onClick={() => confirmDelete(`${l.employeeName} · ${l.hours}h`, async () => { try { await del(`/work-orders/${w.id}/labor/${l.id}`); await onChange(); } catch (e) { notifyError(e); } })}><IconTrash size={14} /></ActionIcon>}</Table.Td></Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      <Modal opened={opened} onClose={m.close} title={t('wo.logLabor')}>
        <form onSubmit={submit}><Stack>
          <Select label={t('wo.who')} required searchable data={(people ?? []).map((p) => ({ value: p.id, label: p.fullName }))} {...form.getInputProps('employeeId')} />
          <DateInput label={t('wo.date')} required valueFormat="DD MMM YYYY" maxDate={dayjs().add(1, 'day').toDate()} {...form.getInputProps('workDate')} />
          <SimpleGrid cols={2}>
            <NumberInput label={t('wo.hours')} min={0} max={24} step={0.25} decimalScale={2} {...form.getInputProps('hours')} />
            <NumberInput label={t('wo.overtime')} min={0} max={24} step={0.25} decimalScale={2} {...form.getInputProps('overtimeHours')} />
          </SimpleGrid>
          <Textarea label={t('common.notes')} autosize {...form.getInputProps('notes')} />
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack></form>
      </Modal>
    </Card>
  );
}

function CostsPanel({ w, canExec, onChange, currency }: { w: WorkOrderDetail; canExec: boolean; onChange: () => Promise<void>; currency: string }) {
  const { t } = useTranslation();
  const form = useForm({ initialValues: { description: '', quantity: 1 as number | '', unitCost: '' as number | '', vendor: '', reference: '' } });
  const submit = form.onSubmit(async (v) => {
    try { await post(`/work-orders/${w.id}/costs`, { ...v, quantity: v.quantity || 0, unitCost: v.unitCost || 0 }); form.reset(); await onChange(); } catch (e) { handleFormError(form, e); }
  });
  return (
    <Card p={0}>
      <Group justify="space-between" p="md"><Title order={5}>{t('wo.costs')} · {fmtMoney(w.totals.otherCost, currency)}</Title></Group>
      <Table.ScrollContainer minWidth={600}>
        <Table verticalSpacing="sm">
          <Table.Thead><Table.Tr><Table.Th>{t('common.description')}</Table.Th><Table.Th>{t('wo.vendor')}</Table.Th><Table.Th>{t('wo.reference')}</Table.Th><Table.Th ta="right">{t('wo.qty')}</Table.Th><Table.Th ta="right">{t('wo.unitCost')}</Table.Th><Table.Th ta="right">{t('wo.total')}</Table.Th><Table.Th /></Table.Tr></Table.Thead>
          <Table.Tbody>
            {w.costs.map((c) => (
              <Table.Tr key={c.id}><Table.Td>{c.description}</Table.Td><Table.Td>{c.vendor}</Table.Td><Table.Td>{c.reference}</Table.Td><Table.Td ta="right">{c.quantity}</Table.Td><Table.Td ta="right">{fmtMoney(c.unitCost, currency)}</Table.Td><Table.Td ta="right">{fmtMoney(c.total, currency)}</Table.Td>
                <Table.Td>{canExec && <ActionIcon variant="subtle" color="gray" size="sm" aria-label={t('common.delete')} onClick={async () => { try { await del(`/work-orders/${w.id}/costs/${c.id}`); await onChange(); } catch (e) { notifyError(e); } }}><IconTrash size={14} /></ActionIcon>}</Table.Td></Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      {canExec && (
        <form onSubmit={submit}>
          <Group p="md" gap="xs" align="flex-start" wrap="wrap">
            <TextInput placeholder={t('common.description')} style={{ flex: '2 1 200px' }} {...form.getInputProps('description')} />
            <TextInput placeholder={t('wo.vendor')} style={{ flex: '1 1 140px' }} {...form.getInputProps('vendor')} />
            <TextInput placeholder={t('wo.reference')} style={{ flex: '1 1 110px' }} {...form.getInputProps('reference')} />
            <NumberInput placeholder={t('wo.qty')} w={90} min={0} {...form.getInputProps('quantity')} />
            <NumberInput placeholder={t('wo.unitCost')} w={130} min={0} thousandSeparator {...form.getInputProps('unitCost')} />
            <Button type="submit" leftSection={<IconPlus size={14} />} loading={form.submitting}>{t('wo.addCost')}</Button>
          </Group>
        </form>
      )}
    </Card>
  );
}

function ActivityPanel({ id }: { id: string }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [body, setBody] = useState('');
  const [busy, setBusy] = useState(false);
  const { data } = useQuery({ queryKey: ['work-order-activity', id], queryFn: () => get<ActivityItem[]>(`/work-orders/${id}/activity`) });
  const postComment = async () => {
    if (!body.trim()) return;
    setBusy(true);
    try { await post(`/work-orders/${id}/comments`, { body }); setBody(''); await qc.invalidateQueries({ queryKey: ['work-order-activity', id] }); } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Card maw={820}>
      <Group align="flex-end" gap="xs" mb="lg"><Textarea style={{ flex: 1 }} autosize minRows={2} placeholder={t('wo.comment')} value={body} onChange={(e) => setBody(e.currentTarget.value)} />
        <Button leftSection={<IconMessage size={14} />} loading={busy} onClick={postComment}>{t('wo.post')}</Button></Group>
      <Timeline bulletSize={26} lineWidth={2}>
        {(data ?? []).map((a) => (
          <Timeline.Item key={a.id} bullet={a.kind === 'comment' ? <IconMessage size={14} /> : <IconInfoCircle size={14} />}
            title={<Text fz="sm"><b>{a.by ?? 'System'}</b>{' '}{a.kind === 'comment' ? t('wo.commented') : a.from ? t('wo.statusChanged', { from: t(`woStatus.${a.from}`), to: t(`woStatus.${a.to}`) }) : t('wo.created')}
              {a.holdReason && <Text span c="dimmed"> · {t(`holdReason.${a.holdReason}`)}</Text>}</Text>}>
            {a.body && <Text fz="sm" mt={2} style={{ whiteSpace: 'pre-wrap' }}>{a.body}</Text>}
            <Text fz="xs" c="dimmed" mt={2}>{fmtDateTime(a.at)}</Text>
          </Timeline.Item>
        ))}
      </Timeline>
    </Card>
  );
}

function CompleteModal({ opened, onClose, w, onDone }: { opened: boolean; onClose: () => void; w: WorkOrderDetail; onDone: () => Promise<void> }) {
  const { t } = useTranslation();
  const { data: codes } = useQuery({ queryKey: ['failure-codes'], queryFn: () => get<Lookup[]>('/failure-codes') });
  // Defaults are recomputed every time the dialog opens, so they reflect work started/labour logged since page load.
  const defaults = () => {
    const earliestLabor = [...w.labor].sort((a, b) => a.workDate.localeCompare(b.workDate))[0];
    const start = w.startedAt ? new Date(w.startedAt) : earliestLabor ? new Date(`${earliestLabor.workDate}T08:00:00`) : null;
    return { completionNotes: w.completionNotes ?? '', causeNotes: w.causeNotes ?? '', preventionNotes: w.preventionNotes ?? '', failureCodeId: (w.failureCodeId ?? null) as string | null,
      assetWasDown: w.assetWasDown || w.type === 'Breakdown', downtimeHours: (w.downtimeHours ?? '') as number | '', actualStart: start as Date | null, actualEnd: new Date() as Date | null };
  };
  const form = useForm({ initialValues: defaults() });
  useEffect(() => { if (opened) { form.setValues(defaults()); form.clearErrors(); } /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [opened]);
  const calcDowntime = () => { const { actualStart: s, actualEnd: e } = form.values; if (s && e && e > s) form.setFieldValue('downtimeHours', Math.round(((e.getTime() - s.getTime()) / 36e5) * 100) / 100); };
  const submit = form.onSubmit(async (v) => {
    try {
      await post(`/work-orders/${w.id}/status`, { status: 'Completed', ...v, downtimeHours: v.downtimeHours === '' ? null : v.downtimeHours,
        actualStart: v.actualStart ? dayjs(v.actualStart).toISOString() : null, actualEnd: v.actualEnd ? dayjs(v.actualEnd).toISOString() : null });
      notifySuccess(t('woStatus.Completed')); onClose(); await onDone();
    } catch (e) { handleFormError(form, e); }
  });
  const open = w.tasks.filter((x) => !x.isDone).length;
  return (
    <Modal opened={opened} onClose={onClose} title={`${t('wo.completeTitle')} · ${w.number}`} size="lg">
      <form onSubmit={submit}><Stack>
        {open > 0 && <Alert color="yellow" icon={<IconAlertTriangle size={16} />}>{t('wo.tasksDone', { done: w.tasks.length - open, total: w.tasks.length })}</Alert>}
        <Textarea label={t('wo.completionNotes')} required autosize minRows={3} data-autofocus {...form.getInputProps('completionNotes')} />
        <SimpleGrid cols={2}>
          <DateTimePicker label={t('wo.actualStart')} valueFormat="DD MMM YYYY HH:mm" clearable {...form.getInputProps('actualStart')} />
          <DateTimePicker label={t('wo.actualEnd')} valueFormat="DD MMM YYYY HH:mm" maxDate={new Date()} {...form.getInputProps('actualEnd')} />
        </SimpleGrid>
        <SimpleGrid cols={2}>
          <Textarea label={t('wo.causeNotes')} autosize minRows={2} {...form.getInputProps('causeNotes')} />
          <Textarea label={t('wo.preventionNotes')} autosize minRows={2} {...form.getInputProps('preventionNotes')} />
        </SimpleGrid>
        <Select label={t('wo.failureCode')} clearable searchable data={(codes ?? []).filter((c) => c.isActive).map((c) => ({ value: c.id, label: `${c.code} · ${c.name}` }))} {...form.getInputProps('failureCodeId')} />
        {w.assetId && <Group grow align="flex-end">
          <Switch label={t('wo.assetWasDown')} {...form.getInputProps('assetWasDown', { type: 'checkbox' })} />
          {form.values.assetWasDown && <NumberInput label={t('wo.downtimeHours')} min={0} decimalScale={2} {...form.getInputProps('downtimeHours')}
            rightSectionWidth={110} rightSection={<Button size="compact-xs" variant="subtle" onClick={calcDowntime}>{t('wo.calcDowntime')}</Button>} />}
        </Group>}
        <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button type="submit" color="teal" leftSection={<IconCheck size={16} />} loading={form.submitting}>{t('transition.Completed')}</Button></Group>
      </Stack></form>
    </Modal>
  );
}

function HoldModal({ opened, onClose, w, onDone }: { opened: boolean; onClose: () => void; w: WorkOrderDetail; onDone: () => Promise<void> }) {
  const { t } = useTranslation();
  const form = useForm({ initialValues: { holdReason: 'WaitingForParts' as string | null, note: '' } });
  const submit = form.onSubmit(async (v) => {
    try { await post(`/work-orders/${w.id}/status`, { status: 'OnHold', ...v }); onClose(); form.reset(); await onDone(); } catch (e) { handleFormError(form, e); }
  });
  return (
    <Modal opened={opened} onClose={onClose} title={t('wo.holdTitle')}>
      <form onSubmit={submit}><Stack>
        <Select label={t('wo.holdReason')} required data={HOLD_REASONS.map((x) => ({ value: x, label: t(`holdReason.${x}`) }))} {...form.getInputProps('holdReason')} />
        <Textarea label={t('wo.note')} autosize minRows={2} {...form.getInputProps('note')} />
        <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button type="submit" color="yellow" loading={form.submitting}>{t('transition.OnHold')}</Button></Group>
      </Stack></form>
    </Modal>
  );
}
