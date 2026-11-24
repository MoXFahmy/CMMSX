import { ApiError } from '@/api/client';
import { ASSET_FIELDS_BLOCK, blocksInOrder, FIXED_FIELDS, type PrintLayout } from './layouts';

/** A new layout starting from another (often the built-in one): same blocks and texts, not the default, no site or type. */
export function copyLayout(from: PrintLayout, name: string): PrintLayout {
  return {
    ...from, id: '', name, isDefault: false, siteId: null, siteName: null, kindKey: null, kindName: null,
    blocks: from.blocks.map((b) => ({ ...b, fields: [...b.fields] })), labelFields: [...from.labelFields],
  };
}

const carriesFixed = (layout: PrintLayout, key: string) => FIXED_FIELDS[layout.paper].some((f) => f.startsWith(`${key}.`));

/** Moves a block a place up (-1) or down (1). The header stays first; at either end nothing changes. */
export function moveBlock(layout: PrintLayout, key: string, by: -1 | 1): PrintLayout {
  const blocks = blocksInOrder(layout, true);
  const i = blocks.findIndex((b) => b.key === key);
  const j = i + by;
  if (i < 0 || key === 'header' || j < 0 || j >= blocks.length || blocks[j].key === 'header') return layout;
  const next = [...blocks];
  [next[i], next[j]] = [next[j], next[i]];
  return { ...layout, blocks: next };
}

/** Shows or hides a block. A block carrying fields that always print can't be hidden. */
export function toggleBlock(layout: PrintLayout, key: string): PrintLayout {
  if (carriesFixed(layout, key)) return layout;
  return { ...layout, blocks: blocksInOrder(layout, true).map((b) => (b.key === key ? { ...b, shown: !b.shown } : b)) };
}

/** Ticks or unticks a field of a block. Fields that always print stay. */
export function toggleField(layout: PrintLayout, block: string, field: string): PrintLayout {
  if (FIXED_FIELDS[layout.paper].includes(`${block}.${field}`)) return layout;
  return {
    ...layout,
    blocks: blocksInOrder(layout, true).map((b) => (b.key !== block ? b
      : { ...b, fields: b.fields.includes(field) ? b.fields.filter((f) => f !== field) : [...b.fields, field] })),
  };
}

/** Ticks or unticks a label field. */
export function toggleLabelField(layout: PrintLayout, field: string): PrintLayout {
  const fields = layout.labelFields.includes(field) ? layout.labelFields.filter((f) => f !== field) : [...layout.labelFields, field];
  return { ...layout, labelFields: fields };
}

/** Why a save was refused: the server's field messages when it sent any, otherwise its message. */
export function saveErrorText(e: unknown): string {
  if (e instanceof ApiError && Object.keys(e.fieldErrors).length > 0) return Object.values(e.fieldErrors).join(' ');
  return e instanceof Error ? e.message : String(e);
}

/** Drops asset fields that were deleted since the layout was saved (the editor can't show them, so they couldn't be unticked). */
export function pruneAssetFields(layout: PrintLayout, known: string[] | undefined): PrintLayout {
  if (!known) return layout;
  return { ...layout, blocks: layout.blocks.map((b) => (b.key === ASSET_FIELDS_BLOCK ? { ...b, fields: b.fields.filter((f) => known.includes(f)) } : b)) };
}
