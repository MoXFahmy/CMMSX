import { useState } from 'react';
import { ActionIcon, Alert, Badge, Button, Card, Checkbox, Group, Modal, Stack, Table, Tabs, Text, TextInput, Textarea, Title, Tooltip } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { DataTable } from 'mantine-datatable';
import { IconCopy, IconEdit, IconInfoCircle, IconPlus, IconTrash } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import type { PermissionInfo, Role } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { MENU_MODULES } from '@/components/menuModules';
import { confirmDelete } from '@/components/confirm';
import { handleFormError, notifyError, notifySuccess } from '@/components/utils';
import type { HomeCard } from '@/pages/home/cards';
import { RoleHomeEditor, tilesOk } from './RoleHomeEditor';

/** Each hideable menu item's label, as the menu shows it. */
function useModuleLabel() {
  const { t } = useTranslation();
  const { terms } = useAuth();
  const labels: Record<string, string> = {
    locations: terms.locations, assets: terms.assets, 'work-requests': t('nav.workRequests'), 'work-orders': t('nav.workOrders'), 'work-groups': t('nav.workGroups'),
    planning: t('nav.planning'), availability: t('nav.availability'), timesheet: t('nav.timesheet'), pm: t('nav.preventive'), contracts: t('nav.contracts'),
    inventory: t('nav.inventory'), purchasing: t('nav.purchasing'), approvals: t('nav.myApprovals'), reports: t('nav.reports'), budgets: t('nav.budgets'), people: t('nav.people'),
  };
  return (key: string) => labels[key] ?? key;
}

