import { Card, Stack, Text, ThemeIcon, Title } from '@mantine/core';
import { IconRoad } from '@tabler/icons-react';
import { useParams } from 'react-router';
import { useTranslation } from 'react-i18next';

const keys: Record<string, string> = {
  'work-requests': 'nav.workRequests', 'work-orders': 'nav.workOrders', 'preventive-maintenance': 'nav.preventive',
  inventory: 'nav.inventory', purchasing: 'nav.purchasing', reports: 'nav.reports',
};

export function ComingSoonPage() {
  const { t } = useTranslation();
  const { module = '' } = useParams();
  return (
    <Card maw={640} mx="auto" mt="xl" p="xl">
      <Stack align="center" ta="center">
        <ThemeIcon size={56} radius="xl" variant="light"><IconRoad size={30} /></ThemeIcon>
        <Title order={3}>{t('comingSoon.title', { module: t(keys[module] ?? 'nav.dashboard') })}</Title>
        <Text c="dimmed">{t('comingSoon.body')}</Text>
      </Stack>
    </Card>
  );
}
