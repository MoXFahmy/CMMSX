import { Group, Select } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { get } from '@/api/client';
import { builtInLayout, isBuiltIn, type Paper, type PrintLayout } from './layouts';

/** The layout to print with: the one picked by hand when it still exists, otherwise the record's own. */
export function chooseLayout(chosen: PrintLayout | null | undefined, matched: PrintLayout): PrintLayout {
  return chosen ?? matched;
}

/**
 * The layout a record prints with (0.23.0): the server's choice for it, unless the address names another (`?layout=<id>`,
 * remembered when the person picks one). A chosen layout that no longer exists falls back to the record's own, and when the
 * server can't answer, the built-in layout prints.
 */
export function usePrintLayout(paper: Paper, recordId: string | undefined, extra: Record<string, string> = {}) {
  const [params, setParams] = useSearchParams();
  const chosenId = params.get('layout');
  const builtIn = builtInLayout(paper);
  const matched = useQuery({
    queryKey: ['print-layout-for', paper, recordId, extra], queryFn: () => get<PrintLayout>('/print-layouts/for', { paper, id: recordId, ...extra }),
    enabled: !!recordId, retry: false,
  });
  const list = useQuery({ queryKey: ['print-layouts', paper], queryFn: () => get<PrintLayout[]>('/print-layouts', { paper }), retry: false });
  const wantsOther = !!chosenId && chosenId !== builtIn.id;
  const chosen = useQuery({ queryKey: ['print-layout', chosenId], queryFn: () => get<PrintLayout>(`/print-layouts/${chosenId}`), enabled: wantsOther, retry: false });
  const own = matched.data ?? (matched.isError ? builtIn : undefined);
  const picked = chosenId === builtIn.id ? builtIn : wantsOther ? (chosen.isError ? null : chosen.data) : null;
  const ready = !!own && (!wantsOther || chosen.isSuccess || chosen.isError);
  const choose = (id: string) => {
    const next = new URLSearchParams(params);
    if (own && id === own.id) next.delete('layout'); else next.set('layout', id);
    setParams(next, { replace: true });
  };
  return { layout: ready && own ? chooseLayout(picked, own) : undefined, matched: own, layouts: list.data ?? [], ready, choose };
}

/** "Layout": every layout of the paper, the record's own first. Not printed. */
export function LayoutChooser({ layout, matched, layouts, onChange }: { layout?: PrintLayout; matched?: PrintLayout; layouts: PrintLayout[]; onChange: (id: string) => void }) {
  const { t } = useTranslation();
  if (!layout || !matched) return null;
  const builtIn = builtInLayout(layout.paper);
  const others = layouts.filter((l) => l.id !== matched.id);
  const data = [
    { value: matched.id, label: `${isBuiltIn(matched) ? t('print.builtIn') : matched.name} (${t('print.automatic')})` },
    ...others.map((l) => ({ value: l.id, label: l.name })),
    ...(isBuiltIn(matched) ? [] : [{ value: builtIn.id, label: t('print.builtIn') }]),
  ];
  if (data.length < 2) return null;
  return (
    <Group justify="center" className="no-print" mb="md">
      <Select label={t('print.layout')} data={data} value={layout.id} onChange={(v) => v && onChange(v)} allowDeselect={false} w={320} />
    </Group>
  );
}
