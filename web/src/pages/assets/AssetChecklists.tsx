import { useState } from 'react';
import { ActionIcon, Anchor, Badge, Button, Card, Group, Select, Stack, Table, Text, Title } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconTrash } from '@tabler/icons-react';
import { ApiError, del, get, post } from '@/api/client';
import type { PmTaskListItem } from '@/api/types';
import { notifyError } from '@/components/utils';

type Row = { id: string; assetId?: string | null; assetCategoryId?: string | null; pmTaskId: string; code: string; name: string; stepCount: number };

/**
 * Work templates whose steps are added to every new work order of an asset (0.12.0), or of every asset of a category.
 * On an asset, its category's templates show too, to be changed in Settings › Asset categories.
 */
export function ChecklistsCard({ assetId, categoryId, canEdit }: { assetId?: string; categoryId?: string | null; canEdit: boolean }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const key = ['asset-checklists', assetId ?? null, categoryId ?? null];
  const { data } = useQuery({ queryKey: key, queryFn: () => get<Row[]>('/asset-checklists', { assetId, categoryId: categoryId ?? undefined }), enabled: !!assetId || !!categoryId });
  const { data: templates } = useQuery({ queryKey: ['pm-tasks'], queryFn: () => get<PmTaskListItem[]>('/pm-tasks'), enabled: canEdit });
  const [pick, setPick] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const own = (r: Row) => (assetId ? r.assetId === assetId : true);
  const on = new Set((data ?? []).filter(own).map((r) => r.pmTaskId));
  const refresh = () => qc.invalidateQueries({ queryKey: ['asset-checklists'] });
  const add = async () => {
    setError(null);
    try { await post('/asset-checklists', { assetId: assetId ?? null, assetCategoryId: assetId ? null : categoryId, pmTaskId: pick }); setPick(null); await refresh(); }
    catch (e) { if (e instanceof ApiError && e.fieldErrors.pmTaskId) setError(e.fieldErrors.pmTaskId); else notifyError(e); }
  };
  const remove = async (id: string) => { try { await del(`/asset-checklists/${id}`); await refresh(); } catch (e) { notifyError(e); } };
  return (
    <Card><Stack gap="xs">
      <div><Title order={5}>{t('checklists.title')}</Title><Text fz="sm" c="dimmed">{t(assetId ? 'checklists.assetHint' : 'checklists.categoryHint')}</Text></div>
      {(data?.length ?? 0) === 0 ? <Text fz="sm" c="dimmed">{t('checklists.none')}</Text> : (
        <Table verticalSpacing={4}><Table.Tbody>
          {data!.map((r) => (
            <Table.Tr key={r.id}>
              <Table.Td><Anchor component={Link} to={`/pm/templates/${r.pmTaskId}`} fz="sm">{r.code} · {r.name}</Anchor></Table.Td>
              <Table.Td><Text fz="xs" c="dimmed">{t('checklists.steps', { count: r.stepCount })}</Text></Table.Td>
              <Table.Td w={120} ta="right">
                {own(r) ? canEdit && <ActionIcon variant="subtle" color="red" aria-label={t('common.delete')} onClick={() => remove(r.id)}><IconTrash size={16} /></ActionIcon>
                  : <Badge size="xs" variant="light" color="gray">{t('checklists.fromCategory')}</Badge>}
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody></Table>
      )}
      {canEdit && (
        <Group gap="xs" align="flex-start" wrap="nowrap">
          <Select style={{ flex: 1 }} searchable clearable placeholder={t('checklists.add')} value={pick} onChange={(x) => { setPick(x); setError(null); }} error={error}
            data={(templates ?? []).filter((x) => x.isActive && !on.has(x.id)).map((x) => ({ value: x.id, label: `${x.code} · ${x.name}` }))} />
          <Button variant="light" disabled={!pick} onClick={add}>{t('checklists.addButton')}</Button>
        </Group>
      )}
    </Stack></Card>
  );
}
