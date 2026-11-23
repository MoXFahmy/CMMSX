import { useState } from 'react';
import { Alert, Anchor, Badge, Button, Card, Grid, Group, Modal, NumberInput, SimpleGrid, Stack, Table, Text, Textarea, Title } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconAlertTriangle, IconBan, IconEdit, IconPlus, IconTrash } from '@tabler/icons-react';
import { del, get, post } from '@/api/client';
import type { ContractDetail } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { Attachments } from '@/components/Attachments';
import { WoStatusBadge } from '@/components/WorkBadges';
import { confirmDelete } from '@/components/confirm';
import { fmtDate, fmtPrice, notifyError, notifySuccess, toIsoDate } from '@/components/utils';
import { daysLeft, deadlineLabel, statusColor } from './contracts';

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <div><Text fz="xs" c="dimmed">{label}</Text><Text fz="sm" component="div">{children ?? '—'}</Text></div>;
}

/** One service contract (0.17.0): terms, alert date, periods, amendments, covered assets, work orders and files. */
export function ContractDetailPage() {
  const { t } = useTranslation();
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { me, terms } = useAuth();
  const { data: c, refetch } = useQuery({ queryKey: ['contract', id], queryFn: () => get<ContractDetail>(`/contracts/${id}`) });
  const [amend, setAmend] = useState(false);
  const [terminate, setTerminate] = useState(false);
  if (!c) return null;
  const currency = c.currency ?? me?.tenant.settings.currency ?? 'USD';
  const base = me?.tenant.settings.currency ?? 'USD';
  const today = new Date().toISOString().slice(0, 10);
  const refresh = async () => { await refetch(); await qc.invalidateQueries({ queryKey: ['contracts'] }); };
  const remove = () => confirmDelete(c.number, async () => {
    try { await del(`/contracts/${c.id}`); await qc.invalidateQueries({ queryKey: ['contracts'] }); navigate('/contracts'); } catch (e) { notifyError(e); }
  });
  return (
    <>
      <PageHeader title={`${c.number} · ${c.title}`} subtitle={c.company.name}
        actions={<Group gap="xs">
          <Badge size="lg" variant="light" color={statusColor(c.status)}>{t(`contracts.status.${c.status}`)}</Badge>
          {c.actions.amend && <Button variant="default" leftSection={<IconPlus size={16} />} onClick={() => setAmend(true)}>{t('contracts.addAmendment')}</Button>}
          {c.actions.terminate && <Button variant="default" color="red" leftSection={<IconBan size={16} />} onClick={() => setTerminate(true)}>{t('contracts.terminate')}</Button>}
          {c.actions.edit && <Button leftSection={<IconEdit size={16} />} component={Link} to={`/contracts/${c.id}/edit`}>{t('common.edit')}</Button>}
          {c.actions.delete && <Button variant="subtle" color="red" leftSection={<IconTrash size={16} />} onClick={remove}>{t('common.delete')}</Button>}
        </Group>} />
      {c.needsAttention && c.deadline && (
        <Alert color="orange" icon={<IconAlertTriangle size={18} />} mb="md" title={t(c.autoRenew ? 'contracts.attentionRenewal' : 'contracts.attentionEnd', { date: fmtDate(c.deadline) })}>
          {t('contracts.daysLeft', { count: daysLeft(c.deadline, today) })}
        </Alert>
      )}
      {c.terminatedOn && <Alert color="red" mb="md" title={t('contracts.terminatedOn', { date: fmtDate(c.terminatedOn) })}>{c.terminationReason}</Alert>}
      <Grid>
        <Grid.Col span={{ base: 12, md: 7 }}>
          <Stack>
            <Card>
              <Title order={5} mb="sm">{t('contracts.terms')}</Title>
              <SimpleGrid cols={{ base: 2, sm: 3 }} spacing="sm">
                <Field label={t('contracts.company')}>{c.company.name}{c.company.phone ? ` · ${c.company.phone}` : ''}</Field>
                <Field label={t('contracts.manager')}>{c.manager?.name}</Field>
                <Field label={t('common.type')}>{c.type?.name}</Field>
                <Field label={t('contracts.trade')}>{c.trade?.name}</Field>
                <Field label={terms.site}>{c.site?.name ?? t('contracts.allSites')}</Field>
                <Field label={t('po.paymentTerms')}>{c.paymentTerms}</Field>
                <Field label={t('contracts.start')}>{fmtDate(c.startDate)}</Field>
                <Field label={t('contracts.end')}>{fmtDate(c.endDate)}</Field>
                <Field label={t('contracts.value')}>{fmtPrice(c.value, currency)}</Field>
                <Field label={t('contracts.autoRenew')}>{c.autoRenew ? t('contracts.renewsEvery', { count: c.renewalMonths ?? 0 }) : t('common.no')}</Field>
                {c.autoRenew && <Field label={t('contracts.noticeDays')}>{t('contracts.days', { count: c.noticeDays })}</Field>}
                <Field label={t(deadlineLabel(c))}>{c.deadline ? fmtDate(c.deadline) : '—'}</Field>
                <Field label={t('contracts.alertDays')}>{t('contracts.days', { count: c.alertDays })}</Field>
                <Field label={t('contracts.expected')}>{c.expectedWorkOrders != null ? `${c.expectedWorkOrders} / ${t(`contracts.per.${c.expectedPer ?? 'Year'}`)}` : null}</Field>
              </SimpleGrid>
              {c.notes && <Text fz="sm" mt="sm" style={{ whiteSpace: 'pre-wrap' }}>{c.notes}</Text>}
            </Card>
            <Card p={0}>
              <Title order={5} p="md" pb="xs">{t('contracts.periods')}</Title>
              <Table striped>
                <Table.Thead><Table.Tr><Table.Th>#</Table.Th><Table.Th>{t('contracts.start')}</Table.Th><Table.Th>{t('contracts.end')}</Table.Th><Table.Th ta="right">{t('contracts.value')}</Table.Th><Table.Th /></Table.Tr></Table.Thead>
                <Table.Tbody>
                  {c.periods.map((p) => (
                    <Table.Tr key={p.id}><Table.Td>{p.number}</Table.Td><Table.Td>{fmtDate(p.startDate)}</Table.Td><Table.Td>{fmtDate(p.endDate)}</Table.Td>
                      <Table.Td ta="right">{fmtPrice(p.value, currency)}</Table.Td><Table.Td>{p.isRenewal && <Badge size="xs" variant="light">{t('contracts.renewal')}</Badge>}</Table.Td></Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Card>
            <Card p={0}>
              <Title order={5} p="md" pb="xs">{t('contracts.amendments')}</Title>
              {c.amendments.length === 0 ? <Text fz="sm" c="dimmed" px="md" pb="md">{t('contracts.noAmendments')}</Text> : (
                <Table striped>
                  <Table.Tbody>
                    {c.amendments.map((a) => (
                      <Table.Tr key={a.id}><Table.Td w={120}>{fmtDate(a.date)}</Table.Td><Table.Td><Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{a.description}</Text>
                        {a.createdByName && <Text fz="xs" c="dimmed">{a.createdByName}</Text>}</Table.Td>
                        <Table.Td ta="right">{a.newValue != null ? fmtPrice(a.newValue, currency) : ''}</Table.Td></Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              )}
            </Card>
          </Stack>
        </Grid.Col>
        <Grid.Col span={{ base: 12, md: 5 }}>
          <Stack>
            <Card p={0}>
              <Title order={5} p="md" pb="xs">{t('contracts.coveredAssets')}</Title>
              {c.assets.length === 0 ? <Text fz="sm" c="dimmed" px="md" pb="md">{t('contracts.noAssets')}</Text> : (
                <Table striped>
                  <Table.Tbody>
                    {c.assets.map((a) => (
                      <Table.Tr key={a.assetId}><Table.Td><Anchor component={Link} to={`/assets/${a.assetId}`} fz="sm">{a.assetNumber}</Anchor><Text fz="xs" c="dimmed">{a.name} · {a.siteName}</Text></Table.Td>
                        <Table.Td ta="right">{a.yearlyCost != null ? `${fmtPrice(a.yearlyCost, currency)} / ${t('contracts.per.Year')}` : ''}</Table.Td></Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              )}
            </Card>
            <Card p={0}>
              <Title order={5} p="md" pb="xs">{t('contracts.workOrders', { count: c.workOrderCount })}</Title>
              {c.workOrders.length === 0 ? <Text fz="sm" c="dimmed" px="md" pb="md">{t('contracts.noWork')}</Text> : (
                <Table striped>
                  <Table.Tbody>
                    {c.workOrders.map((w) => (
                      <Table.Tr key={w.id}><Table.Td><Anchor component={Link} to={`/work-orders/${w.id}`} fz="sm">{w.number}</Anchor><Text fz="xs" c="dimmed" lineClamp={1}>{w.title}</Text></Table.Td>
                        <Table.Td><WoStatusBadge status={w.status} /></Table.Td><Table.Td>{fmtDate(w.completedAt ?? w.dueDate)}</Table.Td></Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              )}
            </Card>
            <Card><Title order={5} mb="sm">{t('wo.files')}</Title><Attachments entityType="Contract" entityId={c.id} canEdit={c.actions.edit} /></Card>
            <Text fz="xs" c="dimmed">{t('contracts.spendNote', { currency: base })}</Text>
          </Stack>
        </Grid.Col>
      </Grid>
      <AmendModal opened={amend} onClose={() => setAmend(false)} id={c.id} currency={currency} onDone={refresh} />
      <TerminateModal opened={terminate} onClose={() => setTerminate(false)} id={c.id} onDone={refresh} />
    </>
  );
}

function AmendModal({ opened, onClose, id, currency, onDone }: { opened: boolean; onClose: () => void; id: string; currency: string; onDone: () => Promise<void> }) {
  const { t } = useTranslation();
  const [date, setDate] = useState<Date | string | null>(new Date());
  const [description, setDescription] = useState('');
  const [value, setValue] = useState<number | ''>('');
  const [busy, setBusy] = useState(false);
  const save = async () => {
    setBusy(true);
    try {
      await post(`/contracts/${id}/amendments`, { date: toIsoDate(date), description, newValue: value === '' ? null : value });
      notifySuccess(t('common.saved')); setDescription(''); setValue(''); onClose(); await onDone();
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Modal opened={opened} onClose={onClose} title={t('contracts.addAmendment')}>
      <Stack>
        <DateInput label={t('contracts.date')} valueFormat="DD MMM YYYY" value={date} onChange={setDate} />
        <Textarea label={t('common.description')} required autosize minRows={3} value={description} onChange={(e) => setDescription(e.currentTarget.value)} />
        <NumberInput label={t('contracts.newValue')} description={t('contracts.newValueHint', { currency })} min={0} decimalScale={2} value={value} onChange={(v) => setValue(v === '' ? '' : Number(v))} />
        <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button loading={busy} onClick={save}>{t('common.save')}</Button></Group>
      </Stack>
    </Modal>
  );
}

function TerminateModal({ opened, onClose, id, onDone }: { opened: boolean; onClose: () => void; id: string; onDone: () => Promise<void> }) {
  const { t } = useTranslation();
  const [date, setDate] = useState<Date | string | null>(new Date());
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const save = async () => {
    setBusy(true);
    try { await post(`/contracts/${id}/terminate`, { date: toIsoDate(date), reason }); notifySuccess(t('common.saved')); onClose(); await onDone(); }
    catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Modal opened={opened} onClose={onClose} title={t('contracts.terminate')}>
      <Stack>
        <Text fz="sm" c="dimmed">{t('contracts.terminateHint')}</Text>
        <DateInput label={t('contracts.terminationDate')} valueFormat="DD MMM YYYY" value={date} onChange={setDate} />
        <Textarea label={t('contracts.reason')} required autosize minRows={2} value={reason} onChange={(e) => setReason(e.currentTarget.value)} />
        <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button color="red" loading={busy} onClick={save}>{t('contracts.terminate')}</Button></Group>
      </Stack>
    </Modal>
  );
}
