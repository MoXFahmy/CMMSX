import { useMemo, useState, type ReactNode } from 'react';
import { Anchor, Badge, Button, Card, Center, Group, Loader, SimpleGrid, Stack, Text, Title, Tooltip, UnstyledButton } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconAlertTriangle } from '@tabler/icons-react';
import { get } from '@/api/client';
import type { AssetListItem, Dashboard, Paged, PartListItem, WorkOrderListItem } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { StatusBadge } from '@/components/Badges';
import { buildTree, LocationTree, type TreeNode } from '@/components/LocationTree';
import { useHierarchy } from '@/components/LocationPicker';
import { PriorityBadge } from '@/components/WorkBadges';
import { fmtDate } from '@/components/utils';
import { RequestFormModal } from '@/pages/work/WorkRequestsPage';
import { CARD_INFO, gridRows, type HomeCard, type HomeCardType, type HomeTile } from './cards';
import {
  AssetsByCategory, AssetsBySite, AssetsByStatus, AssetSummary, RecentAssets, UrgentWork, useDashboard, WarrantySoon, WorkBySite, WorkSummary,
} from './DashboardSections';
import { cardContent, listCardRequest, type ListCardType } from './listCards';

const DASHBOARD_PIECES: Partial<Record<HomeCardType, (p: { data: Dashboard }) => ReactNode>> = {
  'work-summary': WorkSummary, 'work-by-site': WorkBySite, 'urgent-work': UrgentWork, 'asset-summary': AssetSummary,
  'assets-by-status': AssetsByStatus, 'assets-by-category': AssetsByCategory, 'assets-by-site': AssetsBySite,
  'warranty-soon': WarrantySoon, 'recent-assets': RecentAssets,
};

/** A home card's frame: its title, an optional count and "View all" link, then its content. */
function Frame({ title, count, link, children }: { title: string; count?: number; link?: string; children: ReactNode }) {
  const { t } = useTranslation();
  return (
    <Card h="100%">
      <Group justify="space-between" mb="sm" wrap="nowrap">
        <Group gap={8} wrap="nowrap" style={{ minWidth: 0 }}>
          <Title order={5} lineClamp={1}>{title}</Title>
          {count !== undefined && <Badge variant="light" size="lg">{count.toLocaleString()}</Badge>}
        </Group>
        {link && <Anchor component={Link} to={link} fz="sm" style={{ whiteSpace: 'nowrap' }}>{t('home.viewAll')}</Anchor>}
      </Group>
      {children}
    </Card>
  );
}

const Loading = () => <Center h={80}><Loader size="sm" /></Center>;

/** A card whose request failed says so, with the reason on hover. */
function Failed({ error }: { error: unknown }) {
  const { t } = useTranslation();
  return <Tooltip label={error instanceof Error ? error.message : String(error)}><Text c="dimmed" fz="sm">{t('home.cardError')}</Text></Tooltip>;
}

function Row({ to, main, sub, right }: { to: string; main: ReactNode; sub?: ReactNode; right?: ReactNode }) {
  return (
    <Group justify="space-between" wrap="nowrap" gap="xs">
      <Stack gap={0} style={{ minWidth: 0 }}>
        <Anchor component={Link} to={to} fz="sm" lineClamp={1}>{main}</Anchor>
        {sub && <Text fz="xs" c="dimmed" lineClamp={1}>{sub}</Text>}
      </Stack>
      {right && <div style={{ flexShrink: 0 }}>{right}</div>}
    </Group>
  );
}

