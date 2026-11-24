import { Card, Text } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { homeChoice } from './cards';
import { ClassicDashboard, useGreeting } from './DashboardSections';
import { RoleHome } from './HomeCards';

/**
 * The home page (0.22.0): the person's counters, then their roles' cards; people whose roles have no home page keep
 * today's dashboard, and people who can't see that get a short message instead of an empty page.
 */
export function HomePage() {
  const { t } = useTranslation();
  const { me, can } = useAuth();
  const greeting = useGreeting();
  const choice = homeChoice(me?.home?.cards ?? [], can, me ?? {});
  const top = null;

  if (choice.kind === 'classic') return <ClassicDashboard top={top} />;
  return (
    <>
      <PageHeader title={greeting} />
      {top}
      {choice.kind === 'role' ? <RoleHome cards={choice.cards} /> : (
        <Card p="xl" maw={720}><Text c="dimmed">{t('home.nothingYet')}</Text></Card>
      )}
    </>
  );
}
