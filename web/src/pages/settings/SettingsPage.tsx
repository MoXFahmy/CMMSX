import { useEffect, useState } from 'react';
import { ActionIcon, Badge, Button, Card, Checkbox, ColorInput, Group, Modal, MultiSelect, NumberInput, PasswordInput, Select, SimpleGrid, Stack, Switch, Table, Tabs, TagsInput, Text, TextInput, Textarea, Title } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { DataTable } from 'mantine-datatable';
import { IconEdit, IconKey, IconPlus, IconTrash } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import { FIELD_TYPES, type Category, type CustomField, type PermissionInfo, type Role, type TenantSettings, type User } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { confirmDelete } from '@/components/confirm';
import { fromNow, handleFormError, notifyError, notifySuccess } from '@/components/utils';

export function SettingsPage() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const { '*': sub } = useParams();
  const navigate = useNavigate();
  const tabs = [
    { value: 'organization', label: t('settings.organization'), show: true },
    { value: 'categories', label: t('settings.categories'), show: can(P.assetsView) },
    { value: 'fields', label: t('settings.customFields'), show: can(P.settingsManage) },
    { value: 'users', label: t('settings.users'), show: can(P.usersManage) },
    { value: 'roles', label: t('settings.roles'), show: can(P.usersManage) },
  ].filter((x) => x.show);
  const current = tabs.some((x) => x.value === sub) ? sub! : 'organization';
  return (
    <>
      <PageHeader title={t('settings.title')} />
      <Tabs value={current} onChange={(v) => navigate(`/settings/${v}`)} keepMounted={false}>
        <Tabs.List mb="lg">{tabs.map((x) => <Tabs.Tab key={x.value} value={x.value}>{x.label}</Tabs.Tab>)}</Tabs.List>
        <Tabs.Panel value="organization"><OrganizationSettings /></Tabs.Panel>
        <Tabs.Panel value="categories"><CategorySettings /></Tabs.Panel>
        <Tabs.Panel value="fields"><FieldSettings /></Tabs.Panel>
        <Tabs.Panel value="users"><UserSettings /></Tabs.Panel>
        <Tabs.Panel value="roles"><RoleSettings /></Tabs.Panel>
      </Tabs>
    </>
  );
}

function OrganizationSettings() {
  const { t } = useTranslation();
  const { can, refresh } = useAuth();
  const qc = useQueryClient();
  const editable = can(P.settingsManage);
  const { data } = useQuery({ queryKey: ['settings'], queryFn: () => get<{ name: string; slug: string; settings: TenantSettings }>('/settings') });
  const form = useForm<{ name: string; settings: TenantSettings }>({ initialValues: { name: '', settings: {} as TenantSettings } });
  useEffect(() => { if (data) { form.setValues({ name: data.name, settings: data.settings }); form.resetDirty(); } /* eslint-disable-next-line */ }, [data]);
  const sample = useMutation({
    mutationFn: () => post<{ created: number }>('/settings/sample-data'),
    onSuccess: (r) => { notifySuccess(t('dashboard.sampleLoaded', { count: r.created })); void qc.invalidateQueries(); },
    onError: notifyError,
  });
  if (!data || !form.values.settings.terminology) return null;
  const s = form.values.settings;
  const submit = form.onSubmit(async (v) => {
    try { await put('/settings', v); notifySuccess(t('common.saved')); await refresh(); await qc.invalidateQueries({ queryKey: ['settings'] }); } catch (e) { handleFormError(form, e); }
  });
  const termRow = (key: 'site' | 'location' | 'asset', plural: 'sites' | 'locations' | 'assets') => (
    <SimpleGrid cols={2} key={key}>
      <TextInput label={`${t('settings.singular')} (${key})`} disabled={!editable} {...form.getInputProps(`settings.terminology.${key}`)} />
      <TextInput label={t('settings.plural')} disabled={!editable} {...form.getInputProps(`settings.terminology.${plural}`)} />
    </SimpleGrid>
  );
  return (
    <form onSubmit={submit}>
      <Stack maw={760}>
        <Card><Stack>
          <Title order={5}>{t('settings.organization')}</Title>
          <TextInput label={t('auth.organizationName')} disabled={!editable} {...form.getInputProps('name')} />
          <SimpleGrid cols={2}>
            <TextInput label={t('settings.currency')} description="ISO code, e.g. USD, EGP, AED" maxLength={3} disabled={!editable} {...form.getInputProps('settings.currency')} />
            <TextInput label={t('locations.timeZone')} disabled={!editable} {...form.getInputProps('settings.timeZone')} />
          </SimpleGrid>
        </Stack></Card>
        <Card><Stack>
          <div><Title order={5}>{t('settings.terminology')}</Title><Text c="dimmed" fz="sm">{t('settings.terminologyHint')}</Text></div>
          {termRow('site', 'sites')}{termRow('location', 'locations')}{termRow('asset', 'assets')}
        </Stack></Card>
        <Card><Stack>
          <Title order={5}>{t('settings.numbering')}</Title>
          <Switch label={t('settings.autoNumber')} disabled={!editable} {...form.getInputProps('settings.autoNumberAssets', { type: 'checkbox' })} />
          <SimpleGrid cols={2}>
            <TextInput label={t('settings.prefix')} disabled={!editable} {...form.getInputProps('settings.assetNumberPrefix')} />
            <NumberInput label={t('settings.padding')} min={1} max={12} disabled={!editable} {...form.getInputProps('settings.assetNumberPadding')} />
          </SimpleGrid>
          <Text fz="sm" c="dimmed">{t('settings.example', { value: `${s.assetNumberPrefix}${'1'.padStart(Number(s.assetNumberPadding) || 1, '0')}` })}</Text>
        </Stack></Card>
        {editable && <Group justify="flex-end"><Button type="submit" loading={form.submitting}>{t('common.saveChanges')}</Button></Group>}
        {editable && (
          <Card><Group justify="space-between" wrap="nowrap">
            <div><Title order={5}>{t('settings.sampleData')}</Title><Text c="dimmed" fz="sm">{t('settings.sampleDataHint')}</Text></div>
            <Button variant="light" loading={sample.isPending} onClick={() => sample.mutate()}>{t('dashboard.loadSample')}</Button>
          </Group></Card>
        )}
      </Stack>
    </form>
  );
}

