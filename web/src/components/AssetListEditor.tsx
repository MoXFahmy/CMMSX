import { useState } from 'react';
import { ActionIcon, Group, Stack, Text } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { IconArrowDown, IconArrowUp, IconX } from '@tabler/icons-react';
import { AssetPicker, type AssetOption } from '@/components/AssetPicker';

export type AssetRef = AssetOption;

/** Ordered list of assets for a PM round: add with the asset picker, reorder, remove. */
export function AssetListEditor({ value, onChange, siteId, error }: { value: AssetRef[]; onChange: (next: AssetRef[]) => void; siteId: string | null; error?: React.ReactNode }) {
  const { t } = useTranslation();
  const [pickerKey, setPickerKey] = useState(0); // remount to clear the picker after each add
  const move = (i: number, by: number) => {
    const next = [...value];
    [next[i], next[i + by]] = [next[i + by], next[i]];
    onChange(next);
  };
  return (
    <Stack gap={6}>
      {value.length === 0 && <Text fz="sm" c="dimmed">{t('pm.noAssets')}</Text>}
      {value.map((a, i) => (
        <Group key={a.id} gap="xs" wrap="nowrap">
          <Text fz="sm" c="dimmed" w={28} ta="right">{i + 1}.</Text>
          <Text fz="sm" style={{ flex: 1, minWidth: 0 }} truncate>{a.assetNumber} · {a.name}</Text>
          <ActionIcon variant="subtle" color="gray" size="sm" aria-label={t('pm.moveUp')} disabled={i === 0} onClick={() => move(i, -1)}><IconArrowUp size={14} /></ActionIcon>
          <ActionIcon variant="subtle" color="gray" size="sm" aria-label={t('pm.moveDown')} disabled={i === value.length - 1} onClick={() => move(i, 1)}><IconArrowDown size={14} /></ActionIcon>
          <ActionIcon variant="subtle" color="red" size="sm" aria-label={t('common.delete')} onClick={() => onChange(value.filter((x) => x.id !== a.id))}><IconX size={14} /></ActionIcon>
        </Group>
      ))}
      <AssetPicker key={pickerKey} label={t('pm.addAsset')} value={null} onChange={() => {}} error={error} siteId={siteId}
        onPick={(o) => { if (o && !value.some((x) => x.id === o.id)) onChange([...value, o]); setPickerKey((k) => k + 1); }} />
    </Stack>
  );
}
