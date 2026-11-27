import { useEffect, useMemo } from 'react';
import { Button, Card, Center, Checkbox, Grid, Group, Loader, MultiSelect, NumberInput, Select, SimpleGrid, Stack, Text, TextInput, Textarea, Title } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useForm } from '@mantine/form';
import { CostCentrePicker } from '@/components/CostCentrePicker';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { get, post, put } from '@/api/client';
import { DAY_NAMES, WARRANTY_TYPES, type AssetDetail, type Category, type CustomField, type DayName } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { LocationPicker } from '@/components/LocationPicker';
import { AssetPicker } from '@/components/AssetPicker';
import { handleFormError, notifySuccess, toIsoDate } from '@/components/utils';
import { listDefault, listItems } from '@/components/listLabels';
import { BrandModelPicker, useAssetGroups } from '@/components/ReferencePickers';
import { pick } from '@/components/bilingual';
import { formKey } from '@/components/formInit';

type Values = {
  assetNumber: string; name: string; description: string; categoryId: string | null; siteId: string | null; locationId: string | null; parentAssetId: string | null;
  status: string; criticality: string; manufacturer: string; model: string; serialNumber: string; barcode: string; operationNumber: string;
  commissionedOn: Date | null; disposedOn: Date | null; purchasePrice: number | ''; currentValue: number | ''; salvageValue: number | '';
  expectedLifeYears: number | ''; downtimeCostPerHour: number | ''; warrantyType: string; warrantyExpiresOn: Date | null; warrantyNotes: string;
  notes: string; notesToTechnician: string; customFields: Record<string, string>; onlyWhenStopped: boolean;
  /** 0.28.0: the Arabic texts, as stored (blank clears). */
  nameAr: string; notesToTechnicianAr: string;
  runsHoursPerDay: number | ''; runsOn: DayName[]; costCentreId: string | null;
  /** 0.21.0: brand and model from the lists, and the asset's groups. */
  brandId: string | null; modelId: string | null; groupIds: string[];
  /** 0.24.0: a unit's end of life (its serial number is edited here too; its status and place are not). */
  endOfLifeOn: Date | null;
};

const empty: Values = {
  assetNumber: '', name: '', description: '', categoryId: null, siteId: null, locationId: null, parentAssetId: null, status: 'Active', criticality: 'Medium',
  manufacturer: '', model: '', serialNumber: '', barcode: '', operationNumber: '', commissionedOn: null, disposedOn: null, purchasePrice: '', currentValue: '',
  salvageValue: '', expectedLifeYears: '', downtimeCostPerHour: '', warrantyType: 'None', warrantyExpiresOn: null, warrantyNotes: '', notes: '', notesToTechnician: '', customFields: {}, onlyWhenStopped: false, nameAr: '', notesToTechnicianAr: '',
  runsHoursPerDay: 24, runsOn: [...DAY_NAMES], costCentreId: null, brandId: null, modelId: null, groupIds: [], endOfLifeOn: null,
};

const d = (v?: string | null) => (v ? new Date(`${v}T00:00:00`) : null);
const n = (v?: number | null) => (v === undefined || v === null ? '' : v);
const num = (v: number | '') => (v === '' ? null : v);

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return <Card><Title order={5} mb="md">{title}</Title><Stack gap="sm">{children}</Stack></Card>;
}