function CategorySettings() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const qc = useQueryClient();
  const editable = can(P.settingsManage);
  const { data, isFetching } = useQuery({ queryKey: ['categories'], queryFn: () => get<Category[]>('/asset-categories') });
  const [opened, m] = useDisclosure();
  const [editing, setEditing] = useState<Category | null>(null);
  const form = useForm({ initialValues: { code: '', name: '', description: '', color: '#228be6', isActive: true } });
  const open = (c: Category | null) => {
    setEditing(c);
    form.setValues(c ? { code: c.code, name: c.name, description: c.description ?? '', color: c.color ?? '#228be6', isActive: c.isActive } : { code: '', name: '', description: '', color: '#228be6', isActive: true });
    form.clearErrors(); m.open();
  };
  const submit = form.onSubmit(async (v) => {
    try { if (editing) await put(`/asset-categories/${editing.id}`, v); else await post('/asset-categories', v); notifySuccess(t('common.saved')); await qc.invalidateQueries({ queryKey: ['categories'] }); m.close(); }
    catch (e) { handleFormError(form, e); }
  });
  return (
    <Card p={0}>
      <Group justify="space-between" p="md"><Title order={5}>{t('settings.categories')}</Title>{editable && <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('settings.newCategory')}</Button>}</Group>
      <DataTable minHeight={150} fetching={isFetching} records={data ?? []} noRecordsText={t('common.noRecords')} columns={[
        { accessor: 'code', title: t('common.code'), width: 120 },
        { accessor: 'name', title: t('common.name'), render: (c) => <Group gap={8}><span style={{ width: 10, height: 10, borderRadius: 5, background: c.color ?? '#adb5bd', display: 'inline-block' }} />{c.name}</Group> },
        { accessor: 'assetCount', title: t('settings.assets'), textAlign: 'right', width: 100 },
        { accessor: 'isActive', title: t('common.status'), width: 110, render: (c) => <Badge color={c.isActive ? 'teal' : 'gray'}>{c.isActive ? t('common.active') : t('common.inactive')}</Badge> },
        { accessor: 'actions', title: '', width: 90, hidden: !editable, render: (c) => (
          <Group gap={4} justify="flex-end" wrap="nowrap">
            <ActionIcon variant="subtle" onClick={() => open(c)} aria-label={t('common.edit')}><IconEdit size={16} /></ActionIcon>
            <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => confirmDelete(c.name, async () => { try { await del(`/asset-categories/${c.id}`); await qc.invalidateQueries({ queryKey: ['categories'] }); } catch (e) { notifyError(e); } })}><IconTrash size={16} /></ActionIcon>
          </Group>) },
      ]} />
      <Modal opened={opened} onClose={m.close} title={editing ? t('common.edit') : t('settings.newCategory')}>
        <form onSubmit={submit}><Stack>
          <SimpleGrid cols={3}><TextInput label={t('common.code')} required {...form.getInputProps('code')} /><TextInput label={t('common.name')} required style={{ gridColumn: 'span 2' }} {...form.getInputProps('name')} /></SimpleGrid>
          <Textarea label={t('common.description')} autosize {...form.getInputProps('description')} />
          <ColorInput label={t('settings.color')} {...form.getInputProps('color')} />
          <Checkbox label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack></form>
      </Modal>
    </Card>
  );
}

