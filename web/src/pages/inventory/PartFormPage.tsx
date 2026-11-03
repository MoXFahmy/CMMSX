import { useEffect, useMemo } from 'react';
import { ActionIcon, Button, Card, Center, Checkbox, Grid, Group, Loader, MultiSelect, NumberInput, Select, SimpleGrid, Stack, Table, Text, TextInput, Textarea, Title } from '@mantine/core';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconPlus, IconTrash } from '@tabler/icons-react';
import { get, post, put } from '@/api/client';
import type { Lookup, PartDetail, PartLookup, SupplierLookup } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { useStores } from '@/components/PartPicker';
import { handleFormError, notifySuccess } from '@/components/utils';

type Level = { storeId: string | null; bin: string; minLevel: number | ''; reorderLevel: number | ''; maxLevel: number | ''; reorderQuantity: number | ''; quantityOnHand?: number };
type Values = {
  partNumber: string; name: string; specification: string; manufacturer: string; manufacturerPartNumber: string; unitOfMeasure: string; groupId: string | null;
  standardPrice: number | ''; leadTimeDays: number | ''; isCritical: boolean; isShelfItem: boolean; allowNegativeStock: boolean; preferredSupplierId: string | null;
  barcode: string; alternatePartIds: string[]; notes: string; isActive: boolean; levels: Level[];
};
const empty: Values = {
  partNumber: '', name: '', specification: '', manufacturer: '', manufacturerPartNumber: '', unitOfMeasure: 'EA', groupId: null, standardPrice: '', leadTimeDays: '',
  isCritical: false, isShelfItem: false, allowNegativeStock: false, preferredSupplierId: null, barcode: '', alternatePartIds: [], notes: '', isActive: true, levels: [],
};
const UNITS = ['EA', 'SET', 'PR', 'BOX', 'PK', 'ROLL', 'M', 'MM', 'FT', 'KG', 'G', 'LB', 'L', 'ML', 'GAL', 'DRUM', 'HR'];

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return <Card><Title order={5} mb="md">{title}</Title><Stack gap="sm">{children}</Stack></Card>;
}

