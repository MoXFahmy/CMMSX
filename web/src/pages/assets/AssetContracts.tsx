import { Anchor, Badge, Card, Group, Stack, Text, Title } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { get } from '@/api/client';
import type { ContractStatus } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { fmtDate, fmtPrice } from '@/components/utils';
import { statusColor } from '@/pages/contracts/contracts';

type Row = { id: string; number: string; title: string; companyName: string; status: ContractStatus; startDate: string; endDate: string; yearlyCost?: number | null };

/** The asset page's contracts card (0.17.0): the service contracts covering this asset. */
export function AssetContracts({ assetId }: { assetId: string }) {
  const { t } = useTranslation();
  const { me } = useAuth();
  const { data } = useQuery({ queryKey: ['asset-contracts', assetId], queryFn: () => get<Row[]>(`/assets/${assetId}/contracts`) });
  if (!data?.length) return null;
  return (
    <Card>
      <Title order={5} mb="sm">{t('contracts.title')}</Title>
      <Stack gap="xs">
        {data.map((c) => (
          <div key={c.id}>
            <Group gap={6} wrap="nowrap" justify="space-between">
              <Anchor component={Link} to={`/contracts/${c.id}`} fz="sm" fw={600}>{c.number}</Anchor>
              <Badge size="xs" variant="light" color={statusColor(c.status)}>{t(`contracts.status.${c.status}`)}</Badge>
            </Group>
            <Text fz="xs" c="dimmed">{c.title} · {c.companyName}</Text>
            <Text fz="xs" c="dimmed">{fmtDate(c.startDate)} – {fmtDate(c.endDate)}{c.yearlyCost != null ? ` · ${fmtPrice(c.yearlyCost, me?.tenant.settings.currency ?? 'USD', 0)} / ${t('contracts.per.Year')}` : ''}</Text>
          </div>
        ))}
      </Stack>
    </Card>
  );
}
