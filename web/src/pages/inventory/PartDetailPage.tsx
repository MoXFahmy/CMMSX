import { useState } from 'react';
import { Alert, Badge, Button, Card, Center, Grid, Group, Loader, Menu, SimpleGrid, Stack, Table, Tabs, Text, Title, ActionIcon, Anchor, Progress, Tooltip } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconAdjustments, IconArrowBackUp, IconArrowsExchange, IconDots, IconEdit, IconPackageExport, IconPackageImport, IconShoppingCart, IconTrash } from '@tabler/icons-react';
import { del, get } from '@/api/client';
import type { PartDetail } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { StockTxModal, type StockTxKind } from '@/components/StockTxModal';
import { AuditTimeline } from '@/components/AuditTimeline';
import { Attachments } from '@/components/Attachments';
import { confirmDelete } from '@/components/confirm';
import { fmtDate, fmtPrice, fmtQty, notifyError } from '@/components/utils';
import { TransactionsTable } from './InventoryPage';

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <Stack gap={0}><Text fz="xs" c="dimmed" tt="uppercase" fw={600}>{label}</Text><Text fz="sm" component="div">{children ?? '—'}</Text></Stack>;
}

export function Stat({ label, value, sub, color }: { label: string; value: React.ReactNode; sub?: React.ReactNode; color?: string }) {
  return <Card p="md"><Text fz="xs" c="dimmed" tt="uppercase" fw={600}>{label}</Text><Text fz={24} fw={700} c={color} lh={1.3}>{value}</Text>{sub && <Text fz="xs" c="dimmed">{sub}</Text>}</Card>;
}