function FieldSettings() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data, isFetching } = useQuery({ queryKey: ['custom-fields', 'Asset'], queryFn: () => get<CustomField[]>('/custom-fields', { entityType: 'Asset' }) });
  const { data: cats } = useQuery({ queryKey: ['categories'], queryFn: () => get<Category[]>('/asset-categories') });
  const [opened, m] = useDisclosure();
  const [editing, setEditing] = useState<CustomField | null>(null);
  const blank = { label: '', key: '', dataType: 'Text' as string, options: [] as string[], isRequired: false, sortOrder: 0, categoryIds: [] as string[], showInList: false };
  const form = useForm({ initialValues: blank });
  const open = (f: CustomField | null) => {
    setEditing(f);
    form.setValues(f ? { label: f.label, key: f.key, dataType: f.dataType, options: f.options, isRequired: f.isRequired, sortOrder: f.sortOrder, categoryIds: f.categoryIds, showInList: f.showInList } : { ...blank, sortOrder: (data?.length ?? 0) + 1 });
    form.clearErrors(); m.open();
  };
  const submit = form.onSubmit(async (v) => {
    const body = { ...v, entityType: 'Asset', key: v.key || null };
    try { if (editing) await put(`/custom-fields/${editing.id}`, body); else await post('/custom-fields', body); notifySuccess(t('common.saved')); await qc.invalidateQueries({ queryKey: ['custom-fields'] }); m.close(); }
    catch (e) { handleFormError(form, e); }
  });
  const catName = (id: string) => cats?.find((c) => c.id === id)?.name ?? '?';
  return (
    <Card p={0}>
      <Group justify="space-between" p="md"><Title order={5}>{t('settings.customFields')}</Title><Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('settings.newField')}</Button></Group>
      <DataTable minHeight={150} fetching={isFetching} records={data ?? []} noRecordsText={t('common.noRecords')} columns={[
        { accessor: 'sortOrder', title: '#', width: 50 },
        { accessor: 'label', title: t('settings.label'), render: (f) => <Stack gap={0}><Text fz="sm">{f.label}{f.isRequired && <Text span c="red"> *</Text>}</Text><Text fz="xs" c="dimmed">{f.key}</Text></Stack> },
        { accessor: 'dataType', title: t('settings.fieldType'), render: (f) => t(`settings.fieldTypes.${f.dataType}`) },
        { accessor: 'categoryIds', title: t('settings.categories'), render: (f) => f.categoryIds.length ? f.categoryIds.map(catName).join(', ') : t('common.all') },
        { accessor: 'showInList', title: t('settings.showInList'), render: (f) => (f.showInList ? '✓' : '') },
        { accessor: 'actions', title: '', width: 90, render: (f) => (
          <Group gap={4} justify="flex-end" wrap="nowrap">
            <ActionIcon variant="subtle" onClick={() => open(f)} aria-label={t('common.edit')}><IconEdit size={16} /></ActionIcon>
            <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => confirmDelete(f.label, async () => { try { await del(`/custom-fields/${f.id}`); await qc.invalidateQueries({ queryKey: ['custom-fields'] }); } catch (e) { notifyError(e); } })}><IconTrash size={16} /></ActionIcon>
          </Group>) },
      ]} />
      <Modal opened={opened} onClose={m.close} title={editing ? t('common.edit') : t('settings.newField')} size="lg">
        <form onSubmit={submit}><Stack>
          <SimpleGrid cols={2}>
            <TextInput label={t('settings.label')} required {...form.getInputProps('label')} />
            <TextInput label={t('settings.key')} disabled={!!editing} placeholder="auto" {...form.getInputProps('key')} />
            <Select label={t('settings.fieldType')} data={FIELD_TYPES.map((x) => ({ value: x, label: t(`settings.fieldTypes.${x}`) }))} {...form.getInputProps('dataType')} />
            <NumberInput label={t('settings.sortOrder')} {...form.getInputProps('sortOrder')} />
          </SimpleGrid>
          {form.values.dataType === 'Select' && <TagsInput label={t('settings.options')} description={t('settings.optionsHint')} {...form.getInputProps('options')} />}
          <MultiSelect label={t('settings.categories')} description={t('settings.categoriesHint')} data={(cats ?? []).map((c) => ({ value: c.id, label: c.name }))} searchable clearable {...form.getInputProps('categoryIds')} />
          <Group><Checkbox label={t('settings.requiredField')} {...form.getInputProps('isRequired', { type: 'checkbox' })} /><Checkbox label={t('settings.showInList')} {...form.getInputProps('showInList', { type: 'checkbox' })} /></Group>
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack></form>
      </Modal>
    </Card>
  );
}

