import { AppShell, Avatar, Badge, Burger, Divider, Group, Menu, NavLink, ScrollArea, Text, UnstyledButton, useMantineColorScheme, Modal, PasswordInput, Button, Stack, Alert, Anchor, Box } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useQueryClient } from '@tanstack/react-query';
import { Link, Outlet, useLocation, useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { DevicesModal } from './DevicesModal';
import { LanguageSwitch } from './LanguageSwitch';
import { useOnline } from './useOnline';
import { appVersionLabel } from '@/components/utils';
import { AlertBell } from '@/components/AlertBell';
import { SiteSwitch } from '@/components/SiteSwitch';
import { EmailPreferences } from '@/components/EmailPreferences';
import {
  IconLayoutDashboard, IconMapPin, IconBox, IconClipboardText, IconTool, IconCalendarRepeat, IconPackage, IconShoppingCart,
  IconChartBar, IconSettings, IconLogout, IconKey, IconMoon, IconSun, IconChevronDown, IconUsers, IconMail, IconDeviceMobile, IconDevices, IconCloudOff, IconCalendarWeek, IconStack2, IconSignature, IconFileCertificate, IconBeach, IconClockHour4, IconCoin } from '@tabler/icons-react';
import { post } from '@/api/client';
import { P, useAuth } from '@/auth/AuthContext';
import { handleFormError, notifySuccess } from './utils';
import { menuModule } from './menuModules';

type Item = { label: string; to: string; icon: typeof IconBox; perm?: string; anyPerm?: string[]; soon?: boolean; module?: string; show?: boolean };

export function AppLayout() {
  const { t } = useTranslation();
  const { me, can, terms } = useAuth();
  const [opened, { toggle, close }] = useDisclosure();
  const [pwOpen, pw] = useDisclosure();
  const [mailOpen, mailPrefs] = useDisclosure();
  const [devicesOpen, devices] = useDisclosure();
  const online = useOnline();
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { colorScheme, toggleColorScheme } = useMantineColorScheme();

  const roleHome = (me?.home?.cards.length ?? 0) > 0;
  const hidden = me?.home?.hiddenModules ?? [];
  const sections: { title?: string; items: Item[] }[] = [
    // Home for people whose roles have a home page (0.22.0), today's Dashboard for the rest.
    { items: [{ label: roleHome ? t('nav.home') : t('nav.dashboard'), to: '/', icon: IconLayoutDashboard, show: roleHome || can(P.dashboardView) }] },
    { title: t('nav.operations'), items: [
      { label: terms.locations, to: '/locations', icon: IconMapPin, ...menuModule('locations') },
      { label: terms.assets, to: '/assets', icon: IconBox, ...menuModule('assets') },
    ] },
    { title: t('nav.maintenance'), items: [
      { label: t('nav.workRequests'), to: '/work-requests', icon: IconClipboardText, ...menuModule('work-requests') },
      { label: t('nav.workOrders'), to: '/work-orders', icon: IconTool, ...menuModule('work-orders') },
      { label: t('nav.workGroups'), to: '/work-groups', icon: IconStack2, ...menuModule('work-groups') },
      { label: t('nav.planning'), to: '/planning', icon: IconCalendarWeek, ...menuModule('planning') },
      { label: t('nav.availability'), to: '/planning/availability', icon: IconBeach, ...menuModule('availability') },
      { label: t('nav.timesheet'), to: '/planning/timesheet', icon: IconClockHour4, ...menuModule('timesheet') },
      { label: t('nav.preventive'), to: '/pm', icon: IconCalendarRepeat, ...menuModule('pm') },
      { label: t('nav.contracts'), to: '/contracts', icon: IconFileCertificate, ...menuModule('contracts') },
    ] },
    { title: t('nav.materials'), items: [
      { label: t('nav.inventory'), to: '/inventory', icon: IconPackage, ...menuModule('inventory') },
      { label: t('nav.purchasing'), to: '/purchasing', icon: IconShoppingCart, ...menuModule('purchasing') },
      { label: t('nav.myApprovals'), to: '/approvals', icon: IconSignature, ...menuModule('approvals') },
    ] },
    { title: t('nav.insights'), items: [
      { label: t('nav.reports'), to: '/reports', icon: IconChartBar, ...menuModule('reports') },
      { label: t('nav.budgets'), to: '/reports/budgets', icon: IconCoin, ...menuModule('budgets') },
    ] },
    { title: t('nav.administration'), items: [
      { label: t('nav.people'), to: '/people', icon: IconUsers, ...menuModule('people') },
      { label: t('nav.settings'), to: '/settings', icon: IconSettings },
    ] },
  ];

  // The most specific item wins: Budgets (/reports/budgets) lights up instead of Reports.
  const allTo = sections.flatMap((x) => x.items.map((i) => i.to));
  const isActive = (to: string) => (to === '/' ? pathname === '/'
    : pathname.startsWith(to) && !allTo.some((o) => o !== to && o.startsWith(to) && pathname.startsWith(o)));
  const logout = async () => {
    await post('/auth/logout');
    // A shared phone keeps nothing of this person's jobs (the technician view's copy); their unsent changes stay for them.
    try {
      const [{ forgetUser }, store] = await Promise.all([import('@/tech/identity'), import('@/tech/store')]);
      await forgetUser(store);
    } catch { /* no IndexedDB here: nothing to forget */ }
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
            <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" aria-label={t('common.toggleNavigation')} />
            <img src="/favicon.svg" width={28} height={28} alt="" />
            <Text fw={700} visibleFrom="xs">{t('app.name')}</Text>
            <Divider orientation="vertical" visibleFrom="md" />
            <Text c="dimmed" fz="sm" visibleFrom="md" lineClamp={1}>{me?.tenant.name}</Text>
          </Group>
          <Group gap="xs" wrap="nowrap">
            <SiteSwitch />
            <AlertBell />
            <UnstyledButton onClick={() => toggleColorScheme()} aria-label={t('common.toggleColorScheme')} p={6}>
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
                <Box px="sm" py={6}><LanguageSwitch /></Box>
                <Menu.Item leftSection={<IconKey size={16} />} onClick={pw.open}>{t('common.changePassword')}</Menu.Item>
                <Menu.Item leftSection={<IconMail size={16} />} onClick={mailPrefs.open}>{t('email.myTitle')}</Menu.Item>
                <Menu.Item leftSection={<IconDevices size={16} />} onClick={devices.open}>{t('devices.title')}</Menu.Item>
                {me?.employeeId && (
                  <Menu.Item leftSection={<IconDeviceMobile size={16} />} component="a" href="/tech/"
                    onClick={() => { try { localStorage.removeItem('cmms.tech.mode'); } catch { /* private mode */ } }}>{t('tech.title')}</Menu.Item>
                )}
                <Menu.Divider />
                <Menu.Item color="red" leftSection={<IconLogout size={16} />} onClick={logout}>{t('common.logout')}</Menu.Item>
                <Menu.Divider />
                <Menu.Label>{appVersionLabel(t)}</Menu.Label>
              </Menu.Dropdown>
            </Menu>
          </Group>
        </Group>
      </AppShell.Header>
      <AppShell.Navbar p="xs">
        <ScrollArea>
          {sections.map((s, i) => {
            // A role may hide items from the menu: tidying only, the pages still check permissions.
            const items = s.items.filter((it) => (it.show ?? true) && (!it.perm || can(it.perm)) && (!it.anyPerm || it.anyPerm.some((x) => can(x)))
              && !(it.module && hidden.includes(it.module)));
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
        {!online && (
          <Alert color="orange" icon={<IconCloudOff size={18} />} mb="md" title={t('offline.title')}>
            {t('offline.body')}
            {me?.employeeId && (
              <> <Anchor href="/tech/" onClick={() => { try { localStorage.removeItem('cmms.tech.mode'); } catch { /* private mode */ } }}>{t('offline.techView')}</Anchor></>
            )}
          </Alert>
        )}
        <Outlet />
      </AppShell.Main>
      <ChangePasswordModal opened={pwOpen} onClose={pw.close} />
      <EmailPreferences opened={mailOpen} onClose={mailPrefs.close} />
      <DevicesModal opened={devicesOpen} onClose={devices.close} />
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
