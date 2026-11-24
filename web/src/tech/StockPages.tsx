import { useMemo, useState } from 'react';
import { Alert, Anchor, Badge, Button, Card, Group, Image, Modal, NumberInput, Select, Stack, Text, TextInput, Title } from '@mantine/core';
import { useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconChevronLeft, IconPackage, IconPlus, IconSearch } from '@tabler/icons-react';
import { fmtQty, notifyError, notifySuccess } from '@/components/utils';
import { available, canUseByQuantity, findPart, queuedCount, searchParts, shortBy, stillReserved } from './stock';
import { useTech } from './useTech';
import type { PlannedLine, StockPart, TechJob } from './types';

const Back = ({ to, label }: { to: string; label: string }) => {
  const navigate = useNavigate();
  return <Anchor fz="sm" onClick={() => navigate(to)}><Group gap={2}><IconChevronLeft size={14} className="mirror-rtl" />{label}</Group></Anchor>;
};

/** Search the phone's copy of parts (also typed or scanned codes). */
function PartSearch({ onPick, autoFocus }: { onPick: (p: StockPart) => void; autoFocus?: boolean }) {
  const { t } = useTranslation();
  const { stock } = useTech();
  const [text, setText] = useState('');
  const exact = findPart(stock, text);
  const found = exact ? [exact] : searchParts(stock, text);
  return (
    <Stack gap="xs">
      <TextInput leftSection={<IconSearch size={16} />} placeholder={t('techStock.searchParts')} value={text} onChange={(e) => setText(e.currentTarget.value)} data-autofocus={autoFocus} />
      {!stock && <Text fz="sm" c="dimmed">{t('techStock.noCopy')}</Text>}
      {text.trim() && found.length === 0 && stock && <Text fz="sm" c="dimmed">{t('techStock.noMatch')}</Text>}
      {found.map((p) => (
        <Card key={p.id} withBorder p="xs" onClick={() => onPick(p)} style={{ cursor: 'pointer' }}>
          <Text fz="sm" fw={600}>{p.partNumber}</Text><Text fz="xs" c="dimmed">{p.name}</Text>
        </Card>
      ))}
    </Stack>
  );
}

/** Choose a store and a quantity for a part, warning when the store's copy can't cover it. */
function QuantityForm({ part, storeId, ownReserved = 0, initial, label, onSave, allowAnyStore = true }: {
  part: StockPart; storeId?: string | null; ownReserved?: number; initial?: number; label: string; onSave: (storeId: string, qty: number) => void; allowAnyStore?: boolean;
}) {
  const { t } = useTranslation();
  const { stock } = useTech();
  const [store, setStore] = useState<string | null>(storeId ?? part.levels[0]?.storeId ?? null);
  const [qty, setQty] = useState<number | ''>(initial ?? 1);
  const stores = (stock?.stores ?? []).filter((s) => allowAnyStore || s.id === storeId);
  const short = store && qty !== '' ? shortBy(part, store, Number(qty), store === storeId ? ownReserved : 0) : 0;
  return (
    <Stack>
      <Text fw={600}>{part.partNumber} · {part.name}</Text>
      <Select label={t('techStock.store')} data={stores.map((s) => ({ value: s.id, label: `${s.code} · ${t('techStock.inStock', { qty: fmtQty(available(part, s.id, s.id === storeId ? ownReserved : 0)) })}` }))}
        value={store} onChange={setStore} />
      <NumberInput label={`${t('techStock.quantity')} (${part.unitOfMeasure})`} min={0.001} decimalScale={3} value={qty} onChange={(v) => setQty(v === '' ? '' : Number(v))} />
      {short > 0 && <Alert color="orange">{t('techStock.shortWarning', { qty: fmtQty(short) })}</Alert>}
      <Button disabled={!store || qty === '' || Number(qty) <= 0} onClick={() => onSave(store!, Number(qty))}>{label}</Button>
    </Stack>
  );
}