export function PartFormPage() {
  const { t } = useTranslation();
  const { id } = useParams();
  const editing = !!id;
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { me } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const { data: part, isLoading } = useQuery({ queryKey: ['part', id], queryFn: () => get<PartDetail>(`/parts/${id}`), enabled: editing });
  const { data: groups } = useQuery({ queryKey: ['part-groups'], queryFn: () => get<Lookup[]>('/part-groups') });
  const { data: suppliers } = useQuery({ queryKey: ['suppliers', 'lookup'], queryFn: () => get<SupplierLookup[]>('/suppliers/lookup') });
  const { data: stores } = useStores();
  const { data: others } = useQuery({ queryKey: ['part-lookup', 'all'], queryFn: () => get<PartLookup[]>('/parts/lookup') });
  const form = useForm<Values>({ initialValues: empty });
  useEffect(() => {
    if (!part) return;
    form.setValues({
      partNumber: part.partNumber, name: part.name, specification: part.specification ?? '', manufacturer: part.manufacturer ?? '', manufacturerPartNumber: part.manufacturerPartNumber ?? '',
      unitOfMeasure: part.unitOfMeasure, groupId: part.groupId ?? null, standardPrice: part.standardPrice, leadTimeDays: part.leadTimeDays ?? '', isCritical: part.isCritical,
      isShelfItem: part.isShelfItem, allowNegativeStock: part.allowNegativeStock, preferredSupplierId: part.preferredSupplierId ?? null, barcode: part.barcode ?? '',
      alternatePartIds: part.alternatePartIds, notes: part.notes ?? '', isActive: part.isActive,
      levels: part.levels.map((l) => ({ storeId: l.storeId, bin: l.bin ?? '', minLevel: l.minLevel, reorderLevel: l.reorderLevel, maxLevel: l.maxLevel, reorderQuantity: l.reorderQuantity, quantityOnHand: l.quantityOnHand })),
    });
    form.resetDirty();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [part]);
  useEffect(() => {
    if (!editing && stores?.length === 1 && form.getValues().levels.length === 0) form.setFieldValue('levels', [{ storeId: stores[0].id, bin: '', minLevel: 0, reorderLevel: 0, maxLevel: 0, reorderQuantity: 0 }]);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [stores]);
  const altOptions = useMemo(() => {
    const list = (others ?? []).filter((o) => o.id !== id).map((o) => ({ value: o.id, label: `${o.partNumber} · ${o.name}` }));
    for (const a of part?.alternates ?? []) if (!list.some((o) => o.value === a.id)) list.push({ value: a.id, label: `${a.partNumber} · ${a.name}` });
    return list;
  }, [others, part, id]);
  const submit = form.onSubmit(async (v) => {
    const n = (x: number | '') => (x === '' ? 0 : x);
    const body = {
      ...v, standardPrice: n(v.standardPrice), leadTimeDays: v.leadTimeDays === '' ? null : v.leadTimeDays, rowVersion: part?.rowVersion,
      levels: v.levels.filter((l) => l.storeId).map((l) => ({ storeId: l.storeId, bin: l.bin, minLevel: n(l.minLevel), reorderLevel: n(l.reorderLevel), maxLevel: n(l.maxLevel), reorderQuantity: n(l.reorderQuantity) })),
    };
    try {
      const r = editing ? await put<{ id: string }>(`/parts/${id}`, body) : await post<{ id: string }>('/parts', body);
      notifySuccess(t('common.saved'));
      await qc.invalidateQueries({ queryKey: ['parts'] });
      await qc.invalidateQueries({ queryKey: ['part'] });
      await qc.invalidateQueries({ queryKey: ['part-lookup'] });
      navigate(`/inventory/parts/${r.id}`, { replace: true });
    } catch (e) { handleFormError(form, e); }
  });
  if (editing && isLoading) return <Center h={300}><Loader /></Center>;
  const levels = form.getValues().levels;
  const usedStores = levels.map((l) => l.storeId);
  const buttons = <><Button variant="default" onClick={() => navigate(-1)}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{editing ? t('common.saveChanges') : t('common.create')}</Button></>;

  return (
    <form onSubmit={submit}>
      <PageHeader crumbs={[{ label: t('inv.title'), to: '/inventory' }, { label: editing ? part?.partNumber ?? '' : t('inv.newPart') }]} title={editing ? t('inv.editPart') : t('inv.newPart')} actions={buttons} />
      <Grid>
        <Grid.Col span={{ base: 12, lg: 8 }}>
          <Stack>
            <Section title={t('inv.identification')}>
              <SimpleGrid cols={{ base: 1, sm: 3 }}>
                <TextInput label={t('inv.partNumber')} required disabled={editing} description={editing ? t('inv.partNumberFixed') : undefined} {...form.getInputProps('partNumber')} />
                <TextInput label={t('common.description')} required style={{ gridColumn: 'span 2' }} {...form.getInputProps('name')} />
              </SimpleGrid>
              <SimpleGrid cols={{ base: 1, sm: 3 }}>
                <Select label={t('inv.group')} data={(groups ?? []).map((g) => ({ value: g.id, label: g.name }))} clearable searchable {...form.getInputProps('groupId')} />
                <Select label={t('inv.unit')} required data={Array.from(new Set([...UNITS, form.getValues().unitOfMeasure].filter(Boolean)))} searchable {...form.getInputProps('unitOfMeasure')} />
                <TextInput label={t('inv.barcode')} {...form.getInputProps('barcode')} />
              </SimpleGrid>
              <SimpleGrid cols={{ base: 1, sm: 2 }}>
                <TextInput label={t('inv.manufacturer')} {...form.getInputProps('manufacturer')} />
                <TextInput label={t('inv.mfrPartNumber')} {...form.getInputProps('manufacturerPartNumber')} />
              </SimpleGrid>
              <Textarea label={t('inv.specification')} autosize minRows={2} {...form.getInputProps('specification')} />
            </Section>
            <Card p={0}>
              <Group justify="space-between" p="md">
                <Stack gap={0}><Title order={5}>{t('inv.stockLevels')}</Title><Text fz="xs" c="dimmed">{t('inv.levelsHint')}</Text></Stack>
                <Button size="xs" variant="light" leftSection={<IconPlus size={14} />} disabled={(stores ?? []).length <= levels.length}
                  onClick={() => form.insertListItem('levels', { storeId: null, bin: '', minLevel: 0, reorderLevel: 0, maxLevel: 0, reorderQuantity: 0 })}>{t('inv.addStore')}</Button>
              </Group>
              <Table.ScrollContainer minWidth={720}>
                <Table verticalSpacing={6}>
                  <Table.Thead><Table.Tr><Table.Th>{t('inv.store')}</Table.Th><Table.Th w={100}>{t('inv.bin')}</Table.Th><Table.Th w={100}>{t('inv.min')}</Table.Th><Table.Th w={100}>{t('inv.reorderAt')}</Table.Th><Table.Th w={100}>{t('inv.max')}</Table.Th><Table.Th w={110}>{t('inv.reorderQty')}</Table.Th><Table.Th w={40} /></Table.Tr></Table.Thead>
                  <Table.Tbody>
                    {levels.length === 0 && <Table.Tr><Table.Td colSpan={7}><Text fz="sm" c="dimmed" ta="center" py="sm">{t('inv.noLevels')}</Text></Table.Td></Table.Tr>}
                    {levels.map((l, i) => (
                      <Table.Tr key={i}>
                        <Table.Td><Select size="xs" aria-label={t('inv.store')} data={(stores ?? []).filter((s) => s.id === l.storeId || !usedStores.includes(s.id)).map((s) => ({ value: s.id, label: `${s.code} · ${s.name}` }))}
                          disabled={!!l.quantityOnHand} {...form.getInputProps(`levels.${i}.storeId`)} /></Table.Td>
                        <Table.Td><TextInput size="xs" aria-label={t('inv.bin')} {...form.getInputProps(`levels.${i}.bin`)} /></Table.Td>
                        {(['minLevel', 'reorderLevel', 'maxLevel', 'reorderQuantity'] as const).map((k) => <Table.Td key={k}><NumberInput size="xs" min={0} decimalScale={3} aria-label={k} {...form.getInputProps(`levels.${i}.${k}`)} /></Table.Td>)}
                        <Table.Td><ActionIcon variant="subtle" color="red" disabled={!!l.quantityOnHand} aria-label={t('common.delete')} onClick={() => form.removeListItem('levels', i)}><IconTrash size={14} /></ActionIcon></Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              </Table.ScrollContainer>
              {form.errors.levels && <Text c="red" fz="sm" px="md" pb="md">{form.errors.levels}</Text>}
            </Card>
            <Section title={t('common.notes')}><Textarea autosize minRows={3} {...form.getInputProps('notes')} /></Section>
          </Stack>
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 4 }}>
          <Stack>
            <Section title={t('inv.purchasing')}>
              <NumberInput label={t('inv.standardPrice')} min={0} decimalScale={4} thousandSeparator description={t('inv.standardPriceHint', { currency })} {...form.getInputProps('standardPrice')} />
              <Select label={t('inv.preferredSupplier')} data={(suppliers ?? []).map((s) => ({ value: s.id, label: s.name }))} clearable searchable {...form.getInputProps('preferredSupplierId')} />
              <NumberInput label={t('inv.leadTimeLabel')} min={0} suffix=" d" {...form.getInputProps('leadTimeDays')} />
              <MultiSelect label={t('inv.alternates')} data={altOptions} searchable clearable {...form.getInputProps('alternatePartIds')} />
            </Section>
            <Section title={t('inv.control')}>
              <Checkbox label={t('inv.critical')} description={t('inv.criticalHint')} {...form.getInputProps('isCritical', { type: 'checkbox' })} />
              <Checkbox label={t('inv.shelfLife')} description={t('inv.shelfLifeHint')} {...form.getInputProps('isShelfItem', { type: 'checkbox' })} />
              <Checkbox label={t('inv.allowNegative')} description={t('inv.allowNegativeHint')} {...form.getInputProps('allowNegativeStock', { type: 'checkbox' })} />
              <Checkbox label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
            </Section>
          </Stack>
        </Grid.Col>
      </Grid>
      <Group justify="flex-end" mt="lg">{buttons}</Group>
    </form>
  );
}
