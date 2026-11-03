import { useEffect } from 'react';
import { Alert, Anchor, Badge, Button, Card, Drawer, Group, Modal, Select, SimpleGrid, Stack, Tabs, Text, TextInput, Textarea, Title } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { DataTable } from 'mantine-datatable';
import { IconCheck, IconPlus, IconX } from '@tabler/icons-react';
import { get, post } from '@/api/client';
import { WORK_PRIORITIES, WORK_TYPES, type AssetDetail, type Lookup, type Paged, type WorkRequestDetail, type WorkRequestListItem, type WorkRequestStatus } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { LocationPicker } from '@/components/LocationPicker';
import { AssetPicker } from '@/components/AssetPicker';
import { EmployeePicker } from '@/components/EmployeePicker';
import { Attachments } from '@/components/Attachments';
import { AssetCheck } from '@/components/AssetCheck';
import { PriorityBadge, WrStatusBadge } from '@/components/WorkBadges';
import { fmtDate, fmtDateTime, handleFormError, notifyError, notifySuccess, toIsoDate } from '@/components/utils';

const TABS: { key: string; status?: WorkRequestStatus }[] = [{ key: 'New', status: 'New' }, { key: 'Approved', status: 'Approved' }, { key: 'Rejected', status: 'Rejected' }, { key: 'Cancelled', status: 'Cancelled' }, { key: 'all' }];

export function WorkRequestsPage() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const [params, setParams] = useSearchParams();
  const [newOpen, newM] = useDisclosure(params.get('new') === '1');
  const tab = params.get('tab') ?? (can(P.requestsManage) ? 'New' : 'all');
  const page = Number(params.get('page') || 1);
  const openId = params.get('open');
  const status = TABS.find((x) => x.key === tab)?.status;
  const { data, isFetching } = useQuery({
    queryKey: ['work-requests', tab, page], placeholderData: (prev) => prev,
    queryFn: () => get<Paged<WorkRequestListItem>>('/work-requests', { status, page, pageSize: 25, sort: 'createdAt', dir: 'desc' }),
  });
  const { data: summary } = useQuery({ queryKey: ['work-requests', 'summary'], queryFn: () => get<{ status: WorkRequestStatus; count: number }[]>('/work-requests/summary') });
  const count = (s?: WorkRequestStatus) => (summary ?? []).filter((x) => !s || x.status === s).reduce((a, b) => a + b.count, 0);
  const setParam = (k: string, v: string | null) => { const n = new URLSearchParams(params); if (v) n.set(k, v); else n.delete(k); if (k !== 'page' && k !== 'open') n.delete('page'); setParams(n, { replace: true }); };
  return (
    <>
      <PageHeader title={t('wr.title')} subtitle={t('wr.subtitle')}
        actions={can(P.requestsCreate) && <Button leftSection={<IconPlus size={16} />} onClick={newM.open}>{t('wr.new')}</Button>} />
      <Tabs value={tab} onChange={(v) => setParam('tab', v)} mb="sm">
        <Tabs.List>{TABS.map((x) => <Tabs.Tab key={x.key} value={x.key} rightSection={<Badge size="xs" variant="default" circle>{count(x.status)}</Badge>}>{x.status ? t(`wrStatus.${x.status}`) : t('wo.all')}</Tabs.Tab>)}</Tabs.List>
      </Tabs>
      <Card p={0}>
        <DataTable<WorkRequestListItem> minHeight={240} withTableBorder={false} highlightOnHover striped verticalSpacing="xs" fetching={isFetching}
          records={data?.items ?? []} noRecordsText={t('common.noRecords')} totalRecords={data?.total ?? 0} recordsPerPage={25} page={page}
          onPageChange={(n) => setParam('page', String(n))} rowClassName={() => 'clickable-row'} onRowClick={({ record }) => setParam('open', record.id)}
          columns={[
            { accessor: 'number', title: t('wr.number'), noWrap: true, render: (r) => <Text fw={600} fz="sm" c="brand">{r.number}</Text> },
            { accessor: 'title', title: t('common.name'), width: 320, render: (r) => <Stack gap={0}><Text fz="sm" lineClamp={1}>{r.title}</Text><Text fz="xs" c="dimmed" lineClamp={1}>{[r.assetNumber && `${r.assetNumber} · ${r.assetName}`, r.locationName, r.siteName].filter(Boolean).join(' · ')}</Text></Stack> },
            { accessor: 'status', title: t('common.status'), noWrap: true, render: (r) => <WrStatusBadge status={r.status} /> },
            { accessor: 'priority', title: t('priority.label'), noWrap: true, render: (r) => <PriorityBadge value={r.priority} /> },
            { accessor: 'requesterName', title: t('wr.requester') },
            { accessor: 'createdAt', title: t('wo.createdOn'), noWrap: true, render: (r) => fmtDateTime(r.createdAt) },
            { accessor: 'workOrderNumber', title: t('wr.workOrder'), noWrap: true, render: (r) => r.workOrderId ? <Anchor component={Link} to={`/work-orders/${r.workOrderId}`} fz="sm" onClick={(e) => e.stopPropagation()}>{r.workOrderNumber}</Anchor> : '—' },
          ]} />
      </Card>
      <RequestFormModal opened={newOpen} onClose={() => { newM.close(); setParam('new', null); }} />
      <RequestDrawer id={openId} onClose={() => setParam('open', null)} />
    </>
  );
}

