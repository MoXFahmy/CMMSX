import { useState } from 'react';
import { ActionIcon, Alert, Anchor, Badge, Button, Card, Center, Grid, Group, Loader, Menu, Modal, Stack, Table, Text, Textarea, Title } from '@mantine/core';
import { modals } from '@mantine/modals';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconArrowBackUp, IconCheck, IconDots, IconEdit, IconLock, IconSend, IconShoppingCartPlus, IconTrash, IconX } from '@tabler/icons-react';
import { del, get, post } from '@/api/client';
import { useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { Attachments } from '@/components/Attachments';
import { confirmDelete } from '@/components/confirm';
import { fmtDate, fmtDateTime, fmtPrice, fmtQty, notifyError, notifySuccess } from '@/components/utils';
import { RequestStatusBadge } from './RequestsTab';
import { OrderFromRequestsModal } from './OrderFromRequestsModal';
import type { PurchaseRequestDetail } from './purchaseRequests';

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <Stack gap={0}><Text fz="xs" c="dimmed" tt="uppercase" fw={600}>{label}</Text><Text fz="sm" component="div">{children || '—'}</Text></Stack>;
}

export function RequestDetailPage() {
  const { t } = useTranslation();
  const { id = '' } = useParams();
  const { me, terms } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const navigate = useNavigate();
  const qc = useQueryClient();
  const [reason, setReason] = useState<{ kind: 'reject' | 'cancel'; text: string } | null>(null);
  const [ordering, setOrdering] = useState(false);
  const [comment, setComment] = useState('');
  const { data: r, isLoading, error } = useQuery({ queryKey: ['purchase-request', id], queryFn: () => get<PurchaseRequestDetail>(`/purchase-requests/${id}`) });
  if (isLoading) return <Center h={300}><Loader /></Center>;
  if (error || !r) return <Alert color="red">{(error as Error)?.message}</Alert>;
  const refresh = async () => { await qc.invalidateQueries({ queryKey: ['purchase-request', id] }); await qc.invalidateQueries({ queryKey: ['purchase-requests'] }); await qc.invalidateQueries({ queryKey: ['alerts'] }); };
  const act = async (path: string, msg: string, body?: unknown) => {
    try { await post(`/purchase-requests/${r.id}/${path}`, body); notifySuccess(msg); await refresh(); return true; } catch (e) { notifyError(e); return false; }
  };
  const a = r.actions;
  const estimate = r.lines.reduce((s, l) => s + (l.estimatedUnitPrice ?? 0) * l.quantity, 0);

  return (
    <>
      <PageHeader crumbs={[{ label: t('pr.title'), to: '/purchasing/requests' }, { label: r.number }]} title={r.number} badge={<RequestStatusBadge status={r.status} />}
        subtitle={<>{r.siteName} · {r.requestedByName}{r.neededBy && ` · ${t('pr.neededBy')} ${fmtDate(r.neededBy)}`}</>}
        actions={<>
          {a.submit && <Button leftSection={<IconSend size={16} />} onClick={() => act('submit', t('pr.submitted'))}>{t('pr.submit')}</Button>}
          {a.approve && <Button leftSection={<IconCheck size={16} />} color="teal" onClick={() => act('approve', t('pr.approved'))}>{t('pr.approve')}</Button>}
          {a.reject && <Button leftSection={<IconArrowBackUp size={16} />} variant="default" onClick={() => setReason({ kind: 'reject', text: '' })}>{t('pr.reject')}</Button>}
          {a.order && <Button leftSection={<IconShoppingCartPlus size={16} />} onClick={() => setOrdering(true)}>{t('pr.order')}</Button>}
          {(a.edit || a.cancel || a.delete || a.close) && (
            <Menu position="bottom-end">
              <Menu.Target><ActionIcon variant="default" size="lg" aria-label={t('common.actions')}><IconDots size={16} /></ActionIcon></Menu.Target>
              <Menu.Dropdown>
                {a.edit && <Menu.Item leftSection={<IconEdit size={16} />} component={Link} to={`/purchasing/requests/${r.id}/edit`}>{t('common.edit')}</Menu.Item>}
                {a.close && <Menu.Item leftSection={<IconLock size={16} />} onClick={() => modals.openConfirmModal({
                  title: t('pr.close'), children: <Text fz="sm">{t('pr.closeConfirm', { number: r.number })}</Text>, labels: { confirm: t('pr.close'), cancel: t('common.cancel') },
                  onConfirm: () => void act('close', t('pr.closed')) })}>{t('pr.close')}</Menu.Item>}
                {a.cancel && <Menu.Item color="red" leftSection={<IconX size={16} />} onClick={() => setReason({ kind: 'cancel', text: '' })}>{t('pr.cancel')}</Menu.Item>}
                {a.delete && <><Menu.Divider /><Menu.Item color="red" leftSection={<IconTrash size={16} />} onClick={() => confirmDelete(r.number, async () => {
                  try { await del(`/purchase-requests/${r.id}`); await qc.invalidateQueries({ queryKey: ['purchase-requests'] }); navigate('/purchasing/requests'); } catch (e) { notifyError(e); } })}>{t('common.delete')}</Menu.Item></>}
              </Menu.Dropdown>
            </Menu>
          )}
        </>} />
      {r.status === 'Rejected' && r.rejectReason && <Alert color="orange" mb="md" title={t('pr.returnedBy', { name: r.decidedByName ?? '' })}>{r.rejectReason}</Alert>}
      {r.status === 'Cancelled' && <Alert color="red" mb="md" title={t('pr.cancelledTitle')}>{r.cancelReason}</Alert>}
      <Grid>
        <Grid.Col span={{ base: 12, lg: 9 }}>
          <Card p={0}>
            <Table.ScrollContainer minWidth={820}>
              <Table verticalSpacing="sm" styles={{ th: { whiteSpace: 'nowrap' } }}>
                <Table.Thead><Table.Tr>
                  <Table.Th w={36}>#</Table.Th><Table.Th>{t('pr.description')}</Table.Th><Table.Th>{t('pr.store')}</Table.Th><Table.Th ta="right">{t('pr.quantity')}</Table.Th>
                  <Table.Th>{t('pr.neededBy')}</Table.Th><Table.Th ta="right">{t('pr.estimatedPrice')}</Table.Th><Table.Th>{t('pr.ordered')}</Table.Th>
                </Table.Tr></Table.Thead>
                <Table.Tbody>
                  {r.lines.map((l) => (
                    <Table.Tr key={l.id}>
                      <Table.Td c="dimmed">{l.lineNumber}</Table.Td>
                      <Table.Td>{l.part && <Anchor component={Link} to={`/inventory/parts/${l.part.id}`} fz="sm" fw={600}>{l.part.partNumber}</Anchor>}
                        <Text fz="sm">{l.description}</Text>
                        {l.workOrder && <Anchor component={Link} to={`/work-orders/${l.workOrder.id}`} fz="xs">{l.workOrder.number} · {l.workOrder.title}</Anchor>}
                        {!l.workOrder && l.asset && <Text fz="xs" c="dimmed">{l.asset.assetNumber} · {l.asset.name}</Text>}
                        {l.reason && <Text fz="xs" c="dimmed">{l.reason}</Text>}</Table.Td>
                      <Table.Td>{l.store ? <Text fz="sm">{l.store.code}</Text> : <Text fz="sm" c="dimmed">—</Text>}</Table.Td>
                      <Table.Td ta="right">{fmtQty(l.quantity)} {l.unitOfMeasure}</Table.Td>
                      <Table.Td>{fmtDate(l.neededBy ?? r.neededBy)}</Table.Td>
                      <Table.Td ta="right">{l.estimatedUnitPrice == null ? '—' : fmtPrice(l.estimatedUnitPrice, currency)}</Table.Td>
                      <Table.Td>{l.order
                        ? <Stack gap={0}><Anchor component={Link} to={`/purchasing/orders/${l.order.id}`} fz="sm">{l.order.number}</Anchor>
                          <Text fz="xs" c="dimmed">{t('pr.received')} {fmtQty(l.received)} / {fmtQty(l.ordered)}</Text></Stack>
                        : (r.status === 'Approved' || r.status === 'PartlyOrdered') ? <Badge variant="light" color="gray" radius="sm" style={{ textTransform: 'none' }}>{t('pr.toOrder')}</Badge> : '—'}</Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Table.ScrollContainer>
            {estimate > 0 && <Text ta="right" p="md" fz="sm" fw={600}>{t('pr.estimate')}: {fmtPrice(estimate, currency)}</Text>}
          </Card>
          {r.notes && <Card mt="md"><Text fz="xs" c="dimmed" tt="uppercase" fw={600}>{t('pr.notes')}</Text><Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{r.notes}</Text></Card>}
          <Card mt="md">
            <Title order={5} mb="sm">{t('pr.comments')}</Title>
            <Stack gap="xs">
              {r.comments.length === 0 && <Text fz="sm" c="dimmed">{t('pr.noComments')}</Text>}
              {r.comments.map((c) => <Stack key={c.id} gap={0}><Text fz="xs" c="dimmed">{c.userName} · {fmtDateTime(c.createdAt)}</Text><Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{c.body}</Text></Stack>)}
              {a.comment && <Group align="flex-end" gap="xs" mt="xs">
                <Textarea style={{ flex: 1 }} autosize minRows={1} placeholder={t('pr.addComment')} value={comment} onChange={(e) => setComment(e.currentTarget.value)} />
                <Button disabled={!comment.trim()} onClick={async () => { if (await act('comments', t('common.saved'), { body: comment })) setComment(''); }}>{t('pr.post')}</Button>
              </Group>}
            </Stack>
          </Card>
          <Card mt="md"><Title order={5} mb="sm">{t('pr.files')}</Title><Attachments entityType="PurchaseRequest" entityId={r.id} canEdit={r.status !== 'Cancelled'} /></Card>
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 3 }}>
          <Card>
            <Stack gap="sm">
              <Field label={terms.site}>{r.siteName}</Field>
              <Field label={t('pr.requestedBy')}>{r.requestedByName}</Field>
              <Field label={t('pr.submittedAt')}>{r.submittedAt ? fmtDateTime(r.submittedAt) : null}</Field>
              {r.decidedByName && r.status !== 'Rejected' && <Field label={t('pr.approvedByLabel')}>{r.decidedByName} · {fmtDateTime(r.decidedAt)}</Field>}
            </Stack>
          </Card>
        </Grid.Col>
      </Grid>
      <Modal opened={!!reason} onClose={() => setReason(null)} title={reason?.kind === 'reject' ? t('pr.rejectTitle') : t('pr.cancelTitle')}>
        <Textarea autosize minRows={2} data-autofocus label={reason?.kind === 'reject' ? t('pr.rejectReason') : t('pr.cancelReason')} value={reason?.text ?? ''}
          onChange={(e) => setReason((x) => (x ? { ...x, text: e.currentTarget.value } : x))} />
        <Group justify="flex-end" mt="md">
          <Button variant="default" onClick={() => setReason(null)}>{t('common.cancel')}</Button>
          <Button color={reason?.kind === 'cancel' ? 'red' : undefined} disabled={!reason?.text.trim()} onClick={async () => {
            if (!reason) return;
            if (await act(reason.kind, reason.kind === 'reject' ? t('pr.rejected') : t('pr.cancelled'), { reason: reason.text })) setReason(null);
          }}>{reason?.kind === 'reject' ? t('pr.reject') : t('pr.cancel')}</Button>
        </Group>
      </Modal>
      <OrderFromRequestsModal request={r} opened={ordering} onClose={() => setOrdering(false)} />
    </>
  );
}
