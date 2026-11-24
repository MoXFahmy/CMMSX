import { useState } from 'react';
import { ActionIcon, Box, Button, Card, Checkbox, Grid, Group, Modal, ScrollArea, SegmentedControl, Select, SimpleGrid, Stack, Switch, Text, TextInput, Textarea, Title } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { IconArrowDown, IconArrowUp } from '@tabler/icons-react';
import { get, post, put } from '@/api/client';
import type { CustomField, PoDetail, WorkOrderDetail, WorkRequestDetail } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { useHierarchy } from '@/components/LocationPicker';
import { notifyError, notifySuccess } from '@/components/utils';
import { sheetFor } from '@/pages/labels/sheet';
import { moveBlock, toggleBlock, toggleField, toggleLabelField } from '@/print/editor';
import { JobCardBody } from '@/print/JobCardBody';
import { ASSET_FIELDS_BLOCK, blocksInOrder, FIXED_FIELDS, LABEL_FIELDS, LABEL_SIZES, PAPER_BLOCKS, PAPER_SIZES, type Paper, type PrintLayout } from '@/print/layouts';
import { PoBody } from '@/print/PoBody';
import { RequestBody } from '@/print/RequestBody';

const WORK_TYPES = ['Corrective', 'Preventive', 'Breakdown', 'Inspection', 'Predictive', 'Project', 'Safety', 'Other'];
const RECORDS: Record<Exclude<Paper, 'labels'>, { list: string; sort: string; detail: (id: string) => string }> = {
  'job-card': { list: '/work-orders', sort: 'createdAt', detail: (id) => `/work-orders/${id}` },
  'purchase-order': { list: '/purchase-orders', sort: 'orderDate', detail: (id) => `/purchase-orders/${id}` },
  request: { list: '/work-requests', sort: 'createdAt', detail: (id) => `/work-requests/${id}` },
};

/** The preview: the paper's real print, with the layout being edited, for the latest record or one chosen by number. */
function Preview({ layout }: { layout: PrintLayout }) {
  const { t, i18n } = useTranslation();
  const { me, terms } = useAuth();
  const [search, setSearch] = useState('');
  const paper = layout.paper;
  const rec = paper === 'labels' ? null : RECORDS[paper];
  const { data: list } = useQuery({
    queryKey: ['print-preview-list', paper, search],
    queryFn: () => get<{ items: { id: string }[] }>(rec!.list, { search: search || undefined, sort: rec!.sort, dir: 'desc', page: 1, pageSize: 1 }), enabled: !!rec,
  });
  const recordId = list?.items[0]?.id;
  const { data: record } = useQuery({ queryKey: ['print-preview', paper, recordId], queryFn: () => get<unknown>(rec!.detail(recordId!)), enabled: !!rec && !!recordId });
  const ctx = { orgName: me?.tenant.name ?? '', terms, logoUrl: me?.tenant.settings.logoVersion ? `/api/settings/logo?v=${me.tenant.settings.logoVersion}` : null, language: i18n.language };

  if (paper === 'labels') {
    const s = sheetFor(layout.labelSize);
    const scale = Math.min(1, 300 / (s.labelWidthMm * 3.78));
    return (
      <Stack gap="xs">
        <Text fz="xs" c="dimmed">{t(`print.sizes.${layout.labelSize ?? 'a4-3x8'}`)}</Text>
        <div style={{ width: `${s.labelWidthMm * scale}mm`, height: `${s.labelHeightMm * scale}mm`, border: '1px dashed var(--mantine-color-gray-5)', display: 'flex', gap: '2mm', padding: '2mm', alignItems: 'center', boxSizing: 'border-box' }}>
          <div style={{ width: `${s.qrMm * scale}mm`, height: `${s.qrMm * scale}mm`, background: 'repeating-linear-gradient(45deg,#000 0 2px,#fff 2px 4px)', flex: 'none' }} />
          <div style={{ minWidth: 0, fontSize: 11, lineHeight: 1.2 }}>
            {layout.labelFields.includes('number') && <b>P-0001</b>}
            {layout.labelFields.includes('name') && <div>{t('print.sampleName')}</div>}
            {layout.labelFields.includes('detail') && <div style={{ color: '#555', fontSize: 10 }}>{t('print.sampleDetail')}</div>}
            {layout.labelFields.includes('site') && <div style={{ color: '#555', fontSize: 10 }}>{t('print.sampleSite')}</div>}
          </div>
        </div>
      </Stack>
    );
  }
  return (
    <Stack gap="xs">
      <TextInput size="xs" placeholder={t('print.previewNumber')} value={search} onChange={(e) => setSearch(e.currentTarget.value)} />
      <ScrollArea.Autosize mah={620}>
        <Box p="sm" style={{ border: '1px solid var(--mantine-color-default-border)', background: '#fff', color: '#000', fontSize: layout.paperSize === 'A5' ? 12 : undefined }}>
          {!record ? <Text c="dimmed" fz="sm">{list && !recordId ? t('print.noRecord') : t('common.loading')}</Text>
            : paper === 'job-card' ? <JobCardBody w={record as WorkOrderDetail} layout={layout} {...ctx} />
            : paper === 'purchase-order' ? <PoBody o={record as PoDetail} layout={layout} {...ctx} />
            : <RequestBody r={record as WorkRequestDetail} layout={layout} {...ctx} />}
        </Box>
      </ScrollArea.Autosize>
    </Stack>
  );
}

