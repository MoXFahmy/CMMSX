import { useEffect } from 'react';
import { Alert, Anchor, Badge, Button, Card, Checkbox, Drawer, Group, Modal, MultiSelect, Select, SimpleGrid, Stack, Tabs, Text, TextInput, Textarea, Title } from '@mantine/core';
import { DateInput, DateTimePicker } from '@mantine/dates';
import dayjs from 'dayjs';
import { SlaText } from '@/components/SlaBadge';
import { useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { DataTable } from 'mantine-datatable';
import { IconCheck, IconPlus, IconSearch, IconX } from '@tabler/icons-react';
import { get, post } from '@/api/client';
import { SourceBanner } from './SourceBanner';
import { WORK_PRIORITIES, WORK_TYPES, type AccessWindow, type AssetDetail, type Lookup, type Paged, type RequestType, type TenantSettings, type WorkRequestDetail, type WorkRequestListItem, type WorkRequestStatus } from '@/api/types';
import { AccessWindowsEditor, AccessWindowsList, StoppedBadge } from '@/components/AccessWindowsEditor';
import { windowProblem } from '@/components/accessWindows';
import { FeedbackPanel, RequestComments } from './RequestLoop';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { LocationPicker, useHierarchy } from '@/components/LocationPicker';
import { AssetPicker } from '@/components/AssetPicker';
import { EmployeePicker } from '@/components/EmployeePicker';
import { Attachments } from '@/components/Attachments';
import { AssetCheck } from '@/components/AssetCheck';
import { PriorityBadge, WrStatusBadge } from '@/components/WorkBadges';
import { dateTimeFormat, fmtDate, fmtDateTime, handleFormError, notifyError, notifySuccess, toIsoDate } from '@/components/utils';
import { FilterChips, ListTools, useListColumns, useListParams, type FilterDef, type ListColumn } from '@/components/list';

const TABS: { key: string; status?: WorkRequestStatus; late?: boolean }[] = [{ key: 'New', status: 'New' }, { key: 'late', late: true }, { key: 'Approved', status: 'Approved' }, { key: 'Closed', status: 'Closed' }, { key: 'Rejected', status: 'Rejected' }, { key: 'Cancelled', status: 'Cancelled' }, { key: 'all' }];

export function WorkRequestsPage() {
  const { t } = useTranslation();
  const { can, terms } = useAuth();
  const lp = useListParams<WorkRequestListItem>({ sort: 'createdAt', dir: 'desc' });
  const { p, set } = lp;
  const [newOpen, newM] = useDisclosure(p('new') === '1');
  const tab = p('tab') || (can(P.requestsManage) ? 'New' : 'all');
  const openId = p('open') || null;
  const status = TABS.find((x) => x.key === tab)?.status;
  const { data: h } = useHierarchy();
  const late = TABS.find((x) => x.key === tab)?.late ?? false;
  const listQuery = { search: p('search'), siteId: p('siteId'), status, late: late ? 'true' : '', sort: String(lp.sort.columnAccessor), dir: lp.sort.direction };
  const { data, isFetching } = useQuery({
    queryKey: ['work-requests', listQuery, lp.page, lp.pageSize], placeholderData: (prev) => prev,
    queryFn: () => get<Paged<WorkRequestListItem>>('/work-requests', { ...listQuery, page: lp.page, pageSize: lp.pageSize }),
  });
  const { data: summary } = useQuery({ queryKey: ['work-requests', 'summary'], queryFn: () => get<{ status: WorkRequestStatus; count: number }[]>('/work-requests/summary') });
  const count = (s?: WorkRequestStatus) => (summary ?? []).filter((x) => !s || x.status === s).reduce((a, b) => a + b.count, 0);
  // Opening a request or the new-request form doesn't go back to page 1.
  const setKeep = (k: 'open' | 'new', v: string | null) => set({ [k]: v, page: lp.p('page') || null });

  const allColumns: ListColumn<WorkRequestListItem>[] = [
    { accessor: 'number', title: t('wr.number'), sortable: true, noWrap: true, render: (r) => <Text fw={600} fz="sm" c="brand">{r.number}</Text> },
    { accessor: 'title', title: t('common.name'), sortable: true, width: 320,
      render: (r) => <Stack gap={0}><Text fz="sm" lineClamp={1}>{r.title}</Text><Text fz="xs" c="dimmed" lineClamp={1}>{[r.assetNumber && `${r.assetNumber} · ${r.assetName}`, r.locationName, r.siteName].filter(Boolean).join(' · ')}</Text></Stack> },
    { accessor: 'status', title: t('common.status'), sortable: true, noWrap: true, render: (r) => <WrStatusBadge status={r.status} /> },
    { accessor: 'priority', title: t('priority.label'), sortable: true, noWrap: true, render: (r) => <PriorityBadge value={r.priority} /> },
    { accessor: 'type', title: t('workType.label'), noWrap: true, render: (r) => t(`workType.${r.type}`) },
    { accessor: 'requestTypeName', title: t('requestTypes.label'), noWrap: true, render: (r) => r.requestTypeName ?? '—' },
    { accessor: 'site', title: terms.site, sortable: true, noWrap: true, export: [{ key: 'siteName' }], render: (r) => r.siteName },
    { accessor: 'location', title: terms.location, noWrap: true, export: [{ key: 'locationName' }], render: (r) => r.locationName ?? '—' },
    { accessor: 'asset', title: terms.asset, noWrap: true, export: [{ key: 'assetNumber' }, { key: 'assetName', title: `${terms.asset} · ${t('common.name')}` }],
      render: (r) => (r.assetNumber ? `${r.assetNumber} · ${r.assetName}` : '—') },
    { accessor: 'requesterName', title: t('wr.requester') },
    { accessor: 'requiredBy', title: t('wr.requiredBy'), sortable: true, noWrap: true, render: (r) => fmtDate(r.requiredBy) },
    { accessor: 'deadline', title: t('wo.due'), noWrap: true, export: [{ key: 'respondBy', title: t('sla.respondBy') }, { key: 'dueAt', title: t('wo.dueAt') }, { key: 'responseState', title: t('sla.responseState') }, { key: 'completionState', title: t('sla.completionState') }],
      render: (r) => (
        <Stack gap={0}>
          {r.status === 'New' && r.respondBy && <Group gap={4} wrap="nowrap"><Text fz="xs" c="dimmed">{t('sla.respond')}</Text><SlaText fz="xs" deadline={r.respondBy} state={r.responseState} /></Group>}
          <SlaText deadline={r.dueAt} state={r.completionState} />
        </Stack>
      ) },
    { accessor: 'createdAt', title: t('wo.createdOn'), sortable: true, noWrap: true, render: (r) => fmtDateTime(r.createdAt) },
    { accessor: 'workOrderNumber', title: t('wr.workOrder'), noWrap: true,
      render: (r) => r.workOrderId ? <Anchor component={Link} to={`/work-orders/${r.workOrderId}`} fz="sm" onClick={(e) => e.stopPropagation()}>{r.workOrderNumber}</Anchor> : '—' },
  ];
  const cols = useListColumns('work-requests', allColumns, { defaultHidden: ['type', 'site', 'location', 'asset', 'requiredBy', 'requestTypeName'] });
  const chips: FilterDef[] = [
    { keys: ['search'], label: t('common.search') },
    { keys: ['siteId'], label: terms.site, format: ([v]) => h?.sites.find((s) => s.id === v)?.name ?? '…' },
  ];

  return (
    <>
      <PageHeader title={t('wr.title')} subtitle={t('wr.subtitle')}
        actions={can(P.requestsCreate) && <Button leftSection={<IconPlus size={16} />} onClick={newM.open}>{t('wr.new')}</Button>} />
      <Card p="sm" mb="md">
        <Group gap="xs" align="center" wrap="wrap">
          <TextInput placeholder={t('wr.searchPlaceholder')} leftSection={<IconSearch size={16} />} value={lp.search} onChange={(e) => lp.setSearch(e.currentTarget.value)} w={{ base: '100%', sm: 260 }} />
          <Select placeholder={terms.site} data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} value={p('siteId') || null} onChange={(v) => set({ siteId: v })} clearable searchable w={{ base: '100%', xs: 200 }} />
          <Group gap={6} ml="auto">
            <ListTools list="work-requests" lp={lp} cols={cols} exportPath="/work-requests/export" exportQuery={listQuery} total={data?.total} />
          </Group>
        </Group>
        <FilterChips defs={chips} p={p} set={set} />
      </Card>
      <Tabs value={tab} onChange={(v) => set({ tab: v })} mb="sm">
        <Tabs.List>{TABS.map((x) => <Tabs.Tab key={x.key} value={x.key} rightSection={x.late ? undefined : <Badge size="xs" variant="default" circle>{count(x.status)}</Badge>}>
          {x.late ? <Text span c="red" fz="sm">{t('sla.lateFilter')}</Text> : x.status ? t(`wrStatus.${x.status}`) : t('wo.all')}</Tabs.Tab>)}</Tabs.List>
      </Tabs>
      <Card p={0}>
        <DataTable<WorkRequestListItem> minHeight={240} withTableBorder={false} highlightOnHover striped verticalSpacing="xs" fetching={isFetching}
          records={data?.items ?? []} columns={cols.columns} noRecordsText={t('common.noRecords')} totalRecords={data?.total ?? 0} recordsPerPage={lp.pageSize} page={lp.page}
          onPageChange={(n) => set({ page: String(n) })} recordsPerPageOptions={[25, 50, 100]} recordsPerPageLabel={t('common.perPage')} onRecordsPerPageChange={(n) => set({ pageSize: String(n) })}
          sortStatus={lp.sort} onSortStatusChange={lp.setSort}
          rowClassName={() => 'clickable-row'} onRowClick={({ record }) => setKeep('open', record.id)}
          paginationText={({ from, to, totalRecords }) => `${from}–${to} / ${totalRecords}`} />
      </Card>
      <RequestFormModal opened={newOpen} onClose={() => { newM.close(); setKeep('new', null); }} />
      <RequestDrawer id={openId} onClose={() => setKeep('open', null)} />
    </>
  );
}

