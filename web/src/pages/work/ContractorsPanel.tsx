import { useState } from 'react';
import { ActionIcon, Button, Card, Group, Modal, NumberInput, Select, SimpleGrid, Stack, Table, Text, TextInput, Textarea, Title } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useForm } from '@mantine/form';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { IconEdit, IconPlus, IconTrash } from '@tabler/icons-react';
import { del, get, post, put } from '@/api/client';
import type { ContractorCategory, ContractorLine, SupplierLookup, WorkOrderDetail } from '@/api/types';
import { confirmDelete } from '@/components/confirm';
import { fmtDate, fmtMoney, handleFormError, hoursText, notifyError, toIsoDate } from '@/components/utils';
import { lineRate } from '@/pages/purchasing/companies';

type Values = {
  supplierId: string | null; categoryId: string | null; contactId: string | null; personName: string; plannedHours: number | ''; hoursDone: number | '';
  doneOn: Date | string | null; rate: number | ''; notes: string; plannedOn: Date | string | null;
};
const blank: Values = { supplierId: null, categoryId: null, contactId: null, personName: '', plannedHours: '', hoursDone: '', doneOn: null, rate: '', notes: '', plannedOn: null };

/**
 * Contractor lines on a work order (0.17.0). Planners (edit) add, change and remove lines and planned hours; technicians (execute) record the
 * hours done, the date and the person on existing lines. The rate is copied onto the line, so later rate changes never alter it.
 */
