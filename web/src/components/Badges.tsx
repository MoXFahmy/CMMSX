import { Badge, ThemeIcon } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { IconBuilding, IconBuildingFactory2, IconDoor, IconLayersSubtract, IconMapPin, IconStack2, IconVectorTriangle, IconBox } from '@tabler/icons-react';
import type { AssetStatus, Criticality, LocationType } from '@/api/types';

const statusColor: Record<AssetStatus, string> = { Active: 'teal', Inactive: 'gray', InStorage: 'indigo', UnderRepair: 'orange', Disposed: 'dark' };
const critColor: Record<Criticality, string> = { Low: 'gray', Medium: 'blue', High: 'orange', Critical: 'red' };

export function StatusBadge({ status }: { status: AssetStatus }) {
  const { t } = useTranslation();
  return <Badge color={statusColor[status]} style={{ maxWidth: 'none', flexShrink: 0 }} styles={{ label: { overflow: 'visible' } }}>{t(`status.${status}`)}</Badge>;
}

export function CriticalityBadge({ value, variant = 'dot' }: { value: Criticality; variant?: 'dot' | 'light' }) {
  const { t } = useTranslation();
  return <Badge color={critColor[value]} variant={variant} style={{ maxWidth: 'none', flexShrink: 0 }} styles={{ label: { overflow: 'visible' } }}>{t(`criticality.${value}`)}</Badge>;
}

export const statusColors = statusColor;
export const criticalityColors = critColor;

const typeIcon: Record<LocationType, typeof IconMapPin> = {
  Area: IconLayersSubtract, Building: IconBuilding, Floor: IconStack2, Room: IconDoor, Zone: IconVectorTriangle,
  ProductionLine: IconBuildingFactory2, Space: IconBox, Other: IconMapPin,
};

export function LocationTypeIcon({ type, size = 16 }: { type: LocationType; size?: number }) {
  const Icon = typeIcon[type] ?? IconMapPin;
  return <Icon size={size} stroke={1.6} />;
}

export function SiteIcon({ size = 26 }: { size?: number }) {
  return <ThemeIcon size={size} variant="light" radius="sm"><IconBuildingFactory2 size={size * 0.62} /></ThemeIcon>;
}