/** My work, unassigned work, late work, assets down and low stock: the list's total and its first rows, in the list's own terms. */
function ListCard({ type }: { type: ListCardType }) {
  const { t } = useTranslation();
  const { can } = useAuth();
  const req = listCardRequest(type, can);
  const { data, isLoading, isError, error } = useQuery({ queryKey: ['home-card', type, req.query], queryFn: () => get<Paged<unknown>>(req.path, req.query) });
  const rows = data?.items ?? [];
  const show = cardContent({ isLoading, isError, count: rows.length });
  return (
    <Frame title={t(`home.cards.${type}`)} count={data?.total} link={req.link}>
      {show === 'loading' ? <Loading /> : show === 'error' ? <Failed error={error} /> : show === 'empty' ? <Text c="dimmed" fz="sm">{t(`home.empty.${type}`)}</Text> : (
        <Stack gap={8}>
          {type === 'assets-down' ? (rows as AssetListItem[]).map((a) => (
            <Row key={a.id} to={`/assets/${a.id}`} main={`${a.assetNumber} · ${a.name}`} sub={a.locationPath ?? a.siteName} right={<StatusBadge status={a.status} />} />
          )) : type === 'low-stock' ? (rows as PartListItem[]).map((p) => (
            <Row key={p.id} to={`/inventory/parts/${p.id}`} main={`${p.partNumber} · ${p.name}`}
              right={<Text fz="xs" c="red" fw={600} style={{ whiteSpace: 'nowrap' }}>{t('home.available', { count: p.available, unit: p.unitOfMeasure })}</Text>} />
          )) : (rows as WorkOrderListItem[]).map((w) => (
            <Row key={w.id} to={`/work-orders/${w.id}`} main={`${w.number} · ${w.title}`}
              sub={[w.assetNumber ?? w.siteName, w.dueDate ? t('home.due', { date: fmtDate(w.dueDate) }) : null].filter(Boolean).join(' · ')}
              right={<PriorityBadge value={w.priority} />} />
          ))}
        </Stack>
      )}
    </Frame>
  );
}

type Approvals = {
  requests: { id: string; number: string; siteName: string; requestedByName?: string | null }[];
  orders: { id: string; number: string; siteName: string; supplierName: string }[];
};

function ApprovalsCard() {
  const { t } = useTranslation();
  const { data, isLoading, isError, error } = useQuery({ queryKey: ['my-approvals'], queryFn: () => get<Approvals>('/approvals/mine') });
  const items = [
    ...(data?.requests ?? []).map((r) => ({ id: r.id, to: `/purchasing/requests/${r.id}`, main: r.number, sub: [r.siteName, r.requestedByName].filter(Boolean).join(' · '), kind: t('home.purchaseRequest') })),
    ...(data?.orders ?? []).map((o) => ({ id: o.id, to: `/purchasing/orders/${o.id}`, main: o.number, sub: `${o.supplierName} · ${o.siteName}`, kind: t('home.purchaseOrder') })),
  ];
  const show = cardContent({ isLoading, isError, count: items.length });
  return (
    <Frame title={t('home.cards.my-approvals')} count={data ? items.length : undefined} link="/approvals">
      {show === 'loading' ? <Loading /> : show === 'error' ? <Failed error={error} /> : show === 'empty' ? <Text c="dimmed" fz="sm">{t('home.empty.my-approvals')}</Text> : (
        <Stack gap={8}>
          {items.slice(0, 8).map((i) => <Row key={i.to} to={i.to} main={i.main} sub={i.sub} right={<Badge size="sm" variant="default">{i.kind}</Badge>} />)}
        </Stack>
      )}
    </Frame>
  );
}

function QuickRequestCard() {
  const { t } = useTranslation();
  const [opened, m] = useDisclosure();
  return (
    <Frame title={t('home.cards.quick-request')}>
      <Stack align="flex-start" gap="sm">
        <Text c="dimmed" fz="sm">{t('home.quickRequestText')}</Text>
        <Button leftSection={<IconAlertTriangle size={16} />} onClick={m.open}>{t('home.reportProblem')}</Button>
      </Stack>
      <RequestFormModal opened={opened} onClose={m.close} />
    </Frame>
  );
}

