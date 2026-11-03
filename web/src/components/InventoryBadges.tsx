import { Badge } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import type { PurchaseOrderStatus, StockTransactionType } from '@/api/types';

const fixed = { style: { maxWidth: 'none', flexShrink: 0 }, styles: { label: { overflow: 'visible' } } } as const;
export const poStatusColors: Record<PurchaseOrderStatus, string> = {
  Draft: 'gray', Submitted: 'yellow', Approved: 'blue', PartiallyReceived: 'cyan', Received: 'teal', Closed: 'dark', Cancelled: 'red',
};
const txColor: Record<StockTransactionType, string> = { Receipt: 'teal', Issue: 'orange', Return: 'cyan', Adjustment: 'grape', TransferOut: 'indigo', TransferIn: 'indigo' };

export function PoStatusBadge({ status }: { status: PurchaseOrderStatus }) {
  const { t } = useTranslation();
  return <Badge color={poStatusColors[status]} {...fixed}>{t(`poStatus.${status}`)}</Badge>;
}

export function TxTypeBadge({ type }: { type: StockTransactionType }) {
  const { t } = useTranslation();
  return <Badge color={txColor[type]} variant="light" {...fixed}>{t(`txType.${type}`)}</Badge>;
}
