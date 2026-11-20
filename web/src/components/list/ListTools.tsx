import { useEffect, useRef, useState } from 'react';
import { ActionIcon, Button, Checkbox, Group, Menu, Modal, Stack, Text, TextInput, Tooltip } from '@mantine/core';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { IconBookmark, IconCheck, IconColumns3, IconDeviceFloppy, IconDownload, IconPencil, IconStar, IconStarOff, IconTrash } from '@tabler/icons-react';
import { del, get, post, put, toQuery } from '@/api/client';
import { confirmDelete } from '@/components/confirm';
import { notifyError, notifySuccess } from '@/components/utils';
import type { ListColumns } from './useListColumns';
import { normalizeQuery, type ListParams } from './useListParams';
import type { ExportSpec, ListColumn, ListKey, SavedView } from './types';

/** Rows an export can hold (the server stops there too). */
export const EXPORT_MAX = 10_000;

type Query = Record<string, string | number | boolean | null | undefined | string[]>;

/** What a column puts in the export file, in screen order. */
function exportColumns<T>(cols: ListColumn<T>[]): ExportSpec[] {
  return cols.flatMap((c) => {
    if (c.export === false) return [];
    const title = typeof c.title === 'string' ? c.title : String(c.accessor);
    return (c.export ?? [{ key: String(c.accessor) }]).map((e) => ({ key: e.key, title: e.title ?? title }));
  });
}

/** The address of an export: the list's own query, the format, then one c (key) and t (title) per column. */
export function exportUrl<T>(path: string, query: Query, cols: ListColumn<T>[], format: 'xlsx' | 'csv') {
  const sp = new URLSearchParams(toQuery(query).replace(/^\?/, ''));
  sp.set('format', format);
  for (const c of exportColumns(cols)) { sp.append('c', c.key); sp.append('t', c.title ?? c.key); }
  return `/api${path}?${sp.toString()}`;
}

const sameSet = (a: string[], b: string[]) => a.length === b.length && a.every((x) => b.includes(x));

/**
 * The toolbar of a list: saved views, the column chooser and the export menu. Opening a list with nothing in its address
 * applies the person's default view.
 */
