import { Group, Stack, Text, Title } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@/auth/AuthContext';
import { fmtDateTime } from './utils';

/** The organization's logo (0.14.0) for printed pages; nothing when there is none. */
export function OrgLogo({ height = 48 }: { height?: number }) {
  const { me } = useAuth();
  const version = me?.tenant.settings.logoVersion;
  if (!version) return null;
  return <img src={`/api/settings/logo?v=${version}`} alt={me?.tenant.name ?? ''} style={{ height, maxWidth: height * 4, objectFit: 'contain' }} />;
}

/** The top of a printed report (0.14.0): logo and organization, the report's title, its period and filters, and who printed it when. */
export function PrintHeader({ title, lines }: { title: string; lines: string[] }) {
  const { t } = useTranslation();
  const { me } = useAuth();
  return (
    <Group justify="space-between" align="flex-start" wrap="nowrap" mb="sm">
      <Stack gap={4}>
        <Group gap="sm" wrap="nowrap"><OrgLogo /><Text fw={600}>{me?.tenant.name}</Text></Group>
        <Title order={3}>{title}</Title>
        {lines.map((l) => <Text key={l} fz="sm" c="dimmed">{l}</Text>)}
      </Stack>
      <Text fz="xs" c="dimmed" ta="end">{t('rep.printed', { date: fmtDateTime(new Date().toISOString()), name: me?.user.fullName ?? '' })}</Text>
    </Group>
  );
}
