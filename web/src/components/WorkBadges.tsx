import { Badge } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { IconArrowDown, IconArrowUp, IconEqual, IconFlame } from '@tabler/icons-react';
import type { WorkOrderStatus, WorkPriority, WorkRequestStatus } from '@/api/types';

const woColor: Record<WorkOrderStatus, string> = { Open: 'blue', InProgress: 'cyan', OnHold: 'yellow', Completed: 'teal', Closed: 'gray', Cancelled: 'dark' };
const prColor: Record<WorkPriority, string> = { Low: 'gray', Medium: 'blue', High: 'orange', Urgent: 'red' };
const prIcon = { Low: IconArrowDown, Medium: IconEqual, High: IconArrowUp, Urgent: IconFlame };
const wrColor: Record<WorkRequestStatus, string> = { New: 'blue', Approved: 'teal', Rejected: 'red', Cancelled: 'gray' };
const fixed = { style: { maxWidth: 'none', flexShrink: 0 }, styles: { label: { overflow: 'visible' } } } as const;

export const woStatusColors = woColor;
export const priorityColors = prColor;

export function WoStatusBadge({ status }: { status: WorkOrderStatus }) {
  const { t } = useTranslation();
  return <Badge color={woColor[status]} {...fixed}>{t(`woStatus.${status}`)}</Badge>;
}

export function PriorityBadge({ value }: { value: WorkPriority }) {
  const { t } = useTranslation();
  const Icon = prIcon[value];
  return <Badge color={prColor[value]} variant="outline" leftSection={<Icon size={12} />} {...fixed}>{t(`priority.${value}`)}</Badge>;
}

export function WrStatusBadge({ status }: { status: WorkRequestStatus }) {
  const { t } = useTranslation();
  return <Badge color={wrColor[status]} {...fixed}>{t(`wrStatus.${status}`)}</Badge>;
}
