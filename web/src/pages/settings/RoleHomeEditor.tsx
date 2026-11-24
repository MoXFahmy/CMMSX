import { ActionIcon, Alert, Button, Card, ColorSwatch, Group, Select, SimpleGrid, Stack, Text, TextInput, Tooltip } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { IconArrowDown, IconArrowUp, IconInfoCircle, IconLayoutDashboard, IconPlus, IconTrash } from '@tabler/icons-react';
import type { PermissionInfo } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { CARD_INFO, TILE_COLORS, type CardGroup, type HomeCard, type HomeCardType, type HomeTile } from '@/pages/home/cards';
import { addableCards, classicCards, isSafeLink, linkChoices, missingFor, moveCard } from './roleHome';

const MAX_TILES = 12;
const GROUPS: CardGroup[] = ['dashboard', 'work', 'assets', 'tools'];

/** Whether the tiles can be saved: a label each (40 characters at most) and a CMX address. */
export const tilesOk = (cards: HomeCard[]) => cards.every((c) => c.type !== 'shortcuts'
  || (c.tiles ?? []).every((t) => t.label.trim() && t.label.trim().length <= 40 && (t.labelAr?.trim().length ?? 0) <= 40 && isSafeLink(t.to)));

function TileEditor({ tile, onChange, onRemove }: { tile: HomeTile; onChange: (t: HomeTile) => void; onRemove: () => void }) {
  const { t } = useTranslation();
  const { terms } = useAuth();
  const choices = linkChoices(t, terms);
  const badLink = tile.to.trim() !== '' && !isSafeLink(tile.to);
  return (
    <Card withBorder p="sm">
      <Stack gap="xs">
        <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="xs">
          <TextInput size="xs" label={t('roles.tile.label')} required maxLength={40} value={tile.label} onChange={(e) => onChange({ ...tile, label: e.currentTarget.value })} />
          <TextInput size="xs" label={t('roles.tile.labelAr')} dir="rtl" maxLength={40} value={tile.labelAr ?? ''} onChange={(e) => onChange({ ...tile, labelAr: e.currentTarget.value || null })} />
          <Select size="xs" label={t('roles.tile.page')} data={choices} value={choices.some((c) => c.value === tile.to) ? tile.to : null} searchable clearable
            onChange={(v) => v && onChange({ ...tile, to: v, label: tile.label || choices.find((c) => c.value === v)!.label })} />
          <TextInput size="xs" label={t('roles.tile.address')} placeholder="/work-orders?view=mine" value={tile.to} error={badLink ? t('roles.tile.badLink') : undefined}
            onChange={(e) => onChange({ ...tile, to: e.currentTarget.value })} />
        </SimpleGrid>
        <Group justify="space-between">
          <Group gap={6} aria-label={t('roles.tile.colour')}>
            {TILE_COLORS.map((c) => (
              <ColorSwatch key={c} component="button" type="button" color={`var(--mantine-color-${c}-filled)`} size={22} aria-label={c} onClick={() => onChange({ ...tile, color: c })}
                style={{ cursor: 'pointer', outline: tile.color === c ? '2px solid var(--mantine-color-text)' : undefined, outlineOffset: 2 }} />
            ))}
          </Group>
          <ActionIcon variant="subtle" color="red" onClick={onRemove} aria-label={t('common.delete')}><IconTrash size={16} /></ActionIcon>
        </Group>
      </Stack>
    </Card>
  );
}