export function PartDetailPage() {
  const { t } = useTranslation();
  const { id = '' } = useParams();
  const { me, can } = useAuth();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const [tab, setTab] = useState<string | null>('stock');
  const [tx, setTx] = useState<{ kind: StockTxKind; storeId?: string } | null>(null);
  const { data: p, isLoading, error } = useQuery({ queryKey: ['part', id], queryFn: () => get<PartDetail>(`/parts/${id}`) });
  if (isLoading) return <Center h={300}><Loader /></Center>;
  if (error || !p) return <Alert color="red">{(error as Error)?.message}</Alert>;
  const low = p.levels.some((l) => l.reorderQuantity > 0 && l.quantityOnHand <= l.reorderLevel);
  const defaults = { partId: p.id, part: { id: p.id, partNumber: p.partNumber, name: p.name } };
  const actions: { kind: StockTxKind; icon: typeof IconPackageImport }[] = [
    { kind: 'receive', icon: IconPackageImport }, { kind: 'issue', icon: IconPackageExport }, { kind: 'return', icon: IconArrowBackUp },
    { kind: 'transfer', icon: IconArrowsExchange }, { kind: 'adjust', icon: IconAdjustments },
  ];

  return (
    <>
      <PageHeader crumbs={[{ label: t('inv.title'), to: '/inventory' }, { label: p.partNumber }]} title={p.name}
        badge={<Group gap={6}>{p.isCritical && <Badge color="red">{t('inv.critical')}</Badge>}{p.isShelfItem && <Badge color="grape" variant="light">{t('inv.shelfLife')}</Badge>}
          {!p.isActive && <Badge color="gray">{t('common.inactive')}</Badge>}{low && <Badge color="orange" variant="light">{t('inv.atReorder')}</Badge>}</Group>}
        subtitle={<>{p.partNumber}{p.groupName && ` · ${p.groupName}`}{p.manufacturer && ` · ${p.manufacturer}`}{p.manufacturerPartNumber && ` ${p.manufacturerPartNumber}`}</>}
        actions={<>
          {can(P.inventoryTransact) && <>
            <Button leftSection={<IconPackageExport size={16} />} onClick={() => setTx({ kind: 'issue' })}>{t('inv.tx.issue')}</Button>
            <Button variant="default" leftSection={<IconPackageImport size={16} />} onClick={() => setTx({ kind: 'receive' })}>{t('inv.tx.receive')}</Button>
          </>}
          <Menu position="bottom-end">
            <Menu.Target><ActionIcon variant="default" size="lg" aria-label={t('common.actions')}><IconDots size={16} /></ActionIcon></Menu.Target>
            <Menu.Dropdown>
              {can(P.inventoryTransact) && actions.filter((a) => a.kind !== 'issue' && a.kind !== 'receive').map((a) =>
                <Menu.Item key={a.kind} leftSection={<a.icon size={16} />} onClick={() => setTx({ kind: a.kind })}>{t(`inv.tx.${a.kind}`)}</Menu.Item>)}
              {can(P.purchasingCreate) && <Menu.Item leftSection={<IconShoppingCart size={16} />} component={Link} to={`/purchasing/orders/new?partId=${p.id}`}>{t('inv.orderPart')}</Menu.Item>}
              {can(P.inventoryManage) && <>
                <Menu.Divider />
                <Menu.Item leftSection={<IconEdit size={16} />} component={Link} to={`/inventory/parts/${p.id}/edit`}>{t('common.edit')}</Menu.Item>
                <Menu.Item color="red" leftSection={<IconTrash size={16} />} onClick={() => confirmDelete(p.partNumber, async () => {
                  try { await del(`/parts/${p.id}`); await qc.invalidateQueries({ queryKey: ['parts'] }); navigate('/inventory'); } catch (e) { notifyError(e); } })}>{t('common.delete')}</Menu.Item>
              </>}
            </Menu.Dropdown>
          </Menu>
        </>} />
      <SimpleGrid cols={{ base: 2, md: 4 }} mb="lg">
        <Stat label={t('inv.onHand')} value={`${fmtQty(p.onHand)} ${p.unitOfMeasure}`} color={p.onHand <= 0 ? 'red' : low ? 'orange' : undefined} sub={t('inv.inStores', { count: p.levels.length })} />
        <Stat label={t('inv.onOrder')} value={fmtQty(p.onOrder)} sub={p.leadTimeDays ? t('inv.leadTime', { days: p.leadTimeDays }) : undefined} />
        <Stat label={t('inv.avgCost')} value={fmtPrice(p.averageCost, currency)} sub={p.lastPurchasePrice !== undefined && p.lastPurchasePrice !== null ? t('inv.lastPrice', { price: fmtPrice(p.lastPurchasePrice, currency) }) : undefined} />
        <Stat label={t('inv.stockValue')} value={fmtPrice(p.stockValue, currency)} />
      </SimpleGrid>
      <Tabs value={tab} onChange={setTab} keepMounted={false}>
        <Tabs.List mb="md">
          <Tabs.Tab value="stock">{t('inv.stockLevels')}</Tabs.Tab>
          <Tabs.Tab value="history">{t('inv.tabs.transactions')}</Tabs.Tab>
          <Tabs.Tab value="details">{t('common.details')}</Tabs.Tab>
          <Tabs.Tab value="files">{t('wo.files')}</Tabs.Tab>
          <Tabs.Tab value="audit">{t('audit.title')}</Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="stock">
          <Card p={0}>
            <Table.ScrollContainer minWidth={720}>
              <Table verticalSpacing="sm" highlightOnHover>
                <Table.Thead><Table.Tr>
                  <Table.Th>{t('inv.store')}</Table.Th><Table.Th>{t('inv.bin')}</Table.Th><Table.Th ta="right">{t('inv.onHand')}</Table.Th>
                  <Table.Th ta="right">{t('inv.min')}</Table.Th><Table.Th ta="right">{t('inv.reorderAt')}</Table.Th><Table.Th ta="right">{t('inv.max')}</Table.Th><Table.Th ta="right">{t('inv.reorderQty')}</Table.Th><Table.Th w={160} />
                </Table.Tr></Table.Thead>
                <Table.Tbody>
                  {p.levels.length === 0 && <Table.Tr><Table.Td colSpan={8}><Text c="dimmed" ta="center" py="md">{t('inv.noLevels')}</Text></Table.Td></Table.Tr>}
                  {p.levels.map((l) => {
                    const atReorder = l.reorderQuantity > 0 && l.quantityOnHand <= l.reorderLevel;
                    const pct = l.maxLevel > 0 ? Math.min(100, (l.quantityOnHand / l.maxLevel) * 100) : 0;
                    return (
                      <Table.Tr key={l.id}>
                        <Table.Td><Text fz="sm" fw={600}>{l.storeCode}</Text><Text fz="xs" c="dimmed">{l.storeName}</Text></Table.Td>
                        <Table.Td>{l.bin ?? '—'}</Table.Td>
                        <Table.Td ta="right"><Text fz="sm" fw={600} c={l.quantityOnHand <= 0 ? 'red' : atReorder ? 'orange' : undefined}>{fmtQty(l.quantityOnHand)}</Text>
                          {l.maxLevel > 0 && <Tooltip label={`${Math.round(pct)}% of max`}><Progress value={pct} size={4} mt={4} color={atReorder ? 'orange' : 'teal'} /></Tooltip>}</Table.Td>
                        <Table.Td ta="right">{fmtQty(l.minLevel)}</Table.Td><Table.Td ta="right">{fmtQty(l.reorderLevel)}</Table.Td><Table.Td ta="right">{fmtQty(l.maxLevel)}</Table.Td><Table.Td ta="right">{fmtQty(l.reorderQuantity)}</Table.Td>
                        <Table.Td ta="right">{can(P.inventoryTransact) && <Group gap={4} justify="flex-end" wrap="nowrap">
                          <Button size="compact-xs" variant="light" onClick={() => setTx({ kind: 'issue', storeId: l.storeId })}>{t('inv.issueShort')}</Button>
                          <Button size="compact-xs" variant="default" onClick={() => setTx({ kind: 'receive', storeId: l.storeId })}>{t('inv.receiveShort')}</Button></Group>}</Table.Td>
                      </Table.Tr>
                    );
                  })}
                </Table.Tbody>
              </Table>
            </Table.ScrollContainer>
          </Card>
          {p.isShelfItem && (
            <Card p={0} mt="md">
              <Title order={5} p="md">{t('inv.batches')}</Title>
              <Table verticalSpacing="xs">
                <Table.Thead><Table.Tr><Table.Th>{t('inv.batch')}</Table.Th><Table.Th>{t('inv.store')}</Table.Th><Table.Th ta="right">{t('inv.quantity')}</Table.Th><Table.Th>{t('inv.expiry')}</Table.Th></Table.Tr></Table.Thead>
                <Table.Tbody>
                  {p.batches.length === 0 && <Table.Tr><Table.Td colSpan={4}><Text c="dimmed" fz="sm">{t('common.noRecords')}</Text></Table.Td></Table.Tr>}
                  {p.batches.map((b) => {
                    const expired = b.expiry && b.expiry < new Date().toISOString().slice(0, 10);
                    return <Table.Tr key={`${b.storeId}-${b.batchNumber}`}><Table.Td fw={600}>{b.batchNumber}</Table.Td><Table.Td>{p.levels.find((l) => l.storeId === b.storeId)?.storeCode}</Table.Td>
                      <Table.Td ta="right">{fmtQty(b.quantity)}</Table.Td><Table.Td c={expired ? 'red' : undefined}>{fmtDate(b.expiry)}{expired && ` · ${t('inv.expired')}`}</Table.Td></Table.Tr>;
                  })}
                </Table.Tbody>
              </Table>
            </Card>
          )}
        </Tabs.Panel>
        <Tabs.Panel value="history"><TransactionsTable partId={p.id} /></Tabs.Panel>
        <Tabs.Panel value="details">
          <Grid>
            <Grid.Col span={{ base: 12, md: 8 }}>
              <Card>
                <SimpleGrid cols={{ base: 1, sm: 3 }} spacing="lg" verticalSpacing="md">
                  <Field label={t('inv.partNumber')}>{p.partNumber}</Field>
                  <Field label={t('inv.unit')}>{p.unitOfMeasure}</Field>
                  <Field label={t('inv.group')}>{p.groupName}</Field>
                  <Field label={t('inv.manufacturer')}>{p.manufacturer}</Field>
                  <Field label={t('inv.mfrPartNumber')}>{p.manufacturerPartNumber}</Field>
                  <Field label={t('inv.barcode')}>{p.barcode}</Field>
                  <Field label={t('inv.standardPrice')}>{fmtPrice(p.standardPrice, currency)}</Field>
                  <Field label={t('inv.preferredSupplier')}>{p.preferredSupplier?.name}</Field>
                  <Field label={t('inv.leadTimeLabel')}>{p.leadTimeDays ? t('inv.days', { count: p.leadTimeDays }) : null}</Field>
                  <Field label={t('inv.allowNegative')}>{p.allowNegativeStock ? t('common.yes') : t('common.no')}</Field>
                  {p.legacyRef && <Field label={t('assets.legacyRef')}>{p.legacyRef}</Field>}
                </SimpleGrid>
                {p.specification && <><Text fz="xs" c="dimmed" tt="uppercase" fw={600} mt="lg">{t('inv.specification')}</Text><Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{p.specification}</Text></>}
                {p.notes && <><Text fz="xs" c="dimmed" tt="uppercase" fw={600} mt="lg">{t('common.notes')}</Text><Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{p.notes}</Text></>}
              </Card>
            </Grid.Col>
            <Grid.Col span={{ base: 12, md: 4 }}>
              <Card>
                <Text fw={600} mb="xs">{t('inv.alternates')}</Text>
                {p.alternates.length === 0 ? <Text fz="sm" c="dimmed">{t('inv.noAlternates')}</Text>
                  : <Stack gap={4}>{p.alternates.map((a) => <Anchor key={a.id} component={Link} to={`/inventory/parts/${a.id}`} fz="sm">{a.partNumber} · {a.name}</Anchor>)}</Stack>}
              </Card>
            </Grid.Col>
          </Grid>
        </Tabs.Panel>
        <Tabs.Panel value="files"><Card><Attachments entityType="Part" entityId={p.id} canEdit={can(P.inventoryManage)} /></Card></Tabs.Panel>
        <Tabs.Panel value="audit"><Card><AuditTimeline entityType="Part" entityId={p.id} /></Card></Tabs.Panel>
      </Tabs>
      {tx && <StockTxModal kind={tx.kind} opened onClose={() => setTx(null)} defaults={{ ...defaults, storeId: tx.storeId ?? (p.levels.length === 1 ? p.levels[0].storeId : null) }} />}
    </>
  );
}
