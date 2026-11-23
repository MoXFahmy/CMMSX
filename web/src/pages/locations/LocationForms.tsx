import { Accordion, Button, Checkbox, Group, Modal, MultiSelect, NumberInput, Select, SimpleGrid, Stack, Text, TextInput, Textarea } from '@mantine/core';
import { useForm } from '@mantine/form';
import { CostCentrePicker } from '@/components/CostCentrePicker';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useEffect, useMemo } from 'react';
import { get, post, put } from '@/api/client';
import { WorkingHoursEditor, organizationHours } from '@/components/WorkingHoursEditor';
import type { WorkingDay } from '@/components/workingHours';
import { CRITICALITIES, LOCATION_TYPES, type LocationDetail, type Site } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { locationOptions, useHierarchy } from '@/components/LocationPicker';
import { handleFormError, notifySuccess } from '@/components/utils';

export function SiteFormModal({ opened, onClose, site, onSaved }: { opened: boolean; onClose: () => void; site?: Site | null; onSaved?: (id: string) => void }) {
  const { t } = useTranslation();
  const { terms } = useAuth();
  const qc = useQueryClient();
  const { me } = useAuth();
  const blankSettings = { assetNumberPrefix: '', workOrderNumberPrefix: '', workRequestNumberPrefix: '', purchaseOrderNumberPrefix: '', defaultStoreId: null as string | null,
    requestRecipientIds: [] as string[], purchaseRequestRecipientIds: [] as string[], purchaseRequestNumberPrefix: '', emailSenderName: '', workingHours: null as WorkingDay[] | null };
  const form = useForm({
    initialValues: { code: '', name: '', address: '', city: '', country: '', timeZone: '', latitude: '' as number | '', longitude: '' as number | '', notes: '', isActive: true,
      settings: blankSettings },
  });
  // Pickers for the site's own settings (0.9.0); each list may be closed to this person (no access to stores or users): then it's empty.
  const { data: stores } = useQuery({ queryKey: ['stores'], queryFn: () => get<{ id: string; code: string; name: string; siteId: string; isActive: boolean }[]>('/stores'),
    enabled: opened && !!site, retry: false });
  const { data: people } = useQuery({ queryKey: ['users-lookup'], queryFn: () => get<{ id: string; fullName: string; email: string }[]>('/users/lookup'), enabled: opened, retry: false });
  useEffect(() => {
    if (!opened) return;
    form.setValues(site ? {
      code: site.code, name: site.name, address: site.address ?? '', city: site.city ?? '', country: site.country ?? '', timeZone: site.timeZone ?? '',
      latitude: site.latitude ?? '', longitude: site.longitude ?? '', notes: site.notes ?? '', isActive: site.isActive,
      settings: {
        assetNumberPrefix: site.settings?.assetNumberPrefix ?? '', workOrderNumberPrefix: site.settings?.workOrderNumberPrefix ?? '',
        workRequestNumberPrefix: site.settings?.workRequestNumberPrefix ?? '', purchaseOrderNumberPrefix: site.settings?.purchaseOrderNumberPrefix ?? '',
        defaultStoreId: site.settings?.defaultStoreId ?? null, requestRecipientIds: site.settings?.requestRecipientIds ?? [], purchaseRequestRecipientIds: site.settings?.purchaseRequestRecipientIds ?? [],
        purchaseRequestNumberPrefix: site.settings?.purchaseRequestNumberPrefix ?? '', emailSenderName: site.settings?.emailSenderName ?? '',
        workingHours: site.settings?.workingHours ?? null,
      },
    } : { code: '', name: '', address: '', city: '', country: '', timeZone: '', latitude: '', longitude: '', notes: '', isActive: true, settings: blankSettings });
    form.resetDirty();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [opened, site]);
  const submit = form.onSubmit(async (v) => {
    const body = { ...v, latitude: v.latitude === '' ? null : v.latitude, longitude: v.longitude === '' ? null : v.longitude, rowVersion: site?.rowVersion };
    try {
      const r = site ? await put<{ id: string }>(`/sites/${site.id}`, body) : await post<{ id: string }>('/sites', body);
      notifySuccess(t('common.saved'));
      await qc.invalidateQueries({ queryKey: ['hierarchy'] });
      await qc.invalidateQueries({ queryKey: ['site'] });
      onSaved?.(r.id);
      onClose();
    } catch (e) { handleFormError(form, e); }
  });
  return (
    <Modal opened={opened} onClose={onClose} title={site ? t('locations.editSite') : t('locations.newSite')} size="lg">
      <form onSubmit={submit}>
        <Stack>
          <SimpleGrid cols={{ base: 1, sm: 3 }}>
            <TextInput label={t('common.code')} required maxLength={50} {...form.getInputProps('code')} />
            <TextInput label={`${terms.site} ${t('common.name').toLowerCase()}`} required style={{ gridColumn: 'span 2' }} {...form.getInputProps('name')} />
          </SimpleGrid>
          <TextInput label={t('locations.address')} {...form.getInputProps('address')} />
          <SimpleGrid cols={{ base: 1, sm: 3 }}>
            <TextInput label={t('locations.city')} {...form.getInputProps('city')} />
            <TextInput label={t('locations.country')} {...form.getInputProps('country')} />
            <TextInput label={t('locations.timeZone')} placeholder="Africa/Cairo" {...form.getInputProps('timeZone')} />
          </SimpleGrid>
          <SimpleGrid cols={2}>
            <NumberInput label={t('locations.latitude')} decimalScale={6} min={-90} max={90} {...form.getInputProps('latitude')} />
            <NumberInput label={t('locations.longitude')} decimalScale={6} min={-180} max={180} {...form.getInputProps('longitude')} />
          </SimpleGrid>
          <Textarea label={t('common.notes')} autosize minRows={2} {...form.getInputProps('notes')} />
          <Checkbox label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
          <Accordion variant="contained" chevronPosition="left">
            <Accordion.Item value="settings">
              <Accordion.Control><Text fw={500} fz="sm">{t('siteSettings.title', { site: terms.site.toLowerCase() })}</Text>
                <Text c="dimmed" fz="xs">{t('siteSettings.hint')}</Text></Accordion.Control>
              <Accordion.Panel>
                <Stack>
                  <SimpleGrid cols={{ base: 1, sm: 2 }}>
                    <TextInput label={t('siteSettings.woPrefix')} placeholder={me?.tenant.settings.workOrderNumberPrefix} maxLength={20} {...form.getInputProps('settings.workOrderNumberPrefix')} />
                    <TextInput label={t('siteSettings.wrPrefix')} placeholder={me?.tenant.settings.workRequestNumberPrefix} maxLength={20} {...form.getInputProps('settings.workRequestNumberPrefix')} />
                    <TextInput label={t('siteSettings.poPrefix')} placeholder={me?.tenant.settings.purchaseOrderNumberPrefix} maxLength={20} {...form.getInputProps('settings.purchaseOrderNumberPrefix')} />
                    <TextInput label={t('siteSettings.prPrefix')} placeholder={me?.tenant.settings.purchaseRequestNumberPrefix ?? 'PR-'} maxLength={20} {...form.getInputProps('settings.purchaseRequestNumberPrefix')} />
                    <TextInput label={t('siteSettings.assetPrefix', { asset: terms.asset })} placeholder={me?.tenant.settings.assetNumberPrefix} maxLength={20}
                      {...form.getInputProps('settings.assetNumberPrefix')} />
                  </SimpleGrid>
                  <Text c="dimmed" fz="xs">{t('siteSettings.prefixHint')}</Text>
                  <Select label={t('siteSettings.defaultStore')} description={site ? t('siteSettings.defaultStoreHint') : t('siteSettings.saveFirst')} clearable disabled={!site}
                    data={(stores ?? []).filter((x) => x.siteId === site?.id && (x.isActive || x.id === form.values.settings.defaultStoreId)).map((x) => ({ value: x.id, label: `${x.code} · ${x.name}` }))}
                    {...form.getInputProps('settings.defaultStoreId')} />
                  <MultiSelect label={t('siteSettings.recipients')} description={t('siteSettings.recipientsHint')} searchable clearable
                    data={(people ?? []).map((u) => ({ value: u.id, label: u.fullName }))} {...form.getInputProps('settings.requestRecipientIds')} />
                  <MultiSelect label={t('siteSettings.prRecipients')} description={t('siteSettings.prRecipientsHint')} searchable clearable
                    data={(people ?? []).map((u) => ({ value: u.id, label: u.fullName }))} {...form.getInputProps('settings.purchaseRequestRecipientIds')} />
                  <TextInput label={t('siteSettings.senderName')} description={t('siteSettings.senderNameHint')} placeholder={me?.tenant.name} maxLength={100}
                    {...form.getInputProps('settings.emailSenderName')} />
                  <div><Text fw={500} fz="sm">{t('hours.site', { site: terms.site.toLowerCase() })}</Text><Text c="dimmed" fz="xs">{t('hours.siteHint')}</Text></div>
                  <WorkingHoursEditor value={form.values.settings.workingHours} onChange={(v) => form.setFieldValue('settings.workingHours', v)}
                    inheritLabel={t('hours.useOrganization')} inherited={me ? organizationHours(me.tenant.settings) : []} error={form.errors['settings.workingHours']} />
                </Stack>
              </Accordion.Panel>
            </Accordion.Item>
          </Accordion>
          <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack>
      </form>
    </Modal>
  );
}

type LocInit = { siteId: string; parentId?: string | null };

export function LocationFormModal({ opened, onClose, location, init, onSaved }: { opened: boolean; onClose: () => void; location?: LocationDetail | null; init?: LocInit; onSaved?: (id: string) => void }) {
  const { t } = useTranslation();
  const { terms } = useAuth();
  const qc = useQueryClient();
  const { data: h } = useHierarchy();
  const form = useForm({
    initialValues: {
      siteId: '' as string | null, parentId: null as string | null, code: '', name: '', type: 'Space' as string | null, criticality: 'Medium' as string | null,
      description: '', notesToTechnician: '', latitude: '' as number | '', longitude: '' as number | '', isActive: true, costCentreId: null as string | null,
    },
  });
  useEffect(() => {
    if (!opened) return;
    form.setValues(location ? {
      siteId: location.siteId, parentId: location.parentId ?? null, code: location.code, name: location.name, type: location.type, criticality: location.criticality,
      description: location.description ?? '', notesToTechnician: location.notesToTechnician ?? '', latitude: location.latitude ?? '', longitude: location.longitude ?? '', isActive: location.isActive,
      costCentreId: location.costCentreId ?? null,
    } : { siteId: init?.siteId ?? null, parentId: init?.parentId ?? null, code: '', name: '', type: init?.parentId ? 'Room' : 'Building', criticality: 'Medium', description: '', notesToTechnician: '', latitude: '', longitude: '', isActive: true, costCentreId: null });
    form.resetDirty();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [opened, location, init?.siteId, init?.parentId]);
  const siteId = form.values.siteId;
  const parentOpts = useMemo(() => locationOptions(h?.locations ?? [], siteId, location?.id), [h, siteId, location?.id]);
  const submit = form.onSubmit(async (v) => {
    const body = { ...v, latitude: v.latitude === '' ? null : v.latitude, longitude: v.longitude === '' ? null : v.longitude, managesCostCentre: true, rowVersion: location?.rowVersion };
    try {
      const r = location ? await put<{ id: string }>(`/locations/${location.id}`, body) : await post<{ id: string }>('/locations', body);
      notifySuccess(t('common.saved'));
      await qc.invalidateQueries({ queryKey: ['hierarchy'] });
      await qc.invalidateQueries({ queryKey: ['location'] });
      onSaved?.(r.id);
      onClose();
    } catch (e) { handleFormError(form, e); }
  });
  return (
    <Modal opened={opened} onClose={onClose} title={location ? t('locations.editLocation') : t('locations.newLocation')} size="lg">
      <form onSubmit={submit}>
        <Stack>
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <Select label={terms.site} required searchable data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))}
              {...form.getInputProps('siteId')} onChange={(v) => { form.setFieldValue('siteId', v); form.setFieldValue('parentId', null); }} />
            <Select label={t('locations.parent')} placeholder={t('locations.topLevel')} clearable searchable data={parentOpts} {...form.getInputProps('parentId')}
              comboboxProps={{ width: 'max-content', position: 'bottom-start' }} />
          </SimpleGrid>
          <SimpleGrid cols={{ base: 1, sm: 3 }}>
            <TextInput label={t('common.code')} required {...form.getInputProps('code')} />
            <TextInput label={t('common.name')} required style={{ gridColumn: 'span 2' }} {...form.getInputProps('name')} />
          </SimpleGrid>
          <SimpleGrid cols={2}>
            <Select label={t('common.type')} required data={LOCATION_TYPES.map((x) => ({ value: x, label: t(`locationType.${x}`) }))} {...form.getInputProps('type')} />
            <Select label={t('criticality.label')} required data={CRITICALITIES.map((x) => ({ value: x, label: t(`criticality.${x}`) }))} {...form.getInputProps('criticality')} />
          </SimpleGrid>
          <CostCentrePicker value={form.values.costCentreId} onChange={(v) => form.setFieldValue('costCentreId', v)} siteId={siteId}
            description={t('costCentres.locationHint')} error={form.errors.costCentreId} />
          <Textarea label={t('common.description')} autosize minRows={2} {...form.getInputProps('description')} />
          <Textarea label={t('locations.notesToTechnician')} description={t('locations.notesToTechnicianHint')} autosize minRows={2} {...form.getInputProps('notesToTechnician')} />
          <SimpleGrid cols={2}>
            <NumberInput label={t('locations.latitude')} decimalScale={6} {...form.getInputProps('latitude')} />
            <NumberInput label={t('locations.longitude')} decimalScale={6} {...form.getInputProps('longitude')} />
          </SimpleGrid>
          <Checkbox label={t('common.active')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
          <Group justify="flex-end"><Button variant="default" onClick={onClose}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack>
      </form>
    </Modal>
  );
}
