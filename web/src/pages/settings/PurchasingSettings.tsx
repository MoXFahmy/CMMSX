import { useEffect, useState } from 'react';
import { ActionIcon, Alert, Badge, Button, Card, Group, Modal, MultiSelect, NumberInput, SegmentedControl, Select, Stack, Table, Text, TextInput, Title, Tooltip } from '@mantine/core';
import { useForm } from '@mantine/form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { IconAlertTriangle, IconEdit, IconPlus, IconTrash } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import { useAuth } from '@/auth/AuthContext';
import { LookupTable } from '@/components/LookupTable';
import { useHierarchy } from '@/components/LocationPicker';
import { confirmDelete } from '@/components/confirm';
import { fmtPrice, handleFormError, notifyError, notifySuccess } from '@/components/utils';
import { exampleSentence, levelsFor, type LevelRow } from '@/pages/purchasing/approvals';

type OrderType = { id: string; code: string; name: string; isActive: boolean };
type LevelValues = { siteId: string | null; orderTypeId: string | null; name: string; minAmount: number | ''; rule: 'Any' | 'All'; approverIds: string[] };

/** Settings › Purchasing (0.16.0): order types, approval levels and who hears about new purchase requests. */
export function PurchasingSettings() {
  const { t } = useTranslation();
  const { me, terms } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const qc = useQueryClient();
  const { data: h } = useHierarchy();
  const { data: levels } = useQuery({ queryKey: ['approval-levels'], queryFn: () => get<LevelRow[]>('/approval-levels') });
  const { data: types } = useQuery({ queryKey: ['order-types'], queryFn: () => get<OrderType[]>('/order-types') });
  const { data: people } = useQuery({ queryKey: ['users-lookup'], queryFn: () => get<{ id: string; fullName: string }[]>('/users/lookup'), retry: false });
  const [editing, setEditing] = useState<LevelRow | 'new' | null>(null);
  const [example, setExample] = useState<{ amount: number | ''; siteId: string | null; orderTypeId: string | null }>({ amount: 50000, siteId: null, orderTypeId: null });
  const form = useForm<LevelValues>({ initialValues: { siteId: null, orderTypeId: null, name: '', minAmount: 0, rule: 'Any', approverIds: [] } });
  useEffect(() => {
    if (!editing) return;
    form.clearErrors();
    form.setValues(editing === 'new' ? { siteId: null, orderTypeId: null, name: '', minAmount: 0, rule: 'Any', approverIds: [] }
      : { siteId: editing.siteId ?? null, orderTypeId: editing.orderTypeId ?? null, name: editing.name, minAmount: editing.minAmount, rule: editing.rule, approverIds: editing.approvers.map((a) => a.id) });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editing]);
  const save = form.onSubmit(async (v) => {
    const body = { ...v, minAmount: v.minAmount === '' ? 0 : v.minAmount };
    try {
      if (editing === 'new') await post('/approval-levels', body); else if (editing) await put(`/approval-levels/${editing.id}`, body);
      notifySuccess(t('common.saved'));
      setEditing(null);
      await qc.invalidateQueries({ queryKey: ['approval-levels'] });
    } catch (e) { handleFormError(form, e); }
  });
  const fmt = (n: number) => fmtPrice(n, currency);
  const rows = [...(levels ?? [])].sort((a, b) => (a.siteName ?? '').localeCompare(b.siteName ?? '') || (a.orderTypeName ?? '').localeCompare(b.orderTypeName ?? '') || a.minAmount - b.minAmount);
  const amount = example.amount === '' ? 0 : example.amount;
  const chosen = levelsFor(levels ?? [], example.siteId, example.orderTypeId, Number.MAX_SAFE_INTEGER);

  return (
    <Stack>
      <Card p={0}>
        <Group justify="space-between" p="md" wrap="wrap">
          <div><Title order={5}>{t('approvals.levels')}</Title><Text fz="sm" c="dimmed" maw={720}>{t('approvals.levelsHint')}</Text></div>
          <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => setEditing('new')}>{t('approvals.newLevel')}</Button>
        </Group>
        <Table.ScrollContainer minWidth={820}>
          <Table verticalSpacing="sm">
            <Table.Thead><Table.Tr>
              <Table.Th>{t('approvals.set')}</Table.Th><Table.Th>{t('approvals.orderType')}</Table.Th><Table.Th>{t('approvals.name')}</Table.Th>
              <Table.Th ta="end">{t('approvals.minAmount')}</Table.Th><Table.Th>{t('approvals.rule')}</Table.Th><Table.Th>{t('approvals.approvers')}</Table.Th><Table.Th w={80} />
            </Table.Tr></Table.Thead>
            <Table.Tbody>
              {rows.length === 0 && <Table.Tr><Table.Td colSpan={7}><Text c="dimmed" ta="center" fz="sm" py="md">{t('approvals.none')}</Text></Table.Td></Table.Tr>}
              {rows.map((l) => (
                <Table.Tr key={l.id}>
                  <Table.Td>{l.siteName ?? t('approvals.organization')}</Table.Td>
                  <Table.Td>{l.orderTypeName ?? t('approvals.allTypes')}</Table.Td>
                  <Table.Td fw={600}>{l.name}</Table.Td>
                  <Table.Td ta="end">{fmt(l.minAmount)}</Table.Td>
                  <Table.Td>{t(`approvals.rule${l.rule}`)}</Table.Td>
                  <Table.Td><Group gap={4}>{l.approvers.map((a) => a.ok
                    ? <Badge key={a.id} variant="light" radius="sm" style={{ textTransform: 'none' }}>{a.name}</Badge>
                    : <Tooltip key={a.id} label={t(`approvals.problem.${a.problem}`)}><Badge color="orange" variant="light" radius="sm" leftSection={<IconAlertTriangle size={11} />}
                      style={{ textTransform: 'none' }}>{a.name}</Badge></Tooltip>)}</Group></Table.Td>
                  <Table.Td><Group gap={4} wrap="nowrap">
                    <ActionIcon variant="subtle" aria-label={t('common.edit')} onClick={() => setEditing(l)}><IconEdit size={15} /></ActionIcon>
                    <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => confirmDelete(l.name, async () => {
                      try { await del(`/approval-levels/${l.id}`); await qc.invalidateQueries({ queryKey: ['approval-levels'] }); } catch (e) { notifyError(e); } })}><IconTrash size={15} /></ActionIcon>
                  </Group></Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
        <Group p="md" gap="xs" align="flex-end" wrap="wrap" style={{ borderTop: '1px solid var(--mantine-color-default-border)' }}>
          <NumberInput label={t('approvals.tryAmount')} w={160} min={0} thousandSeparator value={example.amount} onChange={(v) => setExample((x) => ({ ...x, amount: v === '' ? '' : Number(v) }))} />
          <Select label={terms.site} w={180} clearable placeholder={t('approvals.anySite')} data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))}
            value={example.siteId} onChange={(v) => setExample((x) => ({ ...x, siteId: v }))} />
          <Select label={t('approvals.orderType')} w={160} clearable placeholder={t('approvals.allTypes')} data={(types ?? []).filter((x) => x.isActive).map((x) => ({ value: x.id, label: x.name }))}
            value={example.orderTypeId} onChange={(v) => setExample((x) => ({ ...x, orderTypeId: v }))} />
          <Text fz="sm" pb={8} style={{ flex: '1 1 300px' }}>{exampleSentence(t, chosen, amount, fmt)}</Text>
        </Group>
      </Card>
      <PrRecipients people={people ?? []} />
      <LookupTable path="/order-types" title={t('approvals.orderTypes')} newLabel={t('approvals.newOrderType')} editable />
      <Modal opened={!!editing} onClose={() => setEditing(null)} title={editing === 'new' ? t('approvals.newLevel') : t('approvals.editLevel')}>
        <form onSubmit={save}>
          <Stack>
            <Select label={t('approvals.set')} data={[{ value: '', label: t('approvals.organization') }, ...(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))]}
              value={form.values.siteId ?? ''} onChange={(v) => form.setFieldValue('siteId', v || null)} description={t('approvals.setHint')} />
            <Select label={t('approvals.orderType')} data={[{ value: '', label: t('approvals.allTypes') }, ...(types ?? []).filter((x) => x.isActive).map((x) => ({ value: x.id, label: x.name }))]}
              value={form.values.orderTypeId ?? ''} onChange={(v) => form.setFieldValue('orderTypeId', v || null)} error={form.errors.orderTypeId} />
            <TextInput label={t('approvals.name')} placeholder={t('approvals.namePlaceholder')} required {...form.getInputProps('name')} />
            <NumberInput label={t('approvals.minAmount')} description={t('approvals.minAmountHint', { currency })} min={0} thousandSeparator decimalScale={2} {...form.getInputProps('minAmount')} />
            <div><Text fz="sm" fw={500} mb={4}>{t('approvals.rule')}</Text>
              <SegmentedControl data={[{ value: 'Any', label: t('approvals.ruleAny') }, { value: 'All', label: t('approvals.ruleAll') }]} {...form.getInputProps('rule')} /></div>
            <MultiSelect label={t('approvals.approvers')} description={t('approvals.approversHint')} searchable maxValues={5}
              data={(people ?? []).map((p) => ({ value: p.id, label: p.fullName }))} {...form.getInputProps('approverIds')} />
            {Object.keys(form.errors).length > 0 && !form.errors.name && <Alert color="red" p="xs">{Object.values(form.errors).join(' ')}</Alert>}
            <Group justify="flex-end"><Button variant="default" onClick={() => setEditing(null)}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
          </Stack>
        </form>
      </Modal>
    </Stack>
  );
}

