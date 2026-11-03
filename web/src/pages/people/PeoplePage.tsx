import { useMemo, useState } from 'react';
import { Badge, Button, Card, Checkbox, Group, Modal, MultiSelect, NumberInput, Select, SimpleGrid, Stack, Switch, Tabs, Text, TextInput, Textarea } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { DataTable } from 'mantine-datatable';
import { IconPlus, IconSearch } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import type { Employee, Lookup, User } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { LookupTable } from '@/components/LookupTable';
import { useHierarchy } from '@/components/LocationPicker';
import { confirmDelete } from '@/components/confirm';
import { fmtMoney, handleFormError, notifyError, notifySuccess } from '@/components/utils';

export function PeoplePage() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const [tab, setTab] = useState<string | null>('people');
  return (
    <>
      <PageHeader title={t('people.title')} subtitle={t('people.subtitle')} />
      <Tabs value={tab} onChange={setTab} keepMounted={false}>
        <Tabs.List mb="lg"><Tabs.Tab value="people">{t('people.peopleTab')}</Tabs.Tab><Tabs.Tab value="trades">{t('people.tradesTab')}</Tabs.Tab></Tabs.List>
        <Tabs.Panel value="people"><EmployeesTable /></Tabs.Panel>
        <Tabs.Panel value="trades"><LookupTable path="/trades" title={t('people.trades')} newLabel={t('people.newTrade')} editable={can(P.peopleManage)} withColor /></Tabs.Panel>
      </Tabs>
    </>
  );
}