/** The Home page tab of the role window (0.22.0): the role's cards in order, with the tiles of the shortcuts card. */
export function RoleHomeEditor({ cards, onChange, permissions, permissionInfo }: {
  cards: HomeCard[]; onChange: (cards: HomeCard[]) => void; permissions: string[]; permissionInfo: PermissionInfo[];
}) {
  const { t } = useTranslation();
  const name = (code: string) => permissionInfo.find((p) => p.code === code)?.name ?? code;
  const left = addableCards(cards);
  const addData = GROUPS.map((g) => ({ group: t(`roles.groups.${g}`), items: left.filter((x) => CARD_INFO[x].group === g).map((x) => ({ value: x, label: t(`home.cards.${x}`) })) }))
    .filter((g) => g.items.length > 0);
  const add = (type: HomeCardType) => onChange([...cards, type === 'shortcuts' ? { type, tiles: [] } : { type }]);
  const startClassic = () => onChange([...cards, ...classicCards().filter((c) => !cards.some((x) => x.type === c.type))]);
  const setTiles = (i: number, tiles: HomeTile[]) => onChange(cards.map((c, j) => (j === i ? { ...c, tiles } : c)));

  return (
    <Stack gap="sm">
      <Group gap="xs" wrap="wrap">
        <Select placeholder={t('roles.home.addCard')} data={addData} value={null} onChange={(v) => v && add(v as HomeCardType)} searchable leftSection={<IconPlus size={14} />} w={260} disabled={left.length === 0} />
        <Button variant="light" leftSection={<IconLayoutDashboard size={16} />} onClick={startClassic}>{t('roles.home.startClassic')}</Button>
      </Group>
      <Text fz="xs" c="dimmed">{t('roles.home.classicNote')}</Text>
      {cards.length === 0 ? <Text c="dimmed" fz="sm">{t('roles.home.none')}</Text> : (
        <Stack gap="xs">
          {cards.map((card, i) => {
            const missing = missingFor(card.type, permissions);
            return (
              <Card key={card.type} withBorder p="sm">
                <Group justify="space-between" wrap="nowrap" align="flex-start">
                  <Stack gap={2} style={{ minWidth: 0 }}>
                    <Text fw={600} fz="sm">{t(`home.cards.${card.type}`)}</Text>
                    <Text fz="xs" c="dimmed">{t(`roles.cardHints.${card.type}`)}</Text>
                    {missing && <Text fz="xs" c="orange">{t('roles.home.missing', { perm: missing.map(name).join(t('roles.home.or')) })}</Text>}
                  </Stack>
                  <Group gap={2} wrap="nowrap">
                    <Tooltip label={t('roles.home.up')}><ActionIcon variant="subtle" disabled={i === 0} onClick={() => onChange(moveCard(cards, i, -1))} aria-label={t('roles.home.up')}><IconArrowUp size={16} /></ActionIcon></Tooltip>
                    <Tooltip label={t('roles.home.down')}><ActionIcon variant="subtle" disabled={i === cards.length - 1} onClick={() => onChange(moveCard(cards, i, 1))} aria-label={t('roles.home.down')}><IconArrowDown size={16} /></ActionIcon></Tooltip>
                    <ActionIcon variant="subtle" color="red" onClick={() => onChange(cards.filter((_, j) => j !== i))} aria-label={t('common.delete')}><IconTrash size={16} /></ActionIcon>
                  </Group>
                </Group>
                {card.type === 'shortcuts' && (
                  <Stack gap="xs" mt="sm">
                    {(card.tiles ?? []).map((tile, k) => (
                      <TileEditor key={k} tile={tile} onChange={(nt) => setTiles(i, (card.tiles ?? []).map((x, m) => (m === k ? nt : x)))}
                        onRemove={() => setTiles(i, (card.tiles ?? []).filter((_, m) => m !== k))} />
                    ))}
                    <Group>
                      <Button size="xs" variant="default" leftSection={<IconPlus size={14} />} disabled={(card.tiles?.length ?? 0) >= MAX_TILES}
                        onClick={() => setTiles(i, [...(card.tiles ?? []), { label: '', labelAr: null, color: 'blue', to: '' }])}>{t('roles.tile.add')}</Button>
                    </Group>
                  </Stack>
                )}
              </Card>
            );
          })}
        </Stack>
      )}
      <Alert variant="light" color="gray" icon={<IconInfoCircle size={16} />}>{t('roles.home.severalRoles')}</Alert>
    </Stack>
  );
}