/** Who hears about new purchase requests when a site names nobody (organization setting). */
function PrRecipients({ people }: { people: { id: string; fullName: string }[] }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data } = useQuery({ queryKey: ['settings'], queryFn: () => get<{ name: string; settings: Record<string, unknown> }>('/settings') });
  const [ids, setIds] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  useEffect(() => { if (data) setIds((data.settings.purchaseRequestRecipientIds as string[] | undefined) ?? []); }, [data]);
  const saveIt = async () => {
    if (!data) return;
    setBusy(true);
    try {
      await put('/settings', { name: data.name, settings: { ...data.settings, purchaseRequestRecipientIds: ids } });
      notifySuccess(t('common.saved'));
      await qc.invalidateQueries({ queryKey: ['settings'] });
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Card>
      <Title order={5}>{t('approvals.prRecipients')}</Title>
      <Text fz="sm" c="dimmed" mb="sm">{t('approvals.prRecipientsHint')}</Text>
      <Group align="flex-end">
        <MultiSelect style={{ flex: 1 }} searchable clearable data={people.map((p) => ({ value: p.id, label: p.fullName }))} value={ids} onChange={setIds} />
        <Button loading={busy} onClick={saveIt}>{t('common.save')}</Button>
      </Group>
    </Card>
  );
}
