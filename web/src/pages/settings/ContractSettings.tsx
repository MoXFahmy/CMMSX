import { useEffect, useState } from 'react';
import { Button, Card, Group, MultiSelect, Text, Title } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { get, put } from '@/api/client';
import { notifyError, notifySuccess } from '@/components/utils';

/** Settings › Contracts (0.17.0): who hears when a contract needs attention, besides its manager. */
export function ContractSettings({ editable }: { editable: boolean }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data } = useQuery({ queryKey: ['settings'], queryFn: () => get<{ name: string; settings: Record<string, unknown> }>('/settings') });
  const { data: people } = useQuery({ queryKey: ['users-lookup'], queryFn: () => get<{ id: string; fullName: string }[]>('/users/lookup'), retry: false });
  const [ids, setIds] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  useEffect(() => { if (data) setIds((data.settings.contractAlertRecipientIds as string[] | undefined) ?? []); }, [data]);
  const saveIt = async () => {
    if (!data) return;
    setBusy(true);
    try {
      await put('/settings', { name: data.name, settings: { ...data.settings, contractAlertRecipientIds: ids } });
      notifySuccess(t('common.saved'));
      await qc.invalidateQueries({ queryKey: ['settings'] });
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Card>
      <Title order={5}>{t('contracts.alertRecipients')}</Title>
      <Text fz="sm" c="dimmed" mb="sm">{t('contracts.alertRecipientsHint')}</Text>
      <Group align="flex-end">
        <MultiSelect style={{ flex: 1 }} searchable clearable disabled={!editable} data={(people ?? []).map((p) => ({ value: p.id, label: p.fullName }))} value={ids} onChange={setIds}
          aria-label={t('contracts.alertRecipients')} />
        {editable && <Button loading={busy} onClick={saveIt}>{t('common.save')}</Button>}
      </Group>
    </Card>
  );
}
