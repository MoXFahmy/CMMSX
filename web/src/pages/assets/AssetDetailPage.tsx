import { useState } from 'react';
import { ActionIcon, Alert, Anchor, Badge, Box, Breadcrumbs, Button, Card, Center, Checkbox, Grid, Group, Image, Loader, Menu, Modal, SimpleGrid, Stack, Table, Tabs, Text, Textarea, ThemeIcon, Title } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { DataTable } from 'mantine-datatable';
import dayjs from 'dayjs';
import { IconArrowsTransferUp, IconBox, IconCopy, IconDots, IconEdit, IconInfoCircle, IconPlus, IconShieldCheck, IconShieldX, IconTrash, IconTool, IconClipboardText } from '@tabler/icons-react';
import type { Paged, WorkOrderListItem } from '@/api/types';
import { PriorityBadge, WoStatusBadge } from '@/components/WorkBadges';
import { RequestFormModal } from '@/pages/work/WorkRequestsPage';
import { del, get, post } from '@/api/client';
import type { AssetDetail, AssetListItem, CustomField, Transfer } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { CriticalityBadge, StatusBadge } from '@/components/Badges';
import { PageHeader } from '@/components/PageHeader';
import { LocationPicker } from '@/components/LocationPicker';
import { Attachments } from '@/components/Attachments';
import { AuditTimeline } from '@/components/AuditTimeline';
import { confirmDelete } from '@/components/confirm';
import { fmtDate, fmtDateTime, fmtMoney, handleFormError, notifyError, notifySuccess } from '@/components/utils';

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <Stack gap={2}>
      <Text fz="xs" c="dimmed">{label}</Text>
      <Text fz="sm" fw={500} style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>{children === undefined || children === null || children === '' ? '—' : children}</Text>
    </Stack>
  );
}