export function ListTools<T>({ list, lp, cols, exportPath, exportQuery, total }: {
  list: ListKey; lp: ListParams<T>; cols: ListColumns<T>; exportPath: string; exportQuery: Query; total?: number;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const key = ['views', list];
  const { data: views } = useQuery({ queryKey: key, queryFn: () => get<SavedView[]>('/views', { list }) });
  const [dialog, setDialog] = useState<{ mode: 'new' | 'rename'; view?: SavedView } | null>(null);
  const [name, setName] = useState('');
  const [makeDefault, setMakeDefault] = useState(false);

  const active = (views ?? []).find((v) => normalizeQuery(v.query) === lp.query && sameSet(v.hiddenColumns, cols.tokens));
  const apply = (v: SavedView) => { lp.replaceAll(v.query); cols.setTokens(v.hiddenColumns); };

  // The default view, once, when the list opens with an empty address (a link or bookmark with filters wins).
  const openedEmpty = useRef(lp.isEmpty);
  const appliedDefault = useRef(false);
  useEffect(() => {
    if (appliedDefault.current || !views) return;
    appliedDefault.current = true;
    const d = views.find((v) => v.isDefault);
    if (openedEmpty.current && d) apply(d);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [views]);

  const refresh = () => qc.invalidateQueries({ queryKey: key });
  const body = (v: Partial<SavedView>) => ({ name: v.name, query: v.query, hiddenColumns: v.hiddenColumns, isDefault: v.isDefault ?? false });
  const create = useMutation({
    mutationFn: () => post<{ id: string }>('/views', { list, name, query: lp.query, hiddenColumns: cols.tokens, isDefault: makeDefault }),
    onSuccess: () => { notifySuccess(t('list.viewSaved')); setDialog(null); void refresh(); }, onError: notifyError,
  });
  const update = useMutation({
    mutationFn: (v: SavedView) => put(`/views/${v.id}`, body(v)),
    onSuccess: () => { notifySuccess(t('common.saved')); setDialog(null); void refresh(); }, onError: notifyError,
  });
  const remove = (v: SavedView) => confirmDelete(v.name, async () => { await del(`/views/${v.id}`); notifySuccess(t('common.deleted')); await refresh(); });
  const openDialog = (mode: 'new' | 'rename', view?: SavedView) => { setName(view?.name ?? ''); setMakeDefault(view?.isDefault ?? false); setDialog({ mode, view }); };
  const submit = () => (dialog?.mode === 'rename' && dialog.view ? update.mutate({ ...dialog.view, name }) : create.mutate());

  const capped = (total ?? 0) > EXPORT_MAX;

  return (
    <Group gap={6}>
      <Menu position="bottom-end" shadow="md" width={280}>
        <Menu.Target>
          <Button variant={active ? 'light' : 'default'} leftSection={<IconBookmark size={16} />} maw={220}>
            <Text fz="sm" truncate>{active ? active.name : t('list.views')}</Text>
          </Button>
        </Menu.Target>
        <Menu.Dropdown mah={420} style={{ overflowY: 'auto' }}>
          {(views ?? []).length === 0 && <Text fz="xs" c="dimmed" px="sm" py={6}>{t('list.noViews')}</Text>}
          {(views ?? []).map((v) => (
            <Menu.Item key={v.id} onClick={() => apply(v)} leftSection={v.id === active?.id ? <IconCheck size={14} /> : <span style={{ width: 14 }} />}
              rightSection={v.isDefault ? <Tooltip label={t('list.default')}><IconStar size={14} /></Tooltip> : null}>
              {v.name}
            </Menu.Item>
          ))}
          <Menu.Divider />
          <Menu.Item leftSection={<IconDeviceFloppy size={14} />} onClick={() => openDialog('new')}>{t('list.saveView')}</Menu.Item>
          {active && (
            <>
              <Menu.Item leftSection={<IconPencil size={14} />} onClick={() => openDialog('rename', active)}>{t('list.renameView')}</Menu.Item>
              <Menu.Item leftSection={active.isDefault ? <IconStarOff size={14} /> : <IconStar size={14} />}
                onClick={() => update.mutate({ ...active, isDefault: !active.isDefault })}>
                {active.isDefault ? t('list.removeDefault') : t('list.setDefault')}
              </Menu.Item>
              <Menu.Item color="red" leftSection={<IconTrash size={14} />} onClick={() => remove(active)}>{t('list.deleteView')}</Menu.Item>
            </>
          )}
          {!active && (views ?? []).length > 0 && (
            <Menu.Sub>
              <Menu.Sub.Target><Menu.Sub.Item leftSection={<IconPencil size={14} />}>{t('list.updateView')}</Menu.Sub.Item></Menu.Sub.Target>
              <Menu.Sub.Dropdown>
                {(views ?? []).map((v) => (
                  <Menu.Item key={v.id} onClick={() => update.mutate({ ...v, query: lp.query, hiddenColumns: cols.tokens })}>{v.name}</Menu.Item>
                ))}
              </Menu.Sub.Dropdown>
            </Menu.Sub>
          )}
        </Menu.Dropdown>
      </Menu>

      <Menu closeOnItemClick={false} position="bottom-end" shadow="md">
        <Menu.Target>
          <Tooltip label={t('list.columns')}><ActionIcon variant="default" size="lg" aria-label={t('list.columns')}><IconColumns3 size={16} /></ActionIcon></Tooltip>
        </Menu.Target>
        <Menu.Dropdown mah={420} style={{ overflowY: 'auto' }}>
          {cols.all.slice(1).map((c) => (
            <Menu.Item key={String(c.accessor)} onClick={() => cols.toggle(String(c.accessor))}>
              <Checkbox size="xs" label={typeof c.title === 'string' ? c.title : String(c.accessor)} checked={cols.isVisible(String(c.accessor))} readOnly
                styles={{ input: { cursor: 'pointer' } }} />
            </Menu.Item>
          ))}
        </Menu.Dropdown>
      </Menu>

      <Menu position="bottom-end" shadow="md">
        <Menu.Target>
          <Tooltip label={t('list.export')}><ActionIcon variant="default" size="lg" aria-label={t('list.export')}><IconDownload size={16} /></ActionIcon></Tooltip>
        </Menu.Target>
        <Menu.Dropdown>
          {capped && <Text fz="xs" c="orange" px="sm" py={6} maw={260}>{t('list.exportCapped', { max: EXPORT_MAX.toLocaleString(), count: total })}</Text>}
          <Menu.Item component="a" href={exportUrl(exportPath, exportQuery, cols.columns, 'xlsx')} download>{t('list.exportXlsx')}</Menu.Item>
          <Menu.Item component="a" href={exportUrl(exportPath, exportQuery, cols.columns, 'csv')} download>{t('list.exportCsv')}</Menu.Item>
        </Menu.Dropdown>
      </Menu>

      <Modal opened={dialog !== null} onClose={() => setDialog(null)} title={dialog?.mode === 'rename' ? t('list.renameView') : t('list.saveView')} size="sm">
        <form onSubmit={(e) => { e.preventDefault(); submit(); }}>
          <Stack>
            <TextInput label={t('list.viewName')} value={name} onChange={(e) => setName(e.currentTarget.value)} maxLength={60} required data-autofocus />
            {dialog?.mode === 'new' && <Checkbox label={t('list.makeDefault')} description={t('list.makeDefaultHint')} checked={makeDefault}
              onChange={(e) => setMakeDefault(e.currentTarget.checked)} />}
            <Group justify="flex-end">
              <Button variant="default" onClick={() => setDialog(null)}>{t('common.cancel')}</Button>
              <Button type="submit" loading={create.isPending || update.isPending} disabled={!name.trim()}>{t('common.save')}</Button>
            </Group>
          </Stack>
        </form>
      </Modal>
    </Group>
  );
}
