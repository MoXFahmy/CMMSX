import { useEffect, useState } from 'react';
import { Button, Group, Modal, Stack, Text } from '@mantine/core';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { post } from '@/api/client';
import { CostCentrePicker } from '@/components/CostCentrePicker';
import { notifyError, notifySuccess } from '@/components/utils';

/**
 * 0.20.0: move one or more work orders to another cost centre, in any status (closed and cancelled included). Their labour, parts,
 * contractors, other costs and purchase commitments move with them; the change shows in each work order's history.
 */
export function ChangeCostCentreModal({ ids, current, opened, onClose, onDone }: { ids: string[]; current?: string | null; opened: boolean; onClose: () => void; onDone?: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [value, setValue] = useState<string | null>(current ?? null);
  const [busy, setBusy] = useState(false);
  useEffect(() => { if (opened) setValue(current ?? null); }, [opened, current]);
  const save = async () => {
    setBusy(true);
    try {
      const r = await post<{ changed: number }>('/work-orders/cost-centre', { ids, costCentreId: value });
      notifySuccess(t('costCentres.changed', { count: r.changed }));
      await qc.invalidateQueries({ queryKey: ['work-orders'] });
      await qc.invalidateQueries({ queryKey: ['work-order'] });
      await qc.invalidateQueries({ queryKey: ['work-order-activity'] });
      onDone?.();
      onClose();
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Modal opened={opened} onClose={onClose} title={t('costCentres.change')}>
      <Stack>
        <Text fz="sm" c="dimmed">{t('costCentres.changeHint', { count: ids.length })}</Text>
        <CostCentrePicker value={value} onChange={setValue} data-autofocus />
        <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button loading={busy} onClick={save}>{t('common.save')}</Button></Group>
      </Stack>
    </Modal>
  );
}