/** The layout window: what prints and where, with the preview beside it (under it on narrow screens). */
export function PrintLayoutEditor({ initial, onClose }: { initial: PrintLayout; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [l, setL] = useState<PrintLayout>(initial);
  const [saving, setSaving] = useState(false);
  const set = (patch: Partial<PrintLayout>) => setL((x) => ({ ...x, ...patch }));
  const { data: h } = useHierarchy();
  const paper = l.paper;
  const { data: orderTypes } = useQuery({ queryKey: ['order-types'], queryFn: () => get<{ id: string; name: string }[]>('/order-types'), enabled: paper === 'purchase-order' });
  const { data: requestTypes } = useQuery({ queryKey: ['request-types'], queryFn: () => get<{ id: string; name: string }[]>('/request-types'), enabled: paper === 'request' });
  const { data: assetFields } = useQuery({ queryKey: ['custom-fields', 'Asset'], queryFn: () => get<CustomField[]>('/custom-fields', { entityType: 'Asset' }), enabled: paper === 'job-card' });
  const kinds = paper === 'job-card' ? WORK_TYPES.map((k) => ({ value: k, label: t(`workType.${k}`) }))
    : paper === 'purchase-order' ? (orderTypes ?? []).map((x) => ({ value: x.id, label: x.name }))
    : paper === 'request' ? (requestTypes ?? []).map((x) => ({ value: x.id, label: x.name }))
    : (['asset', 'location', 'part'] as const).map((k) => ({ value: k, label: t(`print.labelKinds.${k}`) }));
  const fixed = FIXED_FIELDS[paper];

  const save = async () => {
    setSaving(true);
    const body = {
      paper, name: l.name, isDefault: l.isDefault, siteId: l.isDefault ? null : l.siteId ?? null, kindKey: l.isDefault ? null : l.kindKey ?? null,
      paperSize: paper === 'labels' ? null : l.paperSize ?? 'A4', showLogo: l.showLogo, headerText: l.headerText, headerTextAr: l.headerTextAr,
      footerText: l.footerText, footerTextAr: l.footerTextAr, terms: l.terms, termsAr: l.termsAr,
      blocks: paper === 'labels' ? [] : blocksInOrder(l, true), labelSize: paper === 'labels' ? l.labelSize ?? 'a4-3x8' : null, labelFields: paper === 'labels' ? l.labelFields : [],
    };
    try {
      if (l.id) await put(`/print-layouts/${l.id}`, body); else await post('/print-layouts', body);
      notifySuccess(t('common.saved'));
      await qc.invalidateQueries({ queryKey: ['print-layouts'] });
      onClose();
    } catch (e) { notifyError(e); } finally { setSaving(false); }
  };

  const text = (key: 'headerText' | 'footerText' | 'terms', label: string, rows: number, max: number) => {
    const arKey = (key === 'terms' ? 'termsAr' : key === 'headerText' ? 'headerTextAr' : 'footerTextAr') as 'termsAr' | 'headerTextAr' | 'footerTextAr';
    return (
      <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="xs">
        <Textarea label={label} autosize minRows={rows} maxLength={max} value={l[key] ?? ''} onChange={(e) => set({ [key]: e.currentTarget.value })} />
        <Textarea label={`${label} (${t('print.arabic')})`} dir="rtl" autosize minRows={rows} maxLength={max} value={l[arKey] ?? ''} onChange={(e) => set({ [arKey]: e.currentTarget.value })} />
      </SimpleGrid>
    );
  };

  return (
    <Modal opened onClose={onClose} size="90rem" title={l.id ? t('print.editLayout') : t('print.newLayout')}>
      <Grid gutter="lg">
        <Grid.Col span={{ base: 12, lg: 6 }}>
          <Stack gap="sm">
            <TextInput label={t('common.name')} required maxLength={60} value={l.name} onChange={(e) => set({ name: e.currentTarget.value })} />
            <Checkbox label={t('print.isDefault')} description={t('print.isDefaultHint')} checked={l.isDefault} onChange={(e) => set({ isDefault: e.currentTarget.checked })} />
            <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="xs">
              <Select label={t('print.site')} placeholder={t('print.anySite')} disabled={l.isDefault} clearable searchable data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))}
                value={l.siteId ?? null} onChange={(v) => set({ siteId: v })} />
              <Select label={t(`print.kindLabel.${paper}`)} placeholder={t('print.anyKind')} disabled={l.isDefault} clearable searchable data={kinds}
                value={l.kindKey ?? null} onChange={(v) => set({ kindKey: v })} />
            </SimpleGrid>
            <Text fz="xs" c="dimmed">{t('print.choiceHint')}</Text>
            {paper === 'labels' ? (
              <>
                <Select label={t('print.labelSize')} data={LABEL_SIZES.map((s) => ({ value: s, label: t(`print.sizes.${s}`) }))} value={l.labelSize ?? 'a4-3x8'}
                  onChange={(v) => v && set({ labelSize: v as PrintLayout['labelSize'] })} allowDeselect={false} />
                <Text fz="sm" fw={500}>{t('print.labelFields')}</Text>
                <Group gap="md">{LABEL_FIELDS.map((f) => <Checkbox key={f} label={t(`print.fields.${f}`)} checked={l.labelFields.includes(f)} onChange={() => setL(toggleLabelField(l, f))} />)}</Group>
                <Text fz="xs" c="dimmed">{t('print.qrAlways')}</Text>
              </>
            ) : (
              <>
                <Group gap="lg">
                  <SegmentedControl data={PAPER_SIZES.map((s) => ({ value: s, label: s }))} value={l.paperSize ?? 'A4'} onChange={(v) => set({ paperSize: v as PrintLayout['paperSize'] })} />
                  <Switch label={t('print.showLogo')} checked={l.showLogo} onChange={(e) => set({ showLogo: e.currentTarget.checked })} />
                </Group>
                {text('headerText', t('print.headerText'), 1, 500)}
                {text('footerText', t('print.footerText'), 1, 500)}
                {text('terms', t('print.terms'), 2, 4000)}
                <Title order={6} mt="xs">{t('print.blocksTitle')}</Title>
                <Stack gap={6}>
                  {blocksInOrder(l, true).map((b, i, all) => {
                    const catalogue = PAPER_BLOCKS[paper].find((x) => x.key === b.key);
                    const carriesFixed = fixed.some((f) => f.startsWith(`${b.key}.`));
                    const fields = b.key === ASSET_FIELDS_BLOCK ? (assetFields ?? []).map((f) => ({ key: f.key, label: f.label })) : (catalogue?.fields ?? []).map((f) => ({ key: f, label: t(`print.fields.${f}`) }));
                    return (
                      <Card key={b.key} withBorder p="xs">
                        <Group justify="space-between" wrap="nowrap">
                          <Checkbox label={<Text fw={600} fz="sm">{t(`print.blocks.${b.key}`)}</Text>} checked={b.shown} disabled={carriesFixed} onChange={() => setL(toggleBlock(l, b.key))} />
                          {b.key !== 'header' && (
                            <Group gap={2} wrap="nowrap">
                              <ActionIcon variant="subtle" disabled={i <= 1} onClick={() => setL(moveBlock(l, b.key, -1))} aria-label={t('print.moveUp')}><IconArrowUp size={14} /></ActionIcon>
                              <ActionIcon variant="subtle" disabled={i === all.length - 1} onClick={() => setL(moveBlock(l, b.key, 1))} aria-label={t('print.moveDown')}><IconArrowDown size={14} /></ActionIcon>
                            </Group>
                          )}
                        </Group>
                        {b.shown && fields.length > 0 && (
                          <Group gap="sm" mt={6} ml="lg">
                            {fields.map((f) => (
                              <Checkbox key={f.key} size="xs" label={f.label} checked={b.fields.includes(f.key) || fixed.includes(`${b.key}.${f.key}`)} disabled={fixed.includes(`${b.key}.${f.key}`)}
                                onChange={() => setL(toggleField(l, b.key, f.key))} />
                            ))}
                          </Group>
                        )}
                        {b.shown && b.key === ASSET_FIELDS_BLOCK && fields.length === 0 && <Text fz="xs" c="dimmed" ml="lg">{t('print.noAssetFields')}</Text>}
                      </Card>
                    );
                  })}
                </Stack>
              </>
            )}
            <Group justify="flex-end" mt="sm">
              <Button variant="default" onClick={onClose}>{t('common.cancel')}</Button>
              <Button onClick={() => void save()} loading={saving} disabled={!l.name.trim()}>{t('common.save')}</Button>
            </Group>
          </Stack>
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 6 }}>
          <Text fz="sm" fw={600} mb={6}>{t('print.preview')}</Text>
          <Preview layout={l} />
        </Grid.Col>
      </Grid>
    </Modal>
  );
}