export function RequestFormModal({ opened, onClose, assetId }: { opened: boolean; onClose: () => void; assetId?: string }) {
  const { t } = useTranslation();
  const { me } = useAuth();
  const qc = useQueryClient();
  const { data: asset } = useQuery({ queryKey: ['asset', assetId], queryFn: () => get<AssetDetail>(`/assets/${assetId}`), enabled: !!assetId && opened });
  const blank = { title: '', description: '', siteId: null as string | null, locationId: null as string | null, assetId: null as string | null, priority: 'Medium', type: 'Corrective',
    requesterName: me?.user.fullName ?? '', requesterPhone: '', requiredBy: null as Date | null, assetIsDown: false,
    requestTypeId: null as string | null, extraRecipientIds: [] as string[], accessWindows: [] as AccessWindow[] };
  const form = useForm({ initialValues: blank, validate: { accessWindows: (v) => (windowProblem(v) ? t(windowProblem(v)!) : null) } });
  const { data: types } = useQuery({ queryKey: ['request-types'], queryFn: () => get<RequestType[]>('/request-types'), enabled: opened });
  const { data: people } = useQuery({ queryKey: ['request-people', form.values.siteId], queryFn: () => get<{ id: string; name: string }[]>('/work-requests/people', { siteId: form.values.siteId! }),
    enabled: opened && !!form.values.siteId, retry: false });
  const chooseType = (id: string | null) => {
    const type = (types ?? []).find((x) => x.id === id);
    form.setValues({ requestTypeId: id, ...(type?.defaultPriority ? { priority: type.defaultPriority } : {}) });
  };
  useEffect(() => {
    if (!opened) return;
    form.setValues(asset ? { ...blank, siteId: asset.siteId, locationId: asset.locationId ?? null, assetId: asset.id } : blank);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [opened, asset]);
  const submit = form.onSubmit(async (v) => {
    try {
      const r = await post<{ id: string; number: string }>('/work-requests', { ...v, requiredBy: toIsoDate(v.requiredBy), assetIsDown: !!v.assetId && v.assetIsDown });
      notifySuccess(t('wr.submitted', { number: r.number }));
      await qc.invalidateQueries({ queryKey: ['work-requests'] }); await qc.invalidateQueries({ queryKey: ['dashboard'] });
      if (v.assetId && v.assetIsDown) { await qc.invalidateQueries({ queryKey: ['asset', v.assetId] }); await qc.invalidateQueries({ queryKey: ['assets'] }); }
      onClose();
    } catch (e) { handleFormError(form, e); }
  });
  return (
    <Modal opened={opened} onClose={onClose} title={t('wr.new')} size="lg">
      <form onSubmit={submit}><Stack>
        {(types ?? []).some((x) => x.isActive) && (
          <Select label={t('requestTypes.label')} clearable data={(types ?? []).filter((x) => x.isActive).map((x) => ({ value: x.id, label: x.name }))}
            value={form.values.requestTypeId} onChange={chooseType} error={form.errors.requestTypeId} />
        )}
        <TextInput label={t('wr.whatsWrong')} required data-autofocus {...form.getInputProps('title')} />
        <Textarea label={t('wr.details')} autosize minRows={3} {...form.getInputProps('description')} />
        <LocationPicker siteId={form.values.siteId} locationId={form.values.locationId} siteError={form.errors.siteId} locationError={form.errors.locationId}
          onChange={(s, l) => form.setValues({ siteId: s, locationId: l, assetId: s === form.values.siteId ? form.values.assetId : null })} />
        <AssetPicker label={t('wo.asset')} value={form.values.assetId} onChange={(v) => form.setFieldValue('assetId', v)} error={form.errors.assetId}
          initial={asset ? { id: asset.id, assetNumber: asset.assetNumber, name: asset.name } : null} />
        <AssetCheck assetId={form.values.assetId} />
        {form.values.assetId && <Checkbox label={t('downtime.assetIsDown')} description={t('downtime.assetIsDownHint')} color="red"
          {...form.getInputProps('assetIsDown', { type: 'checkbox' })} />}
        <SimpleGrid cols={{ base: 1, sm: 3 }}>
          <Select label={t('priority.label')} data={WORK_PRIORITIES.map((x) => ({ value: x, label: t(`priority.${x}`) }))} {...form.getInputProps('priority')} />
          <Select label={t('workType.label')} data={WORK_TYPES.map((x) => ({ value: x, label: t(`workType.${x}`) }))} {...form.getInputProps('type')} />
          <DateInput label={t('wr.requiredBy')} clearable valueFormat="DD MMM YYYY" {...form.getInputProps('requiredBy')} />
        </SimpleGrid>
        <SimpleGrid cols={2}>
          <TextInput label={t('wr.requester')} {...form.getInputProps('requesterName')} />
          <TextInput label={t('wr.phone')} {...form.getInputProps('requesterPhone')} />
        </SimpleGrid>
        <MultiSelect label={t('requestLoop.keepInformed')} description={t('requestLoop.keepInformedHint')} searchable clearable maxValues={10} disabled={!form.values.siteId}
          data={(people ?? []).map((p) => ({ value: p.id, label: p.name }))} {...form.getInputProps('extraRecipientIds')} />
        <AccessWindowsEditor value={form.values.accessWindows} onChange={(v) => form.setFieldValue('accessWindows', v)} error={form.errors.accessWindows} />
        <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('wr.new')}</Button></Group>
      </Stack></form>
    </Modal>
  );
}