export function AssetFormPage() {
  const { t } = useTranslation();
  const { id } = useParams();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { me, terms, can } = useAuth();
  const { data: groups } = useAssetGroups();
  const editing = !!id;
  const { data: asset, isLoading } = useQuery({ queryKey: ['asset', id], queryFn: () => get<AssetDetail>(`/assets/${id}`), enabled: editing });
  const isUnit = !!asset?.partId; // 0.24.0: a unit's status and place change through the unit actions
  const parentId = params.get('parentId');
  const { data: parent } = useQuery({ queryKey: ['asset', parentId], queryFn: () => get<AssetDetail>(`/assets/${parentId}`), enabled: !!parentId && !editing });
  const { data: cats } = useQuery({ queryKey: ['categories'], queryFn: () => get<Category[]>('/asset-categories') });
  const { data: fields } = useQuery({ queryKey: ['custom-fields', 'Asset'], queryFn: () => get<CustomField[]>('/custom-fields', { entityType: 'Asset' }) });
  // A new asset's status and criticality, unless the organization hid them (0.21.0).
  const start = useMemo<Values>(() => ({ ...empty, status: listDefault('assetStatus', empty.status), criticality: listDefault('criticality', empty.criticality) }), []);
  const form = useForm<Values>({ initialValues: start });
  const currency = me?.tenant.settings.currency ?? 'USD';
  const auto = me?.tenant.settings.autoNumberAssets ?? true;

  useEffect(() => {
    if (asset) {
      form.setValues({
        assetNumber: asset.assetNumber, name: asset.name, description: asset.description ?? '', categoryId: asset.categoryId ?? null, siteId: asset.siteId,
        locationId: asset.locationId ?? null, parentAssetId: asset.parentAssetId ?? null, status: asset.status, criticality: asset.criticality,
        manufacturer: asset.manufacturer ?? '', model: asset.model ?? '', serialNumber: asset.serialNumber ?? '', barcode: asset.barcode ?? '', operationNumber: asset.operationNumber ?? '',
        commissionedOn: d(asset.commissionedOn), disposedOn: d(asset.disposedOn), purchasePrice: n(asset.purchasePrice), currentValue: n(asset.currentValue),
        salvageValue: n(asset.salvageValue), expectedLifeYears: n(asset.expectedLifeYears), downtimeCostPerHour: n(asset.downtimeCostPerHour),
        warrantyType: asset.warrantyType, warrantyExpiresOn: d(asset.warrantyExpiresOn), warrantyNotes: asset.warrantyNotes ?? '', notes: asset.notes ?? '',
        notesToTechnician: asset.notesToTechnician ?? '', nameAr: asset.nameAr ?? '', notesToTechnicianAr: asset.notesToTechnicianAr ?? '', onlyWhenStopped: asset.onlyWhenStopped ?? false, costCentreId: asset.costCentreId ?? null, brandId: asset.brandId ?? null, modelId: asset.modelId ?? null, groupIds: asset.groupIds ?? [], customFields: Object.fromEntries(Object.entries(asset.customFields ?? {}).map(([k, v]) => [k, v ?? ''])),
        runsHoursPerDay: asset.runsHoursPerDay ?? 24, runsOn: asset.runsOn ?? [...DAY_NAMES], endOfLifeOn: d(asset.endOfLifeOn ?? undefined),
      });
      form.resetDirty();
    } else if (parent) {
      form.setValues({ ...start, siteId: parent.siteId, locationId: parent.locationId ?? null, parentAssetId: parent.id, categoryId: parent.categoryId ?? null });
    } else if (!editing) {
      const siteId = params.get('siteId');
      if (siteId) form.setValues({ ...start, siteId, locationId: params.get('locationId') });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formKey(asset, parent)]);

  const categoryId = form.values.categoryId;
  const applicable = useMemo(() => (fields ?? []).filter((f) => f.categoryIds.length === 0 || (categoryId && f.categoryIds.includes(categoryId))), [fields, categoryId]);

  const submit = form.onSubmit(async (v) => {
    const body = {
      ...v, commissionedOn: toIsoDate(v.commissionedOn), disposedOn: toIsoDate(v.disposedOn), warrantyExpiresOn: toIsoDate(v.warrantyExpiresOn),
      purchasePrice: num(v.purchasePrice), currentValue: num(v.currentValue), salvageValue: num(v.salvageValue), expectedLifeYears: num(v.expectedLifeYears),
      downtimeCostPerHour: num(v.downtimeCostPerHour), assetNumber: v.assetNumber || null, rowVersion: asset?.rowVersion, managesCostCentre: true, managesBrand: true,
      runsHoursPerDay: v.runsHoursPerDay === '' ? null : v.runsHoursPerDay, managesRunsHoursPerDay: true, // empty = all day (24)
      endOfLifeOn: toIsoDate(v.endOfLifeOn), managesEndOfLife: isUnit ? true : undefined,
    };
    try {
      const r = editing ? await put<{ id: string }>(`/assets/${id}`, body) : await post<{ id: string; assetNumber: string }>('/assets', body);
      notifySuccess(editing ? t('common.saved') : `${t('common.created')}: ${(r as { assetNumber?: string }).assetNumber ?? ''}`);
      await qc.invalidateQueries({ queryKey: ['assets'] });
      await qc.invalidateQueries({ queryKey: ['asset'] });
      await qc.invalidateQueries({ queryKey: ['hierarchy'] });
      await qc.invalidateQueries({ queryKey: ['dashboard'] });
      navigate(`/assets/${r.id}`, { replace: true });
    } catch (e) { handleFormError(form, e); }
  });

  if (editing && isLoading) return <Center h={300}><Loader /></Center>;
  const err = (k: string) => form.errors[k];
  const fieldInput = (f: CustomField) => {
    const props = { key: f.id, label: f.label, required: f.isRequired, error: err(`customFields.${f.key}`) };
    const val = form.values.customFields[f.key] ?? '';
    const setVal = (v: string) => form.setFieldValue('customFields', { ...form.values.customFields, [f.key]: v });
    switch (f.dataType) {
      case 'Number': return <NumberInput {...props} value={val === '' ? '' : Number(val)} onChange={(v) => setVal(v === '' ? '' : String(v))} />;
      case 'Date': return <DateInput {...props} value={val ? new Date(`${val}T00:00:00`) : null} onChange={(v) => setVal(toIsoDate(v) ?? '')} clearable valueFormat="DD MMM YYYY" />;
      case 'Select': return <Select {...props} data={f.options} value={val || null} onChange={(v) => setVal(v ?? '')} clearable />;
      case 'Boolean': return <Select {...props} data={[{ value: 'true', label: t('common.yes') }, { value: 'false', label: t('common.no') }]} value={val || null} onChange={(v) => setVal(v ?? '')} clearable />;
      case 'LongText': return <Textarea {...props} autosize minRows={2} value={val} onChange={(e) => setVal(e.currentTarget.value)} />;
      default: return <TextInput {...props} value={val} onChange={(e) => setVal(e.currentTarget.value)} />;
    }
  };

  return (
    <form onSubmit={submit}>
      <PageHeader title={editing ? `${t('assets.edit')} · ${asset?.assetNumber}` : t('assets.new')}
        crumbs={[{ label: terms.assets, to: '/assets' }, ...(editing ? [{ label: asset?.assetNumber ?? '', to: `/assets/${id}` }] : [])]}
        actions={<><Button variant="default" onClick={() => navigate(-1)}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{editing ? t('common.saveChanges') : t('common.create')}</Button></>} />
      <Grid gutter="lg">
        <Grid.Col span={{ base: 12, lg: 8 }}>
          <Stack gap="lg">
            <Section title={t('assets.general')}>
              <SimpleGrid cols={{ base: 1, sm: 3 }}>
                <TextInput label={t('assets.number')} placeholder={auto && !editing ? 'Auto' : undefined} description={auto && !editing ? t('assets.numberHint') : undefined}
                  required={editing || !auto} {...form.getInputProps('assetNumber')} />
                <TextInput label={t('common.name')} required style={{ gridColumn: 'span 2' }} {...form.getInputProps('name')} />
                <TextInput label={t('bilingual.nameAr')} dir="rtl" maxLength={300} style={{ gridColumn: 'span 2' }} {...form.getInputProps('nameAr')} />
              </SimpleGrid>
              <SimpleGrid cols={{ base: 1, sm: 3 }}>
                <Select label={t('assets.category')} data={(cats ?? []).filter((c) => c.isActive || c.id === form.values.categoryId).map((c) => ({ value: c.id, label: pick(c.name, c.nameAr) }))} searchable clearable {...form.getInputProps('categoryId')} />
                <Select label={t('common.status')} required data={listItems('assetStatus', t, form.values.status)} disabled={isUnit} {...form.getInputProps('status')} />
                <Select label={t('criticality.label')} required data={listItems('criticality', t, form.values.criticality)} {...form.getInputProps('criticality')} />
              </SimpleGrid>
              <Textarea label={t('common.description')} autosize minRows={2} {...form.getInputProps('description')} />
            </Section>
            {isUnit && <Section title={t('units.unitCard')}>
              <Text fz="sm" c="dimmed">{t('units.placeLocked')}</Text>
              <DateInput label={t('units.endOfLife')} valueFormat="DD MMM YYYY" clearable {...form.getInputProps('endOfLifeOn')} />
            </Section>}
            {!isUnit && <Section title={t('assets.placement')}>
              <LocationPicker siteId={form.values.siteId} locationId={form.values.locationId} siteError={err('siteId')} locationError={err('locationId')}
                onChange={(s, l) => form.setValues({ siteId: s, locationId: l })} />
              <AssetPicker label={t('assets.parent')} description={t('assets.parentHint')} value={form.values.parentAssetId} excludeId={id}
                onChange={(v) => form.setFieldValue('parentAssetId', v)} error={err('parentAssetId')}
                initial={asset?.parentAsset ?? (parent ? { id: parent.id, assetNumber: parent.assetNumber, name: parent.name } : null)} />
              <CostCentrePicker value={form.values.costCentreId} onChange={(v) => form.setFieldValue('costCentreId', v)} siteId={form.values.siteId}
                description={t('costCentres.assetHint')} error={err('costCentreId')} />
            </Section>}
            <Section title={t('assets.identification')}>
              <BrandModelPicker brandId={form.values.brandId} modelId={form.values.modelId} canAdd={can(P.assetsEdit)}
                onChange={(b, mo) => form.setValues({ brandId: b, modelId: mo })} errors={{ brand: err('brandId'), model: err('modelId') }} />
              <MultiSelect label={t('assetGroups.title')} description={t('assetGroups.assetHint')} searchable clearable
                data={(groups ?? []).filter((g) => form.values.groupIds.includes(g.id) || (g.isActive && (!g.siteId || g.siteId === form.values.siteId)))
                  .map((g) => ({ value: g.id, label: `${g.code} · ${g.name}` }))}
                value={form.values.groupIds} onChange={(v) => form.setFieldValue('groupIds', v)} error={err('groupIds')} />
              <SimpleGrid cols={{ base: 1, sm: 3 }}>
                <TextInput label={t('assets.serialNumber')} {...form.getInputProps('serialNumber')} />
                <TextInput label={t('assets.barcode')} {...form.getInputProps('barcode')} />
                <TextInput label={t('assets.operationNumber')} {...form.getInputProps('operationNumber')} />
              </SimpleGrid>
            </Section>
            {applicable.length > 0 && (
              <Section title={t('assets.specifications')}>
                <SimpleGrid cols={{ base: 1, sm: 2 }}>{applicable.map(fieldInput)}</SimpleGrid>
              </Section>
            )}
          </Stack>
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 4 }}>
          <Stack gap="lg">
            <Section title={t('assets.financial')}>
              <SimpleGrid cols={2}>
                <DateInput label={t('assets.commissionedOn')} clearable valueFormat="DD MMM YYYY" {...form.getInputProps('commissionedOn')} />
                <DateInput label={t('assets.disposedOn')} clearable valueFormat="DD MMM YYYY" {...form.getInputProps('disposedOn')} />
              </SimpleGrid>
              <NumberInput label={t('assets.purchasePrice')} prefix={`${currency} `} thousandSeparator min={0} {...form.getInputProps('purchasePrice')} />
              <SimpleGrid cols={2}>
                <NumberInput label={t('assets.currentValue')} thousandSeparator min={0} {...form.getInputProps('currentValue')} />
                <NumberInput label={t('assets.salvageValue')} thousandSeparator min={0} {...form.getInputProps('salvageValue')} />
                <NumberInput label={t('assets.expectedLife')} min={0} decimalScale={1} {...form.getInputProps('expectedLifeYears')} />
                <NumberInput label={t('assets.downtimeCost')} thousandSeparator min={0} {...form.getInputProps('downtimeCostPerHour')} />
              </SimpleGrid>
            </Section>
            <Section title={t('downtime.runningTime')}>
              <SimpleGrid cols={{ base: 1, sm: 2 }}>
                <NumberInput label={t('downtime.runsHoursPerDay')} description={t('downtime.runsHint')} min={0.5} max={24} step={0.5} decimalScale={2} {...form.getInputProps('runsHoursPerDay')} />
                <MultiSelect label={t('downtime.runsOn')} data={DAY_NAMES.map((d) => ({ value: d, label: t(`pm.days.${d}`) }))} {...form.getInputProps('runsOn')} />
              </SimpleGrid>
            </Section>
            <Section title={t('assets.warranty')}>
              <Select label={t('assets.warrantyType')} data={WARRANTY_TYPES.map((w) => ({ value: w, label: t(`warrantyType.${w}`) }))} {...form.getInputProps('warrantyType')} />
              {form.values.warrantyType !== 'None' && <>
                <DateInput label={t('assets.warrantyExpires')} clearable valueFormat="DD MMM YYYY" {...form.getInputProps('warrantyExpiresOn')} />
                <Textarea label={t('assets.warrantyNotes')} autosize minRows={2} {...form.getInputProps('warrantyNotes')} />
              </>}
            </Section>
            <Section title={t('common.notes')}>
              <Textarea label={t('assets.notesToTechnician')} autosize minRows={2} {...form.getInputProps('notesToTechnician')} />
              <Textarea label={t('bilingual.notesAr')} dir="rtl" autosize minRows={2} {...form.getInputProps('notesToTechnicianAr')} />
              <Checkbox label={t('accessWindows.maintainOnlyWhenStopped')} description={t('accessWindows.maintainOnlyWhenStoppedHint')} {...form.getInputProps('onlyWhenStopped', { type: 'checkbox' })} />
              <Textarea label={t('common.notes')} autosize minRows={3} {...form.getInputProps('notes')} />
            </Section>
          </Stack>
        </Grid.Col>
      </Grid>
      <Group justify="flex-end" mt="lg"><Button variant="default" onClick={() => navigate(-1)}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{editing ? t('common.saveChanges') : t('common.create')}</Button></Group>
    </form>
  );
}
