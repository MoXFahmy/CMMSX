import { useEffect, useState } from 'react';
import { ActionIcon, Alert, Anchor, Badge, Button, Card, Center, Grid, Group, Loader, Menu, Modal, NumberInput, Progress, Select, Stack, Table, Tabs, Text, TextInput, Textarea, Title, Timeline } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconAlertTriangle, IconCheck, IconDots, IconEdit, IconLock, IconPackageImport, IconPrinter, IconSend, IconTrash, IconArrowBackUp, IconX } from '@tabler/icons-react';
import { del, get, post } from '@/api/client';
import type { PoDetail } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { PoStatusBadge } from '@/components/InventoryBadges';
import { useStores } from '@/components/PartPicker';
import { AuditTimeline } from '@/components/AuditTimeline';
import { Attachments } from '@/components/Attachments';
import { confirmDelete } from '@/components/confirm';
import { fmtDate, fmtDateTime, fmtPrice, fmtQty, notifyError, notifySuccess, toIsoDate } from '@/components/utils';

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <Stack gap={0}><Text fz="xs" c="dimmed" tt="uppercase" fw={600}>{label}</Text><Text fz="sm" component="div">{children || '—'}</Text></Stack>;
}

export function PoDetailPage() {
  const { t } = useTranslation();
  const { id = '' } = useParams();
  const { me } = useAuth();
  const base = me?.tenant.settings.currency ?? 'USD';
  const navigate = useNavigate();
  const qc = useQueryClient();
  const [tab, setTab] = useState<string | null>('lines');
  const [receiveOpen, setReceiveOpen] = useState(false);
  const [reason, setReason] = useState<{ kind: 'reject' | 'cancel'; text: string } | null>(null);
  const { data: o, isLoading, error } = useQuery({ queryKey: ['purchase-order', id], queryFn: () => get<PoDetail>(`/purchase-orders/${id}`) });
  if (isLoading) return <Center h={300}><Loader /></Center>;
  if (error || !o) return <Alert color="red">{(error as Error)?.message}</Alert>;
  const refresh = async () => {
    await qc.invalidateQueries({ queryKey: ['purchase-order', id] });
    await qc.invalidateQueries({ queryKey: ['purchase-orders'] });
    await qc.invalidateQueries({ queryKey: ['parts'] }); await qc.invalidateQueries({ queryKey: ['part'] }); await qc.invalidateQueries({ queryKey: ['proposal'] });
  };
  const act = async (path: string, msg: string, body?: unknown) => {
    try { await post(`/purchase-orders/${o.id}/${path}`, body); notifySuccess(msg); await refresh(); } catch (e) { notifyError(e); }
  };
  const a = o.actions;
  const ordered = o.lines.reduce((s, l) => s + l.quantity, 0);
  const received = o.lines.reduce((s, l) => s + Math.min(l.quantityReceived, l.quantity), 0);
  const pct = ordered ? Math.round((received / ordered) * 100) : 0;
  const steps: { label: string; at?: string; by?: string; done: boolean }[] = [
    { label: t('po.timeline.created'), at: o.createdAt, by: o.requestedBy, done: true },
    { label: t('po.timeline.submitted'), at: o.submittedAt, done: !!o.submittedAt },
    { label: t('po.timeline.approved'), at: o.approvedAt, by: o.approvedByName, done: !!o.approvedAt },
    { label: t('po.timeline.received'), at: o.receipts[0]?.date, done: o.status === 'Received' || o.status === 'Closed' },
  ];
  if (o.status === 'Cancelled') steps.push({ label: t('po.timeline.cancelled'), at: o.cancelledAt, by: o.cancelReason, done: true });

  return (
    <>
      <PageHeader crumbs={[{ label: t('po.title'), to: '/purchasing' }, { label: o.number }]} title={`${o.number} · ${o.supplier.name}`}
        badge={<Group gap={6}><PoStatusBadge status={o.status} />{o.isOverdue && <Badge color="red" variant="filled">{t('wo.overdueBadge')}</Badge>}</Group>}
        subtitle={<>{o.siteName} · {t('po.orderedOn', { date: fmtDate(o.orderDate) })}{o.expectedDate && ` · ${t('po.expectedOn', { date: fmtDate(o.expectedDate) })}`}</>}
        actions={<>
          {a.submit && <Button leftSection={<IconSend size={16} />} variant={a.approve ? 'default' : 'filled'} onClick={() => act('submit', t('po.submitted'))}>{t('po.submit')}</Button>}
          {a.approve && <Button leftSection={<IconCheck size={16} />} color="teal" onClick={() => act('approve', t('po.approved'))}>{t('po.approve')}</Button>}
          {a.receive && <Button leftSection={<IconPackageImport size={16} />} onClick={() => setReceiveOpen(true)}>{t('po.receive')}</Button>}
          <Button variant="default" leftSection={<IconPrinter size={16} />} component={Link} to={`/purchasing/orders/${o.id}/print`} target="_blank">{t('po.print')}</Button>
          {(a.edit || a.reject || a.close || a.cancel || a.delete) && (
            <Menu position="bottom-end">
              <Menu.Target><ActionIcon variant="default" size="lg" aria-label={t('common.actions')}><IconDots size={16} /></ActionIcon></Menu.Target>
              <Menu.Dropdown>
                {a.edit && <Menu.Item leftSection={<IconEdit size={16} />} component={Link} to={`/purchasing/orders/${o.id}/edit`}>{t('common.edit')}</Menu.Item>}
                {a.reject && <Menu.Item leftSection={<IconArrowBackUp size={16} />} onClick={() => setReason({ kind: 'reject', text: '' })}>{t('po.reject')}</Menu.Item>}
                {a.close && <Menu.Item leftSection={<IconLock size={16} />} onClick={() => act('close', t('po.closed'))}>{o.status === 'PartiallyReceived' ? t('po.shortClose') : t('po.close')}</Menu.Item>}
                {a.cancel && <Menu.Item color="red" leftSection={<IconX size={16} />} onClick={() => setReason({ kind: 'cancel', text: '' })}>{t('po.cancel')}</Menu.Item>}
                {a.delete && <><Menu.Divider /><Menu.Item color="red" leftSection={<IconTrash size={16} />} onClick={() => confirmDelete(o.number, async () => {
                  try { await del(`/purchase-orders/${o.id}`); await qc.invalidateQueries({ queryKey: ['purchase-orders'] }); navigate('/purchasing'); } catch (e) { notifyError(e); } })}>{t('common.delete')}</Menu.Item></>}
              </Menu.Dropdown>
            </Menu>
          )}
        </>} />
      {a.approvalLimitExceeded && <Alert color="orange" icon={<IconAlertTriangle size={16} />} mb="md">{t('po.overLimit')}</Alert>}
      {o.status === 'Cancelled' && <Alert color="red" mb="md" title={t('po.cancelledTitle')}>{o.cancelReason}</Alert>}
      <Grid>
        <Grid.Col span={{ base: 12, lg: 9 }}>
          <Tabs value={tab} onChange={setTab} keepMounted={false}>
            <Tabs.List mb="md">
              <Tabs.Tab value="lines" rightSection={<Badge size="xs" variant="default">{o.lines.length}</Badge>}>{t('po.lines')}</Tabs.Tab>
              <Tabs.Tab value="receipts" rightSection={o.receipts.length ? <Badge size="xs" variant="default">{o.receipts.length}</Badge> : null}>{t('po.receipts')}</Tabs.Tab>
              <Tabs.Tab value="files">{t('wo.files')}</Tabs.Tab>
              <Tabs.Tab value="audit">{t('audit.title')}</Tabs.Tab>
            </Tabs.List>
            <Tabs.Panel value="lines">
              <Card p={0}>
                <Table.ScrollContainer minWidth={760}>
                  <Table verticalSpacing="sm" styles={{ th: { whiteSpace: 'nowrap' } }}>
                    <Table.Thead><Table.Tr>
                      <Table.Th w={36}>#</Table.Th><Table.Th>{t('common.description')}</Table.Th><Table.Th>{t('po.deliverTo')}</Table.Th><Table.Th ta="right">{t('inv.quantity')}</Table.Th>
                      <Table.Th ta="right">{t('po.unitPrice')}</Table.Th><Table.Th ta="right">{t('po.discount')}</Table.Th><Table.Th ta="right">{t('po.lineTotal')}</Table.Th><Table.Th w={150}>{t('po.received')}</Table.Th>
                    </Table.Tr></Table.Thead>
                    <Table.Tbody>
                      {o.lines.map((l) => (
                        <Table.Tr key={l.id}>
                          <Table.Td c="dimmed">{l.lineNumber}</Table.Td>
                          <Table.Td>{l.partId ? <Anchor component={Link} to={`/inventory/parts/${l.partId}`} fz="sm" fw={600}>{l.partNumber}</Anchor> : null}
                            <Text fz="sm">{l.description}</Text>{l.accountCode && <Text fz="xs" c="dimmed">{t('po.accountCode')}: {l.accountCode}</Text>}</Table.Td>
                          <Table.Td><Stack gap={0}>{l.storeCode && <Text fz="sm">{t('po.intoStore')} {l.storeCode}</Text>}
                            {l.workOrderId && <Anchor component={Link} to={`/work-orders/${l.workOrderId}`} fz="sm">{l.workOrderNumber}</Anchor>}
                            {!l.storeCode && !l.workOrderId && <Text fz="sm" c="dimmed">{t('po.expense')}</Text>}</Stack></Table.Td>
                          <Table.Td ta="right">{fmtQty(l.quantity)} {l.unitOfMeasure}</Table.Td>
                          <Table.Td ta="right">{fmtPrice(l.unitPrice, o.currency)}</Table.Td>
                          <Table.Td ta="right">{l.discountPercent ? `${l.discountPercent}%` : '—'}</Table.Td>
                          <Table.Td ta="right" fw={600}>{fmtPrice(l.lineTotal, o.currency)}</Table.Td>
                          <Table.Td>{o.status === 'Draft' || o.status === 'Submitted' ? <Text fz="sm" c="dimmed">—</Text> : <>
                            <Progress value={l.quantity ? Math.min(100, (l.quantityReceived / l.quantity) * 100) : 0} size="sm" color={l.outstanding <= 0 ? 'teal' : 'blue'} />
                            <Text fz="xs" c="dimmed" mt={2}>{fmtQty(l.quantityReceived)} / {fmtQty(l.quantity)}{l.outstanding > 0 && ` · ${t('po.outstandingQty', { qty: fmtQty(l.outstanding) })}`}</Text></>}</Table.Td>
                        </Table.Tr>
                      ))}
                    </Table.Tbody>
                  </Table>
                </Table.ScrollContainer>
                <Group justify="flex-end" p="md">
                  <Stack gap={4} w={300}>
                    <Group justify="space-between"><Text fz="sm">{t('po.subtotal')}</Text><Text fz="sm">{fmtPrice(o.totals.subtotal, o.currency)}</Text></Group>
                    <Group justify="space-between"><Text fz="sm">{t('po.tax')} ({o.taxPercent}%)</Text><Text fz="sm">{fmtPrice(o.totals.tax, o.currency)}</Text></Group>
                    <Group justify="space-between"><Text fz="sm">{t('po.shipping')}</Text><Text fz="sm">{fmtPrice(o.totals.shippingCost, o.currency)}</Text></Group>
                    <Group justify="space-between" pt={6} style={{ borderTop: '1px solid var(--mantine-color-default-border)' }}><Text fw={700}>{t('po.total')}</Text><Text fw={700}>{fmtPrice(o.totals.total, o.currency)}</Text></Group>
                    {o.currency !== base && <Text fz="xs" c="dimmed" ta="right">≈ {fmtPrice(o.totals.totalInBase, base)} @ {o.exchangeRate}</Text>}
                  </Stack>
                </Group>
              </Card>
              {o.notes && <Card mt="md"><Text fz="xs" c="dimmed" tt="uppercase" fw={600}>{t('common.notes')}</Text><Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{o.notes}</Text></Card>}
            </Tabs.Panel>
            <Tabs.Panel value="receipts">
              <Card p={0}>
                <Table verticalSpacing="xs">
                  <Table.Thead><Table.Tr><Table.Th>{t('inv.date')}</Table.Th><Table.Th>{t('inv.part')}</Table.Th><Table.Th>{t('inv.store')}</Table.Th><Table.Th ta="right">{t('inv.quantity')}</Table.Th><Table.Th ta="right">{t('inv.unitCost')}</Table.Th><Table.Th>{t('inv.reference')}</Table.Th><Table.Th>{t('inv.by')}</Table.Th></Table.Tr></Table.Thead>
                  <Table.Tbody>
                    {o.receipts.length === 0 && <Table.Tr><Table.Td colSpan={7}><Text c="dimmed" ta="center" py="md" fz="sm">{t('po.noReceipts')}</Text></Table.Td></Table.Tr>}
                    {o.receipts.map((r) => <Table.Tr key={r.id}><Table.Td>{fmtDate(r.date)}</Table.Td><Table.Td>{r.partNumber}{r.batchNumber && <Text span fz="xs" c="dimmed"> · {r.batchNumber}</Text>}</Table.Td><Table.Td>{r.storeCode}</Table.Td>
                      <Table.Td ta="right">{fmtQty(r.quantity)}</Table.Td><Table.Td ta="right">{fmtPrice(r.unitCost, base)}</Table.Td><Table.Td>{r.reference}</Table.Td><Table.Td c="dimmed">{r.createdByName}</Table.Td></Table.Tr>)}
                  </Table.Tbody>
                </Table>
                <Text fz="xs" c="dimmed" p="md">{t('po.receiptsHint')}</Text>
              </Card>
            </Tabs.Panel>
            <Tabs.Panel value="files"><Card><Attachments entityType="PurchaseOrder" entityId={o.id} canEdit={o.status !== 'Cancelled'} /></Card></Tabs.Panel>
            <Tabs.Panel value="audit"><Card><AuditTimeline entityType="PurchaseOrder" entityId={o.id} /></Card></Tabs.Panel>
          </Tabs>
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 3 }}>
          <Stack>
            <Card>
              <Text fw={600} mb="xs">{t('po.supplier')}</Text>
              <Text fz="sm" fw={500}>{o.supplier.name}</Text>
              <Text fz="sm" c="dimmed" style={{ whiteSpace: 'pre-wrap' }}>{[o.supplier.contactName, o.supplier.address, [o.supplier.city, o.supplier.country].filter(Boolean).join(', '), o.supplier.email, o.supplier.phone].filter(Boolean).join('\n')}</Text>
            </Card>
            <Card>
              <Stack gap="sm">
                <Field label={t('po.paymentTerms')}>{o.paymentTerms}</Field>
                <Field label={t('po.quoteRef')}>{o.supplierQuoteRef}</Field>
                <Field label={t('po.shippingMethod')}>{o.shippingMethod}</Field>
                <Field label={t('po.deliveryAddress')}>{o.deliverTo}</Field>
                {o.status !== 'Draft' && o.status !== 'Submitted' && o.status !== 'Cancelled' && <Field label={t('po.received')}><Progress value={pct} size="sm" mt={4} /><Text fz="xs" c="dimmed">{pct}%</Text></Field>}
              </Stack>
            </Card>
            <Card>
              <Text fw={600} mb="sm">{t('po.progress')}</Text>
              <Timeline active={steps.filter((s) => s.done).length - 1} bulletSize={16} lineWidth={2}>
                {steps.map((s) => <Timeline.Item key={s.label} title={<Text fz="sm" fw={500} c={s.done ? undefined : 'dimmed'}>{s.label}</Text>}>
                  {s.done && s.at && <Text fz="xs" c="dimmed">{s.at.length > 10 ? fmtDateTime(s.at) : fmtDate(s.at)}{s.by && ` · ${s.by}`}</Text>}</Timeline.Item>)}
              </Timeline>
            </Card>
          </Stack>
        </Grid.Col>
      </Grid>
      {receiveOpen && <ReceiveModal o={o} onClose={() => setReceiveOpen(false)} onDone={refresh} />}
      <Modal opened={!!reason} onClose={() => setReason(null)} title={reason?.kind === 'reject' ? t('po.reject') : t('po.cancel')}>
        <Stack>
          <Textarea label={t('inv.reason')} required autosize minRows={2} value={reason?.text ?? ''} onChange={(e) => setReason((r) => (r ? { ...r, text: e.currentTarget.value } : r))} data-autofocus />
          <Group justify="flex-end"><Button variant="default" onClick={() => setReason(null)}>{t('common.close')}</Button>
            <Button color={reason?.kind === 'cancel' ? 'red' : undefined} disabled={!reason?.text.trim()} onClick={async () => {
              const r = reason!; setReason(null);
              await act(r.kind, r.kind === 'reject' ? t('po.rejected') : t('po.cancelled'), { reason: r.text });
            }}>{reason?.kind === 'reject' ? t('po.reject') : t('po.cancel')}</Button></Group>
        </Stack>
      </Modal>
    </>
  );
}

