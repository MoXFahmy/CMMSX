import { useEffect, useState } from 'react';
import { ActionIcon, Button, Card, Group, NumberInput, Select, SimpleGrid, Stack, Switch, Table, Text, TextInput, Textarea, Title } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconX } from '@tabler/icons-react';
import { get, post, put } from '@/api/client';
import type { ContractDetail, ExpectedPer, Lookup, SupplierLookup } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { AssetPicker } from '@/components/AssetPicker';
import { useHierarchy } from '@/components/LocationPicker';
import { handleFormError, notifySuccess, toIsoDate } from '@/components/utils';

type CoveredAsset = { assetId: string; label: string; yearlyCost: number | '' };
type Values = {
  title: string; supplierId: string | null; managerId: string | null; contractTypeId: string | null; tradeId: string | null; siteId: string | null; notes: string;
  startDate: Date | string | null; endDate: Date | string | null; value: number | ''; currency: string; paymentTerms: string;
  expectedWorkOrders: number | ''; expectedPer: ExpectedPer; autoRenew: boolean; renewalMonths: number | ''; noticeDays: number | ''; alertDays: number | ''; assets: CoveredAsset[];
};

const blank: Values = {
  title: '', supplierId: null, managerId: null, contractTypeId: null, tradeId: null, siteId: null, notes: '', startDate: null, endDate: null, value: '', currency: '', paymentTerms: '',
  expectedWorkOrders: '', expectedPer: 'Year', autoRenew: false, renewalMonths: 12, noticeDays: 30, alertDays: 60, assets: [],
};