function EmployeesTable() {
  const { t } = useTranslation();
  const { can, me } = useAuth();
  const qc = useQueryClient();
  const editable = can(P.peopleManage);
  const [search, setSearch] = useState('');
  const { data, isFetching } = useQuery({ queryKey: ['employees', 'list'], queryFn: () => get<Employee[]>('/employees') });
  const { data: trades } = useQuery({ queryKey: ['trades'], queryFn: () => get<Lookup[]>('/trades') });
  const { data: users } = useQuery({ queryKey: ['users'], queryFn: () => get<User[]>('/users'), enabled: can(P.usersManage) });
  const { data: h } = useHierarchy();
  const [opened, m] = useDisclosure();
  const [editing, setEditing] = useState<Employee | null>(null);
  const currency = me?.tenant.settings.currency ?? 'USD';
  const blank = { code: '', fullName: '', jobTitle: '', email: '', phone: '', tradeIds: [] as string[], siteIds: [] as string[], hourlyRate: '' as number | '', overtimeRate: '' as number | '',
    isContractor: false, company: '', userId: null as string | null, notes: '', isActive: true };
  const form = useForm({ initialValues: blank });
  const open = (e: Employee | null) => {
    setEditing(e);
    form.setValues(e ? { code: e.code, fullName: e.fullName, jobTitle: e.jobTitle ?? '', email: e.email ?? '', phone: e.phone ?? '', tradeIds: e.tradeIds, siteIds: e.siteIds,
      hourlyRate: e.hourlyRate ?? '', overtimeRate: e.overtimeRate ?? '', isContractor: e.isContractor, company: e.company ?? '', userId: e.userId ?? null, notes: e.notes ?? '', isActive: e.isActive } : blank);
    form.clearErrors(); m.open();
  };
  const submit = form.onSubmit(async (v) => {
    const body = { ...v, hourlyRate: v.hourlyRate === '' ? null : v.hourlyRate, overtimeRate: v.overtimeRate === '' ? null : v.overtimeRate, rowVersion: editing?.rowVersion };
    try { if (editing) await put(`/employees/${editing.id}`, body); else await post('/employees', body); notifySuccess(t('common.saved')); await qc.invalidateQueries({ queryKey: ['employees'] }); m.close(); }
    catch (e) { handleFormError(form, e); }
  });
  const tradeName = (id: string) => trades?.find((x) => x.id === id)?.name ?? '?';
  const rows = useMemo(() => (data ?? []).filter((e) => !search || `${e.fullName} ${e.code} ${e.company ?? ''}`.toLowerCase().includes(search.toLowerCase())), [data, search]);
  return (
    <Card p={0}>
      <Group justify="space-between" p="md">
        <TextInput placeholder={t('common.search')} leftSection={<IconSearch size={16} />} value={search} onChange={(e) => setSearch(e.currentTarget.value)} w={260} />
        {editable && <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('people.new')}</Button>}
      </Group>
      <DataTable minHeight={160} fetching={isFetching} records={rows} noRecordsText={t('common.noRecords')} highlightOnHover
        rowClassName={() => (editable ? 'clickable-row' : '')} onRowClick={({ record }) => editable && open(record)}
        columns={[
          { accessor: 'fullName', title: t('common.name'), render: (e) => <Stack gap={0}><Text fz="sm" fw={500}>{e.fullName}</Text><Text fz="xs" c="dimmed">{e.code}{e.jobTitle && ` · ${e.jobTitle}`}</Text></Stack> },
          { accessor: 'tradeIds', title: t('people.trades'), render: (e) => <Group gap={4}>{e.tradeIds.map((id) => <Badge key={id} variant="default">{tradeName(id)}</Badge>)}</Group> },
          { accessor: 'isContractor', title: t('common.type'), render: (e) => (e.isContractor ? <Badge color="grape">{e.company ?? t('people.contractor')}</Badge> : <Text fz="sm" c="dimmed">{t('people.inHouse')}</Text>) },
          { accessor: 'hourlyRate', title: t('people.hourlyRate'), textAlign: 'right', render: (e) => fmtMoney(e.hourlyRate, currency) },
          { accessor: 'openWorkOrders', title: t('people.openWork'), textAlign: 'right' },
          { accessor: 'isActive', title: t('common.status'), render: (e) => <Badge color={e.isActive ? 'teal' : 'gray'}>{e.isActive ? t('common.active') : t('common.inactive')}</Badge> },
        ]} />
      <Modal opened={opened} onClose={m.close} title={editing ? t('people.edit') : t('people.new')} size="lg">
        <form onSubmit={submit}><Stack>
          <SimpleGrid cols={{ base: 1, sm: 3 }}>
            <TextInput label={t('people.code')} required {...form.getInputProps('code')} />
            <TextInput label={t('common.name')} required style={{ gridColumn: 'span 2' }} {...form.getInputProps('fullName')} />
          </SimpleGrid>
          <SimpleGrid cols={{ base: 1, sm: 3 }}>
            <TextInput label={t('settings.jobTitle')} {...form.getInputProps('jobTitle')} />
            <TextInput label={t('auth.email')} {...form.getInputProps('email')} />
            <TextInput label={t('settings.phone')} {...form.getInputProps('phone')} />
          </SimpleGrid>
          <MultiSelect label={t('people.trades')} data={(trades ?? []).map((x) => ({ value: x.id, label: x.name }))} searchable clearable {...form.getInputProps('tradeIds')} />
          <MultiSelect label={t('people.sites')} description={t('people.sitesHint')} data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} clearable {...form.getInputProps('siteIds')} />
          <SimpleGrid cols={2}>
            <NumberInput label={t('people.hourlyRate')} min={0} decimalScale={2} {...form.getInputProps('hourlyRate')} />
            <NumberInput label={t('people.overtimeRate')} min={0} decimalScale={2} {...form.getInputProps('overtimeRate')} />
          </SimpleGrid>
          <Group grow align="flex-end">
            <Switch label={t('people.contractor')} {...form.getInputProps('isContractor', { type: 'checkbox' })} />
            {form.values.isContractor && <TextInput label={t('people.company')} {...form.getInputProps('company')} />}
          </Group>
          {users && <Select label={t('people.login')} description={t('people.loginHint')} clearable searchable data={users.map((u) => ({ value: u.id, label: `${u.fullName} (${u.email})` }))} {...form.getInputProps('userId')} />}
          <Textarea label={t('common.notes')} autosize {...form.getInputProps('notes')} />
          <Checkbox label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
          <Group justify="space-between">
            {editing ? <Button variant="subtle" color="red" onClick={() => confirmDelete(editing.fullName, async () => { try { await del(`/employees/${editing.id}`); await qc.invalidateQueries({ queryKey: ['employees'] }); m.close(); } catch (e) { notifyError(e); } })}>{t('common.delete')}</Button> : <span />}
            <Group><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
          </Group>
        </Stack></form>
      </Modal>
    </Card>
  );
}
