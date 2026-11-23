import { useMemo, useState } from 'react';
import { ActionIcon, Badge, Button, Card, Group, Modal, Select, Stack, Switch, Table, Text, TextInput, Title } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useDisclosure } from '@mantine/hooks';
import { useForm } from '@mantine/form';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { IconEdit, IconHistory, IconPlus, IconTrash } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import type { CostCentre } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { confirmDelete } from '@/components/confirm';
import { useCostCentres } from '@/components/CostCentrePicker';
import { useHierarchy } from '@/components/LocationPicker';
import { costCentreOptions, treeRows } from '@/components/costCentres';
import { handleFormError, notifyError, notifySuccess, toIsoDate } from '@/components/utils';

const blank = { code: '', name: '', parentId: null as string | null, siteId: null as string | null, isActive: true };

/** Settings › Cost centres (0.20.0): the tree, for one site or all, and filling in past work that has none. */
export function CostCentresSettings({ editable }: { editable: boolean }) {
  const { t } = useTranslation();
  const { terms } = useAuth();
  const qc = useQueryClient();
  const { data } = useCostCentres();
  const { data: h } = useHierarchy();
  const rows = useMemo(() => treeRows(data ?? []), [data]);
  const [opened, m] = useDisclosure();
  const [fillOpened, fill] = useDisclosure();
  const [editing, setEditing] = useState<CostCentre | null>(null);
  const form = useForm({ initialValues: blank });
  // A parent can't be the cost centre itself or one under it.
  const under = useMemo(() => {
    if (!editing) return new Set<string>();
    const set = new Set<string>([editing.id]);
    let grew = true;
    while (grew) { grew = false; for (const c of data ?? []) if (c.parentId && set.has(c.parentId) && !set.has(c.id)) { set.add(c.id); grew = true; } }
    return set;
  }, [editing, data]);
  const parentOpts = useMemo(() => costCentreOptions((data ?? []).filter((c) => !under.has(c.id)), form.values.parentId), [data, under, form.values.parentId]);
  const refresh = () => qc.invalidateQueries({ queryKey: ['cost-centres'] });
  const open = (c: CostCentre | null) => {
    setEditing(c); form.clearErrors();
    form.setValues(c ? { code: c.code, name: c.name, parentId: c.parentId ?? null, siteId: c.siteId ?? null, isActive: c.isActive } : blank);
    m.open();
  };
  const submit = form.onSubmit(async (v) => {
    try {
      if (editing) await put(`/cost-centres/${editing.id}`, v); else await post('/cost-centres', v);
      notifySuccess(t('common.saved')); await refresh(); m.close();
    } catch (e) { handleFormError(form, e); }
  });
  return (
    <Card>
      <Group justify="space-between" mb="xs" wrap="wrap">
        <div><Title order={5}>{t('costCentres.title')}</Title><Text fz="sm" c="dimmed">{t('costCentres.hint')}</Text></div>
        {editable && <Group gap="xs">
          <Button size="xs" variant="default" leftSection={<IconHistory size={14} />} onClick={fill.open} disabled={rows.length === 0}>{t('costCentres.fillIn')}</Button>
          <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('costCentres.new')}</Button>
        </Group>}
      </Group>
      <Table.ScrollContainer minWidth={520}>
        <Table striped>
          <Table.Thead><Table.Tr><Table.Th>{t('common.code')}</Table.Th><Table.Th>{t('common.name')}</Table.Th><Table.Th>{terms.site}</Table.Th><Table.Th /></Table.Tr></Table.Thead>
          <Table.Tbody>
            {rows.length === 0 && <Table.Tr><Table.Td colSpan={4}><Text fz="sm" c="dimmed">{t('costCentres.none')}</Text></Table.Td></Table.Tr>}
            {rows.map((c) => (
              <Table.Tr key={c.id}>
                <Table.Td><Text fz="sm" fw={600} style={{ paddingInlineStart: c.depth * 20 }}>{c.code}</Text></Table.Td>
                <Table.Td><Group gap={6}><Text fz="sm">{c.name}</Text>{!c.isActive && <Badge size="xs" color="gray">{t('common.inactive')}</Badge>}</Group></Table.Td>
                <Table.Td><Text fz="sm" c={c.siteName ? undefined : 'dimmed'}>{c.siteName ?? t('costCentres.allSites')}</Text></Table.Td>
                <Table.Td>{editable && <Group gap={4} justify="flex-end" wrap="nowrap">
                  <ActionIcon variant="subtle" aria-label={t('common.edit')} onClick={() => open(c)}><IconEdit size={16} /></ActionIcon>
                  <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => confirmDelete(c.name, async () => {
                    try { await del(`/cost-centres/${c.id}`); await refresh(); } catch (e) { notifyError(e); } })}><IconTrash size={16} /></ActionIcon>
                </Group>}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      <Modal opened={opened} onClose={m.close} title={editing ? t('costCentres.edit') : t('costCentres.new')}>
        <form onSubmit={submit}><Stack>
          <TextInput label={t('common.code')} required {...form.getInputProps('code')} />
          <TextInput label={t('common.name')} required {...form.getInputProps('name')} />
          <Select label={t('costCentres.parent')} placeholder={t('costCentres.topLevel')} clearable searchable data={parentOpts} {...form.getInputProps('parentId')}
            comboboxProps={{ width: 'max-content', position: 'bottom-start' }} nothingFoundMessage={t('common.noRecords')} />
          <Select label={terms.site} placeholder={t('costCentres.allSites')} description={t('costCentres.siteHint')} clearable
            data={(h?.sites ?? []).map((s) => ({ value: s.id, label: s.name }))} {...form.getInputProps('siteId')} />
          <Switch label={t('common.active')} description={t('costCentres.inactiveHint')} {...form.getInputProps('isActive', { type: 'checkbox' })} />
          <Group justify="flex-end"><Button variant="default" onClick={m.close}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack></form>
      </Modal>
      <FillInModal opened={fillOpened} onClose={fill.close} />
    </Card>
  );
}

type FillIn = { workOrders: number; issues: number };

/** Fill in past work: shows how many work orders and direct issues it will change first; never overwrites; can run again. */
function FillInModal({ opened, onClose }: { opened: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [from, setFrom] = useState<string | null>(dayjs().startOf('year').format('YYYY-MM-DD'));
  const [count, setCount] = useState<FillIn | null>(null);
  const [busy, setBusy] = useState(false);
  const check = async () => {
    if (!from) return;
    setBusy(true);
    try { setCount(await get<FillIn>('/cost-centres/fill-in', { from: toIsoDate(from) })); } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  const apply = async () => {
    if (!from) return;
    setBusy(true);
    try {
      const r = await post<FillIn>('/cost-centres/fill-in', { from: toIsoDate(from) });
      notifySuccess(t('costCentres.filled', { workOrders: r.workOrders, issues: r.issues }));
      await qc.invalidateQueries({ queryKey: ['work-orders'] }); await qc.invalidateQueries({ queryKey: ['stock-tx'] });
      setCount(null); onClose();
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  const close = () => { setCount(null); onClose(); };
  return (
    <Modal opened={opened} onClose={close} title={t('costCentres.fillIn')}>
      <Stack>
        <Text fz="sm" c="dimmed">{t('costCentres.fillInHint')}</Text>
        <DateInput label={t('costCentres.fillInFrom')} valueFormat="DD MMM YYYY" maxDate={new Date()} value={from} onChange={(v) => { setFrom(v); setCount(null); }} />
        {count && <Text fz="sm" fw={500}>{t('costCentres.fillInCount', { workOrders: count.workOrders, issues: count.issues })}</Text>}
        <Group justify="flex-end">
          <Button variant="default" onClick={close}>{t('common.cancel')}</Button>
          {count
            ? <Button loading={busy} disabled={count.workOrders + count.issues === 0} onClick={apply}>{t('costCentres.fillInApply')}</Button>
            : <Button loading={busy} disabled={!from} onClick={check}>{t('costCentres.fillInCheck')}</Button>}
        </Group>
      </Stack>
    </Modal>
  );
}
