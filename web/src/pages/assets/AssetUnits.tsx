import { useState } from 'react';
import { Anchor, Button, Card, Group, Modal, Select, SimpleGrid, Stack, Table, Text } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { get, post } from '@/api/client';
import type { AssetDetail, FittedUnits, Paged, PartListItem, UnitDetail } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { fmtDate, fmtDateTime, fmtPrice, notifyError, notifySuccess } from '@/components/utils';
import { UnitActionModal, UnitStateBadge, actionsFor, refreshUnits, type UnitAction } from '@/pages/inventory/PartUnits';

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <Stack gap={0}><Text fz="xs" c="dimmed" tt="uppercase" fw={600}>{label}</Text><Text fz="sm" component="div">{children ?? '—'}</Text></Stack>;
}

/** A unit's card (0.24.0): its part, where it is, end of life, repairs and every move it made. */
export function UnitCard({ a }: { a: AssetDetail }) {
  const { t } = useTranslation();
  const { me, can } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const [acting, setActing] = useState<UnitAction | null>(null);
  const { data: u } = useQuery({ queryKey: ['serial-unit', a.id], queryFn: () => get<UnitDetail>(`/serial-units/${a.id}`), enabled: !!a.partId });
  if (!a.partId || !u) return null;
  return (
    <Card>
      <Group justify="space-between" mb="md">
        <Group gap="xs"><Text fw={600}>{t('units.unitCard')}</Text><UnitStateBadge state={u.state} /></Group>
        {can(P.inventoryTransact) && <Group gap="xs">{actionsFor(u.state).map((x) =>
          <Button key={x} size="compact-sm" variant={x === 'scrap' ? 'light' : 'default'} color={x === 'scrap' ? 'red' : undefined} onClick={() => setActing(x)}>{t(`units.action.${x}`)}</Button>)}</Group>}
      </Group>
      <SimpleGrid cols={{ base: 2, sm: 3 }} spacing="md" verticalSpacing="md">
        <Field label={t('inv.part')}><Anchor component={Link} to={`/inventory/parts/${u.partId}`} fz="sm">{u.partNumber} · {u.partName}</Anchor></Field>
        <Field label={t('units.serial')}>{u.serialNumber}</Field>
        <Field label={t('units.where')}>{u.state === 'Fitted' && u.fittedToId
          ? <Anchor component={Link} to={`/assets/${u.fittedToId}`} fz="sm">{u.fittedToNumber} · {u.fittedToName}</Anchor> : u.storeName}</Field>
        <Field label={t('units.endOfLife')}>{u.endOfLifeOn ? fmtDate(u.endOfLifeOn) : null}</Field>
        <Field label={t('units.repairs')}>{u.repairCount > 0 ? `${u.repairCount} · ${fmtPrice(u.repairCost, currency)}` : '0'}</Field>
      </SimpleGrid>
      <Text fw={600} fz="sm" mt="lg" mb="xs">{t('units.history')}</Text>
      <Table.ScrollContainer minWidth={560}>
        <Table verticalSpacing={6}>
          <Table.Tbody>
            {u.movements.map((m, i) => (
              <Table.Tr key={i}>
                <Table.Td w={150}><Text fz="xs" c="dimmed">{fmtDateTime(m.at)}</Text></Table.Td>
                <Table.Td><Text fz="sm" fw={500}>{t(`units.moves.${m.kind}`)}{m.outcome && m.kind === 'Removed' ? ` · ${t(`units.outcomes.${m.outcome}`)}` : ''}</Text>
                  <Text fz="xs" c="dimmed">
                    {m.assetId && <Anchor component={Link} to={`/assets/${m.assetId}`} fz="xs">{m.assetNumber} · {m.assetName}</Anchor>}
                    {m.storeName && <>{m.assetId ? ' · ' : ''}{m.storeName}</>}
                    {m.workOrderId && <> · <Anchor component={Link} to={`/work-orders/${m.workOrderId}`} fz="xs">{m.workOrderNumber}</Anchor></>}
                    {m.purchaseOrderId && <> · <Anchor component={Link} to={`/purchasing/orders/${m.purchaseOrderId}`} fz="xs">{m.purchaseOrderNumber}</Anchor></>}
                    {m.cost != null && <> · {fmtPrice(m.cost, currency)}</>}
                  </Text>
                  {m.note && <Text fz="xs">{m.note}</Text>}</Table.Td>
                <Table.Td ta="end"><Text fz="xs" c="dimmed">{m.byName}</Text></Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      {acting && <UnitActionModal unit={{ id: u.id, serialNumber: u.serialNumber, storeId: u.storeId }} action={acting} onClose={() => setActing(null)} />}
    </Card>
  );
}

/** The units on this asset now (since when) and before (from, to, what happened when taken off). Hidden when it never had any. */
export function FittedUnitsCard({ assetId }: { assetId: string }) {
  const { t } = useTranslation();
  const { data } = useQuery({ queryKey: ['fitted-units', assetId], queryFn: () => get<FittedUnits>(`/assets/${assetId}/fitted-units`) });
  if (!data || (data.now.length === 0 && data.before.length === 0)) return null;
  return (
    <Card>
      <Text fw={600} mb="xs">{t('units.fittedUnits')}</Text>
      {data.now.length > 0 && <Stack gap={4} mb="sm">{data.now.map((u) => (
        <Group key={u.unitId} gap="xs"><Anchor component={Link} to={`/assets/${u.unitId}`} fz="sm" fw={600}>{u.serialNumber}</Anchor>
          <Text fz="sm">{u.partName}</Text><Text fz="xs" c="dimmed">{t('units.sinceDate', { date: fmtDate(u.since) })}</Text></Group>))}</Stack>}
      {data.before.length > 0 && <>
        <Text fz="xs" c="dimmed" tt="uppercase" fw={600} mb={4}>{t('units.before')}</Text>
        <Stack gap={4}>{data.before.map((u, i) => (
          <Group key={i} gap="xs"><Anchor component={Link} to={`/assets/${u.unitId}`} fz="sm">{u.serialNumber}</Anchor><Text fz="sm">{u.partName}</Text>
            <Text fz="xs" c="dimmed">{fmtDate(u.from)} – {fmtDate(u.to)}{u.outcome ? ` · ${t(`units.outcomes.${u.outcome}`)}` : ''}</Text></Group>))}</Stack>
      </>}
    </Card>
  );
}

/** Links an asset fitted to another, with a serial number, to a serial-tracked part (no stock moves). */
export function MakeUnitModal({ a, onClose }: { a: AssetDetail; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data: parts } = useQuery({ queryKey: ['parts', 'serial'], queryFn: () => get<Paged<PartListItem>>('/parts', { pageSize: 500, activeOnly: true }) });
  const [partId, setPartId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const submit = async () => {
    setBusy(true);
    try {
      await post(`/assets/${a.id}/make-unit`, { partId });
      notifySuccess(t('common.saved'));
      await refreshUnits(qc);
      onClose();
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Modal opened onClose={onClose} title={t('units.makeUnit')}>
      <Stack>
        <Text fz="sm" c="dimmed">{t('units.makeUnitHint', { serial: a.serialNumber ?? '' })}</Text>
        <Select label={t('inv.part')} required searchable value={partId} onChange={setPartId}
          data={(parts?.items ?? []).filter((p) => p.isSerialTracked).map((p) => ({ value: p.id, label: `${p.partNumber} · ${p.name}` }))} />
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>{t('common.cancel')}</Button>
          <Button loading={busy} onClick={submit} disabled={!partId}>{t('units.makeUnit')}</Button>
        </Group>
      </Stack>
    </Modal>
  );
}
