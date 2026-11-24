import { useMemo, useState } from 'react';
import { Button, Card, Group, Modal, SegmentedControl, Select, Stack, Text, TextInput, Textarea, Title } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { IconPlus, IconScan } from '@tabler/icons-react';
import { notifyError, notifySuccess } from '@/components/utils';
import { unitsWithQueued } from './overlay';
import { parseCode } from './scan';
import { useTech } from './useTech';
import type { StockUnit, TechJob, TechUnit } from './types';

type Outcome = 'Store' | 'Repair' | 'Scrap';

/** A scanned label (asset link) or typed serial or asset number, among the units the job can take. */
export function findUnit(units: StockUnit[], text: string): StockUnit | undefined {
  const scanned = parseCode(text);
  if (scanned.kind === 'asset') return units.find((u) => u.id === scanned.id);
  const c = text.trim().toLowerCase();
  return c ? units.find((u) => u.serialNumber?.toLowerCase() === c) ?? units.find((u) => u.assetNumber.toLowerCase() === c) : undefined;
}

/** The job's serial units (0.24.0): those fitted to its asset(s), with Remove, and Fit a unit from a store at the job's site — offline too. */
export function JobUnits({ job }: { job: TechJob }) {
  const { t } = useTranslation();
  const { stock, bundle, queue, sent, add } = useTech();
  const canAct = bundle?.can?.issueStock ?? true;
  const assets = useMemo(() => [...(job.asset ? [job.asset] : []), ...job.roundAssets.filter((a) => a.id !== job.asset?.id)], [job.asset, job.roundAssets]);
  const siteStores = new Set((stock?.stores ?? []).filter((s) => s.siteId === job.siteId).map((s) => s.id));
  const spares = unitsWithQueued(stock?.units ?? [], [...sent, ...queue], bundle?.jobs ?? []).filter((u) => siteStores.has(u.storeId));
  const partName = (id: string) => stock?.parts.find((p) => p.id === id)?.name ?? '';
  const storeCode = (id: string) => stock?.stores.find((s) => s.id === id)?.code ?? '';
  const [fitting, setFitting] = useState(false);
  const [code, setCode] = useState('');
  const [unitId, setUnitId] = useState<string | null>(null);
  const [assetId, setAssetId] = useState<string | null>(null);
  const [removing, setRemoving] = useState<TechUnit | null>(null);
  const [outcome, setOutcome] = useState<Outcome>('Store');
  const [note, setNote] = useState('');
  if (assets.length === 0 || (!stock?.units && (job.units ?? []).length === 0)) return null;
  const send = async (type: string, payload: unknown) => {
    try { await add({ type, workOrderId: job.id, payload }); notifySuccess(t('techStock.saved')); } catch (e) { notifyError(e); }
  };
  const scanned = code.trim() ? findUnit(spares, code) : undefined;
  const chosen = spares.find((u) => u.id === (scanned?.id ?? unitId));
  const target = assetId ?? (assets.length === 1 ? assets[0].id : null);
  const fit = () => {
    if (!chosen || !target) return;
    void send('unit.fit', { unitId: chosen.id, assetId: target, serialNumber: chosen.serialNumber, assetNumber: chosen.assetNumber, partId: chosen.partId, partName: partName(chosen.partId) });
    setFitting(false); setCode(''); setUnitId(null); setAssetId(null);
  };
  const remove = () => {
    if (!removing) return;
    void send('unit.remove', { unitId: removing.id, outcome, note: note.trim() || null });
    setRemoving(null); setNote(''); setOutcome('Store');
  };
  return (
    <Card><Stack gap="xs">
      <Group justify="space-between"><Title order={6}>{t('units.tab')}</Title>
        {canAct && <Button size="xs" variant="light" leftSection={<IconPlus size={14} />} onClick={() => setFitting(true)} disabled={!stock}>{t('units.fit')}</Button>}</Group>
      {(job.units ?? []).length === 0 && <Text fz="sm" c="dimmed">{t('units.noneFitted')}</Text>}
      {(job.units ?? []).map((u) => (
        <Group key={u.id} justify="space-between" wrap="nowrap">
          <div style={{ minWidth: 0 }}>
            <Text fz="sm" fw={500} truncate>{u.serialNumber} · {u.partName ?? partName(u.partId)}</Text>
            <Text fz="xs" c="dimmed">{assets.find((a) => a.id === u.fittedToId)?.assetNumber ?? ''}</Text>
          </div>
          {canAct && <Button size="xs" variant="subtle" onClick={() => setRemoving(u)}>{t('units.action.remove')}</Button>}
        </Group>
      ))}
      <Modal opened={fitting} onClose={() => setFitting(false)} title={t('units.fit')}>
        <Stack>
          <TextInput leftSection={<IconScan size={16} />} label={t('techUnits.scan')} placeholder={t('techUnits.scanHint')} value={code} onChange={(e) => setCode(e.currentTarget.value)} data-autofocus />
          {code.trim() && !scanned && <Text fz="sm" c="orange">{t('techUnits.notHere')}</Text>}
          {!code.trim() && <Select label={t('techUnits.orPick')} searchable value={unitId} onChange={setUnitId} nothingFoundMessage={t('units.noneInStore')}
            data={spares.map((u) => ({ value: u.id, label: `${u.serialNumber} · ${partName(u.partId)} · ${storeCode(u.storeId)}` }))} />}
          {chosen && <Text fz="sm" fw={600}>{chosen.serialNumber} · {partName(chosen.partId)}</Text>}
          {assets.length > 1 && <Select label={t('units.fitTo')} value={assetId} onChange={setAssetId} data={assets.map((a) => ({ value: a.id, label: `${a.assetNumber} · ${a.name}` }))} />}
          <Button disabled={!chosen || !target} onClick={fit}>{t('units.fit')}</Button>
        </Stack>
      </Modal>
      <Modal opened={!!removing} onClose={() => setRemoving(null)} title={`${t('units.action.remove')} · ${removing?.serialNumber ?? ''}`}>
        <Stack>
          <SegmentedControl value={outcome} onChange={(v) => setOutcome(v as Outcome)} data={(['Store', 'Repair', 'Scrap'] as Outcome[]).map((o) => ({ value: o, label: t(`units.outcomes.${o}`) }))} />
          {outcome === 'Store' && <Text fz="xs" c="dimmed">{job.defaultStoreId ? t('techUnits.toDefault', { store: storeCode(job.defaultStoreId) }) : t('techUnits.noDefault')}</Text>}
          <Textarea label={t('units.condition')} autosize minRows={2} value={note} onChange={(e) => setNote(e.currentTarget.value)} />
          <Button color={outcome === 'Scrap' ? 'red' : undefined} disabled={outcome === 'Store' && !job.defaultStoreId} onClick={remove}>{t('units.action.remove')}</Button>
        </Stack>
      </Modal>
    </Stack></Card>
  );
}
