import { ActionIcon, Indicator, Menu, Text } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconAlertTriangle, IconBell, IconClipboardText, IconPackage, IconSignature } from '@tabler/icons-react';
import { get } from '@/api/client';

/** Counts the signed-in user may see; null means "not for you". */
type Alerts = { overdueWorkOrders: number | null; myOverdueWorkOrders: number | null; requestsToTriage: number | null; partsBelowReorder: number | null; waitingForMyApproval?: number | null };

/** Header bell: what needs attention now, each line opening the filtered list. */
export function AlertBell() {
  const { t } = useTranslation();
  const { data } = useQuery({ queryKey: ['alerts'], queryFn: () => get<Alerts>('/alerts'), refetchInterval: 120_000 });
  const lines = [
    data?.overdueWorkOrders ? { key: 'overdue', to: '/work-orders?view=overdue', icon: IconAlertTriangle, color: 'red',
      text: t('alerts.overdue', { count: data.overdueWorkOrders, mine: data.myOverdueWorkOrders ?? 0 }) } : null,
    data?.requestsToTriage ? { key: 'triage', to: '/work-requests?tab=New', icon: IconClipboardText, color: 'blue', text: t('alerts.triage', { count: data.requestsToTriage }) } : null,
    data?.partsBelowReorder ? { key: 'stock', to: '/inventory?low=true', icon: IconPackage, color: 'orange', text: t('alerts.lowStock', { count: data.partsBelowReorder }) } : null,
    data?.waitingForMyApproval ? { key: 'approvals', to: '/approvals', icon: IconSignature, color: 'teal', text: t('alerts.approvals', { count: data.waitingForMyApproval }) } : null,
  ].filter((x) => x !== null);
  const total = (data?.overdueWorkOrders ?? 0) + (data?.requestsToTriage ?? 0) + (data?.partsBelowReorder ?? 0) + (data?.waitingForMyApproval ?? 0);
  return (
    <Menu position="bottom-end" width={300} shadow="md">
      <Menu.Target>
        <Indicator label={total > 99 ? '99+' : total} size={16} color="red" disabled={total === 0} offset={4}>
          <ActionIcon variant="subtle" color="gray" size="lg" aria-label={t('alerts.title')}><IconBell size={18} /></ActionIcon>
        </Indicator>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>{t('alerts.title')}</Menu.Label>
        {lines.length === 0 && <Text fz="sm" c="dimmed" px="sm" py={6}>{t('alerts.none')}</Text>}
        {lines.map((l) => (
          <Menu.Item key={l.key} component={Link} to={l.to} leftSection={<l.icon size={16} color={`var(--mantine-color-${l.color}-6)`} />}>{l.text}</Menu.Item>
        ))}
      </Menu.Dropdown>
    </Menu>
  );
}
