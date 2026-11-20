import { Badge, Button, Group, Modal, Stack, Text } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { get, post } from '@/api/client';
import { fmtDateTime, fromNow, notifyError, notifySuccess } from './utils';

type Device = { id: string; deviceName: string; createdAt: string; lastSeenAt: string; current: boolean };

/**
 * Signed-in devices. Without `user`: my own (with "sign out all others"); with `user`: an administrator's view of that
 * user's devices. A signed-out device stops working within a minute; changes still queued on it stay on that phone.
 */
export function DevicesModal({ opened, onClose, user }: { opened: boolean; onClose: () => void; user?: { id: string; fullName: string } }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const base = user ? `/users/${user.id}/devices` : '/auth/me/devices';
  const { data } = useQuery({ queryKey: ['devices', user?.id ?? 'me'], queryFn: () => get<Device[]>(base), enabled: opened });
  const reload = () => qc.invalidateQueries({ queryKey: ['devices', user?.id ?? 'me'] });
  const signOut = async (d: Device) => {
    try { await post(`${base}/${d.id}/sign-out`); notifySuccess(t('devices.signedOut', { name: d.deviceName })); await reload(); } catch (e) { notifyError(e); }
  };
  const signOutOthers = async () => {
    try { await post('/auth/me/devices/sign-out-others'); notifySuccess(t('devices.othersSignedOut')); await reload(); } catch (e) { notifyError(e); }
  };
  return (
    <Modal opened={opened} onClose={onClose} title={user ? t('devices.titleFor', { name: user.fullName }) : t('devices.title')} size="lg">
      <Stack>
        <Text fz="sm" c="dimmed">{t('devices.hint')}</Text>
        {(data ?? []).length === 0 && <Text fz="sm" c="dimmed">{t('devices.none')}</Text>}
        {(data ?? []).map((d) => (
          <Group key={d.id} justify="space-between" wrap="nowrap">
            <div>
              <Group gap={6}><Text fw={600} fz="sm">{d.deviceName}</Text>{d.current && <Badge size="xs" variant="light">{t('devices.thisDevice')}</Badge>}</Group>
              <Text fz="xs" c="dimmed">{t('devices.seen', { last: fromNow(d.lastSeenAt), first: fmtDateTime(d.createdAt) })}</Text>
            </div>
            {!d.current && <Button size="xs" variant="light" color="red" onClick={() => void signOut(d)}>{t('devices.signOut')}</Button>}
          </Group>
        ))}
        {!user && (data ?? []).some((d) => !d.current) && (
          <Button variant="default" color="red" onClick={() => void signOutOthers()}>{t('devices.signOutOthers')}</Button>
        )}
      </Stack>
    </Modal>
  );
}