export function RequestFormModal({ opened, onClose, assetId }: { opened: boolean; onClose: () => void; assetId?: string }) {
  const { t } = useTranslation();
  const { me } = useAuth();
  const qc = useQueryClient();
  const { data: asset } = useQuery({ queryKey: ['asset', assetId], queryFn: () => get<AssetDetail>(`/assets/${assetId}`), enabled: !!assetId && opened });
  const blank = { title: '', description: '', siteId: null as string | null, locationId: null as string | null, assetId: null as string | null, priority: 'Medium', type: 'Corrective',
    requesterName: me?.user.fullName ?? '', requesterPhone: '', requiredBy: null as Date | null };
  const form = useForm({ initialValues: blank });
  useEffect(() => {
    if (!opened) return;
    form.setValues(asset ? { ...blank, siteId: asset.siteId, locationId: asset.locationId ?? null, assetId: asset.id } : blank);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [opened, asset]);
  const submit = form.onSubmit(async (v) => {
    try {
      const r = await post<{ id: string; number: string }>('/work-requests', { ...v, requiredBy: toIsoDate(v.requiredBy) });
      notifySuccess(t('wr.submitted', { number: r.number }));
      await qc.invalidateQueries({ queryKey: ['work-requests'] }); await qc.invalidateQueries({ queryKey: ['dashboard'] });
      onClose();
    } catch (e) { handleFormError(form, e); }
  });
  return (
    <Modal opened={opened} onClose={onClose} title={t('wr.new')} size="lg">
      <form onSubmit={submit}><Stack>
        <TextInput label={t('wr.whatsWrong')} required data-autofocus {...form.getInputProps('title')} />
        <Textarea label={t('wr.details')} autosize minRows={3} {...form.getInputProps('description')} />
        <LocationPicker siteId={form.values.siteId} locationId={form.values.locationId} siteError={form.errors.siteId} locationError={form.errors.locationId}
          onChange={(s, l) => form.setValues({ siteId: s, locationId: l, assetId: s === form.values.siteId ? form.values.assetId : null })} />
        <AssetPicker label={t('wo.asset')} value={form.values.assetId} onChange={(v) => form.setFieldValue('assetId', v)} error={form.errors.assetId}
          initial={asset ? { id: asset.id, assetNumber: asset.assetNumber, name: asset.name } : null} />
        <AssetCheck assetId={form.values.assetId} />
        <SimpleGrid cols={{ base: 1, sm: 3 }}>
          <Select label={t('priority.label')} data={WORK_PRIORITIES.map((x) => ({ value: x, label: t(`priority.${x}`) }))} {...form.getInputProps('priority')} />
          <Select label={t('workType.label')} data={WORK_TYPES.map((x) => ({ value: x, label: t(`workType.${x}`) }))} {...form.getInputProps('type')} />
          <DateInput label={t('wr.requiredBy')} clearable valueFormat="DD MMM YYYY" {...form.getInputProps('requiredBy')} />
        </SimpleGrid>
        <SimpleGrid cols={2}>
          <TextInput label={t('wr.requester')} {...form.getInputProps('requesterName')} />
          <TextInput label={t('wr.phone')} {...form.getInputProps('requesterPhone')} />
        </SimpleGrid>
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
  const af = useForm({ initialValues: { priority: 'Medium' as string | null, type: 'Corrective' as string | null, tradeId: null as string | null, assigneeIds: [] as string[], dueDate: null as Date | null, note: '' } });
  const rf = useForm({ initialValues: { note: '' } });
  const refresh = async () => { await qc.invalidateQueries({ queryKey: ['work-requests'] }); await qc.invalidateQueries({ queryKey: ['work-request', id] }); await qc.invalidateQueries({ queryKey: ['dashboard'] }); };
  const openApprove = () => { if (!r) return; af.setValues({ priority: r.priority, type: r.type, tradeId: null, assigneeIds: [], dueDate: r.requiredBy ? new Date(`${r.requiredBy}T00:00:00`) : null, note: '' }); approve.open(); };
  const doApprove = af.onSubmit(async (v) => {
    try { const res = await post<{ workOrderId: string; number: string }>(`/work-requests/${id}/approve`, { ...v, dueDate: toIsoDate(v.dueDate) }); notifySuccess(t('wr.approved', { number: res.number })); approve.close(); await refresh(); navigate(`/work-orders/${res.workOrderId}`); }
    catch (e) { handleFormError(af, e); }
  });
  const doReject = rf.onSubmit(async (v) => { try { await post(`/work-requests/${id}/reject`, v); reject.close(); rf.reset(); await refresh(); } catch (e) { handleFormError(rf, e); } });
  const cancel = async () => { try { await post(`/work-requests/${id}/cancel`, {}); await refresh(); } catch (e) { notifyError(e); } };
  return (
    <Drawer opened={!!id} onClose={onClose} position="right" size="lg" title={r ? `${r.number}` : ''}>
      {r && (
        <Stack>
          <Group gap="xs"><WrStatusBadge status={r.status} /><PriorityBadge value={r.priority} /><Badge variant="default">{t(`workType.${r.type}`)}</Badge></Group>
          <Title order={4}>{r.title}</Title>
          {r.description && <Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{r.description}</Text>}
          <Card><SimpleGrid cols={2} spacing="sm">
            <Stack gap={2}><Text fz="xs" c="dimmed">{t('wo.location')}</Text><Text fz="sm">{[r.site.name, r.locationName].filter(Boolean).join(' › ')}</Text></Stack>
            <Stack gap={2}><Text fz="xs" c="dimmed">{t('wo.asset')}</Text>{r.asset ? <Anchor component={Link} to={`/assets/${r.asset.id}`} fz="sm">{r.asset.assetNumber} · {r.asset.name}</Anchor> : <Text fz="sm">—</Text>}</Stack>
            <Stack gap={2}><Text fz="xs" c="dimmed">{t('wr.requester')}</Text><Text fz="sm">{r.requesterName ?? r.createdBy}{r.requesterPhone && ` · ${r.requesterPhone}`}</Text></Stack>
            <Stack gap={2}><Text fz="xs" c="dimmed">{t('wr.requiredBy')}</Text><Text fz="sm">{fmtDate(r.requiredBy)}</Text></Stack>
            <Stack gap={2}><Text fz="xs" c="dimmed">{t('wo.createdOn')}</Text><Text fz="sm">{fmtDateTime(r.createdAt)}</Text></Stack>
          </SimpleGrid></Card>
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
          <DateInput label={t('wo.dueDate')} clearable valueFormat="DD MMM YYYY" {...af.getInputProps('dueDate')} />
          <Textarea label={t('wo.note')} autosize {...af.getInputProps('note')} />
          <Group justify="flex-end"><Button variant="default" onClick={approve.close}>{t('common.cancel')}</Button><Button type="submit" color="teal" loading={af.submitting}>{t('wr.approve')}</Button></Group>
        </Stack></form>
      </Modal>
      <Modal opened={rejectOpen} onClose={reject.close} title={t('wr.rejectTitle')}>
        <form onSubmit={doReject}><Stack>
          <Textarea label={t('wr.reason')} required autosize minRows={2} {...rf.getInputProps('note')} />
          <Group justify="flex-end"><Button variant="default" onClick={reject.close}>{t('common.cancel')}</Button><Button type="submit" color="red" loading={rf.submitting}>{t('wr.reject')}</Button></Group>
        </Stack></form>
      </Modal>
    </Drawer>
  );
}