export function AssetDetailPage() {
  const { t } = useTranslation();
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { me, terms, can } = useAuth();
  const [tab, setTab] = useState<string | null>('overview');
  const [transferOpen, transfer] = useDisclosure();
  const [requestOpen, request] = useDisclosure();
  const { data: a, isLoading, error } = useQuery({ queryKey: ['asset', id], queryFn: () => get<AssetDetail>(`/assets/${id}`) });
  const { data: fields } = useQuery({ queryKey: ['custom-fields', 'Asset'], queryFn: () => get<CustomField[]>('/custom-fields', { entityType: 'Asset' }) });
  const { data: children } = useQuery({ queryKey: ['asset', id, 'children'], queryFn: () => get<AssetListItem[]>(`/assets/${id}/children`), enabled: tab === 'components' });
  const { data: wos, isFetching: wosLoading } = useQuery({ queryKey: ['work-orders', 'asset', id], queryFn: () => get<Paged<WorkOrderListItem>>('/work-orders', { assetId: id, pageSize: 50, sort: 'createdAt', dir: 'desc' }), enabled: tab === 'work' && can(P.woView) });
  const { data: transfers } = useQuery({ queryKey: ['asset', id, 'transfers'], queryFn: () => get<Transfer[]>(`/assets/${id}/transfers`), enabled: tab === 'transfers' });
  const currency = me?.tenant.settings.currency ?? 'USD';

  if (isLoading) return <Center h={300}><Loader /></Center>;
  if (error || !a) return <Alert color="red" title={t('common.noRecords')}>{(error as Error)?.message}</Alert>;

  const refresh = () => qc.invalidateQueries({ queryKey: ['asset', id] });
  const duplicate = async () => {
    try { const r = await post<{ id: string; assetNumber: string }>(`/assets/${a.id}/duplicate`); notifySuccess(t('assets.duplicated', { number: r.assetNumber })); navigate(`/assets/${r.id}/edit`); }
    catch (e) { notifyError(e); }
  };
  const remove = () => confirmDelete(`${a.assetNumber} · ${a.name}`, async () => {
    try { await del(`/assets/${a.id}`); notifySuccess(t('common.deleted')); await qc.invalidateQueries({ queryKey: ['assets'] }); navigate('/assets'); } catch (e) { notifyError(e); }
  });
  const today = dayjs().format('YYYY-MM-DD');
  const covered = a.warrantyType !== 'None' && a.warrantyExpiresOn && a.warrantyExpiresOn >= today;
  const ageYears = a.commissionedOn ? dayjs().diff(dayjs(a.commissionedOn), 'year', true) : null;
  const applicableFields = (fields ?? []).filter((f) => f.categoryIds.length === 0 || (a.categoryId && f.categoryIds.includes(a.categoryId)) || a.customFields?.[f.key]);
  const fmtCustom = (f: CustomField, v?: string | null) => (v == null || v === '' ? null : f.dataType === 'Boolean' ? (v === 'true' ? t('common.yes') : t('common.no')) : f.dataType === 'Date' ? fmtDate(v) : v);

  return (
    <>
      <PageHeader
        crumbs={[{ label: terms.assets, to: '/assets' }, ...(a.parentAsset ? [{ label: a.parentAsset.assetNumber, to: `/assets/${a.parentAsset.id}` }] : []), { label: a.assetNumber }]}
        title={<>{a.name}</>} badge={<Group gap={6}><StatusBadge status={a.status} /><CriticalityBadge value={a.criticality} /></Group>}
        subtitle={<>{a.assetNumber}{a.category && <> · {a.category.name}</>}{a.legacyRef && <> · <Badge size="xs" variant="outline" color="gray">4CSQL</Badge></>}</>}
        actions={<>
          {can(P.assetsEdit) && <Button leftSection={<IconEdit size={16} />} component={Link} to={`/assets/${a.id}/edit`}>{t('common.edit')}</Button>}
          {can(P.woCreate) && <Button variant="default" leftSection={<IconTool size={16} />} component={Link} to={`/work-orders/new?assetId=${a.id}`}>{t('wo.createFromAsset')}</Button>}
          {!can(P.woCreate) && can(P.requestsCreate) && <Button variant="default" leftSection={<IconClipboardText size={16} />} onClick={request.open}>{t('assets.requestWork')}</Button>}
          {can(P.assetsTransfer) && <Button variant="default" leftSection={<IconArrowsTransferUp size={16} />} onClick={transfer.open}>{t('assets.transfer')}</Button>}
          {(can(P.assetsCreate) || can(P.assetsDelete)) && (
            <Menu position="bottom-end">
              <Menu.Target><ActionIcon variant="default" size="lg" aria-label={t('common.actions')}><IconDots size={16} /></ActionIcon></Menu.Target>
              <Menu.Dropdown>
                {can(P.requestsCreate) && can(P.woCreate) && <Menu.Item leftSection={<IconClipboardText size={16} />} onClick={request.open}>{t('assets.requestWork')}</Menu.Item>}
                {can(P.assetsCreate) && <Menu.Item leftSection={<IconCopy size={16} />} onClick={duplicate}>{t('assets.duplicate')}</Menu.Item>}
                {can(P.assetsCreate) && <Menu.Item leftSection={<IconPlus size={16} />} component={Link} to={`/assets/new?parentId=${a.id}`}>{t('assets.addComponent')}</Menu.Item>}
                {can(P.assetsDelete) && <><Menu.Divider /><Menu.Item color="red" leftSection={<IconTrash size={16} />} onClick={remove}>{t('common.delete')}</Menu.Item></>}
              </Menu.Dropdown>
            </Menu>
          )}
        </>} />
      <Tabs value={tab} onChange={setTab} keepMounted={false}>
        <Tabs.List mb="md">
          <Tabs.Tab value="overview">{t('assets.overview')}</Tabs.Tab>
          {applicableFields.length > 0 && <Tabs.Tab value="specs">{t('assets.specifications')}</Tabs.Tab>}
          <Tabs.Tab value="components" rightSection={a.childCount ? <Badge size="xs" variant="default">{a.childCount}</Badge> : null}>{t('assets.components')}</Tabs.Tab>
          {can(P.woView) && <Tabs.Tab value="work">{t('assets.workOrders')}</Tabs.Tab>}
          <Tabs.Tab value="files">{t('assets.files')}</Tabs.Tab>
          <Tabs.Tab value="transfers">{t('assets.transfers')}</Tabs.Tab>
          <Tabs.Tab value="history">{t('assets.history')}</Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="overview">
          <Grid gutter="lg">
            <Grid.Col span={{ base: 12, md: 8 }}>
              <Stack gap="lg">
                <Card>
                  <Text fz="xs" c="dimmed" mb={4}>{terms.location}</Text>
                  <Breadcrumbs separator="›" mb="md">
                    <Anchor component={Link} to={`/locations?site=${a.site.id}`} fz="sm" fw={500}>{a.site.name}</Anchor>
                    {a.locationTrail.length === 0 ? <Text fz="sm" c="dimmed">{t('assets.unassigned')}</Text> :
                      a.locationTrail.map((l) => <Anchor key={l.id} component={Link} to={`/locations?loc=${l.id}`} fz="sm" fw={500}>{l.name}</Anchor>)}
                  </Breadcrumbs>
                  {a.parentAsset && <Group gap={6} mb="md"><Text fz="sm" c="dimmed">{t('assets.parent')}:</Text><Anchor component={Link} to={`/assets/${a.parentAsset.id}`} fz="sm">{a.parentAsset.assetNumber} · {a.parentAsset.name}</Anchor></Group>}
                  <SimpleGrid cols={{ base: 2, sm: 3 }} spacing="md" verticalSpacing="md">
                    <Field label={t('assets.manufacturer')}>{a.manufacturer}</Field>
                    <Field label={t('assets.model')}>{a.model}</Field>
                    <Field label={t('assets.serialNumber')}>{a.serialNumber}</Field>
                    <Field label={t('assets.barcode')}>{a.barcode}</Field>
                    <Field label={t('assets.operationNumber')}>{a.operationNumber}</Field>
                    <Field label={t('assets.category')}>{a.category?.name}</Field>
                  </SimpleGrid>
                  {a.description && <Box mt="md"><Field label={t('common.description')}>{a.description}</Field></Box>}
                </Card>
                {(a.notesToTechnician || a.notes) && (
                  <Card>
                    {a.notesToTechnician && <Alert variant="light" color="yellow" icon={<IconInfoCircle size={18} />} title={t('assets.notesToTechnician')} mb={a.notes ? 'md' : 0}>{a.notesToTechnician}</Alert>}
                    {a.notes && <Field label={t('common.notes')}>{a.notes}</Field>}
                  </Card>
                )}
              </Stack>
            </Grid.Col>
            <Grid.Col span={{ base: 12, md: 4 }}>
              <Stack gap="lg">
                {a.primaryImageId ? <Card p={0} style={{ overflow: 'hidden' }}><Image src={`/api/attachments/${a.primaryImageId}/content`} h={220} fit="cover" alt={a.name} /></Card> : (
                  <Card><Center h={140}><Stack align="center" gap={4}><ThemeIcon size={48} variant="light" radius="xl"><IconBox size={26} /></ThemeIcon>
                    {can(P.assetsEdit) && <Anchor fz="sm" onClick={() => setTab('files')}>{t('assets.files')}</Anchor>}</Stack></Center></Card>
                )}
                <Card>
                  <Group gap="xs" mb="sm">
                    <ThemeIcon variant="light" color={covered ? 'teal' : 'gray'}>{covered ? <IconShieldCheck size={18} /> : <IconShieldX size={18} />}</ThemeIcon>
                    <Text fw={600}>{t('assets.warranty')}</Text>
                  </Group>
                  {a.warrantyType === 'None' ? <Text fz="sm" c="dimmed">{t('warrantyType.None')}</Text> : (
                    <Stack gap={4}>
                      <Text fz="sm">{t(`warrantyType.${a.warrantyType}`)}</Text>
                      <Text fz="sm" c={covered ? 'teal' : 'red'}>{covered ? t('assets.warrantyActive', { date: fmtDate(a.warrantyExpiresOn) }) : t('assets.warrantyExpired', { date: fmtDate(a.warrantyExpiresOn) })}</Text>
                      {a.warrantyNotes && <Text fz="sm" c="dimmed">{a.warrantyNotes}</Text>}
                    </Stack>
                  )}
                </Card>
                <Card>
                  <Text fw={600} mb="sm">{t('assets.financial')}</Text>
                  <SimpleGrid cols={2} spacing="sm">
                    <Field label={t('assets.commissionedOn')}>{fmtDate(a.commissionedOn)}</Field>
                    <Field label={t('assets.age')}>{ageYears !== null ? t('assets.years', { count: Number(ageYears.toFixed(1)) }) : null}</Field>
                    <Field label={t('assets.purchasePrice')}>{fmtMoney(a.purchasePrice, currency)}</Field>
                    <Field label={t('assets.currentValue')}>{fmtMoney(a.currentValue, currency)}</Field>
                    <Field label={t('assets.expectedLife')}>{a.expectedLifeYears}</Field>
                    <Field label={t('assets.downtimeCost')}>{fmtMoney(a.downtimeCostPerHour, currency)}</Field>
                    {a.disposedOn && <Field label={t('assets.disposedOn')}>{fmtDate(a.disposedOn)}</Field>}
                  </SimpleGrid>
                </Card>
                <Text fz="xs" c="dimmed">{t('common.createdBy', { name: a.createdBy ?? 'System' })} · {fmtDateTime(a.createdAt)}{a.updatedAt && <><br />{t('common.updatedAt', { date: fmtDateTime(a.updatedAt) })}{a.updatedBy && ` · ${a.updatedBy}`}</>}</Text>
              </Stack>
            </Grid.Col>
          </Grid>
        </Tabs.Panel>
        <Tabs.Panel value="specs">
          <Card><SimpleGrid cols={{ base: 1, sm: 2, md: 3 }} spacing="lg">{applicableFields.map((f) => <Field key={f.id} label={f.label}>{fmtCustom(f, a.customFields?.[f.key])}</Field>)}</SimpleGrid></Card>
        </Tabs.Panel>
        <Tabs.Panel value="components">
          <Card p={0}>
            <Group justify="space-between" p="md"><Title order={5}>{t('assets.components')}</Title>
              {can(P.assetsCreate) && <Button size="xs" leftSection={<IconPlus size={14} />} component={Link} to={`/assets/new?parentId=${a.id}`}>{t('assets.addComponent')}</Button>}</Group>
            <DataTable minHeight={120} records={children ?? []} noRecordsText={t('assets.noComponents')} highlightOnHover rowClassName={() => 'clickable-row'}
              onRowClick={({ record }) => navigate(`/assets/${record.id}`)}
              columns={[
                { accessor: 'assetNumber', title: t('assets.number') }, { accessor: 'name', title: t('common.name') },
                { accessor: 'model', title: t('assets.model') }, { accessor: 'status', title: t('common.status'), render: (r) => <StatusBadge status={r.status} /> },
              ]} />
          </Card>
        </Tabs.Panel>
        <Tabs.Panel value="work">
          <Card p={0}>
            <DataTable minHeight={140} fetching={wosLoading} records={wos?.items ?? []} noRecordsText={t('wo.emptyTitle')} highlightOnHover rowClassName={() => 'clickable-row'}
              onRowClick={({ record }) => navigate(`/work-orders/${record.id}`)}
              columns={[
                { accessor: 'number', title: t('wo.number'), noWrap: true }, { accessor: 'title', title: t('common.name') },
                { accessor: 'type', title: t('workType.label'), render: (w) => t(`workType.${w.type}`) },
                { accessor: 'status', title: t('common.status'), noWrap: true, render: (w) => <WoStatusBadge status={w.status} /> },
                { accessor: 'priority', title: t('priority.label'), noWrap: true, render: (w) => <PriorityBadge value={w.priority} /> },
                { accessor: 'assetNumber', title: t('wo.asset'), noWrap: true },
                { accessor: 'createdAt', title: t('wo.createdOn'), noWrap: true, render: (w) => fmtDate(w.createdAt) },
              ]} />
          </Card>
        </Tabs.Panel>
        <Tabs.Panel value="files"><Card><Attachments entityType="Asset" entityId={a.id} canEdit={can(P.assetsEdit)} onChanged={refresh} /></Card></Tabs.Panel>
        <Tabs.Panel value="transfers">
          <Card>
            {transfers && transfers.length === 0 ? <Text c="dimmed" fz="sm">{t('assets.noTransfers')}</Text> : (
              <Table.ScrollContainer minWidth={600}>
                <Table verticalSpacing="sm">
                  <Table.Thead><Table.Tr><Table.Th>{t('common.details')}</Table.Th><Table.Th>{t('assets.from')}</Table.Th><Table.Th>{t('assets.to')}</Table.Th><Table.Th>{t('assets.transferReason')}</Table.Th><Table.Th>{t('assets.by')}</Table.Th></Table.Tr></Table.Thead>
                  <Table.Tbody>
                    {(transfers ?? []).map((x) => (
                      <Table.Tr key={x.id}><Table.Td>{fmtDateTime(x.transferredAt)}</Table.Td><Table.Td>{[x.fromSite, x.fromLocation].filter(Boolean).join(' › ')}</Table.Td>
                        <Table.Td>{[x.toSite, x.toLocation].filter(Boolean).join(' › ')}</Table.Td><Table.Td>{x.reason}</Table.Td><Table.Td>{x.transferredBy}</Table.Td></Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              </Table.ScrollContainer>
            )}
          </Card>
        </Tabs.Panel>
        <Tabs.Panel value="history"><Card><AuditTimeline entityType="Asset" entityId={a.id} /></Card></Tabs.Panel>
      </Tabs>
      <TransferModal opened={transferOpen} onClose={transfer.close} asset={a} />
      <RequestFormModal opened={requestOpen} onClose={request.close} assetId={a.id} />
    </>
  );
}

function TransferModal({ opened, onClose, asset }: { opened: boolean; onClose: () => void; asset: AssetDetail }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const form = useForm({ initialValues: { siteId: asset.siteId as string | null, locationId: (asset.locationId ?? null) as string | null, reason: '', includeChildren: true } });
  const submit = form.onSubmit(async (v) => {
    try {
      await post(`/assets/${asset.id}/transfer`, v);
      notifySuccess(t('assets.transferred'));
      await qc.invalidateQueries({ queryKey: ['asset', asset.id] });
      await qc.invalidateQueries({ queryKey: ['assets'] });
      await qc.invalidateQueries({ queryKey: ['hierarchy'] });
      onClose();
    } catch (e) { handleFormError(form, e); }
  });
  return (
    <Modal opened={opened} onClose={onClose} title={t('assets.transferTitle', { name: asset.assetNumber })} size="lg">
      <form onSubmit={submit}>
        <Stack>
          <LocationPicker siteId={form.values.siteId} locationId={form.values.locationId} onChange={(s, l) => form.setValues({ siteId: s, locationId: l })}
            siteError={form.errors.siteId} locationError={form.errors.locationId} />
          <Textarea label={t('assets.transferReason')} autosize minRows={2} {...form.getInputProps('reason')} />
          {asset.childCount > 0 && <Checkbox label={t('assets.includeChildren', { count: asset.childCount })} {...form.getInputProps('includeChildren', { type: 'checkbox' })} />}
          <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('assets.transfer')}</Button></Group>
        </Stack>
      </form>
    </Modal>
  );
}
