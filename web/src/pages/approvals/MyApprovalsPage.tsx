import { useState } from 'react';
import { Anchor, Badge, Button, Card, Center, Group, Loader, Modal, Stack, Table, Text, Textarea, Title } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconArrowBackUp, IconCheck } from '@tabler/icons-react';
import { get, post } from '@/api/client';
import { useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { fmtDate, fmtDateTime, fmtPrice, notifyError, notifySuccess } from '@/components/utils';

type RequestItem = { id: string; number: string; siteName: string; requestedByName?: string | null; neededBy?: string | null; lines: number; estimatedTotal?: number | null; submittedAt?: string | null };
type OrderItem = { id: string; number: string; siteName: string; supplierName: string; total: number; currency: string; totalInBase: number; levelName?: string | null;
  signedBefore: string[]; next: string[]; submittedAt?: string | null };
type SentBack = { kind: 'request' | 'order'; id: string; number: string; reason?: string | null; at?: string | null };
type Mine = { requests: RequestItem[]; orders: OrderItem[]; sentBack: SentBack[] };

/** My approvals (0.16.0): requests and orders waiting for my signature, and what was sent back to me. */
export function MyApprovalsPage() {
  const { t } = useTranslation();
  const { me } = useAuth();
  const base = me?.tenant.settings.currency ?? 'USD';
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({ queryKey: ['my-approvals'], queryFn: () => get<Mine>('/approvals/mine') });
  const [rejecting, setRejecting] = useState<{ path: string; number: string; text: string } | null>(null);
  const refresh = async () => { await qc.invalidateQueries({ queryKey: ['my-approvals'] }); await qc.invalidateQueries({ queryKey: ['alerts'] }); };
  const act = async (path: string, msg: string, body?: unknown) => {
    try { await post(path, body); notifySuccess(msg); await refresh(); return true; } catch (e) { notifyError(e); return false; }
  };
  if (isLoading || !data) return <Center h={300}><Loader /></Center>;
  const nothing = data.requests.length === 0 && data.orders.length === 0;

  return (
    <>
      <PageHeader title={t('myApprovals.title')} subtitle={t('myApprovals.subtitle')} />
      <Stack>
        {nothing && <Card><Text c="dimmed" ta="center" py="md">{t('myApprovals.nothing')}</Text></Card>}
        {data.orders.length > 0 && <Card p={0}>
          <Title order={5} p="md">{t('myApprovals.orders')}</Title>
          <Table.ScrollContainer minWidth={760}>
            <Table verticalSpacing="sm">
              <Table.Thead><Table.Tr><Table.Th>{t('po.number')}</Table.Th><Table.Th>{t('po.supplier')}</Table.Th><Table.Th ta="end">{t('po.total')}</Table.Th>
                <Table.Th>{t('approvals.level')}</Table.Th><Table.Th>{t('myApprovals.signedBefore')}</Table.Th><Table.Th>{t('myApprovals.next')}</Table.Th><Table.Th w={220} /></Table.Tr></Table.Thead>
              <Table.Tbody>
                {data.orders.map((o) => (
                  <Table.Tr key={o.id}>
                    <Table.Td><Anchor component={Link} to={`/purchasing/orders/${o.id}`} fw={600} fz="sm">{o.number}</Anchor><Text fz="xs" c="dimmed">{fmtDateTime(o.submittedAt)}</Text></Table.Td>
                    <Table.Td><Text fz="sm">{o.supplierName}</Text><Text fz="xs" c="dimmed">{o.siteName}</Text></Table.Td>
                    <Table.Td ta="end"><Text fz="sm">{fmtPrice(o.total, o.currency)}</Text>{o.currency !== base && <Text fz="xs" c="dimmed">≈ {fmtPrice(o.totalInBase, base)}</Text>}</Table.Td>
                    <Table.Td>{o.levelName ?? <Text fz="sm" c="dimmed">{t('myApprovals.yourLimit')}</Text>}</Table.Td>
                    <Table.Td><Text fz="sm">{o.signedBefore.join(', ') || '—'}</Text></Table.Td>
                    <Table.Td><Text fz="sm">{o.next.join(', ') || '—'}</Text></Table.Td>
                    <Table.Td><Group gap={6} justify="flex-end" wrap="nowrap">
                      <Button size="xs" color="teal" leftSection={<IconCheck size={14} />} onClick={() => act(`/purchase-orders/${o.id}/approve`, t('po.approved'))}>{t('po.approve')}</Button>
                      <Button size="xs" variant="default" leftSection={<IconArrowBackUp size={14} />} onClick={() => setRejecting({ path: `/purchase-orders/${o.id}/reject`, number: o.number, text: '' })}>{t('pr.reject')}</Button>
                    </Group></Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        </Card>}
        {data.requests.length > 0 && <Card p={0}>
          <Title order={5} p="md">{t('myApprovals.requests')}</Title>
          <Table.ScrollContainer minWidth={700}>
            <Table verticalSpacing="sm">
              <Table.Thead><Table.Tr><Table.Th>{t('pr.number')}</Table.Th><Table.Th>{t('pr.requestedBy')}</Table.Th><Table.Th>{t('pr.neededBy')}</Table.Th>
                <Table.Th ta="end">{t('pr.lines')}</Table.Th><Table.Th ta="end">{t('pr.estimate')}</Table.Th><Table.Th w={220} /></Table.Tr></Table.Thead>
              <Table.Tbody>
                {data.requests.map((r) => (
                  <Table.Tr key={r.id}>
                    <Table.Td><Anchor component={Link} to={`/purchasing/requests/${r.id}`} fw={600} fz="sm">{r.number}</Anchor><Text fz="xs" c="dimmed">{fmtDateTime(r.submittedAt)}</Text></Table.Td>
                    <Table.Td><Text fz="sm">{r.requestedByName}</Text><Text fz="xs" c="dimmed">{r.siteName}</Text></Table.Td>
                    <Table.Td>{fmtDate(r.neededBy)}</Table.Td>
                    <Table.Td ta="end">{r.lines}</Table.Td>
                    <Table.Td ta="end">{r.estimatedTotal == null ? '—' : fmtPrice(r.estimatedTotal, base)}</Table.Td>
                    <Table.Td><Group gap={6} justify="flex-end" wrap="nowrap">
                      <Button size="xs" color="teal" leftSection={<IconCheck size={14} />} onClick={() => act(`/purchase-requests/${r.id}/approve`, t('pr.approved'))}>{t('pr.approve')}</Button>
                      <Button size="xs" variant="default" leftSection={<IconArrowBackUp size={14} />} onClick={() => setRejecting({ path: `/purchase-requests/${r.id}/reject`, number: r.number, text: '' })}>{t('pr.reject')}</Button>
                    </Group></Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        </Card>}
        {data.sentBack.length > 0 && <Card>
          <Title order={5} mb="sm">{t('myApprovals.sentBack')}</Title>
          <Stack gap="xs">
            {data.sentBack.map((b) => (
              <Group key={b.id} gap="xs" wrap="nowrap" align="flex-start">
                <Badge variant="light" color="orange" radius="sm" style={{ textTransform: 'none' }}>{b.kind === 'order' ? t('myApprovals.order') : t('myApprovals.request')}</Badge>
                <Anchor component={Link} to={b.kind === 'order' ? `/purchasing/orders/${b.id}` : `/purchasing/requests/${b.id}`} fw={600} fz="sm">{b.number}</Anchor>
                <Text fz="sm" style={{ flex: 1 }}>{b.reason}</Text><Text fz="xs" c="dimmed">{fmtDateTime(b.at)}</Text>
              </Group>
            ))}
          </Stack>
        </Card>}
      </Stack>
      <Modal opened={!!rejecting} onClose={() => setRejecting(null)} title={t('myApprovals.returnTitle', { number: rejecting?.number ?? '' })}>
        <Textarea autosize minRows={2} data-autofocus label={t('pr.rejectReason')} value={rejecting?.text ?? ''} onChange={(e) => setRejecting((x) => (x ? { ...x, text: e.currentTarget.value } : x))} />
        <Group justify="flex-end" mt="md">
          <Button variant="default" onClick={() => setRejecting(null)}>{t('common.cancel')}</Button>
          <Button disabled={!rejecting?.text.trim()} onClick={async () => { if (rejecting && await act(rejecting.path, t('pr.rejected'), { reason: rejecting.text })) setRejecting(null); }}>{t('pr.reject')}</Button>
        </Group>
      </Modal>
    </>
  );
}
