import { useMemo, useState } from 'react';
import { ActionIcon, Anchor, Badge, Button, Card, Checkbox, Divider, Group, Modal, NumberInput, SegmentedControl, Select, SimpleGrid, Stack, Switch, Text, TextInput, Textarea, Title } from '@mantine/core';
import { Link } from 'react-router';
import { useDebouncedValue, useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { DataTable } from 'mantine-datatable';
import { IconEdit, IconPlus, IconSearch, IconTrash, IconX } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import type { ContractorCategory, Paged, SupplierAddress, SupplierContact, SupplierDetail, SupplierItem, SupplierRate } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { confirmDelete } from '@/components/confirm';
import { fmtPrice, handleFormError, notifyError, notifySuccess } from '@/components/utils';
import { companyRoles, rolesError, type CompanyRole } from './companies';

const blank = {
  code: '', name: '', contactName: '', email: '', phone: '', address: '', city: '', country: '', taxNumber: '', paymentTerms: '', currency: '',
  isSupplier: true, isContractor: false, isManufacturer: false, notes: '', isActive: true,
  contacts: [] as SupplierContact[], addresses: [] as SupplierAddress[], rates: [] as SupplierRate[],
};

type SuppliedPart = { partId: string; partNumber: string; partName: string; supplierPartNumber?: string | null; packName: string; packSize: number; packPrice?: number | null; currency: string };

const ROLE_COLOR: Record<CompanyRole, string> = { supplier: 'blue', contractor: 'grape', manufacturer: 'teal' };

/** Purchasing › Companies (0.17.0, was Suppliers): roles, extra contacts and addresses, and contractor rates per category. */
export function CompaniesTab() {
  const { t } = useTranslation();
  const { can, me } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const qc = useQueryClient();
  const [search, setSearch] = useState('');
  const [debounced] = useDebouncedValue(search, 300);
  const [page, setPage] = useState(1);
  const [inactive, setInactive] = useState(false);
  const [role, setRole] = useState<'all' | CompanyRole>('all');
  const { data, isFetching } = useQuery({
    queryKey: ['suppliers', debounced, inactive, role, page],
    queryFn: () => get<Paged<SupplierItem>>('/suppliers', { search: debounced, activeOnly: inactive ? '' : 'true', role: role === 'all' ? '' : role, page, pageSize: 25 }),
    placeholderData: (p) => p,
  });
  const { data: categories } = useQuery({ queryKey: ['contractor-categories'], queryFn: () => get<ContractorCategory[]>('/contractor-categories') });
  const [opened, m] = useDisclosure();
  const [editingId, setEditingId] = useState<string | null>(null);
  const form = useForm({ initialValues: blank, validate: { isSupplier: (_, v) => (rolesError(v) ? t(rolesError(v)!) : null) } });
  const open = async (id: string | null) => {
    setEditingId(id); form.clearErrors();
    if (id) {
      const s = await get<SupplierDetail>(`/suppliers/${id}`);
      form.setValues({
        ...Object.fromEntries(Object.keys(blank).map((k) => [k, (s as Record<string, unknown>)[k] ?? (blank as Record<string, unknown>)[k]])) as typeof blank,
        contacts: s.contacts ?? [], addresses: s.addresses ?? [], rates: (s.rates ?? []).map((r) => ({ categoryId: r.categoryId, rate: r.rate })),
      });
    } else form.setValues(blank);
    m.open();
  };
  const submit = form.onSubmit(async (v) => {
    try {
      // Unticked, the form hides the rates and sends none; the server keeps what it holds and brings it back when the box is ticked again (0.29.1).
      const body = { ...v, rates: v.isContractor ? v.rates.filter((r) => r.categoryId) : [] };
      if (editingId) await put(`/suppliers/${editingId}`, body); else await post('/suppliers', body);
      notifySuccess(t('common.saved')); await qc.invalidateQueries({ queryKey: ['suppliers'] }); m.close();
    } catch (e) { handleFormError(form, e); }
  });
  const manage = can(P.suppliersManage);
  const rows = useMemo(() => data?.items ?? [], [data]);
  const catOptions = (categories ?? []).map((c) => ({ value: c.id, label: `${c.code} · ${c.name}` }));
  // The parts this company supplies (0.18.0), edited on each part's Suppliers tab.
  const { data: supplied } = useQuery({ queryKey: ['supplier-parts', editingId], queryFn: () => get<SuppliedPart[]>(`/suppliers/${editingId}/parts`), enabled: !!editingId && opened });
  return (
    <Card p={0}>
      <Group p="md" gap="xs" wrap="wrap">
        <TextInput placeholder={t('po.searchSuppliers')} leftSection={<IconSearch size={16} />} value={search} onChange={(e) => { setSearch(e.currentTarget.value); setPage(1); }} w={240} />
        <SegmentedControl size="xs" value={role} onChange={(v) => { setRole(v as typeof role); setPage(1); }}
          data={[{ value: 'all', label: t('companies.all') }, { value: 'supplier', label: t('companies.role.supplier') }, { value: 'contractor', label: t('companies.role.contractor') },
            { value: 'manufacturer', label: t('companies.role.manufacturer') }]} />
        <Checkbox label={t('common.showInactive')} checked={inactive} onChange={(e) => setInactive(e.currentTarget.checked)} />
        {manage && <Button size="xs" ml="auto" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('companies.new')}</Button>}
      </Group>
      <DataTable<SupplierItem> minHeight={200} withTableBorder={false} striped highlightOnHover verticalSpacing="xs" fetching={isFetching} records={rows} noRecordsText={t('companies.none')}
        totalRecords={data?.total ?? 0} recordsPerPage={25} page={page} onPageChange={setPage}
        rowClassName={() => (manage ? 'clickable-row' : '')} onRowClick={manage ? ({ record }) => open(record.id) : undefined}
        columns={[
          { accessor: 'code', title: t('common.code'), width: 110, render: (s) => <Text fz="sm" fw={600}>{s.code}</Text> },
          { accessor: 'name', title: t('common.name'), render: (s) => <Group gap={6} wrap="nowrap"><Text fz="sm">{s.name}</Text>
            {companyRoles(s).map((r) => <Badge key={r} size="xs" variant="light" color={ROLE_COLOR[r]}>{t(`companies.role.${r}`)}</Badge>)}
            {!s.isActive && <Badge size="xs" color="gray">{t('common.inactive')}</Badge>}</Group> },
          { accessor: 'contact', title: t('po.contact'), render: (s) => <Stack gap={0}><Text fz="sm">{s.contactName ?? '—'}</Text><Text fz="xs" c="dimmed">{[s.email, s.phone].filter(Boolean).join(' · ')}</Text></Stack> },
          { accessor: 'city', title: t('po.city'), render: (s) => [s.city, s.country].filter(Boolean).join(', ') || '—' },
          { accessor: 'paymentTerms', title: t('po.paymentTerms'), render: (s) => s.paymentTerms ?? '—' },
          { accessor: 'openOrders', title: t('po.openOrders'), textAlign: 'right' },
          { accessor: 'receivedYtd', title: t('po.receivedYtd'), textAlign: 'right', noWrap: true, render: (s) => fmtPrice(s.receivedYtd, currency, 0) },
          { accessor: 'actions', title: '', width: 80, hidden: !manage, render: (s) => <Group gap={4} justify="flex-end" wrap="nowrap">
            <ActionIcon variant="subtle" aria-label={t('common.edit')} onClick={(e) => { e.stopPropagation(); void open(s.id); }}><IconEdit size={16} /></ActionIcon>
            <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={(e) => { e.stopPropagation(); confirmDelete(s.name, async () => { try { await del(`/suppliers/${s.id}`); await qc.invalidateQueries({ queryKey: ['suppliers'] }); } catch (err) { notifyError(err); } }); }}><IconTrash size={16} /></ActionIcon></Group> },
        ]} />
      <Modal opened={opened} onClose={m.close} title={editingId ? t('companies.edit') : t('companies.new')} size="xl">
        <form onSubmit={submit}><Stack>
          <SimpleGrid cols={3}><TextInput label={t('common.code')} required {...form.getInputProps('code')} /><TextInput label={t('common.name')} required style={{ gridColumn: 'span 2' }} {...form.getInputProps('name')} /></SimpleGrid>
          <div>
            <Text fz="sm" fw={500} mb={4}>{t('companies.roles')}</Text>
            <Group>
              <Checkbox label={t('companies.role.supplier')} {...form.getInputProps('isSupplier', { type: 'checkbox' })} />
              <Checkbox label={t('companies.role.contractor')} {...form.getInputProps('isContractor', { type: 'checkbox' })} />
              <Checkbox label={t('companies.role.manufacturer')} {...form.getInputProps('isManufacturer', { type: 'checkbox' })} />
            </Group>
            {form.errors.isSupplier && <Text fz="xs" c="red" mt={4}>{form.errors.isSupplier}</Text>}
          </div>
          <SimpleGrid cols={{ base: 1, sm: 3 }}><TextInput label={t('po.contact')} {...form.getInputProps('contactName')} /><TextInput label={t('settings.email')} type="email" {...form.getInputProps('email')} /><TextInput label={t('settings.phone')} {...form.getInputProps('phone')} /></SimpleGrid>
          <Textarea label={t('po.address')} autosize minRows={2} {...form.getInputProps('address')} />
          <SimpleGrid cols={{ base: 1, sm: 3 }}><TextInput label={t('po.city')} {...form.getInputProps('city')} /><TextInput label={t('po.country')} {...form.getInputProps('country')} /><TextInput label={t('po.taxNumber')} {...form.getInputProps('taxNumber')} /></SimpleGrid>
          <SimpleGrid cols={{ base: 1, sm: 2 }}><TextInput label={t('po.paymentTerms')} placeholder={t('po.termsPlaceholder')} {...form.getInputProps('paymentTerms')} /><TextInput label={t('po.currency')} placeholder={currency} maxLength={3} {...form.getInputProps('currency')} /></SimpleGrid>

          <Divider />
          <Group justify="space-between"><Title order={6}>{t('companies.contacts')}</Title>
            <Button size="xs" variant="light" leftSection={<IconPlus size={14} />} onClick={() => form.insertListItem('contacts', { name: '', role: '', phone: '', email: '' })}>{t('companies.addContact')}</Button></Group>
          {form.values.contacts.length === 0 && <Text fz="sm" c="dimmed">{t('companies.noContacts')}</Text>}
          {form.values.contacts.map((_, i) => (
            <Group key={i} gap="xs" align="flex-start" wrap="nowrap">
              <TextInput style={{ flex: 2 }} placeholder={t('common.name')} aria-label={t('common.name')} {...form.getInputProps(`contacts.${i}.name`)} />
              <TextInput style={{ flex: 2 }} placeholder={t('companies.contactRole')} aria-label={t('companies.contactRole')} {...form.getInputProps(`contacts.${i}.role`)} />
              <TextInput style={{ flex: 2 }} placeholder={t('settings.phone')} aria-label={t('settings.phone')} {...form.getInputProps(`contacts.${i}.phone`)} />
              <TextInput style={{ flex: 3 }} placeholder={t('settings.email')} aria-label={t('settings.email')} {...form.getInputProps(`contacts.${i}.email`)} />
              <ActionIcon variant="subtle" color="red" mt={4} aria-label={t('common.delete')} onClick={() => form.removeListItem('contacts', i)}><IconX size={16} /></ActionIcon>
            </Group>
          ))}

          <Group justify="space-between"><Title order={6}>{t('companies.addresses')}</Title>
            <Button size="xs" variant="light" leftSection={<IconPlus size={14} />} onClick={() => form.insertListItem('addresses', { label: '', address: '', city: '', country: '' })}>{t('companies.addAddress')}</Button></Group>
          {form.values.addresses.length === 0 && <Text fz="sm" c="dimmed">{t('companies.noAddresses')}</Text>}
          {form.values.addresses.map((_, i) => (
            <Group key={i} gap="xs" align="flex-start" wrap="nowrap">
              <TextInput style={{ flex: 2 }} placeholder={t('companies.addressLabel')} aria-label={t('companies.addressLabel')} {...form.getInputProps(`addresses.${i}.label`)} />
              <TextInput style={{ flex: 4 }} placeholder={t('po.address')} aria-label={t('po.address')} {...form.getInputProps(`addresses.${i}.address`)} />
              <TextInput style={{ flex: 2 }} placeholder={t('po.city')} aria-label={t('po.city')} {...form.getInputProps(`addresses.${i}.city`)} />
              <TextInput style={{ flex: 2 }} placeholder={t('po.country')} aria-label={t('po.country')} {...form.getInputProps(`addresses.${i}.country`)} />
              <ActionIcon variant="subtle" color="red" mt={4} aria-label={t('common.delete')} onClick={() => form.removeListItem('addresses', i)}><IconX size={16} /></ActionIcon>
            </Group>
          ))}

          {form.values.isContractor && <>
            <Group justify="space-between"><Title order={6}>{t('companies.rates')}</Title>
              <Button size="xs" variant="light" leftSection={<IconPlus size={14} />} disabled={!catOptions.length} onClick={() => form.insertListItem('rates', { categoryId: '', rate: 0 })}>{t('companies.addRate')}</Button></Group>
            <Text fz="xs" c="dimmed">{catOptions.length ? t('companies.ratesHint') : t('companies.noCategories')}</Text>
            {form.values.rates.map((r, i) => (
              <Group key={i} gap="xs" wrap="nowrap">
                <Select style={{ flex: 3 }} data={catOptions.filter((o) => o.value === r.categoryId || !form.values.rates.some((x) => x.categoryId === o.value))} placeholder={t('companies.category')}
                  aria-label={t('companies.category')} {...form.getInputProps(`rates.${i}.categoryId`)} />
                <NumberInput style={{ flex: 2 }} min={0} decimalScale={2} rightSection={<Text fz="xs" c="dimmed">{currency}/h</Text>} rightSectionWidth={60}
                  aria-label={t('companies.hourlyRate')} {...form.getInputProps(`rates.${i}.rate`)} />
                <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => form.removeListItem('rates', i)}><IconX size={16} /></ActionIcon>
              </Group>
            ))}
          </>}

          {form.values.isSupplier && editingId && <>
            <Title order={6}>{t('catalogue.partsSupplied')}</Title>
            {(supplied ?? []).length === 0 ? <Text fz="sm" c="dimmed">{t('catalogue.noPartsSupplied')}</Text> : (
              <Stack gap={2}>
                {(supplied ?? []).map((p) => (
                  <Group key={p.partId} gap={6} wrap="nowrap">
                    <Anchor component={Link} to={`/inventory/parts/${p.partId}`} fz="sm" fw={500}>{p.partNumber}</Anchor>
                    <Text fz="sm" c="dimmed" lineClamp={1}>{p.partName}</Text>
                    <Text fz="xs" c="dimmed" ms="auto">{p.packName}{p.packPrice != null ? ` · ${fmtPrice(p.packPrice, p.currency)}` : ''}</Text>
                  </Group>
                ))}
              </Stack>
            )}
          </>}
          <Textarea label={t('common.notes')} autosize minRows={2} {...form.getInputProps('notes')} />
          <Switch label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack></form>
      </Modal>
    </Card>
  );
}
