import { useMemo, useState } from 'react';
import { ActionIcon, Alert, Anchor, Badge, Button, Card, Checkbox, Group, Menu, Modal, NumberInput, SegmentedControl, Select, Stack, Table, Text, TextInput, Textarea } from '@mantine/core';
import { useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconDots, IconPlus, IconTags } from '@tabler/icons-react';
import { get, post } from '@/api/client';
import type { PartDetail, UnitList, UnitOutcome, UnitRow, UnitState } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { AssetPicker } from '@/components/AssetPicker';
import { useStores } from '@/components/PartPicker';
import { SerialsBox, serialsError } from '@/components/SerialsBox';
import { fmtDate, fmtPrice, notifyError, notifySuccess } from '@/components/utils';
import { openLabels } from '@/pages/labels/LabelsPage';
import { UNIT_STATES, UNIT_STATE_COLOR, parseSerials } from './units';

export function UnitStateBadge({ state }: { state: UnitState }) {
  const { t } = useTranslation();
  return <Badge color={UNIT_STATE_COLOR[state]} variant="light">{t(`units.state.${state}`)}</Badge>;
}

/** Everything that shows a unit's place or a part's stock, refreshed after a unit moves. */
export async function refreshUnits(qc: QueryClient) {
  await Promise.all(['serial-units', 'serial-unit', 'part', 'parts', 'asset', 'assets', 'fitted-units', 'wo-units', 'stock-tx', 'work-order']
    .map((k) => qc.invalidateQueries({ queryKey: [k] })));
}

export type UnitAction = 'transfer' | 'sendForRepair' | 'backFromRepair' | 'scrap' | 'remove';

/** The actions open to a unit in its state. */
export function actionsFor(state: UnitState): UnitAction[] {
  return state === 'InStore' ? ['transfer', 'sendForRepair', 'scrap'] : state === 'AtRepair' ? ['backFromRepair', 'scrap'] : state === 'Fitted' ? ['remove'] : [];
}