/** A job's parts (0.11.0): planned lines to mark used (or give back), and unplanned parts added from any store. */
export function JobParts({ job }: { job: TechJob }) {
  const { t } = useTranslation();
  const { stock, bundle, add } = useTech();
  const canIssue = bundle?.can?.issueStock ?? true;
  const [using, setUsing] = useState<PlannedLine | null>(null);
  const [returning, setReturning] = useState<PlannedLine | null>(null);
  const [adding, setAdding] = useState(false);
  const [picked, setPicked] = useState<StockPart | null>(null);
  const partOf = (id: string) => stock?.parts.find((p) => p.id === id);
  const lines = job.plannedLines ?? [];
  const send = async (type: string, payload: unknown) => {
    try { await add({ type, workOrderId: job.id, payload }); notifySuccess(t('techStock.saved')); } catch (e) { notifyError(e); }
  };
  if (lines.length === 0 && job.plannedParts.length > 0 && !stock) {
    return <Card><Stack gap={4}><Title order={6}>{t('tech.plannedParts')}</Title>
      {job.plannedParts.map((p) => <Text key={p.partNumber} fz="sm">{fmtQty(p.quantity)} {p.unitOfMeasure} · {p.partNumber} {p.partName}</Text>)}</Stack></Card>;
  }
  return (
    <Card><Stack gap="xs">
      <Group justify="space-between"><Title order={6}>{t('techStock.parts')}</Title>
        <Button size="xs" variant="light" leftSection={<IconPlus size={14} />} onClick={() => { setPicked(null); setAdding(true); }} disabled={!stock}>{t('techStock.addPart')}</Button></Group>
      {lines.map((l) => {
        const p = partOf(l.partId);
        const left = stillReserved(l);
        return (
          <Group key={l.id} justify="space-between" wrap="nowrap">
            <div style={{ minWidth: 0 }}>
              <Text fz="sm" fw={500} truncate>{p ? `${p.partNumber} · ${p.name}` : l.partId}</Text>
              <Text fz="xs" c="dimmed">{t('techStock.usedOf', { used: fmtQty(l.issued), planned: fmtQty(l.quantity), unit: p?.unitOfMeasure ?? '' })}</Text>
            </div>
            <Group gap={4} wrap="nowrap">
              {p && !canUseByQuantity(p) && <Text fz="xs" c="dimmed">{t('techStock.fitInstead')}</Text>}
              {left > 0 && p && canUseByQuantity(p) && <Button size="xs" onClick={() => setUsing(l)}>{t('techStock.markUsed')}</Button>}
              {l.issued > 0 && p && canIssue && canUseByQuantity(p) && <Button size="xs" variant="subtle" onClick={() => setReturning(l)}>{t('techStock.giveBack')}</Button>}
            </Group>
          </Group>
        );
      })}
      {(job.usedParts ?? []).map((u) => {
        const p = partOf(u.partId);
        return (
          <Group key={`used-${u.partId}-${u.storeId}`} justify="space-between" wrap="nowrap">
            <div style={{ minWidth: 0 }}>
              <Text fz="sm" fw={500} truncate>{p ? `${p.partNumber} · ${p.name}` : u.partId}</Text>
              <Text fz="xs" c="dimmed">{t('techStock.used', { qty: fmtQty(u.issued), unit: p?.unitOfMeasure ?? '' })}</Text>
            </div>
            {p && canIssue && canUseByQuantity(p) && <Button size="xs" variant="subtle" onClick={() => setReturning({ id: `used-${u.partId}-${u.storeId}`, partId: u.partId, storeId: u.storeId, quantity: 0, issued: u.issued })}>{t('techStock.giveBack')}</Button>}
          </Group>
        );
      })}
      {lines.length === 0 && (job.usedParts ?? []).length === 0 && <Text fz="sm" c="dimmed">{t('techStock.noParts')}</Text>}
      <Modal opened={!!using} onClose={() => setUsing(null)} title={t('techStock.markUsed')}>
        {using && partOf(using.partId) && (
          <QuantityForm part={partOf(using.partId)!} storeId={using.storeId ?? job.defaultStoreId} ownReserved={stillReserved(using)} initial={stillReserved(using)} label={t('techStock.markUsed')}
            onSave={(storeId, quantity) => { void send('part.use', { partId: using.partId, storeId, quantity, lineId: using.id }); setUsing(null); }} />
        )}
      </Modal>
      <Modal opened={!!returning} onClose={() => setReturning(null)} title={t('techStock.giveBack')}>
        {returning && partOf(returning.partId) && (
          <QuantityForm part={partOf(returning.partId)!} storeId={returning.storeId ?? job.defaultStoreId} initial={1} label={t('techStock.giveBack')} allowAnyStore={false}
            onSave={(storeId, quantity) => { void send('part.return', { partId: returning.partId, storeId, quantity }); setReturning(null); }} />
        )}
      </Modal>
      <Modal opened={adding} onClose={() => setAdding(false)} title={t('techStock.addPart')}>
        {!picked ? <PartSearch onPick={setPicked} autoFocus /> : !canUseByQuantity(picked) ? <Text fz="sm" c="orange">{t('techStock.fitInstead')}</Text> : (
          <QuantityForm part={picked} storeId={job.defaultStoreId} label={t('techStock.use')}
            onSave={(storeId, quantity) => { void send('part.use', { partId: picked.id, storeId, quantity }); setAdding(false); }} />
        )}
      </Modal>
    </Stack></Card>
  );
}

