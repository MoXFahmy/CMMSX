import { AiSettings } from './AiSettings';
import { AiUsageSettings } from './AiUsageSettings';
import { SecuritySettings } from './SecuritySettings';
import { ActivityLog } from './ActivityLog';
import { StorageSettings } from './StorageSettings';
import { ApiKeysSettings } from './ApiKeysSettings';
import { ServiceAccountModal } from './ServiceAccountModal';
import { AccessAfterSave, removesLastDeletedPlace } from './userAccess';
import { useEffect, useState } from 'react';
import { ActionIcon, Badge, Button, Card, Checkbox, ColorInput, FileButton, Group, Modal, MultiSelect, NumberInput, PasswordInput, Select, SimpleGrid, Stack, Switch, Tabs, TagsInput, Text, TextInput, Textarea, Title } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { modals } from '@mantine/modals';
import { useForm, type UseFormReturnType } from '@mantine/form';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { ChecklistsCard } from '@/pages/assets/AssetChecklists';
import { DataTable } from 'mantine-datatable';
import { IconDevices, IconEdit, IconKey, IconPlus, IconTrash, IconUserOff } from '@tabler/icons-react';
import { DevicesModal } from '@/components/DevicesModal';
import { AnonymiseModal } from '@/components/AnonymiseModal';
import { TargetsEditor, timeZones } from './TargetsEditor';
import { WorkingHoursEditor, organizationHours } from '@/components/WorkingHoursEditor';
import { LANGUAGES } from '@/language';
import { api, del, get, getAllSites, post, put } from '@/api/client';
import { DAY_NAMES, FIELD_TYPES, type Category, type CustomField, type FollowUpKind, type Hierarchy, type PmTaskListItem, type Role, type TenantSettings, type User } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { NotificationSettings } from './NotificationSettings';
import { ImportSettings } from './ImportSettings';
import { RequestTypesSettings } from './RequestTypesSettings';
import { PageHeader } from '@/components/PageHeader';
import { LookupTable } from '@/components/LookupTable';
import { PurchasingSettings } from './PurchasingSettings';
import { ContractorCategoriesSettings } from './ContractorCategoriesSettings';
import { ContractSettings } from './ContractSettings';
import { CostCentresSettings } from './CostCentresSettings';
import { ListsSettings } from './ListsSettings';
import { RoleSettings } from './RoleSettings';
import { PrintLayoutsSettings } from './PrintLayoutsSettings';
import { BrandsSettings } from './BrandsSettings';
import { UnitsSettings } from './UnitsSettings';
import { AssetGroupsSettings } from './AssetGroupsSettings';
import { previewNumber } from './numbering';
import { locationOptions } from '@/components/LocationPicker';
import { confirmDelete } from '@/components/confirm';
import { fromNow, handleFormError, notifyError, notifySuccess } from '@/components/utils';
import { listItems } from '@/components/listLabels';
import { TranslateButton } from '@/components/TranslateButton';

