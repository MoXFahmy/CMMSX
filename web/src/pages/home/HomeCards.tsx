import { Card, Center, Loader, SimpleGrid, Stack } from '@mantine/core';
import type { Dashboard } from '@/api/types';
import { CARD_INFO, gridRows, type HomeCard, type HomeCardType } from './cards';
import {
  AssetsByCategory, AssetsBySite, AssetsByStatus, AssetSummary, RecentAssets, UrgentWork, useDashboard, WarrantySoon, WorkBySite, WorkSummary,
} from './DashboardSections';

const DASHBOARD_PIECES: Partial<Record<HomeCardType, (p: { data: Dashboard }) => React.ReactNode>> = {
  'work-summary': WorkSummary, 'work-by-site': WorkBySite, 'urgent-work': UrgentWork, 'asset-summary': AssetSummary,
  'assets-by-status': AssetsByStatus, 'assets-by-category': AssetsByCategory, 'assets-by-site': AssetsBySite,
  'warranty-soon': WarrantySoon, 'recent-assets': RecentAssets,
};

function CardView({ card, dashboard }: { card: HomeCard; dashboard?: Dashboard }) {
  const Piece = DASHBOARD_PIECES[card.type];
  if (Piece) return dashboard ? <Piece data={dashboard} /> : <Card><Center h={120}><Loader size="sm" /></Center></Card>;
  return null;
}

/** A role's home page: its cards in order, full cards on their own row and half cards two by two. */
export function RoleHome({ cards }: { cards: HomeCard[] }) {
  const needsDashboard = cards.some((c) => CARD_INFO[c.type].group === 'dashboard');
  const { data } = useDashboard(needsDashboard);
  return (
    <Stack gap="lg">
      {gridRows(cards).map((row) => row.length === 1 && CARD_INFO[row[0].type].width === 'full'
        ? <CardView key={row[0].type} card={row[0]} dashboard={data} />
        : (
          <SimpleGrid key={row.map((c) => c.type).join('+')} cols={{ base: 1, md: 2 }} spacing="lg">
            {row.map((c) => <CardView key={c.type} card={c} dashboard={data} />)}
          </SimpleGrid>
        ))}
    </Stack>
  );
}