/** Stock: search or scan a part; counts and deliveries (0.11.0). */
export function StockSearchPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { stock } = useTech();
  return (
    <Stack>
      <Back to="/tech" label={t('tech.myJobs')} />
      <Title order={4}>{t('techStock.stock')}</Title>
      <PartSearch onPick={(p) => navigate(`/tech/parts/${p.id}`)} />
      {(stock?.counts.length ?? 0) > 0 && (
        <Card><Stack gap="xs"><Title order={6}>{t('techStock.openCounts')}</Title>
          {stock!.counts.map((c) => <Anchor key={c.id} onClick={() => navigate(`/tech/counts/${c.id}`)}>{c.name} · {stock!.stores.find((s) => s.id === c.storeId)?.code}</Anchor>)}
        </Stack></Card>
      )}
      {stock?.purchaseOrders && <Button variant="light" leftSection={<IconPackage size={16} />} onClick={() => navigate('/tech/receive')}>{t('techStock.receive')}</Button>}
    </Stack>
  );
}

/** A part: photo, stock and bin in each store, and what the person may do with it. */
export function PartPage() {
  const { t } = useTranslation();
  const { id = '' } = useParams();
  const { stock, bundle, online, add } = useTech();
  const [issuing, setIssuing] = useState(false);
  const [job, setJob] = useState<string | null>(null);
  const part = stock?.parts.find((p) => p.id === id);
  if (!part) return <Stack><Back to="/tech/stock" label={t('techStock.stock')} /><Text c="dimmed">{t('techStock.partGone')}</Text></Stack>;
  const jobs = bundle?.jobs ?? [];
  return (
    <Stack>
      <Back to="/tech/stock" label={t('techStock.stock')} />
      <Title order={4}>{part.partNumber}</Title>
      <Text>{part.name}</Text>
      {part.photoId && online && <Image src={`/api/attachments/${part.photoId}/content`} alt={part.name} radius="sm" mah={220} fit="contain" />}
      <Card><Stack gap={4}>
        {part.levels.map((l) => {
          const s = stock?.stores.find((x) => x.id === l.storeId);
          return <Group key={l.storeId} justify="space-between"><Text fz="sm">{s?.code ?? ''}{l.bin ? ` · ${t('techStock.bin')} ${l.bin}` : ''}</Text>
            <Text fz="sm" fw={600}>{fmtQty(l.onHand)} {part.unitOfMeasure}{l.reserved > 0 ? ` (${t('techStock.reserved', { qty: fmtQty(l.reserved) })})` : ''}</Text></Group>;
        })}
      </Stack></Card>
      {jobs.length > 0 && canUseByQuantity(part) && <Button onClick={() => setIssuing(true)}>{t('techStock.issueToJob')}</Button>}
      {!canUseByQuantity(part) && <Text fz="sm" c="dimmed">{t('techStock.fitInstead')}</Text>}
      <Modal opened={issuing} onClose={() => setIssuing(false)} title={t('techStock.issueToJob')}>
        {!job ? (
          <Stack gap="xs">{jobs.map((j) => <Card key={j.id} withBorder p="xs" onClick={() => setJob(j.id)} style={{ cursor: 'pointer' }}><Text fz="sm" fw={600}>{j.number}</Text><Text fz="xs" c="dimmed">{j.title}</Text></Card>)}</Stack>
        ) : (
          <QuantityForm part={part} storeId={jobs.find((j) => j.id === job)?.defaultStoreId} label={t('techStock.issue')}
            onSave={async (storeId, quantity) => {
              try { await add({ type: 'stock.issue', workOrderId: job, payload: { partId: part.id, storeId, quantity, workOrderId: job } }); notifySuccess(t('techStock.saved')); } catch (e) { notifyError(e); }
              setIssuing(false); setJob(null);
            }} />
        )}
      </Modal>
    </Stack>
  );
}

