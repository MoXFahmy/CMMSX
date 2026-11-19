import { Text } from '@mantine/core';
import { modals } from '@mantine/modals';
import i18n from '@/i18n';
import { post } from '@/api/client';
import type { PmRunResult } from '@/api/types';
import { fmtDate, notifyError, notifySuccess } from '@/components/utils';

/** Asks first, then generates a schedule's occurrence on `date` now instead of waiting for its lead time. */
export function generateNow(scheduleId: string, date: string, onDone: () => Promise<unknown> | void) {
  modals.openConfirmModal({
    title: i18n.t('pm.generateNowTitle'),
    children: <Text size="sm">{i18n.t('pm.generateNowBody', { date: fmtDate(date) })}</Text>,
    labels: { confirm: i18n.t('pm.generateNow'), cancel: i18n.t('common.cancel') },
    onConfirm: async () => {
      try {
        const r = await post<{ created: number; results: PmRunResult[] }>('/pm/generate', { until: date, scheduleIds: [scheduleId] });
        if (r.created > 0) notifySuccess(i18n.t('pm.generated', { count: r.created }));
        else notifyError(new Error(r.results.find((x) => x.skipped)?.skipped ?? i18n.t('pm.nothingGenerated')));
        await onDone();
      } catch (e) { notifyError(e); }
    },
  });
}
