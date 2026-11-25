import { useEffect, useState } from 'react';
import { ActionIcon, Alert, Anchor, Badge, Breadcrumbs, Button, Card, Center, Grid, Group, Loader, Menu, Modal, NumberInput, Select, SimpleGrid, Stack, Switch, Table, Tabs, Text, TextInput, Textarea, Timeline, Title, Tooltip } from '@mantine/core';
import { DateInput, DateTimePicker } from '@mantine/dates';
import { useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { WorkOrderUnits } from './WorkOrderUnits';
import { SlaText } from '@/components/SlaBadge';
import dayjs from 'dayjs';
import { IconAlertTriangle, IconCheck, IconCopy, IconDots, IconEdit, IconPrinter, IconInfoCircle, IconMessage, IconPlayerPause, IconPlayerPlay, IconPlus, IconRefresh, IconTrash, IconX, IconLock, IconShieldCheck, IconArrowBackUp, IconSparkles, IconCoin } from '@tabler/icons-react';
import { del, get, post } from '@/api/client';
import { HOLD_REASONS, type ActivityItem, type Lookup, type WorkOrderDetail, type WorkOrderStatus } from '@/api/types';
import { CodeSelect } from '@/components/CodeSelect';
import { DiagnosisPanel } from '@/components/DiagnosisPanel';
import { aiOn, useAiStatus } from '@/components/aiStatus';
import { applyReport, reportHints, type AiReportResult } from '@/components/aiReport';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { Attachments } from '@/components/Attachments';
import { useEmployees } from '@/components/EmployeePicker';
import { PriorityBadge, WoStatusBadge } from '@/components/WorkBadges';
import { CriticalityBadge } from '@/components/Badges';
import { confirmDelete } from '@/components/confirm';
import { StockTxModal } from '@/components/StockTxModal';
import { AccessWindowsList, StoppedBadge } from '@/components/AccessWindowsEditor';
import { FeedbackLine, RequestComments } from './RequestLoop';
import { noteRequired } from './statusRules';
import { TxTypeBadge } from '@/components/InventoryBadges';
import { fmtDate, fmtDateTime, fmtMoney, fmtPrice, fmtQty, handleFormError, hoursText, notifyError, notifySuccess, toIsoDate } from '@/components/utils';
import { PlannedHoursCard, PlannedPartsCard, PlanVsActualCard } from './WorkOrderPlanning';
import { GroupCard, TemplateCard } from './WorkOrderGroupBits';
import { ChecklistPanel } from './ChecklistPanel';
import { SourceBanner } from './SourceBanner';
import { ContractorsPanel } from './ContractorsPanel';
import { ChangeCostCentreModal } from './ChangeCostCentre';
import { costLines } from './woTotals';
import { CoveredBanner } from '@/components/CoveredBanner';
import { BackRunningButton } from '@/pages/assets/AssetDowntime';
import { downFor } from '@/components/downtime';
import { pick } from '@/components/bilingual';

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
  const [costCentreOpened, costCentre] = useDisclosure();
  const [noteFor, setNoteFor] = useState<WorkOrderStatus | null>(null);
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
  // The plan (parts and hours) can change while the work is open, by people who edit work orders and see inventory.
  const isOpen = w.status === 'Open' || w.status === 'InProgress' || w.status === 'OnHold';
  const canPlan = can(P.woEdit) && can(P.inventoryView) && isOpen;
  // Planned hours are part of editing the work order and need no inventory permission.
  const canPlanHours = can(P.woEdit) && isOpen;
  const move = async (to: WorkOrderStatus) => {
    if (to === 'Completed') return complete.open();
    if (to === 'OnHold') return hold.open();
    // Cancelling asks for a reason; the organization may want a comment to reopen (0.10.0).
    if (to === 'Cancelled' || noteRequired(me?.tenant.settings.commentRequiredFor, w.status, to)) return setNoteFor(to);
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
      <PageHeader crumbs={[{ label: t('wo.title'), to: '/work-orders' }, { label: w.number }]} title={pick(w.title, w.titleAr)}
        badge={<Group gap={6}><WoStatusBadge status={w.status} /><PriorityBadge value={w.priority} />{w.isOverdue && <Badge color="red" variant="filled">{t('wo.overdueBadge')}</Badge>}</Group>}
        subtitle={<>{w.onlyWhenStopped && <><StoppedBadge show size="sm" />{' '}</>}{w.number} · {t(`workType.${w.type}`)}{w.tradeName && ` · ${w.tradeName}`}{w.status === 'OnHold' && w.holdReason && ` · ${t(`holdReason.${w.holdReason}`)}`}
          {w.request && <> · <Anchor component={Link} to={`/work-requests?open=${w.request.id}`} fz="sm">{t('wo.fromRequest', { number: w.request.number })}</Anchor></>}
          {w.pm && <> · <Anchor component={Link} to={`/pm/schedules/${w.pm.id}`} fz="sm">{t('pm.fromPm', { code: w.pm.code })}</Anchor></>}</>}
        actions={<>
          <Tooltip label={t('wo.printJobCard')}><ActionIcon variant="default" size="lg" component={Link} to={`/work-orders/${w.id}/print`} target="_blank" aria-label={t('wo.printJobCard')}>
            <IconPrinter size={16} /></ActionIcon></Tooltip>
          {primary.map((s) => { const Icon = transitionIcon[s] ?? IconCheck; return (
            <Button key={s} leftSection={<Icon size={16} />} variant={s === 'Completed' || (s === 'InProgress' && w.status !== 'Completed') ? 'filled' : 'default'}
              color={s === 'Completed' ? 'teal' : undefined} onClick={() => move(s)}>{label(s)}</Button>); })}
          {(can(P.woEdit) || can(P.woDelete) || can(P.woCreate) || secondary.length > 0) && (
            <Menu position="bottom-end">
              <Menu.Target><ActionIcon variant="default" size="lg" aria-label={t('common.actions')}><IconDots size={16} /></ActionIcon></Menu.Target>
              <Menu.Dropdown>
                {can(P.woEdit) && !locked && <Menu.Item leftSection={<IconEdit size={16} />} component={Link} to={`/work-orders/${w.id}/edit`}>{t('common.edit')}</Menu.Item>}
                {can(P.woEdit) && <Menu.Item leftSection={<IconCoin size={16} />} onClick={costCentre.open}>{t('costCentres.change')}</Menu.Item>}
                {/* A PM round's asset list can't be set from the form, so rounds aren't duplicated. */}
                {can(P.woCreate) && w.assets.length === 0 && <Menu.Item leftSection={<IconCopy size={16} />} component={Link} to={`/work-orders/new?copyFrom=${w.id}`}>{t('wo.duplicate')}</Menu.Item>}
                {secondary.map((s) => <Menu.Item key={s} color={s === 'Cancelled' ? 'red' : undefined} leftSection={s === 'Cancelled' ? <IconX size={16} /> : <IconRefresh size={16} />} onClick={() => move(s)}>{label(s)}</Menu.Item>)}
                {can(P.woDelete) && <><Menu.Divider /><Menu.Item color="red" leftSection={<IconTrash size={16} />} onClick={() => confirmDelete(w.number, async () => {
                  try { await del(`/work-orders/${w.id}`); await qc.invalidateQueries({ queryKey: ['work-orders'] }); navigate('/work-orders'); } catch (e) { notifyError(e); } })}>{t('common.delete')}</Menu.Item></>}
              </Menu.Dropdown>
            </Menu>
          )}
        </>} />
      {w.assetId && w.assetDownSince && (
        <Alert color="red" mb="md" icon={<IconAlertTriangle size={16} />}
          title={t('downtime.assetDownSince', { asset: w.asset?.assetNumber ?? '', when: fmtDateTime(w.assetDownSince), for: downFor(w.assetDownSince, new Date()) })}>
          <Group justify="space-between" gap="xs"><Text fz="sm">{t('downtime.backRunningHint')}</Text>{can(P.woExecute) && <BackRunningButton assetId={w.assetId} size="xs" />}</Group>
        </Alert>
      )}
      <Tabs value={tab} onChange={setTab} keepMounted={false}>
        <Tabs.List mb="md">
          <Tabs.Tab value="details">{t('wo.details')}</Tabs.Tab>
          <Tabs.Tab value="tasks" rightSection={w.tasks.length ? <Badge size="xs" variant="default">{doneTasks}/{w.tasks.length}</Badge> : null}>{t('wo.tasks')}</Tabs.Tab>
          <Tabs.Tab value="labor" rightSection={w.labor.length ? <Badge size="xs" variant="default">{hoursText(w.totals.laborHours)}</Badge> : null}>{t('wo.labor')}</Tabs.Tab>
          <Tabs.Tab value="contractors" rightSection={(w.contractors?.length ?? 0) > 0 ? <Badge size="xs" variant="default">{w.contractors.length}</Badge> : null}>{t('contractors.title')}</Tabs.Tab>
          {can(P.inventoryView) && <Tabs.Tab value="parts" rightSection={w.parts.length ? <Badge size="xs" variant="default">{w.parts.length}</Badge> : null}>{t('wo.parts')}</Tabs.Tab>}
          <Tabs.Tab value="costs">{t('wo.costs')}</Tabs.Tab>
          <Tabs.Tab value="activity">{t('wo.activity')}</Tabs.Tab>
          <Tabs.Tab value="files">{t('wo.files')}</Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="details">
          <Grid gutter="lg">
            <Grid.Col span={{ base: 12, md: 8 }}>
              <Stack gap="lg">
                {w.group && <GroupCard group={w.group} canComment={canExec} onChange={refresh} />}
                <DiagnosisPanel kind="work-order" id={w.id} hasAsset={!!w.assetId} canRun={can(P.woExecute)} canPlan={canPlan}
                  plannedPartIds={w.plannedParts.map((p) => p.partId)} onPlanned={refresh} />
                {w.source && <SourceBanner source={w.source} />}
                <CoveredBanner covered={w.coveredBy} />
                {w.asset?.underWarranty && !locked && (
                  <Alert variant="light" color="teal" icon={<IconShieldCheck size={18} />} title={t('wo.underWarrantyTitle', { type: t(`warrantyType.${w.asset.warrantyType}`) })}>
                    {t('wo.underWarrantyBody', { date: fmtDate(w.asset.warrantyExpiresOn) })}
                  </Alert>
                )}
                {w.status === 'Cancelled' && w.cancelReason && (
                  <Alert variant="light" color="gray" icon={<IconX size={18} />} title={`${t('wo.cancelReason')}: ${w.cancelReason.name}`} />
                )}
                {(w.accessWindows?.length ?? 0) > 0 && <Card><AccessWindowsList windows={w.accessWindows} outside={w.outsideWindows} /></Card>}
                {w.request && (w.request.feedback || w.request.awaitingFeedback || (w.request.comments?.length ?? 0) > 0) && (
                  <Card><Stack gap="sm">
                    <Text fz="xs" c="dimmed">{t('wo.fromRequest', { number: w.request.number })}</Text>
                    {w.request.awaitingFeedback && <Text fz="sm" c="dimmed">{t('requestLoop.waitingForRequester')}</Text>}
                    {w.request.feedback && <Stack gap={4}><Text fz="sm" fw={500}>{t('requestLoop.answer')}</Text><FeedbackLine f={w.request.feedback} /></Stack>}
                    {(w.request.comments?.length ?? 0) > 0 && <RequestComments requestId={w.request.id} comments={w.request.comments} readOnly />}
                  </Stack></Card>
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
                  {w.assets.length > 0 && (
                    <Group gap="xs" mb="md" align="flex-start" wrap="nowrap"><Text fz="sm" c="dimmed">{t('wo.assets')}:</Text>
                      <Group gap={8}>{w.assets.map((a) => <Anchor key={a.id} component={Link} to={`/assets/${a.id}`} fz="sm" fw={500} title={a.name}>{a.assetNumber}</Anchor>)}</Group></Group>
                  )}
                  <Field label={t('wo.description')}>{w.description}</Field>
                  {w.safetyNotes && <Field label={t('wo.safety')}>{pick(w.safetyNotes, w.safetyNotesAr)}</Field>}
                </Card>
                {(w.completedAt || w.completionNotes || w.signatures.length > 0) && (
                  <Card>
                    <Title order={5} mb="sm">{t('wo.completed')}</Title>
                    <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="md">
                      <Field label={t('wo.completionNotes')}>{w.completionNotes}</Field>
                      <Field label={t('wo.causeNotes')}>{w.causeNotes}</Field>
                      <Field label={t('wo.preventionNotes')}>{w.preventionNotes}</Field>
                      <Field label={t('codes.cause')}>{w.failureCode ? `${w.failureCode.code} · ${w.failureCode.name}` : null}</Field>
                      <Field label={t('codes.remedy')}>{w.remedy ? `${w.remedy.code} · ${w.remedy.name}` : null}</Field>
                      <Field label={t('wo.downtimeHours')}>{w.assetWasDown ? hoursText(w.downtimeHours ?? 0) : t('common.no')}
                        {w.downtime && <Text span fz="xs" c="dimmed"> · {t('downtime.fromLogShort')}</Text>}</Field>
                    </SimpleGrid>
                    {w.signatures.length > 0 && (
                      <Group mt="md" gap="lg" align="flex-end">
                        {w.signatures.map((s) => (
                          <Stack key={s.id} gap={2}>
                            <img src={`/api/attachments/${s.id}/content`} alt={t('wo.signature')} style={{ height: 70, maxWidth: 220, objectFit: 'contain', background: '#fff', borderBottom: '1px solid var(--mantine-color-gray-5)' }} />
                            <Text fz="xs" c="dimmed">{s.signedBy ? `${s.signedBy} · ` : ''}{fmtDateTime(s.createdAt)}</Text>
                          </Stack>
                        ))}
                      </Group>
                    )}
                  </Card>
                )}
              </Stack>
            </Grid.Col>
            <Grid.Col span={{ base: 12, md: 4 }}>
              <Stack gap="lg">
                <Card><SimpleGrid cols={2} spacing="sm">
                  <Field label={t('wo.assignees')}>{w.assignees.map((a) => a.fullName).join(', ') || t('wo.noAssignees')}</Field>
                  {(w.recipients?.length ?? 0) > 0 && <Field label={t('pm12.recipients')}>{w.recipients!.map((r) => r.fullName).join(', ')}</Field>}
                  <Field label={t('wo.trade')}>{w.tradeName}</Field>
                  {w.contract && <Field label={t('contracts.contract')}>{can(P.contractsView)
                    ? <Anchor component={Link} to={`/contracts/${w.contract.id}`} fz="sm">{w.contract.number} · {w.contract.companyName}</Anchor>
                    : `${w.contract.number} · ${w.contract.companyName}`}</Field>}
                  {w.team && <Field label={t('teams.team')}>{w.team.name}</Field>}
                  <Field label={t('costCentres.one')}>{w.costCentrePath}</Field>
                  <Field label={t('codes.symptom')}>{w.symptom ? `${w.symptom.code} · ${w.symptom.name}` : null}</Field>
                  <Field label={t('wo.requestedBy')}>{w.requestedBy ?? w.request?.requesterName}</Field>
                  <Field label={t('wo.plannedStart')}>{fmtDate(w.plannedStart)}</Field>
                  <Field label={t('wo.due')}>
                    <Stack gap={0}>
                      <Text span fw={500} fz="sm">{w.dueAt ? fmtDateTime(w.dueAt) : fmtDate(w.dueDate)}</Text>
                      <SlaText fz="xs" deadline={w.sla.dueAt ?? w.dueAt} state={w.sla.completion} />
                      {w.group && <Text span fz="xs" c="dimmed">{t('groups.dueFromGroup', { number: w.group.number })}</Text>}
                    </Stack>
                  </Field>
                  {w.sla.respondBy && (
                    <Field label={w.request ? t('sla.requestAnswered') : t('sla.startBy')}>
                      <Stack gap={0}>
                        <Text span fw={500} fz="sm">{fmtDateTime(w.sla.respondBy)}</Text>
                        {w.sla.respondedAt
                          ? <Text span fz="xs" c={w.sla.response === 'Missed' ? 'red' : 'teal'}>{t(w.sla.response === 'Missed' ? 'sla.respondedLate' : 'sla.respondedAt', { when: fmtDateTime(w.sla.respondedAt) })}</Text>
                          : <SlaText fz="xs" deadline={w.sla.respondBy} state={w.sla.response} />}
                      </Stack>
                    </Field>
                  )}
                  <Field label={t('wo.estimatedHours')}>{w.estimatedHours}</Field>
                  <Field label={t('wo.createdOn')}>{fmtDateTime(w.createdAt)}</Field>
                  {w.startedAt && <Field label={t('wo.started')}>{fmtDateTime(w.startedAt)}</Field>}
                  {w.completedAt && <Field label={t('wo.completed')}>{fmtDateTime(w.completedAt)}</Field>}
                  {w.closedAt && <Field label={t('wo.closed')}>{fmtDateTime(w.closedAt)}</Field>}
                </SimpleGrid></Card>
                {w.template && <TemplateCard template={w.template} />}
                <Card>
                  <Text fw={600} mb="sm">{t('wo.totals')}</Text>
                  <Table verticalSpacing={4}><Table.Tbody>
                    <Table.Tr><Table.Td>{t('wo.laborHours')}</Table.Td><Table.Td ta="right">{w.totals.laborHours}</Table.Td></Table.Tr>
                    {costLines(w.totals).map((l) => <Table.Tr key={l.key}><Table.Td>{t(l.key)}</Table.Td><Table.Td ta="right">{fmtMoney(l.value, currency)}</Table.Td></Table.Tr>)}
                    <Table.Tr><Table.Td fw={700}>{t('wo.totalCost')}</Table.Td><Table.Td ta="right" fw={700}>{fmtMoney(w.totals.totalCost, currency)}</Table.Td></Table.Tr>
                  </Table.Tbody></Table>
                </Card>
              </Stack>
            </Grid.Col>
          </Grid>
        </Tabs.Panel>
        <Tabs.Panel value="tasks"><ChecklistPanel w={w} canExec={canExec} onChange={refresh} /></Tabs.Panel>
        <Tabs.Panel value="labor"><Stack><PlannedHoursCard w={w} canPlan={canPlanHours} onChange={refresh} currency={currency} /><LaborPanel w={w} canExec={canExec} onChange={refresh} currency={currency} /></Stack></Tabs.Panel>
        <Tabs.Panel value="contractors"><ContractorsPanel w={w} canPlan={canPlanHours} canExec={canExec} onChange={refresh} currency={currency} /></Tabs.Panel>
        <Tabs.Panel value="parts"><Stack>
          <WoPartsPanel w={w} canPlan={canPlan} canIssue={can(P.inventoryTransact) && !locked} canOrder={can(P.purchasingCreate) && !locked} onChange={refresh} currency={currency} />
          <WorkOrderUnits w={w} canAct={can(P.woExecute) && can(P.inventoryTransact) && !locked} />
        </Stack></Tabs.Panel>
        <Tabs.Panel value="costs"><Stack><PlanVsActualCard w={w} currency={currency} /><CostsPanel w={w} canExec={canExec} onChange={refresh} currency={currency} /></Stack></Tabs.Panel>
        <Tabs.Panel value="activity"><ActivityPanel id={w.id} /></Tabs.Panel>
        <Tabs.Panel value="files"><Card><Attachments entityType="WorkOrder" entityId={w.id} canEdit={canExec} /></Card></Tabs.Panel>
      </Tabs>
      <CompleteModal opened={completeOpen} onClose={complete.close} w={w} onDone={refresh} />
      <ChangeCostCentreModal ids={[w.id]} current={w.costCentreId} opened={costCentreOpened} onClose={costCentre.close} />
      <HoldModal opened={holdOpen} onClose={hold.close} w={w} onDone={refresh} />
      {noteFor && <StatusNoteModal to={noteFor} onClose={() => setNoteFor(null)} w={w} onDone={refresh} />}
    </>
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
              <Table.Tr key={l.id}><Table.Td>{fmtDate(l.workDate)}</Table.Td><Table.Td>{l.employeeName}{l.companyName && <Badge size="xs" variant="light" color="grape" ms={6}>{l.companyName}</Badge>}</Table.Td><Table.Td ta="right">{l.hours}</Table.Td><Table.Td ta="right">{l.overtimeHours || '—'}</Table.Td>
                <Table.Td ta="right">{fmtMoney(l.cost, currency)}</Table.Td><Table.Td><Text fz="sm" lineClamp={2}>{l.notes}</Text></Table.Td>
                <Table.Td>{canExec && <ActionIcon variant="subtle" color="gray" size="sm" aria-label={t('common.delete')} onClick={() => confirmDelete(`${l.employeeName} · ${hoursText(l.hours)}`, async () => { try { await del(`/work-orders/${w.id}/labor/${l.id}`); await onChange(); } catch (e) { notifyError(e); } })}><IconTrash size={14} /></ActionIcon>}</Table.Td></Table.Tr>
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

function WoPartsPanel({ w, canPlan, canIssue, canOrder, onChange, currency }: { w: WorkOrderDetail; canPlan: boolean; canIssue: boolean; canOrder: boolean; onChange: () => Promise<void>; currency: string }) {
  const { t } = useTranslation();
  const [kind, setKind] = useState<'issue' | 'return' | null>(null);
  const [part, setPart] = useState<{ id: string; partNumber: string; name: string } | null>(null);
  // Issuing a planned line starts from its store and what it still reserves.
  const [preset, setPreset] = useState<{ storeId?: string | null; quantity?: number }>({});
  const open = (k: 'issue' | 'return', p: { id: string; partNumber: string; name: string } | null, x: { storeId?: string | null; quantity?: number } = {}) => { setPart(p); setPreset(x); setKind(k); };
  const net = Object.values(w.parts.reduce<Record<string, { partId: string; partNumber: string; partName: string; unit: string; qty: number; cost: number }>>((a, p) => {
    const x = (a[p.partId] ??= { partId: p.partId, partNumber: p.partNumber, partName: p.partName, unit: p.unitOfMeasure, qty: 0, cost: 0 });
    x.qty += p.quantity; x.cost += p.total; return a;
  }, {}));
  return (
    <Stack>
      {(canPlan || w.plannedParts.length > 0) && (
        <PlannedPartsCard w={w} canPlan={canPlan} canIssue={canIssue} onChange={onChange} currency={currency}
          onIssue={(l) => open('issue', { id: l.partId, partNumber: l.partNumber, name: l.partName }, { storeId: l.storeId, quantity: l.reserved })} />
      )}
      <Card p={0}>
        <Group justify="space-between" p="md" wrap="wrap">
          <Title order={5}>{t('wo.parts')} · {fmtPrice(w.totals.partsCost, currency)}</Title>
          <Group gap="xs">
            {canOrder && <Button size="xs" variant="default" component={Link} to={`/purchasing/orders/new?workOrderId=${w.id}`}>{t('wo.orderForJob')}</Button>}
            {canIssue && <Button size="xs" variant="default" leftSection={<IconArrowBackUp size={14} />} disabled={!net.some((n) => n.qty > 0)} onClick={() => open('return', null)}>{t('inv.tx.return')}</Button>}
            {canIssue && <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open('issue', null)}>{t('wo.issuePart')}</Button>}
          </Group>
        </Group>
        <Table.ScrollContainer minWidth={640}>
          <Table verticalSpacing="sm">
            <Table.Thead><Table.Tr><Table.Th>{t('inv.part')}</Table.Th><Table.Th ta="right">{t('wo.netIssued')}</Table.Th><Table.Th ta="right">{t('wo.total')}</Table.Th><Table.Th w={110} /></Table.Tr></Table.Thead>
            <Table.Tbody>
              {net.length === 0 && <Table.Tr><Table.Td colSpan={4}><Text c="dimmed" ta="center" py="md" fz="sm">{t('wo.noParts')}</Text></Table.Td></Table.Tr>}
              {net.map((n) => (
                <Table.Tr key={n.partId}>
                  <Table.Td><Anchor component={Link} to={`/inventory/parts/${n.partId}`} fz="sm" fw={600}>{n.partNumber}</Anchor><Text fz="xs" c="dimmed">{n.partName}</Text></Table.Td>
                  <Table.Td ta="right">{fmtQty(n.qty)} {n.unit}</Table.Td><Table.Td ta="right">{fmtPrice(n.cost, currency)}</Table.Td>
                  <Table.Td ta="right">{canIssue && n.qty > 0 && <Button size="compact-xs" variant="subtle" onClick={() => open('return', { id: n.partId, partNumber: n.partNumber, name: n.partName })}>{t('inv.tx.return')}</Button>}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      </Card>
      {w.parts.length > 0 && (
        <Card p={0}>
          <Text fw={600} p="md" pb={0}>{t('wo.partsMovements')}</Text>
          <Table verticalSpacing={6} mt="xs">
            <Table.Tbody>
              {w.parts.map((p) => <Table.Tr key={p.id}><Table.Td>{fmtDate(p.date)}</Table.Td><Table.Td><TxTypeBadge type={p.type} /></Table.Td><Table.Td>{p.partNumber}</Table.Td><Table.Td>{p.storeCode}</Table.Td>
                <Table.Td ta="right">{fmtQty(p.quantity)}</Table.Td><Table.Td ta="right">{fmtPrice(p.unitCost, currency)}</Table.Td><Table.Td c="dimmed" fz="sm">{p.createdByName}{p.notes && ` · ${p.notes}`}</Table.Td></Table.Tr>)}
            </Table.Tbody>
          </Table>
        </Card>
      )}
      {kind && <StockTxModal kind={kind} opened onClose={() => setKind(null)} onDone={onChange}
        defaults={{ workOrderId: w.id, workOrder: { id: w.id, number: w.number, title: w.title }, assetId: w.assetId, partId: part?.id, part, storeId: preset.storeId, quantity: preset.quantity }} />}
    </Stack>
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
  // Defaults are recomputed every time the dialog opens, so they reflect work started/labour logged since page load.
  const defaults = () => {
    const earliestLabor = [...w.labor].sort((a, b) => a.workDate.localeCompare(b.workDate))[0];
    const start = w.startedAt ? new Date(w.startedAt) : earliestLabor ? new Date(`${earliestLabor.workDate}T08:00:00`) : null;
    return { completionNotes: w.completionNotes ?? '', causeNotes: w.causeNotes ?? '', preventionNotes: w.preventionNotes ?? '', failureCodeId: (w.failureCodeId ?? null) as string | null,
      symptomId: (w.symptomId ?? null) as string | null,
      remedyId: (w.remedyId ?? null) as string | null, assetWasDown: w.assetWasDown || w.type === 'Breakdown', downtimeHours: (w.downtimeHours ?? '') as number | '', actualStart: start as Date | null, actualEnd: new Date() as Date | null };
  };
  const form = useForm({ initialValues: defaults() });
  // AI report (0.15.0): fills work done, cause details and the codes from what is typed in Work done; hours and parts are hints.
  const { data: ai } = useAiStatus();
  const [filling, setFilling] = useState(false);
  const [aiHints, setAiHints] = useState<string[] | null>(null);
  useEffect(() => { if (opened) { form.setValues(defaults()); form.clearErrors(); setAiHints(null); } /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [opened]);
  const fillFromNotes = async () => {
    setFilling(true);
    try {
      const r = await post<AiReportResult>('/ai/report', { workOrderId: w.id, notes: form.values.completionNotes });
      form.setValues(applyReport(form.values, r));
      setAiHints(reportHints(r, t));
    } catch (e) { notifyError(e); } finally { setFilling(false); }
  };
  const calcDowntime = () => { const { actualStart: s, actualEnd: e } = form.values; if (s && e && e > s) form.setFieldValue('downtimeHours', Math.round(((e.getTime() - s.getTime()) / 36e5) * 100) / 100); };
  const submit = form.onSubmit(async (v) => {
    try {
      // With a stop on the asset's downtime log, the server fills the downtime from the stop.
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
        {aiOn(ai) && (
          <Group justify="space-between" gap="xs">
            {aiHints ? <Badge variant="light" color="violet" leftSection={<IconSparkles size={12} />}>{t('ai.fromAi')}</Badge> : <span />}
            <Button size="xs" variant="light" color="violet" leftSection={<IconSparkles size={14} />} loading={filling}
              disabled={!form.values.completionNotes.trim()} onClick={() => void fillFromNotes()}>{t('ai.fillFromNotes')}</Button>
          </Group>
        )}
        {aiHints && aiHints.length > 0 && (
          <Alert color="violet" variant="light" title={t('ai.addYourself')}>{aiHints.map((h) => <Text key={h} fz="sm">{h}</Text>)}</Alert>
        )}
        <SimpleGrid cols={2}>
          <DateTimePicker label={t('wo.actualStart')} valueFormat="DD MMM YYYY HH:mm" clearable {...form.getInputProps('actualStart')} />
          <DateTimePicker label={t('wo.actualEnd')} valueFormat="DD MMM YYYY HH:mm" maxDate={new Date()} {...form.getInputProps('actualEnd')} />
        </SimpleGrid>
        <SimpleGrid cols={2}>
          <Textarea label={t('wo.causeNotes')} autosize minRows={2} {...form.getInputProps('causeNotes')} />
          <Textarea label={t('wo.preventionNotes')} autosize minRows={2} {...form.getInputProps('preventionNotes')} />
        </SimpleGrid>
        <SimpleGrid cols={{ base: 1, sm: 3 }}>
          <CodeSelect path="/symptoms" label={t('codes.symptom')} value={form.values.symptomId} onChange={(v) => form.setFieldValue('symptomId', v)}
            current={w.symptom ?? null} error={form.errors.symptomId} />
          <CodeSelect path="/failure-codes" label={t('codes.cause')} value={form.values.failureCodeId} onChange={(v) => form.setFieldValue('failureCodeId', v)}
            current={w.failureCode ?? null} pairing="symptomCause" pairedWith={w.symptomId} error={form.errors.failureCodeId} />
          <CodeSelect path="/remedies" label={t('codes.remedy')} value={form.values.remedyId} onChange={(v) => form.setFieldValue('remedyId', v)}
            current={w.remedy ?? null} pairing="causeRemedy" pairedWith={form.values.failureCodeId} error={form.errors.remedyId} />
        </SimpleGrid>
        {w.assetId && w.downtime && (
          <Alert color="blue" variant="light" title={t('wo.assetWasDown')}>
            {w.downtime.endedAt
              ? t('downtime.fromLogEnded', { start: fmtDateTime(w.downtime.startedAt), end: fmtDateTime(w.downtime.endedAt) })
              : t('downtime.fromLogOpen', { start: fmtDateTime(w.downtime.startedAt) })}
          </Alert>
        )}
        {w.assetId && !w.downtime && <Group grow align="flex-end">
          <Switch label={t('wo.assetWasDown')} {...form.getInputProps('assetWasDown', { type: 'checkbox' })} />
          {form.values.assetWasDown && <NumberInput label={t('wo.downtimeHours')} min={0} decimalScale={2} {...form.getInputProps('downtimeHours')}
            rightSectionWidth={110} rightSection={<Button size="compact-xs" variant="subtle" onClick={calcDowntime}>{t('wo.calcDowntime')}</Button>} />}
        </Group>}
        <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button type="submit" color="teal" leftSection={<IconCheck size={16} />} loading={form.submitting}>{t('transition.Completed')}</Button></Group>
      </Stack></form>
    </Modal>
  );
}

/** Cancel (with a reason from the list) or reopen with a comment, as the organization requires (0.10.0). */
function StatusNoteModal({ to, onClose, w, onDone }: { to: WorkOrderStatus; onClose: () => void; w: WorkOrderDetail; onDone: () => Promise<void> }) {
  const { t } = useTranslation();
  const { me } = useAuth();
  const needsNote = !!noteRequired(me?.tenant.settings.commentRequiredFor, w.status, to);
  const { data: reasons } = useQuery({ queryKey: ['cancel-reasons'], queryFn: () => get<Lookup[]>('/cancel-reasons'), enabled: to === 'Cancelled' });
  const active = (reasons ?? []).filter((x) => x.isActive).map((x) => ({ ...x, name: pick(x.name, x.nameAr) }));
  const form = useForm({ initialValues: { cancelReasonId: null as string | null, note: '' } });
  const submit = form.onSubmit(async (v) => {
    try { await post(`/work-orders/${w.id}/status`, { status: to, note: v.note, cancelReasonId: to === 'Cancelled' ? v.cancelReasonId : null }); notifySuccess(t(`woStatus.${to}`)); onClose(); await onDone(); }
    catch (e) { handleFormError(form, e); }
  });
  return (
    <Modal opened onClose={onClose} title={`${to === 'Cancelled' ? t('transition.Cancelled') : t('transition.reopen')} · ${w.number}`}>
      <form onSubmit={submit}><Stack>
        {to === 'Cancelled' && active.length > 0 && (
          <Select label={t('wo.cancelReason')} required data={active.map((x) => ({ value: x.id, label: x.name }))} {...form.getInputProps('cancelReasonId')} />
        )}
        <Textarea label={t('wo.note')} required={needsNote} autosize minRows={2} data-autofocus {...form.getInputProps('note')} />
        <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" color={to === 'Cancelled' ? 'red' : undefined} loading={form.submitting}>{to === 'Cancelled' ? t('transition.Cancelled') : t('transition.reopen')}</Button></Group>
      </Stack></form>
    </Modal>
  );
}

function HoldModal({ opened, onClose, w, onDone }: { opened: boolean; onClose: () => void; w: WorkOrderDetail; onDone: () => Promise<void> }) {
  const { t } = useTranslation();
  const { me } = useAuth();
  const noteNeeded = (me?.tenant.settings.commentRequiredFor ?? []).includes('hold');
  const form = useForm({ initialValues: { holdReason: 'WaitingForParts' as string | null, note: '' } });
  const submit = form.onSubmit(async (v) => {
    try { await post(`/work-orders/${w.id}/status`, { status: 'OnHold', ...v }); onClose(); form.reset(); await onDone(); } catch (e) { handleFormError(form, e); }
  });
  return (
    <Modal opened={opened} onClose={onClose} title={t('wo.holdTitle')}>
      <form onSubmit={submit}><Stack>
        <Select label={t('wo.holdReason')} required data={HOLD_REASONS.map((x) => ({ value: x, label: t(`holdReason.${x}`) }))} {...form.getInputProps('holdReason')} />
        <Textarea label={t('wo.note')} required={noteNeeded} autosize minRows={2} {...form.getInputProps('note')} />
        <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button type="submit" color="yellow" loading={form.submitting}>{t('transition.OnHold')}</Button></Group>
      </Stack></form>
    </Modal>
  );
}