function UserSettings() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data, isFetching } = useQuery({ queryKey: ['users'], queryFn: () => get<User[]>('/users') });
  const { data: roles } = useQuery({ queryKey: ['roles'], queryFn: () => get<Role[]>('/roles') });
  const [opened, m] = useDisclosure();
  const [pwUser, setPwUser] = useState<User | null>(null);
  const [editing, setEditing] = useState<User | null>(null);
  const blank = { fullName: '', email: '', jobTitle: '', phone: '', isActive: true, roleIds: [] as string[], password: '' };
  const form = useForm({ initialValues: blank });
  const pwForm = useForm({ initialValues: { password: '' } });
  const open = (u: User | null) => {
    setEditing(u);
    form.setValues(u ? { fullName: u.fullName, email: u.email, jobTitle: u.jobTitle ?? '', phone: u.phone ?? '', isActive: u.isActive, roleIds: u.roles.map((r) => r.roleId), password: '' } : blank);
    form.clearErrors(); m.open();
  };
  const submit = form.onSubmit(async (v) => {
    try { if (editing) await put(`/users/${editing.id}`, { ...v, password: null }); else await post('/users', v); notifySuccess(t('common.saved')); await qc.invalidateQueries({ queryKey: ['users'] }); m.close(); }
    catch (e) { handleFormError(form, e); }
  });
  const submitPw = pwForm.onSubmit(async (v) => {
    try { await post(`/users/${pwUser!.id}/password`, v); notifySuccess(t('settings.passwordReset')); setPwUser(null); pwForm.reset(); } catch (e) { handleFormError(pwForm, e); }
  });
  return (
    <Card p={0}>
      <Group justify="space-between" p="md"><Title order={5}>{t('settings.users')}</Title><Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('settings.newUser')}</Button></Group>
      <DataTable minHeight={150} fetching={isFetching} records={data ?? []} noRecordsText={t('common.noRecords')} columns={[
        { accessor: 'fullName', title: t('common.name'), render: (u) => <Stack gap={0}><Text fz="sm" fw={500}>{u.fullName}</Text><Text fz="xs" c="dimmed">{u.email}</Text></Stack> },
        { accessor: 'jobTitle', title: t('settings.jobTitle') },
        { accessor: 'roles', title: t('settings.rolesLabel'), render: (u) => <Group gap={4}>{u.roles.map((r) => <Badge key={r.roleId} variant="default">{r.name}</Badge>)}</Group> },
        { accessor: 'lastLoginAt', title: t('settings.lastLogin'), render: (u) => (u.lastLoginAt ? fromNow(u.lastLoginAt) : t('settings.never')) },
        { accessor: 'isActive', title: t('common.status'), render: (u) => <Badge color={u.isActive ? 'teal' : 'gray'}>{u.isActive ? t('common.active') : t('common.inactive')}</Badge> },
        { accessor: 'actions', title: '', width: 90, render: (u) => (
          <Group gap={4} justify="flex-end" wrap="nowrap">
            <ActionIcon variant="subtle" onClick={() => open(u)} aria-label={t('common.edit')}><IconEdit size={16} /></ActionIcon>
            <ActionIcon variant="subtle" color="gray" onClick={() => setPwUser(u)} aria-label={t('settings.resetPassword')}><IconKey size={16} /></ActionIcon>
          </Group>) },
      ]} />
      <Modal opened={opened} onClose={m.close} title={editing ? t('settings.editUser') : t('settings.newUser')} size="lg">
        <form onSubmit={submit}><Stack>
          <SimpleGrid cols={2}>
            <TextInput label={t('common.name')} required {...form.getInputProps('fullName')} />
            <TextInput label={t('auth.email')} required type="email" {...form.getInputProps('email')} />
            <TextInput label={t('settings.jobTitle')} {...form.getInputProps('jobTitle')} />
            <TextInput label={t('settings.phone')} {...form.getInputProps('phone')} />
          </SimpleGrid>
          <MultiSelect label={t('settings.rolesLabel')} required data={(roles ?? []).map((r) => ({ value: r.id, label: r.name }))} {...form.getInputProps('roleIds')} />
          {!editing && <PasswordInput label={t('settings.initialPassword')} required {...form.getInputProps('password')} />}
          <Switch label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack></form>
      </Modal>
      <Modal opened={!!pwUser} onClose={() => setPwUser(null)} title={`${t('settings.resetPassword')} · ${pwUser?.fullName ?? ''}`}>
        <form onSubmit={submitPw}><Stack>
          <PasswordInput label={t('auth.newPassword')} required {...pwForm.getInputProps('password')} />
          <Group justify="flex-end"><Button variant="default" onClick={() => setPwUser(null)}>{t('common.cancel')}</Button><Button type="submit">{t('common.save')}</Button></Group>
        </Stack></form>
      </Modal>
    </Card>
  );
}