export function ContractorsPanel({ w, canPlan, canExec, onChange, currency }: { w: WorkOrderDetail; canPlan: boolean; canExec: boolean; onChange: () => Promise<void>; currency: string }) {
  const { t } = useTranslation();
  const { data: companies } = useQuery({ queryKey: ['suppliers', 'lookup', 'contractor'], queryFn: () => get<SupplierLookup[]>('/suppliers/lookup', { role: 'contractor' }) });
  const { data: categories } = useQuery({ queryKey: ['contractor-categories'], queryFn: () => get<ContractorCategory[]>('/contractor-categories') });
  const [editing, setEditing] = useState<ContractorLine | null>(null);
  const [opened, setOpened] = useState(false);
  const form = useForm<Values>({ initialValues: blank });
  const planOnly = !canPlan; // a technician: only what was done
  const open = (l: ContractorLine | null) => {
    setEditing(l); form.clearErrors();
    form.setValues(l ? {
      supplierId: l.supplierId, categoryId: l.categoryId ?? null, contactId: l.contactId ?? null, personName: l.contactId ? '' : l.personName ?? '', plannedHours: l.plannedHours,
      hoursDone: l.hoursDone || '', doneOn: l.doneOn ?? null, rate: l.rate, notes: l.notes ?? '', plannedOn: l.plannedOn ?? null,
    } : blank);
    setOpened(true);
  };
  const company = (companies ?? []).find((c) => c.id === form.values.supplierId);
  // A new line (or a new company or category) starts from the company's rate, else the category's default; the planner may change it.
  const prefill = (supplierId: string | null, categoryId: string | null) => {
    const c = (companies ?? []).find((x) => x.id === supplierId);
    form.setFieldValue('rate', lineRate(c, categories ?? [], categoryId));
  };
  const submit = form.onSubmit(async (v) => {
    const num = (x: number | '') => (x === '' ? 0 : x);
    const body = {
      supplierId: v.supplierId, categoryId: v.categoryId, contactId: v.contactId, personName: v.contactId ? null : v.personName || null,
      plannedHours: num(v.plannedHours), hoursDone: num(v.hoursDone), doneOn: toIsoDate(v.doneOn), rate: v.rate === '' ? null : v.rate, notes: v.notes || null,
      plannedOn: toIsoDate(v.plannedOn),
    };
    try {
      if (editing) await put(`/work-orders/${w.id}/contractors/${editing.id}`, body); else await post(`/work-orders/${w.id}/contractors`, body);
      setOpened(false); await onChange();
    } catch (e) { handleFormError(form, e); }
  });
  // A company or category no longer offered still shows on the line that has it.
  const companyOptions = [...(companies ?? []).map((c) => ({ value: c.id, label: c.name })),
    ...(editing && !(companies ?? []).some((c) => c.id === editing.supplierId) ? [{ value: editing.supplierId, label: editing.companyName }] : [])];
  const categoryOptions = (categories ?? []).filter((c) => c.isActive || c.id === editing?.categoryId).map((c) => ({ value: c.id, label: c.name }));
  const contactOptions = [...(company?.contacts ?? []).map((c) => ({ value: c.id, label: c.role ? `${c.name} · ${c.role}` : c.name })),
    ...(editing?.contactId && !(company?.contacts ?? []).some((c) => c.id === editing.contactId) ? [{ value: editing.contactId, label: editing.personName ?? '—' }] : [])];
  const lines = w.contractors ?? [];
  return (
    <Card p={0}>
      <Group justify="space-between" p="md">
        <Title order={5}>{t('contractors.title')} · {hoursText(w.totals.contractorHours)} · {fmtMoney(lines.reduce((a, l) => a + l.cost, 0), currency)}</Title>
        {canPlan && <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => open(null)}>{t('contractors.add')}</Button>}
      </Group>
      <Table.ScrollContainer minWidth={760}>
        <Table verticalSpacing="sm" highlightOnHover>
          <Table.Thead><Table.Tr>
            <Table.Th>{t('contracts.company')}</Table.Th><Table.Th>{t('companies.category')}</Table.Th><Table.Th>{t('contractors.person')}</Table.Th>
            <Table.Th ta="right">{t('contractors.planned')}</Table.Th><Table.Th ta="right">{t('contractors.done')}</Table.Th><Table.Th>{t('wo.date')}</Table.Th>
            <Table.Th ta="right">{t('contractors.rate')}</Table.Th><Table.Th ta="right">{t('wo.cost')}</Table.Th><Table.Th />
          </Table.Tr></Table.Thead>
          <Table.Tbody>
            {lines.length === 0 && <Table.Tr><Table.Td colSpan={9}><Text c="dimmed" fz="sm" ta="center">{t('contractors.none')}</Text></Table.Td></Table.Tr>}
            {lines.map((l) => (
              <Table.Tr key={l.id}>
                <Table.Td>{l.companyName}</Table.Td><Table.Td>{l.categoryName ?? '—'}</Table.Td><Table.Td>{l.personName ?? '—'}</Table.Td>
                <Table.Td ta="right">{l.plannedHours || '—'}</Table.Td><Table.Td ta="right">{l.hoursDone || '—'}</Table.Td><Table.Td>{fmtDate(l.doneOn)}</Table.Td>
                <Table.Td ta="right">{fmtMoney(l.rate, currency)}</Table.Td><Table.Td ta="right">{fmtMoney(l.cost, currency)}</Table.Td>
                <Table.Td><Group gap={2} wrap="nowrap" justify="flex-end">
                  {(canPlan || canExec) && <ActionIcon variant="subtle" size="sm" aria-label={t('common.edit')} onClick={() => open(l)}><IconEdit size={14} /></ActionIcon>}
                  {canPlan && <ActionIcon variant="subtle" color="gray" size="sm" aria-label={t('common.delete')}
                    onClick={() => confirmDelete(`${l.companyName} · ${l.categoryName ?? ''}`, async () => { try { await del(`/work-orders/${w.id}/contractors/${l.id}`); await onChange(); } catch (e) { notifyError(e); } })}><IconTrash size={14} /></ActionIcon>}
                </Group></Table.Td>
              </Table.Tr>
            ))}
            {lines.length > 0 && (
              <Table.Tr fw={700}>
                <Table.Td colSpan={3}>{t('plan.total')}</Table.Td>
                <Table.Td ta="right">{lines.reduce((a, l) => a + l.plannedHours, 0)}</Table.Td><Table.Td ta="right">{lines.reduce((a, l) => a + l.hoursDone, 0)}</Table.Td><Table.Td />
                <Table.Td /><Table.Td ta="right">{fmtMoney(lines.reduce((a, l) => a + l.cost, 0), currency)}</Table.Td><Table.Td />
              </Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      <Modal opened={opened} onClose={() => setOpened(false)} title={editing ? t('contractors.edit') : t('contractors.add')} size="lg">
        <form onSubmit={submit}><Stack>
          {planOnly && <Text fz="sm" c="dimmed">{t('contractors.techHint')}</Text>}
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <Select label={t('contracts.company')} required searchable disabled={planOnly} data={companyOptions} {...form.getInputProps('supplierId')}
              onChange={(v) => { form.setFieldValue('supplierId', v); form.setFieldValue('contactId', null); prefill(v, form.values.categoryId); }} />
            <Select label={t('companies.category')} clearable disabled={planOnly} data={categoryOptions} {...form.getInputProps('categoryId')}
              onChange={(v) => { form.setFieldValue('categoryId', v); prefill(form.values.supplierId, v); }} />
            <Select label={t('contractors.person')} clearable searchable data={contactOptions} {...form.getInputProps('contactId')} />
            {!form.values.contactId && <TextInput label={t('contractors.personName')} {...form.getInputProps('personName')} />}
          </SimpleGrid>
          <SimpleGrid cols={{ base: 2, sm: 3 }}>
            <NumberInput label={t('contractors.plannedHours')} min={0} decimalScale={2} disabled={planOnly} {...form.getInputProps('plannedHours')} />
            <NumberInput label={t('contractors.rate')} min={0} decimalScale={2} disabled={planOnly} {...form.getInputProps('rate')} />
            <DateInput label={t('contractors.visitOn')} description={t('contractors.visitOnHint')} clearable valueFormat="DD MMM YYYY" disabled={planOnly} {...form.getInputProps('plannedOn')} />
          </SimpleGrid>
          <SimpleGrid cols={{ base: 2, sm: 3 }}>
            <NumberInput label={t('contractors.hoursDone')} min={0} decimalScale={2} disabled={!canExec && !canPlan} {...form.getInputProps('hoursDone')} />
            <DateInput label={t('contractors.doneOn')} description={t('contractors.doneOnHint')} clearable valueFormat="DD MMM YYYY" maxDate={dayjs().add(1, 'day').toDate()} {...form.getInputProps('doneOn')} />
          </SimpleGrid>
          <Textarea label={t('common.notes')} autosize disabled={planOnly} {...form.getInputProps('notes')} />
          <Group justify="flex-end"><Button variant="default" onClick={() => setOpened(false)}>{t('common.cancel')}</Button><Button type="submit" loading={form.submitting}>{t('common.save')}</Button></Group>
        </Stack></form>
      </Modal>
    </Card>
  );
}