/** Settings › Roles: permissions, and since 0.22.0 the role's home page, menu and default columns. */
export function RoleSettings() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const moduleLabel = useModuleLabel();
  const { data, isFetching } = useQuery({ queryKey: ['roles'], queryFn: () => get<Role[]>('/roles') });
  const { data: perms } = useQuery({ queryKey: ['permissions'], queryFn: () => get<PermissionInfo[]>('/roles/permissions') });
  const [opened, m] = useDisclosure();
  const [editing, setEditing] = useState<Role | null>(null);
  const [home, setHome] = useState<HomeCard[]>([]);
  const [hidden, setHidden] = useState<string[]>([]);
  const [columns, setColumns] = useState<string[]>([]);
  const form = useForm({ initialValues: { name: '', description: '', permissions: [] as string[] } });
  const open = (r: Role | null) => {
    setEditing(r);
    form.setValues(r ? { name: r.name, description: r.description ?? '', permissions: r.permissions } : { name: '', description: '', permissions: [] });
    setHome(r?.home ?? []);
    setHidden(r?.hiddenModules ?? []);
    setColumns(r?.defaultColumns ?? []);
    form.clearErrors(); m.open();
  };
  const refresh = () => qc.invalidateQueries({ queryKey: ['roles'] });
  const submit = form.onSubmit(async (v) => {
    if (!tilesOk(home)) { notifyError(new Error(t('roles.home.fixTiles'))); return; }
    const body = { ...v, home, hiddenModules: hidden };
    try {
      if (editing) await put(`/roles/${editing.id}`, body); else await post('/roles', body);
      notifySuccess(t('common.saved'));
      await Promise.all([refresh(), qc.invalidateQueries({ queryKey: ['me'] })]);
      m.close();
    } catch (e) { handleFormError(form, e); }
  });
  const duplicate = async (r: Role) => {
    try { const c = await post<{ id: string; name: string }>(`/roles/${r.id}/duplicate`); notifySuccess(t('roles.duplicated', { name: c.name })); await refresh(); }
    catch (e) { notifyError(e); }
  };
  const clearColumns = async (list: string) => {
    if (!editing) return;
    try { await del(`/roles/${editing.id}/columns/${list}`); setColumns((c) => c.filter((x) => x !== list)); await refresh(); } catch (e) { notifyError(e); }
  };
  const groups = [...new Set((perms ?? []).map((p) => p.group))];
  const togglePerm = (code: string) => form.setFieldValue('permissions', form.values.permissions.includes(code) ? form.values.permissions.filter((x) => x !== code) : [...form.values.permissions, code]);
  const rolePerms = editing?.isSystem ? (perms ?? []).map((p) => p.code) : form.values.permissions;

  return (
    <Card p={0}>
      <Group justify="space-between" p="md"><Title order={5}>{t('settings.roles')}</Title><Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('settings.newRole')}</Button></Group>
      <DataTable minHeight={150} fetching={isFetching} records={data ?? []} columns={[
        { accessor: 'name', title: t('common.name'), render: (r) => (
          <Group gap={6}>
            <Text fz="sm" fw={500}>{r.name}</Text>
            {r.isSystem && <Badge size="xs" variant="outline">{t('settings.systemRole')}</Badge>}
            {r.home && r.home.length > 0 && <Badge size="xs" variant="light">{t('roles.homeBadge')}</Badge>}
          </Group>) },
        { accessor: 'description', title: t('common.description') },
        { accessor: 'permissions', title: t('settings.permissions'), render: (r) => `${r.permissions.length} / ${perms?.length ?? '…'}` },
        { accessor: 'userCount', title: t('settings.users_count'), textAlign: 'right' },
        { accessor: 'actions', title: '', width: 120, render: (r) => (
          <Group gap={4} justify="flex-end" wrap="nowrap">
            <ActionIcon variant="subtle" onClick={() => open(r)} aria-label={t('common.edit')}><IconEdit size={16} /></ActionIcon>
            <Tooltip label={t('roles.duplicate')}><ActionIcon variant="subtle" onClick={() => void duplicate(r)} aria-label={t('roles.duplicate')}><IconCopy size={16} /></ActionIcon></Tooltip>
            {!r.isSystem && <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => confirmDelete(r.name, async () => { try { await del(`/roles/${r.id}`); await refresh(); } catch (e) { notifyError(e); } })}><IconTrash size={16} /></ActionIcon>}
          </Group>) },
      ]} />
      <Modal opened={opened} onClose={m.close} title={editing ? t('settings.editRole') : t('settings.newRole')} size="xl">
        <form onSubmit={submit}><Stack>
          <TextInput label={t('common.name')} required disabled={editing?.isSystem} {...form.getInputProps('name')} />
          <Textarea label={t('common.description')} autosize {...form.getInputProps('description')} />
          <Tabs defaultValue="permissions" keepMounted={false}>
            <Tabs.List mb="sm">
              <Tabs.Tab value="permissions">{t('settings.permissions')}</Tabs.Tab>
              <Tabs.Tab value="home">{t('roles.tabs.home')}</Tabs.Tab>
              <Tabs.Tab value="menu">{t('roles.tabs.menu')}</Tabs.Tab>
              <Tabs.Tab value="columns">{t('roles.tabs.columns')}</Tabs.Tab>
            </Tabs.List>
            <Tabs.Panel value="permissions">
              <Table withTableBorder>
                <Table.Tbody>
                  {groups.map((g) => (
                    <Table.Tr key={g}>
                      <Table.Td w={140} fw={600} fz="sm" style={{ verticalAlign: 'top' }}>{g}</Table.Td>
                      <Table.Td><Stack gap={6}>{(perms ?? []).filter((p) => p.group === g).map((p) => (
                        <Checkbox key={p.code} label={p.name} checked={editing?.isSystem || form.values.permissions.includes(p.code)} disabled={editing?.isSystem} onChange={() => togglePerm(p.code)} />
                      ))}</Stack></Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Tabs.Panel>
            <Tabs.Panel value="home">
              <RoleHomeEditor cards={home} onChange={setHome} permissions={rolePerms} permissionInfo={perms ?? []} />
            </Tabs.Panel>
            <Tabs.Panel value="menu">
              <Stack gap="xs">
                <Alert variant="light" icon={<IconInfoCircle size={16} />}>{t('roles.menu.note')}</Alert>
                {MENU_MODULES.map((mod) => (
                  <Checkbox key={mod.key} label={moduleLabel(mod.key)} checked={!hidden.includes(mod.key)}
                    onChange={(e) => setHidden(e.currentTarget.checked ? hidden.filter((x) => x !== mod.key) : [...hidden, mod.key])} />
                ))}
                <Text fz="xs" c="dimmed">{t('roles.menu.severalRoles')}</Text>
              </Stack>
            </Tabs.Panel>
            <Tabs.Panel value="columns">
              <Stack gap="xs">
                {columns.length === 0 ? <Text fz="sm" c="dimmed">{t('roles.columns.none')}</Text> : columns.map((list) => (
                  <Group key={list} justify="space-between">
                    <Text fz="sm">{t(`home.lists.${list}`)}</Text>
                    <Button size="xs" variant="subtle" color="red" onClick={() => void clearColumns(list)}>{t('roles.columns.clear')}</Button>
                  </Group>
                ))}
                <Text fz="xs" c="dimmed">{t('roles.columns.where')}</Text>
              </Stack>
            </Tabs.Panel>
          </Tabs>
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack></form>
      </Modal>
    </Card>
  );
}
