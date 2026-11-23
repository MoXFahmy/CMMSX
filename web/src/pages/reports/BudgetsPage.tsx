import { useEffect, useMemo, useState } from 'react';
import { Alert, Anchor, Badge, Button, Card, Center, Group, Loader, NumberInput, Table, Text } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { IconChartBar, IconDeviceFloppy } from '@tabler/icons-react';
import { get, put } from '@/api/client';
import type { BudgetYear } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { PageHeader } from '@/components/PageHeader';
import { confirmAction } from '@/components/confirm';
import { useCostCentres } from '@/components/CostCentrePicker';
import { spread, treeRows } from '@/components/costCentres';
import { fmtPrice, notifyError, notifySuccess } from '@/components/utils';

const MONTHS = Array.from({ length: 12 }, (_, i) => i);
const sum = (xs: number[]) => Math.round(xs.reduce((a, b) => a + b, 0) * 100) / 100;

/**
 * Reports › Budgets (0.20.0): one amount per cost centre per month, for a chosen year. Cost centres down, months across and a
 * total; a value typed in the total spreads evenly over the months. Edited with Manage settings, seen with View reports.
 */
export function BudgetsPage() {
  const { t } = useTranslation();
  const { me, can } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const editable = can(P.settingsManage);
  const qc = useQueryClient();
  const [year, setYear] = useState(dayjs().year());
  const { data, isLoading, error } = useQuery({ queryKey: ['budgets', year], queryFn: () => get<BudgetYear>('/budgets', { year }) });
  const { data: centres } = useCostCentres();
  const [edits, setEdits] = useState<Record<string, number[]>>({});
  const [saving, setSaving] = useState(false);
  useEffect(() => setEdits({}), [year]);

  // The budget rows in tree order (parents first, children indented); a cost centre the tree doesn't list goes at the end.
  const rows = useMemo(() => {
    const byId = new Map((data?.rows ?? []).map((r) => [r.costCentreId, r]));
    const ordered = treeRows(centres ?? []).filter((c) => byId.has(c.id)).map((c) => ({ ...byId.get(c.id)!, name: c.name, code: c.code, depth: c.depth }));
    const seen = new Set(ordered.map((r) => r.costCentreId));
    return [...ordered, ...(data?.rows ?? []).filter((r) => !seen.has(r.costCentreId)).map((r) => ({ ...r, name: r.path, code: '', depth: 0 }))];
  }, [data, centres]);
  const monthsOf = (id: string, saved: number[]) => edits[id] ?? saved;
  const dirty = Object.keys(edits).length > 0;
  const setMonth = (id: string, saved: number[], m: number, v: number) =>
    setEdits((e) => ({ ...e, [id]: (e[id] ?? saved).map((x, i) => (i === m ? v : x)) }));
  const setTotal = (id: string, v: number) => setEdits((e) => ({ ...e, [id]: spread(v) }));
  const totals = MONTHS.map((m) => sum(rows.map((r) => monthsOf(r.costCentreId, r.months)[m])));

  const changeYear = (v: number) => {
    if (!v || v === year) return;
    if (dirty) confirmAction(t('budgets.discard'), () => setYear(v)); else setYear(v);
  };
  const save = async () => {
    setSaving(true);
    try {
      await put('/budgets', { year, rows: Object.entries(edits).map(([costCentreId, months]) => ({ costCentreId, months: months.map((x) => x || null) })) });
      notifySuccess(t('common.saved'));
      setEdits({});
      await qc.invalidateQueries({ queryKey: ['budgets', year] });
      await qc.invalidateQueries({ queryKey: ['report'] });
    } catch (e) { notifyError(e); } finally { setSaving(false); }
  };

  return (
    <>
      <PageHeader crumbs={[{ label: t('rep.title'), to: '/reports' }, { label: t('budgets.title') }]} title={t('budgets.title')} subtitle={t('budgets.subtitle', { currency })}
        actions={<>
          <NumberInput aria-label={t('rep.year')} w={110} min={2000} max={2100} value={year} onChange={(v) => changeYear(Number(v))} />
          <Button variant="default" leftSection={<IconChartBar size={16} />} component={Link} to="/reports/budget-vs-actual">{t('budgets.againstActual')}</Button>
          {editable && <Button leftSection={<IconDeviceFloppy size={16} />} disabled={!dirty} loading={saving} onClick={save}>{t('common.save')}</Button>}
        </>} />
      {error && <Alert color="red" mb="md">{(error as Error).message}</Alert>}
      {isLoading && <Center h={200}><Loader /></Center>}
      {data && rows.length === 0 && (
        <Alert color="blue" variant="light">{t('budgets.noCostCentres')} {can(P.woView) && <Anchor component={Link} to="/settings/cost-centres" fz="sm">{t('costCentres.title')}</Anchor>}</Alert>
      )}
      {data && rows.length > 0 && (
        <Card p={0}>
          <Text fz="sm" c="dimmed" p="sm">{t('budgets.hint')}</Text>
          <Table.ScrollContainer minWidth={1400}>
            <Table striped withColumnBorders verticalSpacing={4} horizontalSpacing={6}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th miw={200}>{t('costCentres.one')}</Table.Th>
                  {MONTHS.map((m) => <Table.Th key={m} ta="right">{dayjs().month(m).format('MMM')}</Table.Th>)}
                  <Table.Th ta="right">{t('common.total')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {rows.map((r) => {
                  const months = monthsOf(r.costCentreId, r.months);
                  return (
                    <Table.Tr key={r.costCentreId}>
                      <Table.Td><Group gap={6} wrap="nowrap" style={{ paddingInlineStart: r.depth * 16 }}>
                        <Text fz="sm" fw={r.depth === 0 ? 600 : 400} lineClamp={1} title={r.path}>{r.name}</Text>
                        {!r.isActive && <Badge size="xs" color="gray">{t('common.inactive')}</Badge>}
                      </Group></Table.Td>
                      {MONTHS.map((m) => (
                        <Table.Td key={m} ta="right">
                          {editable
                            ? <NumberInput size="xs" w={92} hideControls min={0} decimalScale={2} thousandSeparator aria-label={`${r.name} ${dayjs().month(m).format('MMMM')}`}
                                value={months[m] || ''} onChange={(v) => setMonth(r.costCentreId, r.months, m, Number(v) || 0)} styles={{ input: { textAlign: 'end' } }} />
                            : <Text fz="sm">{months[m] ? fmtPrice(months[m], currency, 0) : '—'}</Text>}
                        </Table.Td>
                      ))}
                      <Table.Td ta="right">
                        {editable
                          ? <TotalCell total={sum(months)} label={`${r.name} ${t('common.total')}`} onSpread={(v) => setTotal(r.costCentreId, v)} />
                          : <Text fz="sm" fw={600}>{fmtPrice(sum(months), currency, 0)}</Text>}
                      </Table.Td>
                    </Table.Tr>
                  );
                })}
              </Table.Tbody>
              <Table.Tfoot>
                <Table.Tr>
                  <Table.Th>{t('common.total')}</Table.Th>
                  {totals.map((v, m) => <Table.Th key={m} ta="right">{fmtPrice(v, currency, 0)}</Table.Th>)}
                  <Table.Th ta="right">{fmtPrice(sum(totals), currency, 0)}</Table.Th>
                </Table.Tr>
              </Table.Tfoot>
            </Table>
          </Table.ScrollContainer>
        </Card>
      )}
    </>
  );
}

/** The year's total for one cost centre: typing a value and leaving the box (or pressing Enter) spreads it evenly over the months. */
function TotalCell({ total, label, onSpread }: { total: number; label: string; onSpread: (v: number) => void }) {
  const [draft, setDraft] = useState<number | string | null>(null);
  // Only a changed total spreads; tabbing through leaves uneven months as they are.
  const apply = () => { if (draft !== null && (Number(draft) || 0) !== total) onSpread(Number(draft) || 0); setDraft(null); };
  return (
    <NumberInput size="xs" w={110} hideControls min={0} decimalScale={2} thousandSeparator aria-label={label} fw={600}
      value={draft ?? (total || '')} onFocus={() => setDraft(total || '')} onChange={setDraft} onBlur={apply}
      onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); apply(); } }} styles={{ input: { textAlign: 'end', fontWeight: 600 } }} />
  );
}
