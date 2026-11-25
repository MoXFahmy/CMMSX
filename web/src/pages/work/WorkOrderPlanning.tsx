import { useEffect, useMemo, useState } from 'react';
import { ActionIcon, Anchor, Button, Card, Group, Modal, NumberInput, Select, Stack, Table, Text, TextInput, Title, Tooltip } from '@mantine/core';
import { useForm } from '@mantine/form';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router';
import { IconEdit, IconPackageExport, IconPlus, IconShoppingCartPlus, IconTrash } from '@tabler/icons-react';
import { del, post, put } from '@/api/client';
import type { PlannedPart, WorkOrderDetail } from '@/api/types';
import { confirmDelete } from '@/components/confirm';
import { P, useAuth } from '@/auth/AuthContext';
import { PartPicker, useStores } from '@/components/PartPicker';
import { fmtMoney, fmtPrice, fmtQty, handleFormError, hoursText, notifyError, notifySuccess } from '@/components/utils';

/** The parts a job needs, planned before anyone goes to the store; what is not yet issued is reserved for it. */
export function PlannedPartsCard({ w, canPlan, canIssue, onChange, onIssue, currency }: {
  w: WorkOrderDetail; canPlan: boolean; canIssue: boolean; onChange: () => Promise<void>; onIssue: (line: PlannedPart) => void; currency: string;
}) {
  const { t } = useTranslation();
  const { can } = useAuth();
  const navigate = useNavigate();
  const { data: stores } = useStores();
  const [editing, setEditing] = useState<PlannedPart | 'new' | null>(null);
  // 0.16.0: what's short becomes a purchase request for this job, needed by its due date.
  const short = w.plannedParts.filter((p) => p.short > 0);
  const requestPurchase = () => navigate('/purchasing/requests/new', { state: { draft: {
    siteId: w.siteId,
    lines: short.map((p) => ({ partId: p.partId, part: { id: p.partId, partNumber: p.partNumber, name: p.partName }, description: p.mainPartName ?? p.partName, unitOfMeasure: p.unitOfMeasure,
      quantity: p.short, storeId: p.storeId ?? null, workOrderId: w.id, workOrder: { id: w.id, number: w.number }, neededBy: w.dueDate ? w.dueDate.slice(0, 10) : null,
      estimatedUnitPrice: null, reason: '' })),
  } } });
  const form = useForm({ initialValues: { partId: null as string | null, storeId: null as string | null, quantity: 1 as number | '', notes: '' } });
  useEffect(() => {
    if (!editing) return;
    form.clearErrors();
    form.setValues(editing === 'new'
      ? { partId: null, storeId: null, quantity: 1, notes: '' }
      : { partId: editing.partId, storeId: editing.storeId ?? null, quantity: editing.quantity, notes: editing.notes ?? '' });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editing]);
  const storeOpts = useMemo(() => (stores ?? []).filter((s) => s.isActive).map((s) => ({ value: s.id, label: `${s.code} · ${s.name}` })), [stores]);
  const submit = form.onSubmit(async (v) => {
    const body = { partId: v.partId, storeId: v.storeId, quantity: v.quantity || 0, notes: v.notes || null };
    try {
      if (editing === 'new') await post(`/work-orders/${w.id}/planned-parts`, body);
      else if (editing) await put(`/work-orders/${w.id}/planned-parts/${editing.id}`, body);
      notifySuccess(t('common.saved'));
      setEditing(null);
      await onChange();
    } catch (e) { handleFormError(form, e); }
  });
  const total = w.plannedParts.reduce((a, p) => a + p.plannedCost, 0);

  return (
    <Card p={0}>
      <Group justify="space-between" p="md" wrap="wrap">
        <Title order={5}>{t('plan.plannedParts')} · {fmtMoney(total, currency)}</Title>
        <Group gap="xs">
          {short.length > 0 && can(P.purchasingRequest) && <Tooltip label={t('pr.requestPurchaseHint')}>
            <Button size="xs" variant="light" color="orange" leftSection={<IconShoppingCartPlus size={14} />} onClick={requestPurchase}>{t('pr.requestPurchase')}</Button></Tooltip>}
          {canPlan && <Button size="xs" variant="default" leftSection={<IconPlus size={14} />} onClick={() => setEditing('new')}>{t('plan.planPart')}</Button>}
        </Group>
      </Group>
      <Table.ScrollContainer minWidth={760}>
        <Table verticalSpacing="sm">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>{t('inv.part')}</Table.Th><Table.Th>{t('inv.store')}</Table.Th><Table.Th ta="right">{t('plan.planned')}</Table.Th><Table.Th ta="right">{t('plan.issued')}</Table.Th>
              <Table.Th ta="right">{t('plan.reserved')}</Table.Th><Table.Th ta="right">{t('plan.short')}</Table.Th><Table.Th ta="right">{t('plan.plannedCost')}</Table.Th><Table.Th w={110} />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {w.plannedParts.length === 0 && <Table.Tr><Table.Td colSpan={8}><Text c="dimmed" ta="center" py="md" fz="sm">{t('plan.noPlanned')}</Text></Table.Td></Table.Tr>}
            {w.plannedParts.map((p) => (
              <Table.Tr key={p.id}>
                <Table.Td>
                  <Anchor component={Link} to={`/inventory/parts/${p.partId}`} fz="sm" fw={600}>{p.partNumber}</Anchor>
                  <Text fz="xs" c="dimmed">{p.partName}{p.notes ? ` · ${p.notes}` : ''}</Text>
                </Table.Td>
                <Table.Td>{p.storeCode ?? <Text fz="sm" c="orange">{t('plan.chooseStore')}</Text>}</Table.Td>
                <Table.Td ta="right">{fmtQty(p.quantity)} {p.unitOfMeasure}</Table.Td>
                <Table.Td ta="right">{fmtQty(p.issued)}</Table.Td>
                <Table.Td ta="right">{fmtQty(p.reserved)}</Table.Td>
                <Table.Td ta="right">{p.short > 0 ? <Text fz="sm" fw={600} c="red">{fmtQty(p.short)}</Text> : '—'}</Table.Td>
                <Table.Td ta="right">{fmtPrice(p.plannedCost, currency)}</Table.Td>
                <Table.Td ta="right">
                  <Group gap={2} justify="flex-end" wrap="nowrap">
                    {canIssue && p.reserved > 0 && (
                      <Tooltip label={t('plan.issueThis')}><ActionIcon variant="subtle" size="sm" aria-label={t('plan.issueThis')} onClick={() => onIssue(p)}><IconPackageExport size={14} /></ActionIcon></Tooltip>
                    )}
                    {canPlan && <ActionIcon variant="subtle" color="gray" size="sm" aria-label={t('common.edit')} onClick={() => setEditing(p)}><IconEdit size={14} /></ActionIcon>}
                    {canPlan && (
                      <ActionIcon variant="subtle" color="gray" size="sm" aria-label={t('common.delete')}
                        onClick={() => confirmDelete(p.partNumber, async () => { try { await del(`/work-orders/${w.id}/planned-parts/${p.id}`); await onChange(); } catch (e) { notifyError(e); } })}>
                        <IconTrash size={14} />
                      </ActionIcon>
                    )}
                  </Group>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      <Modal opened={editing !== null} onClose={() => setEditing(null)} title={editing === 'new' ? t('plan.planPart') : t('plan.editPlanned')}>
        <form onSubmit={submit}>
          <Stack>
            {editing === 'new'
              ? <PartPicker label={t('inv.part')} required value={form.getValues().partId} assetId={w.assetId} onChange={(id) => form.setFieldValue('partId', id)} error={form.errors.partId} />
              : editing && <Text fw={600}>{editing.partNumber} · {editing.partName}</Text>}
            <Select label={t('inv.store')} data={storeOpts} searchable clearable={editing === 'new'} placeholder={t('plan.defaultStore')} {...form.getInputProps('storeId')} />
            <NumberInput label={t('inv.quantity')} required min={0} decimalScale={3} {...form.getInputProps('quantity')} />
            <TextInput label={t('common.notes')} maxLength={500} {...form.getInputProps('notes')} />
            <Group justify="flex-end"><Button variant="default" onClick={() => setEditing(null)}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
          </Stack>
        </form>
      </Modal>
    </Card>
  );
}

/** Hours planned for each assigned technician; planned labour falls back to the estimate when none are set. */
export function PlannedHoursCard({ w, canPlan, onChange, currency }: { w: WorkOrderDetail; canPlan: boolean; onChange: () => Promise<void>; currency: string }) {
  const { t } = useTranslation();
  // Raw input per technician (a half-typed "1." stays as typed); reset only when the saved hours change, so a refetch
  // (window focus, another save on the page) doesn't wipe what someone is typing.
  const [hours, setHours] = useState<Record<string, string | number>>({});
  const [saving, setSaving] = useState(false);
  const saved = w.assignees.map((a) => `${a.employeeId}:${a.plannedHours ?? ''}`).join('|');
  useEffect(() => { setHours(Object.fromEntries(w.assignees.map((a) => [a.employeeId, a.plannedHours ?? '']))); }, [saved]); // eslint-disable-line react-hooks/exhaustive-deps
  if (w.assignees.length === 0) return null;
  const dirty = w.assignees.some((a) => String(a.plannedHours ?? '') !== String(hours[a.employeeId] ?? ''));
  const parse = (v: string | number | undefined) => (v === undefined || v === '' ? null : Number(v));
  const save = async () => {
    const lines = w.assignees.map((a) => ({ employeeId: a.employeeId, plannedHours: parse(hours[a.employeeId]) }));
    if (lines.some((l) => l.plannedHours !== null && Number.isNaN(l.plannedHours))) { notifyError(new Error(t('plan.hoursInvalid'))); return; }
    setSaving(true);
    try {
      await put(`/work-orders/${w.id}/planned-hours`, { lines });
      notifySuccess(t('common.saved'));
      await onChange();
    } catch (e) { notifyError(e); } finally { setSaving(false); }
  };
  return (
    <Card p={0}>
      <Group justify="space-between" p="md" wrap="wrap">
        <div>
          <Title order={5}>{t('plan.plannedLabour')} · {w.planned.laborHours} h · {fmtMoney(w.planned.laborCost, currency)}</Title>
          <Text fz="xs" c="dimmed">{t('plan.hoursHint')}</Text>
        </div>
        {canPlan && <Button size="xs" variant="default" disabled={!dirty} loading={saving} onClick={save}>{t('plan.saveHours')}</Button>}
      </Group>
      <Table verticalSpacing={6}>
        <Table.Tbody>
          {w.assignees.map((a) => (
            <Table.Tr key={a.employeeId}>
              <Table.Td>{a.fullName}</Table.Td>
              <Table.Td w={160}>
                {canPlan
                  ? <NumberInput size="xs" min={0} max={10000} step={0.5} decimalScale={2} suffix=" h" value={hours[a.employeeId] ?? ''} aria-label={t('plan.hours')}
                      onChange={(x) => setHours((h) => ({ ...h, [a.employeeId]: x }))} />
                  : <Text fz="sm" ta="right">{a.plannedHours != null ? hoursText(a.plannedHours) : '—'}</Text>}
              </Table.Td>
              <Table.Td ta="right" c="dimmed" fz="sm">{a.hourlyRate != null ? `${fmtMoney(a.hourlyRate, currency)}/${t('unit.h')}` : '—'}</Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Card>
  );
}

/** Planned against actual cost for labour, parts and the total. Other costs have no planned value. */
export function PlanVsActualCard({ w, currency }: { w: WorkOrderDetail; currency: string }) {
  const { t } = useTranslation();
  const money = (x: number) => fmtMoney(x, currency);
  const rows: { label: string; planned: number | null; actual: number; fmt: (x: number) => string; bold?: boolean }[] = [
    { label: t('plan.laborHours'), planned: w.planned.laborHours, actual: w.totals.laborHours, fmt: (x) => hoursText(fmtQty(x)) },
    { label: t('plan.laborCost'), planned: w.planned.laborCost, actual: w.totals.laborCost, fmt: money },
    { label: t('plan.parts'), planned: w.planned.partsCost, actual: w.totals.partsCost, fmt: money },
    { label: t('plan.other'), planned: null, actual: w.totals.otherCost, fmt: money },
    // Contractors (0.17.0): shown once there is something to show.
    ...(w.planned.contractorCost || w.totals.contractorCost ? [{ label: t('contractors.cost'), planned: w.planned.contractorCost, actual: w.totals.contractorCost, fmt: money }] : []),
    { label: t('plan.total'), planned: w.planned.totalCost, actual: w.totals.totalCost, fmt: money, bold: true },
  ];
  return (
    <Card p={0}>
      <Title order={5} p="md">{t('plan.vsActual')}</Title>
      <Table.ScrollContainer minWidth={420}>
        <Table verticalSpacing={6}>
          <Table.Thead><Table.Tr><Table.Th /><Table.Th ta="right">{t('plan.planned')}</Table.Th><Table.Th ta="right">{t('plan.actual')}</Table.Th><Table.Th ta="right">{t('plan.difference')}</Table.Th></Table.Tr></Table.Thead>
          <Table.Tbody>
            {rows.map((r) => {
              const diff = r.planned === null ? null : r.actual - r.planned;
              return (
                <Table.Tr key={r.label}>
                  <Table.Td fw={r.bold ? 700 : undefined}>{r.label}</Table.Td>
                  <Table.Td ta="right" fw={r.bold ? 700 : undefined}>{r.planned === null ? '—' : r.fmt(r.planned)}</Table.Td>
                  <Table.Td ta="right" fw={r.bold ? 700 : undefined}>{r.fmt(r.actual)}</Table.Td>
                  <Table.Td ta="right" c={diff !== null && diff > 0 ? 'red' : undefined}>{diff === null ? '—' : `${diff > 0 ? '+' : ''}${r.fmt(diff)}`}</Table.Td>
                </Table.Tr>
              );
            })}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
    </Card>
  );
}
