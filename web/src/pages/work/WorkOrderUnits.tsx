import { useEffect, useMemo, useState } from 'react';
import { Anchor, Button, Card, Checkbox, Group, Modal, Select, Stack, Table, Text } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconPlus } from '@tabler/icons-react';
import { get, post } from '@/api/client';
import type { MeterItem, SiteSettings, UnitRow, WoUnit, WorkOrderDetail } from '@/api/types';
import { fmtDate, notifyError, notifySuccess } from '@/components/utils';
import { UnitActionModal, refreshUnits } from '@/pages/inventory/PartUnits';
import { defaultHoursMeter } from '@/pages/inventory/units';

type AssetRef = { id: string; assetNumber: string; name: string };

/** Fit a unit from a store at the job's site onto one of its assets; its running hours follow the asset's hours meter. */
function FitUnitModal({ w, assets, onClose }: { w: WorkOrderDetail; assets: AssetRef[]; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data: spares } = useQuery({ queryKey: ['serial-units', 'to-fit', w.siteId], queryFn: () => get<UnitRow[]>('/serial-units/to-fit', { siteId: w.siteId }) });
  const [partId, setPartId] = useState<string | null>(null);
  const [unitId, setUnitId] = useState<string | null>(null);
  const [assetId, setAssetId] = useState<string | null>(assets.length === 1 ? assets[0].id : null);
  const [countHours, setCountHours] = useState(true);
  const [meterId, setMeterId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const { data: meters } = useQuery({ queryKey: ['meters', assetId ?? 'none'], queryFn: () => get<MeterItem[]>('/meters', { assetId: assetId ?? undefined }), enabled: !!assetId });
  useEffect(() => setMeterId(meters ? defaultHoursMeter(meters) : null), [meters]);
  const parts = useMemo(() => {
    const seen = new Map<string, string>();
    for (const u of spares ?? []) if (!seen.has(u.partId)) seen.set(u.partId, `${u.partNumber ?? ''} · ${u.partName ?? ''}`);
    return [...seen].map(([value, label]) => ({ value, label }));
  }, [spares]);
  const units = (spares ?? []).filter((u) => u.partId === partId).map((u) => ({ value: u.id, label: `${u.serialNumber} · ${u.storeName ?? ''}` }));
  const submit = async () => {
    setBusy(true);
    try {
      await post(`/work-orders/${w.id}/units/fit`, { unitId, assetId, countHours: countHours && !!meterId, hoursMeterId: countHours ? meterId : null });
      notifySuccess(t('units.fitted'));
      await refreshUnits(qc);
      onClose();
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Modal opened onClose={onClose} title={t('units.fit')}>
      <Stack>
        {spares && spares.length === 0 && <Text fz="sm" c="dimmed">{t('units.noneInStore')}</Text>}
        <Select label={t('inv.part')} data={parts} value={partId} onChange={(v) => { setPartId(v); setUnitId(null); }} searchable />
        <Select label={t('units.unit')} data={units} value={unitId} onChange={setUnitId} searchable disabled={!partId} />
        <Select label={t('units.fitTo')} data={assets.map((a) => ({ value: a.id, label: `${a.assetNumber} · ${a.name}` }))} value={assetId} onChange={setAssetId} />
        <Checkbox label={t('units.countHours')} checked={countHours} onChange={(e) => setCountHours(e.currentTarget.checked)} />
        {countHours && assetId && ((meters ?? []).length > 0
          ? <Select label={t('units.hoursFrom')} data={(meters ?? []).filter((m) => m.isActive).map((m) => ({ value: m.id, label: `${m.code} · ${m.name} (${m.unit})` }))}
              value={meterId} onChange={setMeterId} clearable description={meterId ? undefined : t('units.noHoursMeter')} />
          : <Text fz="xs" c="dimmed">{t('units.noHoursMeter')}</Text>)}
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>{t('common.cancel')}</Button>
          <Button loading={busy} onClick={submit} disabled={!unitId || !assetId}>{t('units.fit')}</Button>
        </Group>
      </Stack>
    </Modal>
  );
}

/** The units fitted to the work order's asset(s), with Remove, and Fit a unit (0.24.0). */
export function WorkOrderUnits({ w, canAct }: { w: WorkOrderDetail; canAct: boolean }) {
  const { t } = useTranslation();
  const assets: AssetRef[] = useMemo(() => [...(w.asset ? [w.asset] : []), ...(w.assets ?? []).filter((a) => a.id !== w.asset?.id)], [w.asset, w.assets]);
  const { data: units } = useQuery({ queryKey: ['wo-units', w.id], queryFn: () => get<WoUnit[]>(`/work-orders/${w.id}/units`), enabled: assets.length > 0 });
  // Back to a store: the job's site's default store first (the person can choose another).
  const { data: site } = useQuery({ queryKey: ['site', w.siteId], queryFn: () => get<{ settings?: SiteSettings }>(`/sites/${w.siteId}`), retry: false, enabled: canAct });
  const [fitting, setFitting] = useState(false);
  const [removing, setRemoving] = useState<WoUnit | null>(null);
  if (assets.length === 0) return null;
  const assetName = (id: string) => assets.find((a) => a.id === id)?.assetNumber ?? '';
  return (
    <Card p={0}>
      <Group justify="space-between" p="md" pb="xs">
        <Text fw={600}>{t('units.tab')}</Text>
        {canAct && <Button size="xs" variant="light" leftSection={<IconPlus size={14} />} onClick={() => setFitting(true)}>{t('units.fit')}</Button>}
      </Group>
      {(units ?? []).length === 0
        ? <Text fz="sm" c="dimmed" px="md" pb="md">{t('units.noneFitted')}</Text>
        : <Table.ScrollContainer minWidth={560}>
            <Table verticalSpacing="xs">
              <Table.Thead><Table.Tr><Table.Th>{t('units.serial')}</Table.Th><Table.Th>{t('inv.part')}</Table.Th><Table.Th>{t('units.fittedTo')}</Table.Th>
                <Table.Th>{t('units.since')}</Table.Th><Table.Th w={100} /></Table.Tr></Table.Thead>
              <Table.Tbody>
                {(units ?? []).map((u) => (
                  <Table.Tr key={u.id}>
                    <Table.Td><Anchor component={Link} to={`/assets/${u.id}`} fz="sm" fw={600}>{u.serialNumber}</Anchor><Text fz="xs" c="dimmed">{u.assetNumber}</Text></Table.Td>
                    <Table.Td><Text fz="sm">{u.partNumber}</Text><Text fz="xs" c="dimmed">{u.partName}</Text></Table.Td>
                    <Table.Td>{assetName(u.fittedToId)}</Table.Td>
                    <Table.Td>{fmtDate(u.since)}</Table.Td>
                    <Table.Td ta="end">{canAct && <Button size="compact-xs" variant="default" onClick={() => setRemoving(u)}>{t('units.action.remove')}</Button>}</Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>}
      {fitting && <FitUnitModal w={w} assets={assets} onClose={() => setFitting(false)} />}
      {removing && <UnitActionModal unit={removing} action="remove" workOrderId={w.id} defaultStoreId={site?.settings?.defaultStoreId} onClose={() => setRemoving(null)} />}
    </Card>
  );
}