function LocationTreeCard() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const { data: h, isLoading } = useHierarchy();
  const tree = useMemo(() => (h ? buildTree(h) : []), [h]);
  const [picked, setPicked] = useState<TreeNode | null>(null);
  const [requestOpen, request] = useDisclosure();
  const place = picked ? { siteId: picked.siteId, locationId: picked.kind === 'location' ? picked.id : null } : undefined;
  const placeQuery = place ? `siteId=${place.siteId}${place.locationId ? `&locationId=${place.locationId}` : ''}` : '';
  return (
    <Frame title={t('home.cards.location-tree')}>
      {isLoading ? <Loading /> : (
        <SimpleGrid cols={{ base: 1, md: 2 }} spacing="lg">
          <div style={{ minWidth: 0 }}><LocationTree tree={tree} selected={picked?.id ?? null} onSelect={setPicked} mah={360} /></div>
          {picked ? (
            <Stack gap="sm" align="flex-start">
              <Text fw={600}>{picked.name}</Text>
              <Text fz="xs" c="dimmed">{picked.code}</Text>
              <Group gap="xs">
                {can(P.requestsCreate) && <Button size="xs" onClick={request.open}>{t('home.reportHere')}</Button>}
                {can(P.woCreate) && <Button size="xs" variant="light" component={Link} to={`/work-orders/new?${placeQuery}`}>{t('home.newWorkOrderHere')}</Button>}
                {can(P.assetsCreate) && <Button size="xs" variant="light" component={Link} to={`/assets/new?${placeQuery}`}>{t('home.newAssetHere')}</Button>}
                <Button size="xs" variant="default" component={Link} to={picked.kind === 'site' ? `/locations?site=${picked.id}` : `/locations?loc=${picked.id}`}>{t('home.open')}</Button>
              </Group>
            </Stack>
          ) : <Text c="dimmed" fz="sm">{t('home.pickPlace')}</Text>}
        </SimpleGrid>
      )}
      <RequestFormModal opened={requestOpen} onClose={request.close} place={place} />
    </Frame>
  );
}

/** A tile's label in the reader's language: the Arabic wording when there is one. */
export function tileLabel(tile: HomeTile, language: string) {
  return language === 'ar' && tile.labelAr ? tile.labelAr : tile.label;
}

function ShortcutsCard({ tiles }: { tiles: HomeTile[] }) {
  const { i18n } = useTranslation();
  return (
    <SimpleGrid cols={{ base: 2, sm: 3, lg: 6 }} spacing="md">
      {tiles.map((tile) => (
        <UnstyledButton key={tile.to} component={Link} to={tile.to} p="md"
          style={{ borderRadius: 'var(--mantine-radius-md)', background: `var(--mantine-color-${tile.color}-light)`, color: `var(--mantine-color-${tile.color}-light-color)`, minHeight: 72, display: 'flex', alignItems: 'center' }}>
          <Text fw={600} fz="sm" lineClamp={2}>{tileLabel(tile, i18n.language)}</Text>
        </UnstyledButton>
      ))}
    </SimpleGrid>
  );
}

function CardView({ card, dashboard }: { card: HomeCard; dashboard?: Dashboard }) {
  const Piece = DASHBOARD_PIECES[card.type];
  if (Piece) return dashboard ? <Piece data={dashboard} /> : <Card><Loading /></Card>;
  switch (card.type) {
    case 'my-work': case 'unassigned-work': case 'late-work': case 'assets-down': case 'low-stock': return <ListCard type={card.type} />;
    case 'my-approvals': return <ApprovalsCard />;
    case 'quick-request': return <QuickRequestCard />;
    case 'location-tree': return <LocationTreeCard />;
    case 'shortcuts': return <ShortcutsCard tiles={card.tiles ?? []} />;
    default: return null;
  }
}

/** A role's home page: its cards in order, full cards on their own row and half cards two by two. */
export function RoleHome({ cards }: { cards: HomeCard[] }) {
  const needsDashboard = cards.some((c) => CARD_INFO[c.type].group === 'dashboard');
  const { data } = useDashboard(needsDashboard);
  return (
    <Stack gap="lg">
      {/* Keys use the row's place: several roles can each bring a shortcuts card. */}
      {gridRows(cards).map((row, i) => row.length === 1 && CARD_INFO[row[0].type].width === 'full'
        ? <CardView key={`${i}-${row[0].type}`} card={row[0]} dashboard={data} />
        : (
          <SimpleGrid key={`${i}-${row.map((c) => c.type).join('+')}`} cols={{ base: 1, md: 2 }} spacing="lg">
            {row.map((c) => <CardView key={c.type} card={c} dashboard={data} />)}
          </SimpleGrid>
        ))}
    </Stack>
  );
}
