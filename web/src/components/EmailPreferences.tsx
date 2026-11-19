import { useEffect, useState } from 'react';
import { Alert, Button, Group, Modal, Stack, Switch, Text } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { get, put } from '@/api/client';
import type { EmailStatusInfo } from '@/api/types';
import { notifyError, notifySuccess } from '@/components/utils';
import { eventKey } from '@/pages/settings/NotificationSettings';

/** The signed-in user's own choice of notification e-mails (sending a PO to a supplier isn't one: it goes to the supplier). */
export function EmailPreferences({ opened, onClose }: { opened: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data: status } = useQuery({ queryKey: ['email-status'], queryFn: () => get<EmailStatusInfo>('/email/status'), enabled: opened });
  const { data: saved } = useQuery({ queryKey: ['email-opt-outs'], queryFn: () => get<string[]>('/auth/me/email-opt-outs'), enabled: opened });
  const [optOuts, setOptOuts] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);
  useEffect(() => { if (saved) setOptOuts(saved); }, [saved]);
  const events = (status?.events ?? []).filter((k) => k !== 'po.sent');

  const save = async () => {
    setSaving(true);
    try {
      await put('/auth/me/email-opt-outs', optOuts);
      await qc.invalidateQueries({ queryKey: ['email-opt-outs'] });
      notifySuccess(t('common.saved'));
      onClose();
    } catch (e) { notifyError(e); } finally { setSaving(false); }
  };

  return (
    <Modal opened={opened} onClose={onClose} title={t('email.myTitle')} size="lg">
      <Stack>
        <Text fz="sm" c="dimmed">{t('email.myHint')}</Text>
        {status && (!status.available || !status.enabled) && <Alert color="yellow">{t('email.orgOff')}</Alert>}
        {events.map((key) => (
          <Switch key={key} label={t(`email.events.${eventKey(key)}.label`)} description={t(`email.events.${eventKey(key)}.description`)}
            checked={!optOuts.includes(key)} onChange={(e) => {
              const on = e.currentTarget.checked;
              setOptOuts((cur) => (on ? cur.filter((x) => x !== key) : [...cur, key]));
            }} />
        ))}
        <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button onClick={save} loading={saving}>{t('common.save')}</Button></Group>
      </Stack>
    </Modal>
  );
}
