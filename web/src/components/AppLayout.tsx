import { AppShell, Avatar, Badge, Burger, Divider, Group, Menu, NavLink, ScrollArea, Text, UnstyledButton, useMantineColorScheme, Modal, PasswordInput, Button, Stack } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useQueryClient } from '@tanstack/react-query';
import { Link, Outlet, useLocation, useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import {
  IconLayoutDashboard, IconMapPin, IconBox, IconClipboardText, IconTool, IconCalendarRepeat, IconPackage, IconShoppingCart,
  IconChartBar, IconSettings, IconLogout, IconKey, IconMoon, IconSun, IconChevronDown, IconUsers,
} from '@tabler/icons-react';
import { post } from '@/api/client';
import { P, useAuth } from '@/auth/AuthContext';
import { handleFormError, notifySuccess } from './utils';

type Item = { label: string; to: string; icon: typeof IconBox; perm?: string; soon?: boolean };

export function AppLayout() {
  const { t } = useTranslation();
  const { me, can, terms } = useAuth();
  const [opened, { toggle, close }] = useDisclosure();
  const [pwOpen, pw] = useDisclosure();
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { colorScheme, toggleColorScheme } = useMantineColorScheme();

  const sections: { title?: string; items: Item[] }[] = [
    { items: [{ label: t('nav.dashboard'), to: '/', icon: IconLayoutDashboard, perm: P.dashboardView }] },
    { title: t('nav.operations'), items: [
      { label: terms.locations, to: '/locations', icon: IconMapPin, perm: P.locationsView },
      { label: terms.assets, to: '/assets', icon: IconBox, perm: P.assetsView },
    ] },
    { title: t('nav.maintenance'), items: [
      { label: t('nav.workRequests'), to: '/work-requests', icon: IconClipboardText, perm: P.requestsCreate },
      { label: t('nav.workOrders'), to: '/work-orders', icon: IconTool, perm: P.woView },
      { label: t('nav.preventive'), to: '/soon/preventive-maintenance', icon: IconCalendarRepeat, soon: true },
    ] },
    { title: t('nav.materials'), items: [
      { label: t('nav.inventory'), to: '/soon/inventory', icon: IconPackage, soon: true },
      { label: t('nav.purchasing'), to: '/soon/purchasing', icon: IconShoppingCart, soon: true },
      { label: t('nav.reports'), to: '/soon/reports', icon: IconChartBar, soon: true },
    ] },
    { title: t('nav.administration'), items: [
      { label: t('nav.people'), to: '/people', icon: IconUsers, perm: P.peopleView },
      { label: t('nav.settings'), to: '/settings', icon: IconSettings },
    ] },
  ];

  const isActive = (to: string) => (to === '/' ? pathname === '/' : pathname.startsWith(to));
  const logout = async () => {
    await post('/auth/logout');
    qc.clear();
    navigate('/login');
    window.location.reload();
  };
  const initials = (me?.user.fullName ?? '?').split(' ').map((p) => p[0]).slice(0, 2).join('').toUpperCase();

  return (
    <AppShell header={{ height: 56 }} navbar={{ width: 248, breakpoint: 'sm', collapsed: { mobile: !opened } }} padding="lg">
      <AppShell.Header>
        <Group h="100%" px="md" justify="space-between" wrap="nowrap">
          <Group gap="sm" wrap="nowrap">
            <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" aria-label="Toggle navigation" />
            <img src="/favicon.svg" width={28} height={28} alt="" />
            <Text fw={700} visibleFrom="xs">{t('app.name')}</Text>
            <Divider orientation="vertical" visibleFrom="md" />
            <Text c="dimmed" fz="sm" visibleFrom="md" lineClamp={1}>{me?.tenant.name}</Text>
          </Group>
          <Group gap="xs" wrap="nowrap">
            <UnstyledButton onClick={() => toggleColorScheme()} aria-label="Toggle color scheme" p={6}>
              {colorScheme === 'dark' ? <IconSun size={18} /> : <IconMoon size={18} />}
            </UnstyledButton>
            <Menu position="bottom-end" width={220} shadow="md">
              <Menu.Target>
                <UnstyledButton>
                  <Group gap={8} wrap="nowrap">
                    <Avatar color="brand" radius="xl" size={32}>{initials}</Avatar>
                    <Text fz="sm" fw={500} visibleFrom="sm" lineClamp={1} maw={160}>{me?.user.fullName}</Text>
                    <IconChevronDown size={14} />
                  </Group>
                </UnstyledButton>
              </Menu.Target>
              <Menu.Dropdown>
                <Menu.Label>{me?.user.email}</Menu.Label>
                <Menu.Item leftSection={<IconKey size={16} />} onClick={pw.open}>{t('common.changePassword')}</Menu.Item>
                <Menu.Divider />
                <Menu.Item color="red" leftSection={<IconLogout size={16} />} onClick={logout}>{t('common.logout')}</Menu.Item>
              </Menu.Dropdown>
            </Menu>
          </Group>
        </Group>
      </AppShell.Header>
      <AppShell.Navbar p="xs">
        <ScrollArea>
          {sections.map((s, i) => {
            const items = s.items.filter((it) => !it.perm || can(it.perm));
            if (!items.length) return null;
            return (
              <div key={i}>
                {s.title && <Text fz={11} fw={700} c="dimmed" tt="uppercase" px="sm" mt="md" mb={4}>{s.title}</Text>}
                {items.map((it) => (
                  <NavLink key={it.to} component={Link} to={it.to} label={it.label} active={isActive(it.to)} onClick={close}
                    leftSection={<it.icon size={18} stroke={1.6} />} style={{ borderRadius: 8 }} c={it.soon ? 'dimmed' : undefined}
                    rightSection={it.soon ? <Badge size="xs" variant="default">{t('nav.soon')}</Badge> : undefined} />
                ))}
              </div>
            );
          })}
        </ScrollArea>
      </AppShell.Navbar>
      <AppShell.Main>
        <Outlet />
      </AppShell.Main>
      <ChangePasswordModal opened={pwOpen} onClose={pw.close} />
    </AppShell>
  );
}

function ChangePasswordModal({ opened, onClose }: { opened: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const form = useForm({ initialValues: { currentPassword: '', newPassword: '' } });
  const submit = form.onSubmit(async (v) => {
    try {
      await post('/auth/change-password', v);
      notifySuccess(t('auth.passwordChanged'));
      form.reset();
      onClose();
    } catch (e) { handleFormError(form, e); }
  });
  return (
    <Modal opened={opened} onClose={onClose} title={t('common.changePassword')}>
      <form onSubmit={submit}>
        <Stack>
          <PasswordInput label={t('auth.currentPassword')} required {...form.getInputProps('currentPassword')} />
          <PasswordInput label={t('auth.newPassword')} required {...form.getInputProps('newPassword')} />
          <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button type="submit">{t('common.save')}</Button></Group>
        </Stack>
      </form>
    </Modal>
  );
}
