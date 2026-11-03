import { Alert, Anchor, Stack, Text } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconAlertTriangle, IconShieldCheck } from '@tabler/icons-react';
import { get } from '@/api/client';
import type { AssetOpenWork } from '@/api/types';
import { fmtDate } from './utils';

/** 4CSQL "Check Warranty/WO": warn about warranty cover and open work before raising another job on an asset. */
export function AssetCheck({ assetId, excludeWorkOrderId }: { assetId?: string | null; excludeWorkOrderId?: string }) {
  const { t } = useTranslation();
  const { data } = useQuery({ queryKey: ['asset-open-work', assetId], queryFn: () => get<AssetOpenWork>(`/assets/${assetId}/open-work`), enabled: !!assetId });
  if (!assetId || !data) return null;
  const orders = data.openWorkOrders.filter((w) => w.id !== excludeWorkOrderId);
  return (
    <Stack gap="xs">
      {data.underWarranty && (
        <Alert color="teal" variant="light" icon={<IconShieldCheck size={18} />} title={t('wo.underWarrantyTitle', { type: t(`warrantyType.${data.warrantyType}`) })}>
          {t('wo.underWarrantyBody', { date: fmtDate(data.warrantyExpiresOn) })}
        </Alert>
      )}
      {(orders.length > 0 || data.openRequests.length > 0) && (
        <Alert color="yellow" variant="light" icon={<IconAlertTriangle size={18} />} title={t('wo.openWorkTitle')}>
          <Stack gap={2}>
            {orders.map((w) => <Anchor key={w.id} component={Link} to={`/work-orders/${w.id}`} fz="sm" target="_blank">{w.number} · {w.title} ({t(`woStatus.${w.status}`)})</Anchor>)}
            {data.openRequests.map((r) => <Text key={r.id} fz="sm">{r.number} · {r.title} ({t('wrStatus.New')})</Text>)}
          </Stack>
        </Alert>
      )}
    </Stack>
  );
}