/** Counting a store: scan or search each part and enter the quantity counted (offline too). */
export function CountPage() {
  const { t } = useTranslation();
  const { id = '' } = useParams();
  const { stock, queue, add } = useTech();
  const [part, setPart] = useState<StockPart | null>(null);
  const [qty, setQty] = useState<number | ''>('');
  const count = stock?.counts.find((c) => c.id === id);
  const counted = useMemo(() => (stock?.parts ?? []).map((p) => ({ p, q: queuedCount(queue, id, p.id) })).filter((x) => x.q !== undefined), [stock, queue, id]);
  if (!count) return <Stack><Back to="/tech/stock" label={t('techStock.stock')} /><Text c="dimmed">{t('techStock.countGone')}</Text></Stack>;
  const store = stock!.stores.find((s) => s.id === count.storeId);
  const save = async () => {
    if (!part || qty === '') return;
    try { await add({ type: 'count.line', payload: { countId: id, partId: part.id, quantity: Number(qty) } }); notifySuccess(t('techStock.counted', { part: part.partNumber })); }
    catch (e) { notifyError(e); }
    setPart(null); setQty('');
  };
  return (
    <Stack>
      <Back to="/tech/stock" label={t('techStock.stock')} />
      <Title order={4}>{count.name}</Title>
      <Text fz="sm" c="dimmed">{store?.code} · {store?.name}</Text>
      {!part ? <PartSearch onPick={setPart} autoFocus /> : (
        <Card><Stack>
          <Text fw={600}>{part.partNumber} · {part.name}</Text>
          {part.levels.find((l) => l.storeId === count.storeId)?.bin && <Text fz="sm">{t('techStock.bin')} {part.levels.find((l) => l.storeId === count.storeId)?.bin}</Text>}
          {part.isSerialTracked
            ? <Text fz="sm" c="orange">{t('techStock.serialCount')}</Text>
            : part.isShelfItem
            ? <Text fz="sm" c="orange">{t('techStock.shelfCount')}</Text>
            : <NumberInput label={`${t('techStock.countedQty')} (${part.unitOfMeasure})`} min={0} decimalScale={3} value={qty} onChange={(v) => setQty(v === '' ? '' : Number(v))} data-autofocus />}
          <Group>{!part.isShelfItem && !part.isSerialTracked && <Button onClick={save} disabled={qty === '' || Number(qty) < 0}>{t('techStock.saveCount')}</Button>}<Button variant="subtle" onClick={() => setPart(null)}>{t('common.cancel')}</Button></Group>
        </Stack></Card>
      )}
      {counted.length > 0 && (
        <Card><Stack gap={4}><Title order={6}>{t('techStock.waitingToSend')}</Title>
          {counted.map(({ p, q }) => <Group key={p.id} justify="space-between"><Text fz="sm">{p.partNumber}</Text><Badge variant="light">{fmtQty(q!)} {p.unitOfMeasure}</Badge></Group>)}
        </Stack></Card>
      )}
    </Stack>
  );
}

/** Receiving a delivery against a purchase order line. */
export function ReceivePage() {
  const { t } = useTranslation();
  const { stock, add } = useTech();
  const [po, setPo] = useState<string | null>(null);
  const [line, setLine] = useState<string | null>(null);
  const [qty, setQty] = useState<number | ''>('');
  const orders = stock?.purchaseOrders ?? [];
  const order = orders.find((o) => o.id === po);
  const l = order?.lines.find((x) => x.id === line);
  const save = async () => {
    if (!order || !l || qty === '') return;
    try { await add({ type: 'po.receive', payload: { purchaseOrderId: order.id, lineId: l.id, quantity: Number(qty) } }); notifySuccess(t('techStock.received')); }
    catch (e) { notifyError(e); }
    setLine(null); setQty('');
  };
  return (
    <Stack>
      <Back to="/tech/stock" label={t('techStock.stock')} />
      <Title order={4}>{t('techStock.receive')}</Title>
      {orders.length === 0 && <Text c="dimmed">{t('techStock.noOrders')}</Text>}
      <Select label={t('techStock.order')} data={orders.map((o) => ({ value: o.id, label: `${o.number} · ${o.supplier}` }))} value={po} onChange={(v) => { setPo(v); setLine(null); }} searchable />
      {order && <Select label={t('techStock.line')} data={order.lines.map((x) => ({ value: x.id, label: `${x.lineNumber}. ${x.description} · ${t('techStock.outstanding', { qty: fmtQty(x.outstanding) })}` }))} value={line} onChange={setLine} />}
      {l && <NumberInput label={`${t('techStock.quantity')}${l.unitOfMeasure ? ` (${l.unitOfMeasure})` : ''}`} min={0.001} max={l.outstanding} decimalScale={3} value={qty} onChange={(v) => setQty(v === '' ? '' : Number(v))}
        description={(l.packSize ?? 1) !== 1 ? t('techStock.piecesPerPack', { count: l.packSize ?? 1, pieces: fmtQty((Number(qty) || 0) * (l.packSize ?? 1)) }) : undefined} />}
      {l && <Button onClick={save} disabled={qty === '' || Number(qty) <= 0 || Number(qty) > l.outstanding}>{t('techStock.receiveButton')}</Button>}
    </Stack>
  );
}