function RequestDrawer({ id, onClose }: { id: string | null; onClose: () => void }) {
  const { t } = useTranslation();
  const { can } = useAuth();
  const qc = useQueryClient();
  const navigate = useNavigate();
  const [approveOpen, approve] = useDisclosure();
  const [rejectOpen, reject] = useDisclosure();
  const { data: r } = useQuery({ queryKey: ['work-request', id], queryFn: () => get<WorkRequestDetail>(`/work-requests/${id}`), enabled: !!id });
  const { data: trades } = useQuery({ queryKey: ['trades'], queryFn: () => get<Lookup[]>('/trades') });
  const { data: org } = useQuery({ queryKey: ['settings'], queryFn: () => get<{ settings: TenantSettings }>('/settings'), enabled: !!id, retry: false });
  const rules = org?.settings.commentRequiredFor ?? ['reject', 'refuse'];
  const af = useForm({ initialValues: { priority: 'Medium' as string | null, type: 'Corrective' as string | null, tradeId: null as string | null, assigneeIds: [] as string[], dueAt: null as string | null, note: '' } });
  const rf = useForm({ initialValues: { note: '' } });
  const refresh = async () => { await qc.invalidateQueries({ queryKey: ['work-requests'] }); await qc.invalidateQueries({ queryKey: ['work-request', id] }); await qc.invalidateQueries({ queryKey: ['dashboard'] }); };
  const openApprove = () => { if (!r) return; af.setValues({ priority: r.priority, type: r.type, tradeId: r.requestType?.defaultTradeId ?? null, assigneeIds: [], dueAt: null, note: '' }); approve.open(); };
  const doApprove = af.onSubmit(async (v) => {
    try { const res = await post<{ workOrderId: string; number: string }>(`/work-requests/${id}/approve`, { ...v, dueAt: v.dueAt ? dayjs(v.dueAt).toISOString() : null, dueDate: null }); notifySuccess(t('wr.approved', { number: res.number })); approve.close(); await refresh(); navigate(`/work-orders/${res.workOrderId}`); }
    catch (e) { handleFormError(af, e); }
  });
  const doReject = rf.onSubmit(async (v) => { try { await post(`/work-requests/${id}/reject`, v); reject.close(); rf.reset(); await refresh(); } catch (e) { handleFormError(rf, e); } });
  const cancel = async () => { try { await post(`/work-requests/${id}/cancel`, {}); await refresh(); } catch (e) { notifyError(e); } };
  return (
    <Drawer opened={!!id} onClose={onClose} position="right" size="lg" title={r ? `${r.number}` : ''}>
      {r && (
        <Stack>
          <Group gap="xs"><WrStatusBadge status={r.status} /><PriorityBadge value={r.priority} /><Badge variant="default">{t(`workType.${r.type}`)}</Badge>
            {r.requestType && <Badge variant="light" color="grape">{r.requestType.name}</Badge>}<StoppedBadge show={r.onlyWhenStopped} /></Group>
          <Title order={4}>{r.title}</Title>
          {r.source && <SourceBanner source={r.source} />}
          {r.description && <Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{r.description}</Text>}
          <Card><SimpleGrid cols={2} spacing="sm">
            <Stack gap={2}><Text fz="xs" c="dimmed">{t('wo.location')}</Text><Text fz="sm">{[r.site.name, r.locationName].filter(Boolean).join(' › ')}</Text></Stack>
            <Stack gap={2}><Text fz="xs" c="dimmed">{t('wo.asset')}</Text>{r.asset ? <Anchor component={Link} to={`/assets/${r.asset.id}`} fz="sm">{r.asset.assetNumber} · {r.asset.name}</Anchor> : <Text fz="sm">—</Text>}</Stack>
            <Stack gap={2}><Text fz="xs" c="dimmed">{t('wr.requester')}</Text><Text fz="sm">{r.requesterName ?? r.createdBy}{r.requesterPhone && ` · ${r.requesterPhone}`}</Text></Stack>
            <Stack gap={2}><Text fz="xs" c="dimmed">{t('wr.requiredBy')}</Text><Text fz="sm">{fmtDate(r.requiredBy)}</Text></Stack>
            <Stack gap={2}><Text fz="xs" c="dimmed">{t('sla.respondBy')}</Text><SlaText deadline={r.sla.respondBy} state={r.sla.response} /></Stack>
            <Stack gap={2}><Text fz="xs" c="dimmed">{t('wo.due')}</Text><SlaText deadline={r.dueAt} state={r.sla.completion} /></Stack>
            <Stack gap={2}><Text fz="xs" c="dimmed">{t('wo.createdOn')}</Text><Text fz="sm">{fmtDateTime(r.createdAt)}</Text></Stack>
            {r.extraRecipients.length > 0 && <Stack gap={2}><Text fz="xs" c="dimmed">{t('requestLoop.keepInformed')}</Text><Text fz="sm">{r.extraRecipients.map((p) => p.name).join(', ')}</Text></Stack>}
          </SimpleGrid></Card>
          <AccessWindowsList windows={r.accessWindows} />
          {r.status !== 'New' && (
            <Alert color={r.status === 'Approved' ? 'teal' : 'gray'} title={`${t('wr.decision')}: ${t(`wrStatus.${r.status}`)}`}>
              <Stack gap={4}>{r.decisionNote && <Text fz="sm">{r.decisionNote}</Text>}<Text fz="xs" c="dimmed">{r.decidedBy} · {fmtDateTime(r.decidedAt)}</Text>
                {r.workOrder && <Anchor component={Link} to={`/work-orders/${r.workOrder.id}`} fz="sm">{t('wr.workOrder')} {r.workOrder.number} ({t(`woStatus.${r.workOrder.status}`)})</Anchor>}</Stack>
            </Alert>
          )}
          {r.status === 'New' && (
            <Group>
              {can(P.requestsManage) && <Button color="teal" leftSection={<IconCheck size={16} />} onClick={openApprove}>{t('wr.approve')}</Button>}
              {can(P.requestsManage) && <Button variant="default" color="red" leftSection={<IconX size={16} />} onClick={reject.open}>{t('wr.reject')}</Button>}
              {r.canCancel && !can(P.requestsManage) && <Button variant="default" onClick={cancel}>{t('wr.cancel')}</Button>}
            </Group>
          )}
          <FeedbackPanel requestId={r.id} awaiting={r.awaitingFeedback} canAnswer={r.canAnswer} feedback={r.feedback ?? []} refuseNeedsComment={rules.includes('refuse')} onDone={refresh} />
          <RequestComments requestId={r.id} />
          <Title order={6}>{t('wo.files')}</Title>
          <Attachments entityType="WorkRequest" entityId={r.id} canEdit={r.status === 'New' && can(P.requestsCreate)} />
        </Stack>
      )}
      <Modal opened={approveOpen} onClose={approve.close} title={t('wr.approveTitle')} size="lg">
        <form onSubmit={doApprove}><Stack>
          <SimpleGrid cols={{ base: 1, sm: 3 }}>
            <Select label={t('priority.label')} data={WORK_PRIORITIES.map((x) => ({ value: x, label: t(`priority.${x}`) }))} {...af.getInputProps('priority')} />
            <Select label={t('workType.label')} data={WORK_TYPES.map((x) => ({ value: x, label: t(`workType.${x}`) }))} {...af.getInputProps('type')} />
            <Select label={t('wo.trade')} clearable data={(trades ?? []).filter((x) => x.isActive).map((x) => ({ value: x.id, label: x.name }))} {...af.getInputProps('tradeId')} />
          </SimpleGrid>
          <EmployeePicker label={t('wo.assignees')} value={af.values.assigneeIds} onChange={(v) => af.setFieldValue('assigneeIds', v)} siteId={r?.siteId} tradeId={af.values.tradeId} />
          {/* Empty: the priority's target from when the request was raised (recomputed if the priority changes here). */}
          <DateTimePicker label={t('wo.dueAt')} clearable valueFormat={dateTimeFormat()} {...af.getInputProps('dueAt')}
            placeholder={r?.dueAt && af.values.priority === r.priority ? fmtDateTime(r.dueAt) : t('wo.dueAtHint')}
            description={r?.requiredBy && (
              <Anchor fz="xs" onClick={() => af.setFieldValue('dueAt', `${r.requiredBy} 23:59:00`)}>{t('wr.useNeededBy', { date: fmtDate(r.requiredBy) })}</Anchor>
            )} />
          <Textarea label={t('wo.note')} autosize {...af.getInputProps('note')} />
          <Group justify="flex-end"><Button variant="default" onClick={approve.close}>{t('common.cancel')}</Button><Button type="submit" color="teal" loading={af.submitting}>{t('wr.approve')}</Button></Group>
        </Stack></form>
      </Modal>
      <Modal opened={rejectOpen} onClose={reject.close} title={t('wr.rejectTitle')}>
        <form onSubmit={doReject}><Stack>
          <Textarea label={t('wr.reason')} required={rules.includes('reject')} autosize minRows={2} {...rf.getInputProps('note')} />
          <Group justify="flex-end"><Button variant="default" onClick={reject.close}>{t('common.cancel')}</Button><Button type="submit" color="red" loading={rf.submitting}>{t('wr.reject')}</Button></Group>
        </Stack></form>
      </Modal>
    </Drawer>
  );
}