export function SettingsPage() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const { '*': sub } = useParams();
  const navigate = useNavigate();
  const tabs = [
    { value: 'organization', label: t('settings.organization'), show: true },
    { value: 'categories', label: t('settings.categories'), show: can(P.assetsView) },
    { value: 'fields', label: t('settings.customFields'), show: can(P.settingsManage) },
    { value: 'notifications', label: t('settings.notifications'), show: can(P.settingsManage) },
    { value: 'symptoms', label: t('settings.symptoms'), show: can(P.woView) },
    { value: 'failure-codes', label: t('settings.causes'), show: can(P.woView) },
    { value: 'remedies', label: t('settings.remedies'), show: can(P.woView) },
    { value: 'request-types', label: t('requestTypes.title'), show: can(P.settingsManage) },
    { value: 'cancel-reasons', label: t('settings.cancelReasons'), show: can(P.woView) },
    { value: 'availability-types', label: t('availability.types'), show: can(P.woView) },
    { value: 'lists', label: t('lists.title'), show: can(P.settingsManage) },
    { value: 'brands', label: t('brands.title'), show: can(P.assetsView) },
    { value: 'units', label: t('units.title'), show: can(P.inventoryView) || can(P.settingsManage) },
    { value: 'asset-groups', label: t('assetGroups.title'), show: can(P.assetsView) },
    { value: 'cost-centres', label: t('costCentres.title'), show: can(P.woView) || can(P.settingsManage) },
    { value: 'purchasing', label: t('settings.purchasingTab'), show: can(P.settingsManage) },
    { value: 'contractor-categories', label: t('contractorCategories.title'), show: can(P.purchasingView) || can(P.suppliersManage) },
    { value: 'contract-types', label: t('contracts.types'), show: can(P.contractsView) },
    { value: 'contracts', label: t('contracts.title'), show: can(P.settingsManage) },
    { value: 'users', label: t('settings.users'), show: can(P.usersManage) },
    { value: 'roles', label: t('settings.roles'), show: can(P.usersManage) },
    { value: 'api-keys', label: t('apiKeys.title'), show: can(P.usersManage) },
    { value: 'security', label: t('security.title'), show: can(P.settingsManage) },
    { value: 'activity', label: t('activity.title'), show: can(P.settingsManage) },
    { value: 'storage', label: t('storage.title'), show: can(P.settingsManage) },
    { value: 'print-layouts', label: t('print.title'), show: can(P.settingsManage) },
    { value: 'import', label: t('settings.import'), show: can(P.settingsManage) },
    { value: 'ai-usage', label: t('ai.usageTab'), show: can(P.settingsManage) },
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
        <Tabs.Panel value="notifications"><NotificationSettings /></Tabs.Panel>
        <Tabs.Panel value="symptoms"><LookupTable path="/symptoms" bilingual="symptoms" title={t('settings.symptoms')} newLabel={t('settings.newSymptom')} editable={can(P.settingsManage)} /></Tabs.Panel>
        <Tabs.Panel value="failure-codes"><LookupTable path="/failure-codes" bilingual="causes" title={t('settings.causes')} newLabel={t('settings.newCause')} editable={can(P.settingsManage)} /></Tabs.Panel>
        <Tabs.Panel value="remedies"><LookupTable path="/remedies" bilingual="remedies" title={t('settings.remedies')} newLabel={t('settings.newRemedy')} editable={can(P.settingsManage)} /></Tabs.Panel>
        <Tabs.Panel value="request-types"><RequestTypesSettings editable={can(P.settingsManage)} /></Tabs.Panel>
        <Tabs.Panel value="availability-types"><LookupTable path="/availability-types" title={t('availability.types')} newLabel={t('availability.newType')} editable={can(P.settingsManage)} withColor
          kinds={[{ value: 'Away', label: t('availability.away') }, { value: 'Extra', label: t('availability.extra') }]} /></Tabs.Panel>
        <Tabs.Panel value="cancel-reasons"><LookupTable path="/cancel-reasons" bilingual="cancel-reasons" title={t('settings.cancelReasons')} newLabel={t('settings.newCancelReason')} editable={can(P.settingsManage)} /></Tabs.Panel>
        <Tabs.Panel value="users"><UserSettings /></Tabs.Panel>
        <Tabs.Panel value="roles"><RoleSettings /></Tabs.Panel>
        <Tabs.Panel value="api-keys"><ApiKeysSettings account={new URLSearchParams(window.location.search).get('account')} /></Tabs.Panel>
        <Tabs.Panel value="security"><SecuritySettings editable={can(P.settingsManage)} /></Tabs.Panel>
        <Tabs.Panel value="activity"><ActivityLog /></Tabs.Panel>
        <Tabs.Panel value="storage"><StorageSettings /></Tabs.Panel>
        <Tabs.Panel value="print-layouts"><PrintLayoutsSettings /></Tabs.Panel>
        <Tabs.Panel value="import"><ImportSettings /></Tabs.Panel>
        <Tabs.Panel value="ai-usage"><AiUsageSettings /></Tabs.Panel>
        <Tabs.Panel value="lists"><ListsSettings editable={can(P.settingsManage)} /></Tabs.Panel>
        <Tabs.Panel value="brands"><BrandsSettings editable={can(P.assetsEdit)} /></Tabs.Panel>
        <Tabs.Panel value="units"><UnitsSettings editable={can(P.settingsManage)} /></Tabs.Panel>
        <Tabs.Panel value="asset-groups"><AssetGroupsSettings editable={can(P.assetsEdit)} /></Tabs.Panel>
        <Tabs.Panel value="cost-centres"><CostCentresSettings editable={can(P.settingsManage)} /></Tabs.Panel>
        <Tabs.Panel value="purchasing"><PurchasingSettings /></Tabs.Panel>
        <Tabs.Panel value="contractor-categories"><ContractorCategoriesSettings editable={can(P.suppliersManage)} /></Tabs.Panel>
        <Tabs.Panel value="contract-types"><LookupTable path="/contract-types" title={t('contracts.types')} newLabel={t('contracts.newType')} editable={can(P.contractsManage)} /></Tabs.Panel>
        <Tabs.Panel value="contracts"><ContractSettings editable={can(P.settingsManage)} /></Tabs.Panel>
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
    try {
      // E-mail settings belong to the Notifications tab: keep what is saved now, not what this form loaded.
      const now = await get<{ settings: TenantSettings }>('/settings');
      await put('/settings', { ...v, settings: { ...v.settings, emailEnabled: now.settings.emailEnabled, disabledEmails: now.settings.disabledEmails, emailRecipients: now.settings.emailRecipients } });
      notifySuccess(t('common.saved')); await refresh(); await qc.invalidateQueries({ queryKey: ['settings'] });
    } catch (e) { handleFormError(form, e); }
  });
  // Each term in English and in Arabic: whoever reads CMX in Arabic sees the organization's Arabic names.
  const termRow = (key: 'site' | 'location' | 'asset', plural: 'sites' | 'locations' | 'assets') => (
    <SimpleGrid cols={{ base: 2, sm: 4 }} key={key}>
      <TextInput label={`${t(`settings.levels.${key}`)} · English`} disabled={!editable} {...form.getInputProps(`settings.terminology.${key}`)} />
      <TextInput label={t('settings.plural')} disabled={!editable} {...form.getInputProps(`settings.terminology.${plural}`)} />
      <TextInput label={`${t(`settings.levels.${key}`)} · العربية`} dir="rtl" disabled={!editable} {...form.getInputProps(`settings.terminologyArabic.${key}`)} />
      <TextInput label={`${t('settings.plural')} · العربية`} dir="rtl" disabled={!editable} {...form.getInputProps(`settings.terminologyArabic.${plural}`)} />
    </SimpleGrid>
  );
  return (
    <form onSubmit={submit}>
      <Stack maw={760}>
        <Card><Stack>
          <Title order={5}>{t('settings.organization')}</Title>
          <TextInput label={t('auth.organizationName')} disabled={!editable} {...form.getInputProps('name')} />
          <LogoSetting editable={editable} version={data.settings.logoVersion ?? null} onChange={async () => { await refresh(); await qc.invalidateQueries({ queryKey: ['settings'] }); }} />
          <SimpleGrid cols={2}>
            <TextInput label={t('settings.currency')} description={t('settings.currencyHint')} maxLength={3} disabled={!editable} {...form.getInputProps('settings.currency')} />
            <Select label={t('locations.timeZone')} disabled={!editable} searchable data={timeZones(form.values.settings.timeZone)} {...form.getInputProps('settings.timeZone')} />
            <Select label={t('settings.defaultLanguage')} description={t('settings.defaultLanguageHint')} disabled={!editable} allowDeselect={false}
              data={LANGUAGES.map((l) => ({ value: l.code, label: l.name }))} {...form.getInputProps('settings.defaultLanguage')} />
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
        <Card><Stack>
          <Title order={5}>{t('settings.woNumbering')}</Title>
          <SimpleGrid cols={2}>
            <TextInput label={t('settings.woPrefix')} disabled={!editable} {...form.getInputProps('settings.workOrderNumberPrefix')} />
            <TextInput label={t('settings.wrPrefix')} disabled={!editable} {...form.getInputProps('settings.workRequestNumberPrefix')} />
          </SimpleGrid>
          <Switch label={t('settings.restartYearly')} description={t('settings.restartYearlyHint')} disabled={!editable}
            {...form.getInputProps('settings.restartNumbersYearly', { type: 'checkbox' })} />
          <Text fz="sm" c="dimmed">{t('settings.example', { value: previewNumber(s.workOrderNumberPrefix ?? '', 5, !!s.restartNumbersYearly, new Date()) })}</Text>
          <Switch label={t('settings.requireLaborToComplete')} disabled={!editable} {...form.getInputProps('settings.requireLaborToComplete', { type: 'checkbox' })} />
          <Switch label={t('settings.requireAllTasksDone')} disabled={!editable} {...form.getInputProps('settings.requireAllTasksDone', { type: 'checkbox' })} />
          <Switch label={t('settings.requireReadingSteps')} disabled={!editable} {...form.getInputProps('settings.requireReadingSteps', { type: 'checkbox' })} />
          <FollowUpDefault form={form} editable={editable} />
          <Switch label={t('settings.requireFailureCodeForBreakdowns')} disabled={!editable} {...form.getInputProps('settings.requireFailureCodeForBreakdowns', { type: 'checkbox' })} />
          <Switch label={t('settings.autoStartWork')} disabled={!editable} {...form.getInputProps('settings.autoStartWork', { type: 'checkbox' })} />
          <Switch label={t('settings.autoGeneratePm')} disabled={!editable} {...form.getInputProps('settings.autoGeneratePm', { type: 'checkbox' })} />
        </Stack></Card>
        <Card><Stack gap="xs">
          <Title order={5}>{t('settings.requiredComments')}</Title>
          <Text fz="sm" c="dimmed">{t('settings.requiredCommentsHint')}</Text>
          <Checkbox.Group value={s.commentRequiredFor ?? []} onChange={(v) => form.setFieldValue('settings.commentRequiredFor', v)}>
            <Stack gap={6} mt={4}>
              {['reject', 'refuse', 'hold', 'reopen', 'cancel'].map((a) => <Checkbox key={a} value={a} disabled={!editable} label={t(`settings.commentFor.${a}`)} />)}
            </Stack>
          </Checkbox.Group>
          <Checkbox label={t('settings.commentFor.complete')} disabled={!editable} {...form.getInputProps('settings.requireCompletionNotes', { type: 'checkbox' })} />
        </Stack></Card>
        <Card><Stack>
          <div><Title order={5}>{t('settings.pmRules')}</Title><Text c="dimmed" fz="sm">{t('settings.pmRulesHint')}</Text></div>
          <MultiSelect label={t('settings.pmSkipAssetStatuses')} disabled={!editable} clearable
            data={listItems('assetStatus', t, null, true).filter((x) => x.value !== 'Active')} {...form.getInputProps('settings.pmSkipAssetStatuses')} />
          <MultiSelect label={t('settings.pmNonWorkingDays')} disabled={!editable} clearable
            data={DAY_NAMES.map((d) => ({ value: d, label: t(`pm.days.${d}`) }))}
            {...form.getInputProps('settings.pmNonWorkingDays')} />
          <Switch label={t('settings.pmNoStacking')} disabled={!editable} {...form.getInputProps('settings.pmNoStacking', { type: 'checkbox' })} />
        </Stack></Card>
        <Card><Stack>
          <div><Title order={5}>{t('settings.targets')}</Title><Text c="dimmed" fz="sm">{t('settings.targetsHint')}</Text></div>
          <TargetsEditor disabled={!editable} value={form.values.settings.slaTargets ?? []} onChange={(v) => form.setFieldValue('settings.slaTargets', v)} />
          {form.errors['settings.slaTargets'] && <Text c="red" fz="sm">{form.errors['settings.slaTargets']}</Text>}
          <div><Text fw={500} fz="sm">{t('hours.organization')}</Text><Text c="dimmed" fz="xs">{t('hours.organizationHint')}</Text></div>
          <WorkingHoursEditor disabled={!editable} value={organizationHours(form.values.settings)} error={form.errors['settings.workingHours']}
            onChange={(v) => form.setFieldValue('settings.workingHours', v ?? [])} />
        </Stack></Card>
        <Card><Stack>
          <Title order={5}>{t('settings.purchasing')}</Title>
          <SimpleGrid cols={2}>
            <TextInput label={t('settings.poPrefix')} disabled={!editable} {...form.getInputProps('settings.purchaseOrderNumberPrefix')} />
            <TextInput label={t('settings.prPrefix')} disabled={!editable} {...form.getInputProps('settings.purchaseRequestNumberPrefix')} />
            <NumberInput label={t('settings.defaultTax')} min={0} max={100} suffix="%" decimalScale={3} disabled={!editable} {...form.getInputProps('settings.defaultTaxPercent')} />
          </SimpleGrid>
        </Stack></Card>
        <AiSettings form={form} editable={editable} />
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

/** What a failed checklist step raises when the step says "Default" (0.13.0): a request for triage, a work order from a template, or nothing. */
function FollowUpDefault({ form, editable }: { form: UseFormReturnType<{ name: string; settings: TenantSettings }>; editable: boolean }) {
  const { t } = useTranslation();
  const { data: templates } = useQuery({ queryKey: ['pm-tasks'], queryFn: () => get<PmTaskListItem[]>('/pm-tasks') });
  const kind = form.values.settings.followUpDefault ?? 'Request';
  return (
    <Group gap="xs" align="flex-end" wrap="wrap">
      <Select w={240} label={t('settings.followUpDefault')} description={t('settings.followUpDefaultHint')} disabled={!editable}
        data={(['Request', 'WorkOrder', 'None'] as const).map((x) => ({ value: x, label: t(`steps.followUps.${x}`) }))} value={kind}
        onChange={(x) => form.setFieldValue('settings.followUpDefault', (x ?? 'Request') as FollowUpKind)} />
      {kind === 'WorkOrder' && (
        <Select style={{ flex: 1, minWidth: 240 }} searchable label={t('steps.followUpTemplate')} disabled={!editable} error={form.errors['settings.followUpTemplateId']}
          data={(templates ?? []).filter((x) => x.isActive).map((x) => ({ value: x.id, label: `${x.code} · ${x.name}` }))}
          value={form.values.settings.followUpTemplateId ?? null} onChange={(x) => form.setFieldValue('settings.followUpTemplateId', x)} />
      )}
    </Group>
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
  const form = useForm<{ code: string; name: string; nameAr: string; description: string; color: string; isActive: boolean; downtimeCostPerHour: number | '' }>({
    initialValues: { code: '', name: '', nameAr: '', description: '', color: '#228be6', isActive: true, downtimeCostPerHour: '' } });
  const open = (c: Category | null) => {
    setEditing(c);
    form.setValues(c ? { code: c.code, name: c.name, nameAr: c.nameAr ?? '', description: c.description ?? '', color: c.color ?? '#228be6', isActive: c.isActive, downtimeCostPerHour: c.downtimeCostPerHour ?? '' }
      : { code: '', name: '', nameAr: '', description: '', color: '#228be6', isActive: true, downtimeCostPerHour: '' });
    form.clearErrors(); m.open();
  };
  const submit = form.onSubmit(async (values) => {
    const v = { ...values, downtimeCostPerHour: values.downtimeCostPerHour === '' ? null : values.downtimeCostPerHour };
    try { if (editing) await put(`/asset-categories/${editing.id}`, v); else await post('/asset-categories', v); notifySuccess(t('common.saved')); await qc.invalidateQueries({ queryKey: ['categories'] }); m.close(); }
    catch (e) { handleFormError(form, e); }
  });
  return (
    <Card p={0}>
      <Group justify="space-between" p="md"><Title order={5}>{t('settings.categories')}</Title><Group gap="xs"><TranslateButton list="categories" refresh={[['categories']]} />{editable && <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('settings.newCategory')}</Button>}</Group></Group>
      <DataTable minHeight={150} fetching={isFetching} records={data ?? []} noRecordsText={t('common.noRecords')} columns={[
        { accessor: 'code', title: t('common.code'), width: 120 },
        { accessor: 'name', title: t('common.name'), render: (c) => <Group gap={8}><span style={{ width: 10, height: 10, borderRadius: 5, background: c.color ?? '#adb5bd', display: 'inline-block' }} />{c.name}</Group> },
        { accessor: 'nameAr', title: t('bilingual.nameAr'), render: (c) => <Text fz="sm" dir="rtl">{c.nameAr}</Text> },
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
          <TextInput label={t('bilingual.nameAr')} dir="rtl" maxLength={200} {...form.getInputProps('nameAr')} />
          <Textarea label={t('common.description')} autosize {...form.getInputProps('description')} />
          <ColorInput label={t('settings.color')} {...form.getInputProps('color')} />
          <NumberInput label={t('settings.downtimeCostPerHour')} description={t('settings.downtimeCostHint')} min={0} decimalScale={2} {...form.getInputProps('downtimeCostPerHour')} />
          <Checkbox label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack></form>
        {editing && <Stack mt="md"><ChecklistsCard categoryId={editing.id} canEdit={editable} /></Stack>}
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
  const [anonymiseUser, setAnonymiseUser] = useState<string | null>(null);
  const [serviceAccount, setServiceAccount] = useState<User | null | undefined>(undefined);
  const navigate = useNavigate();
  const { data, isFetching } = useQuery({ queryKey: ['users'], queryFn: () => get<User[]>('/users') });
  const { data: roles } = useQuery({ queryKey: ['roles'], queryFn: () => get<Role[]>('/roles') });
  // Every site and place the editor may see, whatever site the header switch is on.
  const { data: h } = useQuery({ queryKey: ['hierarchy', 'all-sites'], queryFn: () => getAllSites<Hierarchy>('/locations/hierarchy'), staleTime: 60_000 });
  const { terms } = useAuth();
  const siteName = (id: string) => h?.sites.find((s) => s.id === id)?.name;
  // Every place the editor can see, labelled with its site and path.
  const placeOptions = (h?.sites ?? []).flatMap((s) => locationOptions(h!.locations, s.id).map((o) => ({ value: o.value, label: `${s.name} › ${o.label}` })));
  const [opened, m] = useDisclosure();
  const [pwUser, setPwUser] = useState<User | null>(null);
  const [editing, setEditing] = useState<User | null>(null);
  const [devicesUser, setDevicesUser] = useState<User | null>(null);
  const blank = { fullName: '', email: '', jobTitle: '', phone: '', isActive: true, roleIds: [] as string[], password: '', purchaseApprovalLimit: '' as number | '',
    siteIds: [] as string[], locationIds: [] as string[] };
  const form = useForm({ initialValues: blank });
  const pwForm = useForm({ initialValues: { password: '' } });
  const open = (u: User | null) => {
    setEditing(u);
    form.setValues(u ? { fullName: u.fullName, email: u.email, jobTitle: u.jobTitle ?? '', phone: u.phone ?? '', isActive: u.isActive, roleIds: u.roles.map((r) => r.roleId), password: '',
      purchaseApprovalLimit: u.purchaseApprovalLimit ?? '', siteIds: u.siteIds ?? [], locationIds: u.locationIds ?? [] } : blank);
    form.clearErrors(); m.open();
  };
  const save = async (v: typeof blank) => {
    const body = { ...v, purchaseApprovalLimit: v.purchaseApprovalLimit === '' ? null : v.purchaseApprovalLimit };
    try { if (editing) await put(`/users/${editing.id}`, { ...body, password: null }); else await post('/users', body); notifySuccess(t('common.saved')); await qc.invalidateQueries({ queryKey: ['users'] }); m.close(); }
    catch (e) { handleFormError(form, e); }
  };
  const submit = form.onSubmit(async (v) => {
    // Taking the last "(deleted place)" off turns "sees nothing" into the stored sites (0.9.1): say so before saving. What is deleted
    // is the server's word (the editor's tree hides places they can't see), so this doesn't wait for the tree to load.
    if (editing && removesLastDeletedPlace(editing.locationIds ?? [], v.locationIds, editing.deletedLocationIds ?? [])) {
      modals.openConfirmModal({
        title: t('access.deletedPlaceTitle'),
        children: <AccessAfterSave sites={v.siteIds.map((id) => siteName(id) ?? id)} />,
        labels: { confirm: t('common.confirm'), cancel: t('common.cancel') },
        onConfirm: () => void save(v),
      });
      return;
    }
    await save(v);
  });
  const submitPw = pwForm.onSubmit(async (v) => {
    try { await post(`/users/${pwUser!.id}/password`, v); notifySuccess(t('settings.passwordReset')); setPwUser(null); pwForm.reset(); } catch (e) { handleFormError(pwForm, e); }
  });
  return (
    <Card p={0}>
      <Group justify="space-between" p="md"><Title order={5}>{t('settings.users')}</Title>
        <Group gap="xs">
          <Button size="xs" variant="default" leftSection={<IconPlus size={14} />} onClick={() => setServiceAccount(null)}>{t('serviceAccounts.new')}</Button>
          <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('settings.newUser')}</Button>
        </Group>
      </Group>
      <DataTable minHeight={150} fetching={isFetching} records={data ?? []} noRecordsText={t('common.noRecords')} columns={[
        { accessor: 'fullName', title: t('common.name'), render: (u) => <Stack gap={0}><Text fz="sm" fw={500}>{u.fullName}</Text>
          {u.isServiceAccount ? <Badge size="xs" variant="light" color="violet" w="fit-content">{t('serviceAccounts.badge')}</Badge> : <Text fz="xs" c="dimmed">{u.email}</Text>}</Stack> },
        { accessor: 'jobTitle', title: t('settings.jobTitle') },
        { accessor: 'roles', title: t('settings.rolesLabel'), render: (u) => <Group gap={4}>{u.roles.map((r) => <Badge key={r.roleId} variant="default">{r.name}</Badge>)}</Group> },
        { accessor: 'siteIds', title: terms.sites, render: (u) => (u.locationIds?.length
          ? <Text fz="sm">{t('access.placesCount', { count: u.locationIds.length })}</Text>
          : u.siteIds?.length ? <Text fz="sm" lineClamp={1}>{u.siteIds.map((id) => siteName(id) ?? '…').join(', ')}</Text>
          : <Text fz="sm" c="dimmed">{t('access.allSites', { sites: terms.sites.toLowerCase() })}</Text>) },
        { accessor: 'lastLoginAt', title: t('settings.lastLogin'), render: (u) => (u.lastLoginAt ? fromNow(u.lastLoginAt) : t('settings.never')) },
        { accessor: 'isActive', title: t('common.status'), render: (u) => (u.anonymisedAt ? <Badge color="dark" variant="light">{t('anonymise.badge')}</Badge>
          : <Badge color={u.isActive ? 'teal' : 'gray'}>{u.isActive ? t('common.active') : t('common.inactive')}</Badge>) },
        { accessor: 'actions', title: '', width: 150, render: (u) => (u.anonymisedAt ? null : u.isServiceAccount ? (
          // 0.26.0: a service account is edited in its own dialog; its keys are under API keys; deleting it revokes them.
          <Group gap={4} justify="flex-end" wrap="nowrap">
            <ActionIcon variant="subtle" onClick={() => setServiceAccount(u)} aria-label={t('common.edit')}><IconEdit size={16} /></ActionIcon>
            <ActionIcon variant="subtle" color="gray" onClick={() => navigate(`/settings/api-keys?account=${u.id}`)} aria-label={t('apiKeys.title')}><IconKey size={16} /></ActionIcon>
            <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => confirmDelete(u.fullName, async () => {
              try { await del(`/service-accounts/${u.id}`); await qc.invalidateQueries({ queryKey: ['users'] }); await qc.invalidateQueries({ queryKey: ['api-keys'] }); }
              catch (e) { notifyError(e); }
            }, t('serviceAccounts.deleteBody'))}><IconTrash size={16} /></ActionIcon>
          </Group>
        ) : (
          <Group gap={4} justify="flex-end" wrap="nowrap">
            <ActionIcon variant="subtle" onClick={() => open(u)} aria-label={t('common.edit')}><IconEdit size={16} /></ActionIcon>
            <ActionIcon variant="subtle" color="gray" onClick={() => setPwUser(u)} aria-label={t('settings.resetPassword')}><IconKey size={16} /></ActionIcon>
            <ActionIcon variant="subtle" color="gray" onClick={() => setDevicesUser(u)} aria-label={t('devices.title')}><IconDevices size={16} /></ActionIcon>
            {/* 0.25.0: an inactive sign-in (with or without an employee record) can be anonymised. */}
            {!u.isActive && <ActionIcon variant="subtle" color="gray" onClick={() => setAnonymiseUser(u.id)} aria-label={t('anonymise.action')}><IconUserOff size={16} /></ActionIcon>}
          </Group>)) },
      ]} />
      <AnonymiseModal who={anonymiseUser ? { userId: anonymiseUser } : null} onClose={() => setAnonymiseUser(null)} />
      <ServiceAccountModal account={serviceAccount ?? null} opened={serviceAccount !== undefined} onClose={() => setServiceAccount(undefined)} roles={roles ?? []}
        sites={(h?.sites ?? []).map((x) => ({ value: x.id, label: x.name }))} places={placeOptions} />
      <Modal opened={opened} onClose={m.close} title={editing ? t('settings.editUser') : t('settings.newUser')} size="lg">
        <form onSubmit={submit}><Stack>
          <SimpleGrid cols={2}>
            <TextInput label={t('common.name')} required {...form.getInputProps('fullName')} />
            <TextInput label={t('auth.email')} required type="email" {...form.getInputProps('email')} />
            <TextInput label={t('settings.jobTitle')} {...form.getInputProps('jobTitle')} />
            <TextInput label={t('settings.phone')} {...form.getInputProps('phone')} />
          </SimpleGrid>
          <MultiSelect label={t('settings.rolesLabel')} required data={(roles ?? []).map((r) => ({ value: r.id, label: r.name }))} {...form.getInputProps('roleIds')} />
          <NumberInput label={t('settings.approvalLimit')} description={t('settings.approvalLimitHint')} min={0} thousandSeparator {...form.getInputProps('purchaseApprovalLimit')} />
          <MultiSelect label={terms.sites} description={t('access.userSitesHint', { sites: terms.sites.toLowerCase() })} searchable clearable disabled={form.values.locationIds.length > 0}
            data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} {...form.getInputProps('siteIds')} />
          <MultiSelect label={t('access.userBranches')} description={t('access.userBranchesHint')} searchable clearable
            // A place deleted since it was given still shows, so it can be taken off.
            data={[...placeOptions, ...form.values.locationIds.filter((id) => h && !placeOptions.some((o) => o.value === id)).map((id) => ({ value: id, label: (editing?.deletedLocationIds ?? []).includes(id) ? t('access.deletedPlace') : t('access.hiddenPlace') }))]}
            {...form.getInputProps('locationIds')} />
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
      <DevicesModal opened={!!devicesUser} onClose={() => setDevicesUser(null)} user={devicesUser ?? undefined} />
    </Card>
  );
}

/** The organization's logo (0.14.0), shown on printed reports and job cards: PNG, JPEG or WebP up to 1 MB. */
function LogoSetting({ editable, version, onChange }: { editable: boolean; version: number | null; onChange: () => Promise<void> }) {
  const { t } = useTranslation();
  const upload = async (file: File | null) => {
    if (!file) return;
    if (file.size > 1024 * 1024) { notifyError(new Error(t('settings.logoTooBig'))); return; }
    const fd = new FormData(); fd.append('file', file);
    try { await api('/settings/logo', { method: 'POST', body: fd }); notifySuccess(t('common.saved')); await onChange(); } catch (e) { notifyError(e); }
  };
  const remove = async () => { try { await del('/settings/logo'); await onChange(); } catch (e) { notifyError(e); } };
  return (
    <Stack gap={4}>
      <Text fz="sm" fw={500}>{t('settings.logo')}</Text>
      <Group gap="sm">
        {version ? <img src={`/api/settings/logo?v=${version}`} alt={t('settings.logo')} style={{ height: 48, maxWidth: 192, objectFit: 'contain' }} />
          : <Text fz="sm" c="dimmed">{t('settings.noLogo')}</Text>}
        {editable && <FileButton onChange={(f) => void upload(f)} accept="image/png,image/jpeg,image/webp">
          {(props) => <Button {...props} size="xs" variant="light">{version ? t('settings.replaceLogo') : t('settings.uploadLogo')}</Button>}
        </FileButton>}
        {editable && version && <Button size="xs" variant="subtle" color="red" onClick={() => void remove()}>{t('settings.removeLogo')}</Button>}
      </Group>
      <Text fz="xs" c="dimmed">{t('settings.logoHint')}</Text>
    </Stack>
  );
}