/** New or edited service contract (0.17.0). */
export function ContractFormPage() {
  const { t } = useTranslation();
  const { id } = useParams();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { me, terms } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const { data: existing } = useQuery({ queryKey: ['contract', id], queryFn: () => get<ContractDetail>(`/contracts/${id}`), enabled: !!id });
  const { data: companies } = useQuery({ queryKey: ['suppliers', 'lookup', 'contractor'], queryFn: () => get<SupplierLookup[]>('/suppliers/lookup', { role: 'contractor' }) });
  const { data: types } = useQuery({ queryKey: ['contract-types'], queryFn: () => get<Lookup[]>('/contract-types') });
  const { data: trades } = useQuery({ queryKey: ['trades'], queryFn: () => get<Lookup[]>('/trades') });
  const { data: people } = useQuery({ queryKey: ['users-lookup'], queryFn: () => get<{ id: string; fullName: string }[]>('/users/lookup'), retry: false });
  const { data: h } = useHierarchy();
  const [picking, setPicking] = useState<string | null>(null);
  const form = useForm<Values>({ initialValues: blank });
  useEffect(() => {
    if (!existing) return;
    form.setValues({
      title: existing.title, supplierId: existing.supplierId, managerId: existing.managerId ?? null, contractTypeId: existing.contractTypeId ?? null, tradeId: existing.tradeId ?? null,
      siteId: existing.siteId ?? null, notes: existing.notes ?? '', startDate: existing.startDate, endDate: existing.endDate, value: existing.value ?? '', currency: existing.currency ?? '',
      paymentTerms: existing.paymentTerms ?? '', expectedWorkOrders: existing.expectedWorkOrders ?? '', expectedPer: existing.expectedPer ?? 'Year', autoRenew: existing.autoRenew,
      renewalMonths: existing.renewalMonths ?? 12, noticeDays: existing.noticeDays, alertDays: existing.alertDays,
      assets: existing.assets.map((a) => ({ assetId: a.assetId, label: `${a.assetNumber} · ${a.name}`, yearlyCost: a.yearlyCost ?? '' })),
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [existing]);
  const submit = form.onSubmit(async (v) => {
    const num = (x: number | '') => (x === '' ? null : x);
    const body = {
      title: v.title, supplierId: v.supplierId, managerId: v.managerId, contractTypeId: v.contractTypeId, tradeId: v.tradeId, siteId: v.siteId, notes: v.notes || null,
      startDate: toIsoDate(v.startDate), endDate: toIsoDate(v.endDate), value: num(v.value), currency: v.currency || null, paymentTerms: v.paymentTerms || null,
      expectedWorkOrders: num(v.expectedWorkOrders), expectedPer: v.expectedWorkOrders === '' ? null : v.expectedPer, autoRenew: v.autoRenew,
      renewalMonths: v.autoRenew ? num(v.renewalMonths) : null, noticeDays: num(v.noticeDays) ?? 0, alertDays: num(v.alertDays) ?? 0,
      assets: v.assets.map((a) => ({ assetId: a.assetId, yearlyCost: num(a.yearlyCost) })), rowVersion: existing?.rowVersion,
    };
    try {
      const saved = id ? (await put<{ id: string }>(`/contracts/${id}`, body), { id }) : await post<{ id: string }>('/contracts', body);
      notifySuccess(t('common.saved'));
      await qc.invalidateQueries({ queryKey: ['contracts'] });
      await qc.invalidateQueries({ queryKey: ['contract', saved.id] });
      navigate(`/contracts/${saved.id}`);
    } catch (e) { handleFormError(form, e); }
  });
  // A company or manager that is no longer offered still shows on the contract it is on.
  const companyOptions = [...(companies ?? []).map((c) => ({ value: c.id, label: c.name })),
    ...(existing && !(companies ?? []).some((c) => c.id === existing.supplierId) ? [{ value: existing.supplierId, label: existing.company.name }] : [])];
  const typeOptions = (types ?? []).filter((x) => x.isActive || x.id === existing?.contractTypeId).map((x) => ({ value: x.id, label: x.name }));
  return (
    <>
      <PageHeader title={id ? t('contracts.edit', { number: existing?.number ?? '' }) : t('contracts.new')} />
      <form onSubmit={submit}>
        <Stack maw={980}>
          <Card><Stack>
            <TextInput label={t('contracts.contractTitle')} required {...form.getInputProps('title')} />
            <SimpleGrid cols={{ base: 1, sm: 3 }}>
              <Select label={t('contracts.company')} required searchable data={companyOptions} {...form.getInputProps('supplierId')} />
              <Select label={t('contracts.manager')} clearable searchable data={(people ?? []).map((p) => ({ value: p.id, label: p.fullName }))} {...form.getInputProps('managerId')} />
              <Select label={t('common.type')} clearable data={typeOptions} {...form.getInputProps('contractTypeId')} />
              <Select label={t('contracts.trade')} clearable searchable data={(trades ?? []).map((x) => ({ value: x.id, label: x.name }))} {...form.getInputProps('tradeId')} />
              <Select label={terms.site} description={t('contracts.siteHint')} clearable placeholder={t('contracts.allSites')}
                data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} {...form.getInputProps('siteId')} />
            </SimpleGrid>
            <Textarea label={t('common.notes')} autosize minRows={2} {...form.getInputProps('notes')} />
          </Stack></Card>

          <Card><Stack>
            <Title order={5}>{t('contracts.terms')}</Title>
            <SimpleGrid cols={{ base: 1, sm: 4 }}>
              <DateInput label={t('contracts.start')} required valueFormat="DD MMM YYYY" {...form.getInputProps('startDate')} />
              <DateInput label={t('contracts.end')} required valueFormat="DD MMM YYYY" {...form.getInputProps('endDate')} />
              <NumberInput label={t('contracts.value')} min={0} decimalScale={2} thousandSeparator="," {...form.getInputProps('value')} />
              <TextInput label={t('po.currency')} placeholder={currency} maxLength={3} {...form.getInputProps('currency')} />
            </SimpleGrid>
            <SimpleGrid cols={{ base: 1, sm: 3 }}>
              <TextInput label={t('po.paymentTerms')} {...form.getInputProps('paymentTerms')} />
              <NumberInput label={t('contracts.expected')} description={t('contracts.expectedHint')} min={0} {...form.getInputProps('expectedWorkOrders')} />
              <Select label={t('contracts.expectedPer')} data={(['Year', 'Quarter', 'Month'] as ExpectedPer[]).map((p) => ({ value: p, label: t(`contracts.per.${p}`) }))}
                disabled={form.values.expectedWorkOrders === ''} {...form.getInputProps('expectedPer')} />
            </SimpleGrid>
            <Switch label={t('contracts.autoRenew')} description={t('contracts.autoRenewHint')} {...form.getInputProps('autoRenew', { type: 'checkbox' })} />
            <SimpleGrid cols={{ base: 1, sm: 3 }}>
              {form.values.autoRenew && <NumberInput label={t('contracts.renewalMonths')} min={1} max={120} {...form.getInputProps('renewalMonths')} />}
              {form.values.autoRenew && <NumberInput label={t('contracts.noticeDays')} description={t('contracts.noticeDaysHint')} min={0} max={3650} {...form.getInputProps('noticeDays')} />}
              <NumberInput label={t('contracts.alertDays')} description={t('contracts.alertDaysHint')} min={0} max={3650} {...form.getInputProps('alertDays')} />
            </SimpleGrid>
          </Stack></Card>

          <Card><Stack>
            <Title order={5}>{t('contracts.coveredAssets')}</Title>
            <Group align="flex-end">
              <div style={{ flex: 1 }}>
                <AssetPicker label={t('contracts.addAsset')} value={picking} siteId={form.values.siteId} onChange={setPicking}
                  onPick={(o) => {
                    if (o && !form.values.assets.some((a) => a.assetId === o.id)) form.insertListItem('assets', { assetId: o.id, label: `${o.assetNumber} · ${o.name}`, yearlyCost: '' });
                    setPicking(null);
                  }} />
              </div>
            </Group>
            {form.errors.assets && <Text fz="sm" c="red">{form.errors.assets}</Text>}
            {form.values.assets.length === 0 ? <Text fz="sm" c="dimmed">{t('contracts.noAssets')}</Text> : (
              <Table>
                <Table.Thead><Table.Tr><Table.Th>{terms.asset}</Table.Th><Table.Th w={200}>{t('contracts.yearlyCost')}</Table.Th><Table.Th w={40} /></Table.Tr></Table.Thead>
                <Table.Tbody>
                  {form.values.assets.map((a, i) => (
                    <Table.Tr key={a.assetId}>
                      <Table.Td><Text fz="sm">{a.label}</Text></Table.Td>
                      <Table.Td><NumberInput size="xs" min={0} decimalScale={2} aria-label={t('contracts.yearlyCost')} {...form.getInputProps(`assets.${i}.yearlyCost`)} /></Table.Td>
                      <Table.Td><ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => form.removeListItem('assets', i)}><IconX size={16} /></ActionIcon></Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            )}
          </Stack></Card>

          <Group justify="flex-end">
            <Button variant="default" onClick={() => navigate(id ? `/contracts/${id}` : '/contracts')}>{t('common.cancel')}</Button>
            <Button type="submit" loading={form.submitting}>{t('common.save')}</Button>
          </Group>
        </Stack>
      </form>
    </>
  );
}
