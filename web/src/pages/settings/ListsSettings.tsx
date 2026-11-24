import { useEffect, useState } from 'react';
import { ActionIcon, Badge, Button, Card, ColorSwatch, Group, Select, SimpleGrid, Stack, Switch, Table, Text, TextInput, Title } from '@mantine/core';
import { IconArrowDown, IconArrowUp, IconRestore } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { put } from '@/api/client';
import type { ListKey, ListLabel, ListLabels } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { LIST_PALETTE, LIST_PREFIX, LIST_VALUES } from '@/components/listLabels';
import en from '@/locales/en.json';
import ar from '@/locales/ar.json';
import { notifyError, notifySuccess } from '@/components/utils';

const LISTS: { key: ListKey; title: string }[] = [
  { key: 'workType', title: 'lists.workTypes' }, { key: 'priority', title: 'lists.priorities' },
  { key: 'assetStatus', title: 'lists.assetStatuses' }, { key: 'criticality', title: 'lists.criticality' },
];

/** The built-in words of a value, from the app's own text (not the organization's override). */
function builtIn(list: ListKey, value: string, lang: 'en' | 'ar'): string {
  const book = (lang === 'ar' ? ar : en) as unknown as Record<string, Record<string, string>>;
  return book[LIST_PREFIX[list]]?.[value] ?? value;
}

type Row = { value: string } & ListLabel;

/** Rows in the organization's order (then the built-in one), with what each says. */
function rowsOf(list: ListKey, labels: ListLabels | undefined): Row[] {
  const own = labels?.[list] ?? {};
  return LIST_VALUES[list]
    .map((value, i) => ({ value, i, ...own[value] }))
    .sort((a, b) => (a.order ?? Number.MAX_SAFE_INTEGER) - (b.order ?? Number.MAX_SAFE_INTEGER) || a.i - b.i)
    .map(({ i: _i, ...row }) => row);
}

/**
 * Settings › Lists (0.21.0): the organization's names (English and Arabic), colours, order and hidden values for work types,
 * priorities, asset statuses and criticality. The meanings stay the same, so rules, targets and reports keep working.
 */