function RoleSettings() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data, isFetching } = useQuery({ queryKey: ['roles'], queryFn: () => get<Role[]>('/roles') });
  const { data: perms } = useQuery({ queryKey: ['permissions'], queryFn: () => get<PermissionInfo[]>('/roles/permissions') });
  const [opened, m] = useDisclosure();
  const [editing, setEditing] = useState<Role | null>(null);
  const form = useForm({ initialValues: { name: '', description: '', permissions: [] as string[] } });
  const open = (r: Role | null) => {
    setEditing(r);
    form.setValues(r ? { name: r.name, description: r.description ?? '', permissions: r.permissions } : { name: '', description: '', permissions: [] });
    form.clearErrors(); m.open();
  };
  const submit = form.onSubmit(async (v) => {
    try { if (editing) await put(`/roles/${editing.id}`, v); else await post('/roles', v); notifySuccess(t('common.saved')); await qc.invalidateQueries({ queryKey: ['roles'] }); m.close(); }
    catch (e) { handleFormError(form, e); }
  });
  const groups = [...new Set((perms ?? []).map((p) => p.group))];
  const togglePerm = (code: string) => form.setFieldValue('permissions', form.values.permissions.includes(code) ? form.values.permissions.filter((x) => x !== code) : [...form.values.permissions, code]);
  return (
    <Card p={0}>
      <Group justify="space-between" p="md"><Title order={5}>{t('settings.roles')}</Title><Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('settings.newRole')}</Button></Group>
      <DataTable minHeight={150} fetching={isFetching} records={data ?? []} columns={[
        { accessor: 'name', title: t('common.name'), render: (r) => <Group gap={6}><Text fz="sm" fw={500}>{r.name}</Text>{r.isSystem && <Badge size="xs" variant="outline">{t('settings.systemRole')}</Badge>}</Group> },
        { accessor: 'description', title: t('common.description') },
        { accessor: 'permissions', title: t('settings.permissions'), render: (r) => `${r.permissions.length} / ${perms?.length ?? '…'}` },
        { accessor: 'userCount', title: t('settings.users_count'), textAlign: 'right' },
        { accessor: 'actions', title: '', width: 90, render: (r) => (
          <Group gap={4} justify="flex-end" wrap="nowrap">
            <ActionIcon variant="subtle" onClick={() => open(r)} aria-label={t('common.edit')}><IconEdit size={16} /></ActionIcon>
            {!r.isSystem && <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => confirmDelete(r.name, async () => { try { await del(`/roles/${r.id}`); await qc.invalidateQueries({ queryKey: ['roles'] }); } catch (e) { notifyError(e); } })}><IconTrash size={16} /></ActionIcon>}
          </Group>) },
      ]} />
      <Modal opened={opened} onClose={m.close} title={editing ? t('settings.editRole') : t('settings.newRole')} size="lg">
        <form onSubmit={submit}><Stack>
          <TextInput label={t('common.name')} required disabled={editing?.isSystem} {...form.getInputProps('name')} />
          <Textarea label={t('common.description')} autosize {...form.getInputProps('description')} />
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
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack></form>
      </Modal>
    </Card>
  );
}
