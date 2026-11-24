import { ActionIcon, Card, Group, Menu, SimpleGrid, Stack, Text, Tooltip } from '@mantine/core';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconArrowLeft, IconArrowRight, IconDots, IconPinnedOff } from '@tabler/icons-react';
import { get, put } from '@/api/client';
import type { Paged } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { notifyError } from '@/components/utils';
import { counterAccess, counterLink, counterRequest, moveInOrder, type PinnedView } from './counters';

const HOME_KEY = ['views', 'home'];

function Tile({ view, order }: { view: PinnedView; order: string[] }) {
  const { t } = useTranslation();
  const { can } = useAuth();
  const qc = useQueryClient();
  const allowed = counterAccess(view.list, can);
  const req = counterRequest(view, can);
  const { data, error } = useQuery({
    queryKey: ['home-counter', view.id, view.query], queryFn: () => get<Paged<unknown>>(req.path, req.query),
    enabled: allowed, refetchInterval: 60_000,
  });
  const refresh = () => Promise.all([qc.invalidateQueries({ queryKey: HOME_KEY }), qc.invalidateQueries({ queryKey: ['views', view.list] })]);
  const move = useMutation({ mutationFn: (by: -1 | 1) => put('/views/home-order', { ids: moveInOrder(order, view.id, by) }), onSuccess: refresh, onError: notifyError });
  const unpin = useMutation({ mutationFn: () => put(`/views/${view.id}/pin`, { pinned: false }), onSuccess: refresh, onError: notifyError });
  const link = counterLink(view);
  const value = !allowed ? <Text fz="sm" c="dimmed">{t('home.noAccess')}</Text>
    : error ? <Tooltip label={error instanceof Error ? error.message : String(error)}><Text fz={28} fw={700} lh={1.1} c="dimmed">—</Text></Tooltip>
    : <Text fz={28} fw={700} lh={1.1}>{data ? data.total.toLocaleString() : '…'}</Text>;
  return (
    <Card p="sm" style={{ position: 'relative' }}>
      <Menu position="bottom-end" shadow="md">
        <Menu.Target>
          <ActionIcon variant="subtle" color="gray" size="sm" aria-label={t('home.tileMenu')} style={{ position: 'absolute', insetInlineEnd: 6, top: 6 }}><IconDots size={14} /></ActionIcon>
        </Menu.Target>
        <Menu.Dropdown>
          <Menu.Item leftSection={<IconArrowLeft size={14} />} disabled={order[0] === view.id} onClick={() => move.mutate(-1)}>{t('home.moveEarlier')}</Menu.Item>
          <Menu.Item leftSection={<IconArrowRight size={14} />} disabled={order[order.length - 1] === view.id} onClick={() => move.mutate(1)}>{t('home.moveLater')}</Menu.Item>
          <Menu.Item leftSection={<IconPinnedOff size={14} />} onClick={() => unpin.mutate()}>{t('home.removeFromHome')}</Menu.Item>
        </Menu.Dropdown>
      </Menu>
      <Link to={link.to} state={link.state} style={{ color: 'inherit', textDecoration: 'none' }}>
        <Stack gap={2} pe={24}>
          <Text fz="xs" c="dimmed" tt="uppercase" fw={700} lineClamp={1}>{t(`home.lists.${view.list}`)}</Text>
          {value}
          <Text fz="sm" fw={500} lineClamp={1}>{view.name}</Text>
        </Stack>
      </Link>
    </Card>
  );
}

/** "My counters": the person's pinned saved views, each showing how many records match right now. */
export function Counters() {
  const { t } = useTranslation();
  const { data } = useQuery({ queryKey: HOME_KEY, queryFn: () => get<PinnedView[]>('/views/home') });
  if (!data) return null;
  if (data.length === 0) return <Text fz="sm" c="dimmed" mb="md">{t('home.pinHint')}</Text>;
  const order = data.map((v) => v.id);
  return (
    <Stack gap={6} mb="lg">
      <Group justify="space-between"><Text fw={600} fz="sm">{t('home.myCounters')}</Text></Group>
      <SimpleGrid cols={{ base: 2, sm: 3, md: 4, lg: 6 }} spacing="sm">
        {data.map((v) => <Tile key={v.id} view={v} order={order} />)}
      </SimpleGrid>
    </Stack>
  );
}
