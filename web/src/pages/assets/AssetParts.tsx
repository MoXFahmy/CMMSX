import { useEffect, useState } from 'react';
import { ActionIcon, Anchor, Button, Card, Group, Modal, NumberInput, Stack, Table, Text, TextInput, Title } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconCopy, IconEdit, IconPlus, IconTrash } from '@tabler/icons-react';
import { get, post, put } from '@/api/client';
import type { AssetPartLine } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { AssetPicker } from '@/components/AssetPicker';
import { PartPicker } from '@/components/PartPicker';
import { fmtQty, notifyError, notifySuccess } from '@/components/utils';

type Draft = { partId: string; partNumber: string; name: string; unitOfMeasure: string; quantity: number | ''; note: string };

/** The asset's spare-parts list: the parts it takes and how many. People with assets.edit change it or copy another asset's list. */
export function AssetPartsTab({ assetId }: { assetId: string }) {
  const { t } = useTranslation();
  const { can } = useAuth();
  const qc = useQueryClient();
  const key = ['asset', assetId, 'parts'];
  const { data } = useQuery({ queryKey: key, queryFn: () => get<AssetPartLine[]>(`/assets/${assetId}/parts`) });
  const [draft, setDraft] = useState<Draft[] | null>(null);
  const [adding, setAdding] = useState<string | null>(null);
  const [copyOpen, setCopyOpen] = useState(false);
  const [source, setSource] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => { if (!copyOpen) setSource(null); }, [copyOpen]);

  const edit = () => setDraft((data ?? []).map((l) => ({ ...l, note: l.note ?? '' })));
  const save = async () => {
    if (!draft) return;
    setBusy(true);
    try {
      await put(`/assets/${assetId}/parts`, draft.map((l) => ({ partId: l.partId, quantity: l.quantity === '' ? 0 : l.quantity, note: l.note })));
      notifySuccess(t('common.saved')); setDraft(null);
      await qc.invalidateQueries({ queryKey: key });
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  const copy = async () => {
    if (!source) return;
    setBusy(true);
    try {
      await post(`/assets/${assetId}/parts/copy-from/${source}`);
      notifySuccess(t('assetParts.copied')); setCopyOpen(false);
      await qc.invalidateQueries({ queryKey: key });
    } catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  const set = (i: number, change: Partial<Draft>) => setDraft((d) => d && d.map((l, j) => (j === i ? { ...l, ...change } : l)));

  return (
    <Card p={0}>
      <Group justify="space-between" p="md">
        <div><Title order={5}>{t('assetParts.title')}</Title><Text fz="sm" c="dimmed">{t('assetParts.hint')}</Text></div>
        {can(P.assetsEdit) && !draft && (
          <Group gap="xs">
            <Button size="xs" variant="default" leftSection={<IconCopy size={14} />} onClick={() => setCopyOpen(true)}>{t('assetParts.copyFrom')}</Button>
            <Button size="xs" leftSection={<IconEdit size={14} />} onClick={edit}>{t('assetParts.edit')}</Button>
          </Group>
        )}
        {draft && <Group gap="xs"><Button size="xs" variant="default" onClick={() => setDraft(null)}>{t('common.cancel')}</Button>
          <Button size="xs" loading={busy} onClick={save}>{t('common.save')}</Button></Group>}
      </Group>
      {!draft && (data?.length ?? 0) === 0 && <Text c="dimmed" fz="sm" px="md" pb="md">{t('assetParts.empty')}</Text>}
      {(draft || (data?.length ?? 0) > 0) && (
        <Table.ScrollContainer minWidth={640}>
          <Table verticalSpacing="xs">
            <Table.Thead><Table.Tr><Table.Th>{t('inv.part')}</Table.Th><Table.Th w={160}>{t('inv.quantity')}</Table.Th><Table.Th>{t('assetParts.note')}</Table.Th>{draft && <Table.Th w={40} />}</Table.Tr></Table.Thead>
            <Table.Tbody>
              {!draft && (data ?? []).map((l) => (
                <Table.Tr key={l.partId}>
                  <Table.Td><Anchor component={Link} to={`/inventory/parts/${l.partId}`} fz="sm" fw={600}>{l.partNumber}</Anchor><Text fz="xs" c="dimmed">{l.name}</Text></Table.Td>
                  <Table.Td>{fmtQty(l.quantity)} {l.unitOfMeasure}</Table.Td>
                  <Table.Td>{l.note}</Table.Td>
                </Table.Tr>
              ))}
              {draft && draft.map((l, i) => (
                <Table.Tr key={l.partId}>
                  <Table.Td><Text fz="sm" fw={600}>{l.partNumber}</Text><Text fz="xs" c="dimmed">{l.name}</Text></Table.Td>
                  <Table.Td><NumberInput size="xs" min={0} decimalScale={3} value={l.quantity} rightSection={<Text fz="xs" c="dimmed">{l.unitOfMeasure}</Text>} rightSectionWidth={44}
                    onChange={(v) => set(i, { quantity: typeof v === 'number' ? v : '' })} aria-label={t('inv.quantity')} /></Table.Td>
                  <Table.Td><TextInput size="xs" maxLength={200} value={l.note} onChange={(e) => set(i, { note: e.currentTarget.value })} aria-label={t('assetParts.note')} /></Table.Td>
                  <Table.Td><ActionIcon variant="subtle" color="red" onClick={() => setDraft((d) => d && d.filter((_, j) => j !== i))} aria-label={t('common.delete')}><IconTrash size={16} /></ActionIcon></Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      {draft && (
        <Group p="md" align="flex-end">
          <PartPicker label={t('assetParts.addPart')} value={adding} withStock={false} w={360}
            onChange={(id, p) => {
              setAdding(null);
              if (!id || !p || draft.some((l) => l.partId === id)) return;
              setDraft([...draft, { partId: p.id, partNumber: p.partNumber, name: p.name, unitOfMeasure: p.unitOfMeasure, quantity: 1, note: '' }]);
            }} />
          <Text fz="xs" c="dimmed"><IconPlus size={12} /> {t('assetParts.addHint')}</Text>
        </Group>
      )}
      <Modal opened={copyOpen} onClose={() => setCopyOpen(false)} title={t('assetParts.copyFrom')}>
        <Stack>
          <Text fz="sm" c="dimmed">{t('assetParts.copyHint')}</Text>
          <AssetPicker label={t('assetParts.copySource')} value={source} onChange={setSource} excludeId={assetId} />
          <Group justify="flex-end"><Button variant="default" onClick={() => setCopyOpen(false)}>{t('common.cancel')}</Button>
            <Button disabled={!source} loading={busy} onClick={copy}>{t('assetParts.copy')}</Button></Group>
        </Stack>
      </Modal>
    </Card>
  );
}