function ReceiveModal({ o, onClose, onDone }: { o: PoDetail; onClose: () => void; onDone: () => Promise<void> }) {
  const { t } = useTranslation();
  const { data: stores } = useStores();
  const open = o.lines.filter((l) => l.outstanding > 0);
  const [rows, setRows] = useState(() => Object.fromEntries(open.map((l) => [l.id, { qty: l.outstanding as number | '', storeId: l.storeId ?? null, batch: '', expiry: null as string | null }])));
  const [date, setDate] = useState<string | null>(new Date().toISOString().slice(0, 10));
  const [reference, setReference] = useState('');
  const [busy, setBusy] = useState(false);
  useEffect(() => { /* keep rows when lines refresh */ }, [o.id]);
  const set = (id: string, patch: Partial<(typeof rows)[string]>) => setRows((r) => ({ ...r, [id]: { ...r[id], ...patch } }));
  const submit = async () => {
    setBusy(true);
    try {
      await post(`/purchase-orders/${o.id}/receive`, { date: toIsoDate(date), reference: reference || null,
        lines: open.map((l) => ({ lineId: l.id, quantity: Number(rows[l.id].qty || 0), storeId: rows[l.id].storeId, batchNumber: rows[l.id].batch || null, expiryDate: toIsoDate(rows[l.id].expiry) })).filter((x) => x.quantity > 0) });
      notifySuccess(t('po.goodsReceived'));
      await onDone(); onClose();
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Modal opened onClose={onClose} title={t('po.receiveTitle', { number: o.number })} size="xl">
      <Stack>
        <Group grow>
          <DateInput label={t('po.receivedOn')} value={date} onChange={setDate} maxDate={new Date()} valueFormat="DD MMM YYYY" />
          <TextInput label={t('po.deliveryNote')} value={reference} onChange={(e) => setReference(e.currentTarget.value)} placeholder={o.number} />
        </Group>
        <Table.ScrollContainer minWidth={760}>
          <Table verticalSpacing={6}>
            <Table.Thead><Table.Tr><Table.Th>{t('common.description')}</Table.Th><Table.Th ta="right">{t('po.outstanding')}</Table.Th><Table.Th w={120}>{t('po.receiveNow')}</Table.Th><Table.Th w={170}>{t('po.deliverTo')}</Table.Th><Table.Th w={210}>{t('inv.batch')}</Table.Th></Table.Tr></Table.Thead>
            <Table.Tbody>
              {open.map((l) => (
                <Table.Tr key={l.id}>
                  <Table.Td><Text fz="sm" fw={500}>{l.partNumber ?? l.description}</Text>{l.partNumber && <Text fz="xs" c="dimmed">{l.description}</Text>}</Table.Td>
                  <Table.Td ta="right">{fmtQty(l.outstanding)} {l.unitOfMeasure}</Table.Td>
                  <Table.Td><NumberInput size="xs" min={0} max={l.outstanding} decimalScale={3} value={rows[l.id].qty} onChange={(v) => set(l.id, { qty: v === '' ? '' : Number(v) })} aria-label={`${t('po.receiveNow')} ${l.lineNumber}`} /></Table.Td>
                  <Table.Td>{l.partId
                    ? <Select size="xs" data={(stores ?? []).map((s) => ({ value: s.id, label: s.code }))} value={rows[l.id].storeId} onChange={(v) => set(l.id, { storeId: v })} aria-label={t('inv.store')} />
                    : <Text fz="sm" c="dimmed">{l.workOrderNumber ? t('po.chargeTo', { number: l.workOrderNumber }) : t('po.expense')}</Text>}
                    {l.partId && l.workOrderNumber && <Text fz="xs" c="dimmed">{t('po.thenIssue', { number: l.workOrderNumber })}</Text>}</Table.Td>
                  <Table.Td>{l.isShelfItem ? <Group gap={4} wrap="nowrap">
                    <TextInput size="xs" placeholder={t('inv.batch')} value={rows[l.id].batch} onChange={(e) => set(l.id, { batch: e.currentTarget.value })} w={90} />
                    <DateInput size="xs" placeholder={t('inv.expiry')} value={rows[l.id].expiry} onChange={(v) => set(l.id, { expiry: v })} w={110} valueFormat="DD/MM/YY" /></Group> : <Text fz="sm" c="dimmed">—</Text>}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
        <Group justify="space-between">
          <Button variant="subtle" size="xs" onClick={() => setRows((r) => Object.fromEntries(Object.entries(r).map(([k, v]) => [k, { ...v, qty: 0 }])))}>{t('po.clearQuantities')}</Button>
          <Group><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button loading={busy} onClick={submit} leftSection={<IconPackageImport size={16} />}>{t('po.postReceipt')}</Button></Group>
        </Group>
      </Stack>
    </Modal>
  );
}

export function PoPrintPage() {
  const { t } = useTranslation();
  const { id = '' } = useParams();
  const { me } = useAuth();
  const { data: o } = useQuery({ queryKey: ['purchase-order', id], queryFn: () => get<PoDetail>(`/purchase-orders/${id}`) });
  useEffect(() => { if (o) { document.title = o.number; const h = setTimeout(() => window.print(), 400); return () => clearTimeout(h); } }, [o]);
  if (!o) return <Center h={300}><Loader /></Center>;
  return (
    <div className="print-doc">
      <Group justify="space-between" align="flex-start" mb="xl">
        <Stack gap={2}><Title order={2}>{me?.tenant.name}</Title><Text c="dimmed">{o.siteName}</Text>{o.deliverTo && <Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{t('po.deliveryAddress')}: {o.deliverTo}</Text>}</Stack>
        <Stack gap={2} align="flex-end"><Title order={3}>{t('po.printTitle')}</Title><Text fw={700} fz="lg">{o.number}</Text>
          <Text fz="sm">{t('po.orderDate')}: {fmtDate(o.orderDate)}</Text>{o.expectedDate && <Text fz="sm">{t('po.expected')}: {fmtDate(o.expectedDate)}</Text>}
          {o.status === 'Draft' || o.status === 'Submitted' ? <Badge color="orange" variant="outline">{t(`poStatus.${o.status}`)} — {t('po.notApproved')}</Badge> : null}</Stack>
      </Group>
      <Group align="flex-start" grow mb="lg">
        <Card withBorder><Text fz="xs" c="dimmed" tt="uppercase" fw={600}>{t('po.supplier')}</Text><Text fw={600}>{o.supplier.name}</Text>
          <Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{[o.supplier.contactName, o.supplier.address, [o.supplier.city, o.supplier.country].filter(Boolean).join(', '), o.supplier.email, o.supplier.phone, o.supplier.taxNumber && `${t('po.taxNumber')}: ${o.supplier.taxNumber}`].filter(Boolean).join('\n')}</Text></Card>
        <Card withBorder><Stack gap={4}>
          <Text fz="sm"><b>{t('po.paymentTerms')}:</b> {o.paymentTerms ?? '—'}</Text><Text fz="sm"><b>{t('po.quoteRef')}:</b> {o.supplierQuoteRef ?? '—'}</Text>
          <Text fz="sm"><b>{t('po.shippingMethod')}:</b> {o.shippingMethod ?? '—'}</Text><Text fz="sm"><b>{t('po.requestedBy')}:</b> {o.requestedBy ?? '—'}</Text>
          <Text fz="sm"><b>{t('po.currency')}:</b> {o.currency}</Text></Stack></Card>
      </Group>
      <Table withTableBorder withColumnBorders verticalSpacing={6}>
        <Table.Thead><Table.Tr><Table.Th w={36}>#</Table.Th><Table.Th>{t('inv.partNumber')}</Table.Th><Table.Th>{t('common.description')}</Table.Th><Table.Th ta="right">{t('inv.quantity')}</Table.Th><Table.Th>{t('inv.unit')}</Table.Th><Table.Th ta="right">{t('po.unitPrice')}</Table.Th><Table.Th ta="right">{t('po.discount')}</Table.Th><Table.Th ta="right">{t('po.lineTotal')}</Table.Th></Table.Tr></Table.Thead>
        <Table.Tbody>{o.lines.map((l) => <Table.Tr key={l.id}><Table.Td>{l.lineNumber}</Table.Td><Table.Td>{l.partNumber ?? ''}</Table.Td><Table.Td>{l.description}</Table.Td><Table.Td ta="right">{fmtQty(l.quantity)}</Table.Td><Table.Td>{l.unitOfMeasure}</Table.Td>
          <Table.Td ta="right">{fmtPrice(l.unitPrice, o.currency)}</Table.Td><Table.Td ta="right">{l.discountPercent ? `${l.discountPercent}%` : ''}</Table.Td><Table.Td ta="right">{fmtPrice(l.lineTotal, o.currency)}</Table.Td></Table.Tr>)}</Table.Tbody>
      </Table>
      <Group justify="flex-end" mt="md"><Stack gap={2} w={280}>
        <Group justify="space-between"><Text fz="sm">{t('po.subtotal')}</Text><Text fz="sm">{fmtPrice(o.totals.subtotal, o.currency)}</Text></Group>
        <Group justify="space-between"><Text fz="sm">{t('po.tax')} ({o.taxPercent}%)</Text><Text fz="sm">{fmtPrice(o.totals.tax, o.currency)}</Text></Group>
        <Group justify="space-between"><Text fz="sm">{t('po.shipping')}</Text><Text fz="sm">{fmtPrice(o.totals.shippingCost, o.currency)}</Text></Group>
        <Group justify="space-between" style={{ borderTop: '2px solid #000' }} pt={4}><Text fw={700}>{t('po.total')}</Text><Text fw={700}>{fmtPrice(o.totals.total, o.currency)}</Text></Group>
      </Stack></Group>
      {o.notes && <Text mt="lg" fz="sm" style={{ whiteSpace: 'pre-wrap' }}><b>{t('common.notes')}:</b> {o.notes}</Text>}
      <Group mt={60} grow>
        <Stack gap={2}><div style={{ borderTop: '1px solid #000' }} /><Text fz="xs">{t('po.approvedBy')}{o.approvedByName ? `: ${o.approvedByName} · ${fmtDate(o.approvedAt)}` : ''}</Text></Stack>
        <Stack gap={2}><div style={{ borderTop: '1px solid #000' }} /><Text fz="xs">{t('po.supplierAcceptance')}</Text></Stack>
      </Group>
      <Group justify="center" mt="xl" className="no-print"><Button onClick={() => window.print()} leftSection={<IconPrinter size={16} />}>{t('po.print')}</Button></Group>
    </div>
  );
}