/** One dialog for a unit's moves: transfer, send for repair, back from repair, scrap, and remove from its asset (with or without a work order). */
export function UnitActionModal({ unit, action, workOrderId, defaultStoreId, onClose }: {
  unit: { id: string; serialNumber?: string | null; storeId?: string | null }; action: UnitAction; workOrderId?: string | null; defaultStoreId?: string | null; onClose: () => void;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data: stores } = useStores();
  const [storeId, setStoreId] = useState<string | null>(action === 'transfer' ? null : defaultStoreId ?? null);
  const [outcome, setOutcome] = useState<UnitOutcome>('Store');
  const [cost, setCost] = useState<number | ''>('');
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const needsStore = action === 'transfer' || action === 'backFromRepair' || (action === 'remove' && outcome === 'Store');
  const storeOpts = (stores ?? []).filter((s) => s.isActive && (action !== 'transfer' || s.id !== unit.storeId)).map((s) => ({ value: s.id, label: `${s.code} · ${s.name}` }));
  const submit = async () => {
    setBusy(true);
    try {
      const path = { transfer: 'transfer', sendForRepair: 'send-for-repair', backFromRepair: 'back-from-repair', scrap: 'scrap', remove: 'remove' }[action];
      await post(`/serial-units/${unit.id}/${path}`, action === 'remove'
        ? { workOrderId: workOrderId ?? null, outcome, storeId: outcome === 'Store' ? storeId : null, note: note || null }
        : { storeId: needsStore ? storeId : undefined, cost: action === 'backFromRepair' && cost !== '' ? cost : null, note: note || null });
      notifySuccess(t('common.saved'));
      await refreshUnits(qc);
      onClose();
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Modal opened onClose={onClose} title={`${t(`units.action.${action}`)} · ${unit.serialNumber ?? ''}`}>
      <Stack>
        {action === 'remove' && <SegmentedControl value={outcome} onChange={(v) => setOutcome(v as UnitOutcome)}
          data={(['Store', 'Repair', 'Scrap'] as UnitOutcome[]).map((o) => ({ value: o, label: t(`units.outcomes.${o}`) }))} />}
        {needsStore && <Select label={t('inv.store')} required data={storeOpts} value={storeId} onChange={setStoreId} searchable />}
        {action === 'backFromRepair' && <NumberInput label={t('units.repairCost')} min={0} decimalScale={2} thousandSeparator value={cost} onChange={(v) => setCost(v === '' ? '' : Number(v))}
          description={t('units.repairCostHint')} />}
        <Textarea label={action === 'scrap' ? t('inv.reason') : action === 'remove' ? t('units.condition') : t('common.notes')} required={action === 'scrap'} autosize minRows={2}
          value={note} onChange={(e) => setNote(e.currentTarget.value)} />
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>{t('common.cancel')}</Button>
          <Button loading={busy} onClick={submit} disabled={(needsStore && !storeId) || (action === 'scrap' && !note.trim())}
            color={action === 'scrap' || (action === 'remove' && outcome === 'Scrap') ? 'red' : undefined}>{t(`units.action.${action}`)}</Button>
        </Group>
      </Stack>
    </Modal>
  );
}

/** Switching serial tracking on (a serial for each piece in stock) or off, and the unit life. */
export function SerialTrackingModal({ part, onClose }: { part: PartDetail; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const on = !!part.isSerialTracked;
  const stocked = part.levels.filter((l) => l.quantityOnHand !== 0);
  const [life, setLife] = useState<number | ''>(part.unitLifeMonths ?? '');
  const [serials, setSerials] = useState<Record<string, string>>(() => Object.fromEntries(stocked.map((l) => [l.storeId, ''])));
  const [busy, setBusy] = useState(false);
  const save = async (switchOn: boolean) => {
    if (switchOn && !on) {
      const problem = stocked.map((l) => serialsError(t, serials[l.storeId], l.quantityOnHand)).find((x) => x);
      if (problem) { notifyError(new Error(problem)); return; }
    }
    setBusy(true);
    try {
      await post(`/parts/${part.id}/serial-tracking`, { on: switchOn, unitLifeMonths: life === '' ? null : life,
        stores: switchOn && !on ? stocked.map((l) => ({ storeId: l.storeId, serials: parseSerials(serials[l.storeId]) })) : null });
      notifySuccess(switchOn === on ? t('common.saved') : t(switchOn ? 'units.switchedOn' : 'units.switchedOff'));
      await refreshUnits(qc);
      onClose();
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Modal opened onClose={onClose} title={t('units.serialTracking')} size="lg">
      <Stack>
        <Text fz="sm" c="dimmed">{t('units.serialTrackedHint')}</Text>
        <NumberInput label={t('units.lifeMonths')} description={t('units.lifeHint')} min={1} max={600} value={life} onChange={(v) => setLife(v === '' ? '' : Number(v))} />
        {!on && stocked.length > 0 && <>
          <Text fz="sm">{t('units.switchOnIntro')}</Text>
          {stocked.map((l) => <SerialsBox key={l.storeId} label={t('units.storeHas', { store: l.storeCode, qty: l.quantityOnHand })} needed={l.quantityOnHand}
            value={serials[l.storeId]} onChange={(v) => setSerials((s) => ({ ...s, [l.storeId]: v }))} />)}
          {stocked.some((l) => !Number.isInteger(l.quantityOnHand)) && <Alert color="orange" p="xs">{t('units.wholePieces')}</Alert>}
        </>}
        {on && <Text fz="sm" c="dimmed">{t('units.offIntro')}</Text>}
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>{t('common.cancel')}</Button>
          {on && <Button variant="light" color="red" loading={busy} onClick={() => save(false)}>{t('units.switchOff')}</Button>}
          <Button loading={busy} onClick={() => save(true)}>{on ? t('units.saveLife') : t('units.switchOn')}</Button>
        </Group>
      </Stack>
    </Modal>
  );
}

/** A unit for a piece found in a store, or for one already on a machine at go-live. */
function RegisterUnitModal({ part, kind, onClose }: { part: PartDetail; kind: 'found' | 'fitted'; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data: stores } = useStores();
  const [storeId, setStoreId] = useState<string | null>(part.levels.length === 1 ? part.levels[0].storeId : null);
  const [assetId, setAssetId] = useState<string | null>(null);
  const [serial, setSerial] = useState('');
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const submit = async () => {
    setBusy(true);
    try {
      await post(`/parts/${part.id}/units/${kind}`, kind === 'found' ? { storeId, serial, note: note || null } : { assetId, serial, note: note || null });
      notifySuccess(t('common.saved'));
      await refreshUnits(qc);
      onClose();
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Modal opened onClose={onClose} title={t(kind === 'found' ? 'units.registerFound' : 'units.registerFitted')}>
      <Stack>
        <Text fz="sm" c="dimmed">{t(kind === 'found' ? 'units.registerFoundHint' : 'units.registerFittedHint')}</Text>
        {kind === 'found'
          ? <Select label={t('inv.store')} required data={(stores ?? []).filter((s) => s.isActive).map((s) => ({ value: s.id, label: `${s.code} · ${s.name}` }))} value={storeId} onChange={setStoreId} searchable />
          : <AssetPicker label={t('units.fittedTo')} value={assetId} onChange={setAssetId} />}
        <TextInput label={t('units.serial')} required value={serial} onChange={(e) => setSerial(e.currentTarget.value)} />
        <TextInput label={t('common.notes')} value={note} onChange={(e) => setNote(e.currentTarget.value)} />
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>{t('common.cancel')}</Button>
          <Button loading={busy} onClick={submit} disabled={!serial.trim() || (kind === 'found' ? !storeId : !assetId)}>{t('units.register')}</Button>
        </Group>
      </Stack>
    </Modal>
  );
}

/** The part's units (0.24.0): where each one is, its end of life and repairs, with the actions its state allows. */
export function PartUnits({ part }: { part: PartDetail }) {
  const { t } = useTranslation();
  const { me, can } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const [state, setState] = useState<string>('active');
  const [storeId, setStoreId] = useState<string | null>(null);
  const [selected, setSelected] = useState<string[]>([]);
  const [acting, setActing] = useState<{ unit: UnitRow; action: UnitAction } | null>(null);
  const [register, setRegister] = useState<'found' | 'fitted' | null>(null);
  // Filtered by the server before its 500-row cap (0.26.1).
  const { data, isLoading } = useQuery({
    queryKey: ['serial-units', part.id, state, storeId],
    queryFn: () => get<UnitList>('/serial-units', { partId: part.id, state: state === 'all' ? undefined : state, storeId: storeId ?? undefined }),
  });
  const shown = useMemo(() => data?.items ?? [], [data]);
  const today = new Date().toISOString().slice(0, 10);
  const soon = new Date(Date.now() + 30 * 86_400_000).toISOString().slice(0, 10);
  const transact = can(P.inventoryTransact);
  return (
    <Card p={0}>
      <Group justify="space-between" p="md" gap="sm">
        <Group gap="sm">
          <Select size="xs" w={170} value={state} onChange={(v) => setState(v ?? 'active')} allowDeselect={false} aria-label={t('units.stateFilter')}
            data={[{ value: 'active', label: t('units.notScrapped') }, { value: 'all', label: t('units.all') }, ...UNIT_STATES.map((s) => ({ value: s, label: t(`units.state.${s}`) }))]} />
          <Select size="xs" w={170} value={storeId} onChange={setStoreId} clearable placeholder={t('units.allStores')}
            data={part.levels.map((l) => ({ value: l.storeId, label: l.storeCode }))} aria-label={t('inv.store')} />
        </Group>
        <Group gap="xs">
          <Button size="xs" variant="default" leftSection={<IconTags size={14} />} disabled={selected.length === 0} onClick={() => openLabels('asset', selected)}>
            {t('units.printLabels')}{selected.length > 0 ? ` (${selected.length})` : ''}</Button>
          {transact && <Menu position="bottom-end">
            <Menu.Target><Button size="xs" leftSection={<IconPlus size={14} />}>{t('units.register')}</Button></Menu.Target>
            <Menu.Dropdown>
              <Menu.Item onClick={() => setRegister('found')}>{t('units.registerFound')}</Menu.Item>
              <Menu.Item onClick={() => setRegister('fitted')}>{t('units.registerFitted')}</Menu.Item>
            </Menu.Dropdown>
          </Menu>}
        </Group>
      </Group>
      <Table.ScrollContainer minWidth={760}>
        <Table verticalSpacing="sm" highlightOnHover>
          <Table.Thead><Table.Tr>
            <Table.Th w={36}><Checkbox aria-label={t('units.selectAll')} checked={shown.length > 0 && selected.length === shown.length}
              onChange={(e) => setSelected(e.currentTarget.checked ? shown.map((r) => r.id) : [])} /></Table.Th>
            <Table.Th>{t('units.serial')}</Table.Th><Table.Th>{t('common.status')}</Table.Th><Table.Th>{t('units.where')}</Table.Th>
            <Table.Th>{t('units.endOfLife')}</Table.Th><Table.Th ta="right">{t('units.repairs')}</Table.Th><Table.Th w={48} />
          </Table.Tr></Table.Thead>
          <Table.Tbody>
            {!isLoading && shown.length === 0 && <Table.Tr><Table.Td colSpan={7}><Text c="dimmed" ta="center" py="md">{t('units.none')}</Text></Table.Td></Table.Tr>}
            {shown.map((r) => {
              const ending = r.state !== 'Scrapped' && r.endOfLifeOn && r.endOfLifeOn <= soon;
              return (
                <Table.Tr key={r.id}>
                  <Table.Td><Checkbox aria-label={r.serialNumber ?? r.assetNumber} checked={selected.includes(r.id)}
                    onChange={(e) => setSelected((s) => (e.currentTarget.checked ? [...s, r.id] : s.filter((x) => x !== r.id)))} /></Table.Td>
                  <Table.Td><Anchor component={Link} to={`/assets/${r.id}`} fz="sm" fw={600}>{r.serialNumber}</Anchor><Text fz="xs" c="dimmed">{r.assetNumber}</Text></Table.Td>
                  <Table.Td><UnitStateBadge state={r.state} /></Table.Td>
                  <Table.Td>{r.state === 'Fitted' && r.fittedToId
                    ? <Anchor component={Link} to={`/assets/${r.fittedToId}`} fz="sm">{r.fittedToNumber} · {r.fittedToName}</Anchor>
                    : <Text fz="sm">{r.storeName ?? '—'}</Text>}<Text fz="xs" c="dimmed">{r.siteName}</Text></Table.Td>
                  <Table.Td><Text fz="sm" c={ending ? (r.endOfLifeOn! < today ? 'red' : 'orange') : undefined}>{fmtDate(r.endOfLifeOn)}</Text></Table.Td>
                  <Table.Td ta="right">{r.repairCount > 0 ? <Text fz="sm">{r.repairCount} · {fmtPrice(r.repairCost, currency)}</Text> : '—'}</Table.Td>
                  <Table.Td>{transact && actionsFor(r.state).length > 0 && <Menu position="bottom-end">
                    <Menu.Target><ActionIcon variant="subtle" aria-label={t('common.actions')}><IconDots size={16} /></ActionIcon></Menu.Target>
                    <Menu.Dropdown>{actionsFor(r.state).map((a) =>
                      <Menu.Item key={a} color={a === 'scrap' ? 'red' : undefined} onClick={() => setActing({ unit: r, action: a })}>{t(`units.action.${a}`)}</Menu.Item>)}</Menu.Dropdown>
                  </Menu>}</Table.Td>
                </Table.Tr>
              );
            })}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      {data?.capped && <Text fz="xs" c="dimmed" p="md">{t('units.capped')}</Text>}
      {acting && <UnitActionModal unit={acting.unit} action={acting.action} onClose={() => setActing(null)} />}
      {register && <RegisterUnitModal part={part} kind={register} onClose={() => setRegister(null)} />}
    </Card>
  );
}