export function ListsSettings({ editable }: { editable: boolean }) {
  const { t } = useTranslation();
  const { me, refresh } = useAuth();
  const saved = me?.tenant.settings.listLabels;
  const [draft, setDraft] = useState<Record<ListKey, Row[]>>(() => Object.fromEntries(LISTS.map((l) => [l.key, rowsOf(l.key, saved)])) as Record<ListKey, Row[]>);
  const [saving, setSaving] = useState(false);
  useEffect(() => setDraft(Object.fromEntries(LISTS.map((l) => [l.key, rowsOf(l.key, saved)])) as Record<ListKey, Row[]>), [saved]);

  const change = (list: ListKey, value: string, patch: Partial<ListLabel>) =>
    setDraft((d) => ({ ...d, [list]: d[list].map((r) => (r.value === value ? { ...r, ...patch } : r)) }));
  const move = (list: ListKey, index: number, by: number) => setDraft((d) => {
    const rows = [...d[list]];
    const [row] = rows.splice(index, 1);
    rows.splice(index + by, 0, row);
    return { ...d, [list]: rows };
  });
  const reset = (list: ListKey, value: string) =>
    setDraft((d) => ({ ...d, [list]: d[list].map((r) => (r.value === value ? { value } : r)) }));

  const save = async () => {
    setSaving(true);
    try {
      // Each list's order is saved only when it differs from the built-in one.
      const listLabels = Object.fromEntries(LISTS.map(({ key }) => {
        const reordered = draft[key].some((r, i) => LIST_VALUES[key][i] !== r.value);
        return [key, Object.fromEntries(draft[key].map((r, i) => [r.value,
          { name: r.name || null, nameAr: r.nameAr || null, color: r.color || null, hidden: !!r.hidden, order: reordered ? i : null }]))];
      }));
      await put('/settings/lists', { listLabels });
      await refresh();
      notifySuccess(t('common.saved'));
    } catch (e) { notifyError(e); } finally { setSaving(false); }
  };

  const colorData = [{ value: '', label: t('lists.screenColour') }, ...LIST_PALETTE.map((c) => ({ value: c, label: c }))];
  return (
    <Stack>
      <Group justify="space-between" wrap="wrap">
        <div><Title order={5}>{t('lists.title')}</Title><Text fz="sm" c="dimmed">{t('lists.hint')}</Text></div>
        {editable && <Button loading={saving} onClick={save}>{t('common.save')}</Button>}
      </Group>
      <SimpleGrid cols={{ base: 1, lg: 2 }}>
        {LISTS.map(({ key, title }) => (
          <Card key={key}>
            <Title order={6} mb="xs">{t(title)}</Title>
            <Table.ScrollContainer minWidth={560}>
              <Table verticalSpacing={4}>
                <Table.Thead><Table.Tr>
                  <Table.Th>{t('lists.builtIn')}</Table.Th><Table.Th>{t('lists.nameEn')}</Table.Th><Table.Th>{t('lists.nameAr')}</Table.Th>
                  <Table.Th>{t('lists.colour')}</Table.Th><Table.Th>{t('lists.hidden')}</Table.Th><Table.Th />
                </Table.Tr></Table.Thead>
                <Table.Tbody>
                  {draft[key].map((r, i) => (
                    <Table.Tr key={r.value}>
                      <Table.Td><Text fz="sm" c="dimmed">{builtIn(key, r.value, 'en')}</Text></Table.Td>
                      <Table.Td><TextInput size="xs" maxLength={50} placeholder={builtIn(key, r.value, 'en')} value={r.name ?? ''} disabled={!editable}
                        onChange={(e) => change(key, r.value, { name: e.currentTarget.value })} aria-label={t('lists.nameEn')} /></Table.Td>
                      <Table.Td><TextInput size="xs" maxLength={50} dir="rtl" placeholder={builtIn(key, r.value, 'ar')} value={r.nameAr ?? ''} disabled={!editable}
                        onChange={(e) => change(key, r.value, { nameAr: e.currentTarget.value })} aria-label={t('lists.nameAr')} /></Table.Td>
                      <Table.Td><Select size="xs" w={120} data={colorData} value={r.color ?? ''} disabled={!editable} allowDeselect={false} aria-label={t('lists.colour')}
                        onChange={(v) => change(key, r.value, { color: v || null })}
                        leftSection={r.color ? <ColorSwatch size={12} color={`var(--mantine-color-${r.color}-6)`} /> : undefined} /></Table.Td>
                      <Table.Td><Switch size="xs" checked={!!r.hidden} disabled={!editable} onChange={(e) => change(key, r.value, { hidden: e.currentTarget.checked })}
                        aria-label={t('lists.hidden')} /></Table.Td>
                      <Table.Td>{editable && <Group gap={2} wrap="nowrap" justify="flex-end">
                        <ActionIcon size="sm" variant="subtle" disabled={i === 0} onClick={() => move(key, i, -1)} aria-label={t('lists.up')}><IconArrowUp size={14} /></ActionIcon>
                        <ActionIcon size="sm" variant="subtle" disabled={i === draft[key].length - 1} onClick={() => move(key, i, 1)} aria-label={t('lists.down')}><IconArrowDown size={14} /></ActionIcon>
                        <ActionIcon size="sm" variant="subtle" onClick={() => reset(key, r.value)} aria-label={t('lists.reset')}><IconRestore size={14} /></ActionIcon>
                      </Group>}</Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Table.ScrollContainer>
            {draft[key].every((r) => r.hidden) && <Badge color="red" variant="light" mt="xs">{t('lists.keepOne')}</Badge>}
          </Card>
        ))}
      </SimpleGrid>
    </Stack>
  );
}
