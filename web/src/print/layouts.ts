/**
 * Print layouts (0.23.0): the papers, their blocks and fields, and helpers the print pages and the editor share. The server
 * keeps the same catalogue (Core PrintPapers); tests on both sides pin it to one list.
 */

export type Paper = 'job-card' | 'purchase-order' | 'request' | 'labels';
export type LabelSize = 'a4-3x8' | 'a4-3x7' | 'a4-2x7' | 'roll-50x25' | 'roll-100x50';
export type PrintBlock = { key: string; shown: boolean; fields: string[] };
export type PrintLayout = {
  id: string; paper: Paper; name: string; isDefault: boolean; siteId?: string | null; siteName?: string | null; kindKey?: string | null; kindName?: string | null;
  paperSize?: 'A4' | 'A5' | 'Letter' | null; showLogo: boolean;
  headerText?: string | null; headerTextAr?: string | null; footerText?: string | null; footerTextAr?: string | null; terms?: string | null; termsAr?: string | null;
  blocks: PrintBlock[]; labelSize?: LabelSize | null; labelFields: string[];
};

type CatalogueBlock = { key: string; fields: string[]; builtInFields: string[]; builtInShown: boolean };

const b = (key: string, fields = '', builtIn?: string, shown = true): CatalogueBlock => {
  const all = fields ? fields.split(',') : [];
  return { key, fields: all, builtInFields: builtIn === undefined ? all : builtIn ? builtIn.split(',') : [], builtInShown: shown };
};

export const ASSET_FIELDS_BLOCK = 'asset-fields';

export const PAPER_BLOCKS: Record<Paper, CatalogueBlock[]> = {
  'job-card': [
    b('header', 'orgName,site,title,printedDate'),
    b('details', 'status,priority,workType,trade,team,site,location,asset,serialNumber,plannedStart,due,assignees,requestedBy,costCentre',
      'status,priority,workType,trade,site,location,asset,serialNumber,plannedStart,due,assignees,requestedBy'),
    b('description'), b('technician-notes'), b('warranty'), b(ASSET_FIELDS_BLOCK, '', undefined, false),
    b('checklist'), b('contractors'), b('labour'), b('parts'),
    b('completion', 'completionNotes,causeNotes,preventionNotes,symptom,cause,remedy,assetWasDown,downtimeHours'),
    b('signatures'), b('terms', '', undefined, false),
  ],
  'purchase-order': [
    b('header', 'orgName,site,deliveryAddress,title,orderDate,expectedDate'),
    b('supplier', 'name,contact,address,email,phone,taxNumber'),
    b('details', 'paymentTerms,quoteRef,shippingMethod,requestedBy,currency,orderType', 'paymentTerms,quoteRef,shippingMethod,requestedBy,currency'),
    b('lines', 'lineNumber,partNumber,description,supplierPartNumber,quantity,unit,unitPrice,discount,lineTotal'),
    b('totals', 'subtotal,tax,shipping,total'),
    b('notes'), b('approvals'), b('terms', '', undefined, false),
  ],
  request: [
    b('header', 'orgName,site,title,printedDate'),
    b('details', 'status,submitted,requester,phone,requestType,priority,workType,site,location,asset,requiredBy,symptom'),
    b('description'), b('access-windows'), b('progress'), b('terms', '', undefined, false),
  ],
  labels: [],
};

/** Fields ("block.field") that always print. */
export const FIXED_FIELDS: Record<Paper, string[]> = {
  'job-card': [], 'purchase-order': ['lines.description', 'lines.lineTotal', 'totals.total'], request: [], labels: [],
};

export const LABEL_FIELDS = ['number', 'name', 'detail', 'site'];
export const LABEL_SIZES: LabelSize[] = ['a4-3x8', 'a4-3x7', 'a4-2x7', 'roll-50x25', 'roll-100x50'];
export const PAPER_SIZES = ['A4', 'A5', 'Letter'] as const;

/** Today's print of a paper, as a layout. */
export function builtInLayout(paper: Paper): PrintLayout {
  return {
    id: '00000000-0000-0000-0000-000000000000', paper, name: 'Built-in', isDefault: false, paperSize: paper === 'labels' ? null : 'A4',
    showLogo: paper !== 'purchase-order',
    blocks: PAPER_BLOCKS[paper].map((x) => ({ key: x.key, shown: x.builtInShown, fields: [...x.builtInFields] })),
    labelSize: paper === 'labels' ? 'a4-3x8' : null, labelFields: paper === 'labels' ? ['number', 'name', 'detail'] : [],
  };
}

export const isBuiltIn = (l: Pick<PrintLayout, 'id'>) => /^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(l.id);

/**
 * The layout's blocks in its order: the shown ones, or with `all`, every block of the paper (those the layout doesn't list
 * added at the end, hidden).
 */
export function blocksInOrder(layout: PrintLayout, all = false): PrintBlock[] {
  const known = PAPER_BLOCKS[layout.paper].map((x) => x.key);
  const listed = layout.blocks.filter((x) => known.includes(x.key));
  const missing = PAPER_BLOCKS[layout.paper].filter((x) => !listed.some((y) => y.key === x.key)).map((x) => ({ key: x.key, shown: false, fields: [...x.builtInFields] }));
  const full = [...listed, ...missing];
  return all ? full : full.filter((x) => x.shown);
}

/** Whether a block (or one of its fields) prints. Fixed fields always do. */
export function shows(layout: PrintLayout, block: string, field?: string): boolean {
  if (field && FIXED_FIELDS[layout.paper].includes(`${block}.${field}`)) return true;
  if (!field && FIXED_FIELDS[layout.paper].some((f) => f.startsWith(`${block}.`))) return true;
  const x = layout.blocks.find((y) => y.key === block);
  if (!x || !x.shown) return false;
  return field ? x.fields.includes(field) : true;
}

/** A layout text in the page's language when filled in, otherwise the other language's (never nothing when one is filled). */
export function layoutText(layout: PrintLayout, which: 'header' | 'footer' | 'terms', language: string): string {
  const [en, ar] = which === 'header' ? [layout.headerText, layout.headerTextAr] : which === 'footer' ? [layout.footerText, layout.footerTextAr] : [layout.terms, layout.termsAr];
  const pick = (a?: string | null, b?: string | null) => (a?.trim() ? a : b?.trim() ? b : '');
  return language === 'ar' ? pick(ar, en) : pick(en, ar);
}

/** A purchase order line's unit price as printed: before discount when the discount column shows, after it otherwise. */
export function poUnitPrice(line: { unitPrice: number; discountPercent?: number | null; quantity: number; lineTotal: number }, discountShown: boolean): number {
  if (discountShown) return line.unitPrice;
  return line.quantity ? line.lineTotal / line.quantity : line.unitPrice * (1 - (line.discountPercent ?? 0) / 100);
}

/**
 * Rows of blocks for printing: two blocks that sit side by side on today's paper (labour and parts; supplier and details) stay
 * together when they follow each other, in either order; every other block has its own row.
 */
export function pairRows(blocks: PrintBlock[], pairs: [string, string][]): PrintBlock[][] {
  const rows: PrintBlock[][] = [];
  for (let i = 0; i < blocks.length; i++) {
    const a = blocks[i];
    const next = blocks[i + 1];
    if (next && pairs.some(([x, y]) => (a.key === x && next.key === y) || (a.key === y && next.key === x))) { rows.push([a, next]); i++; }
    else rows.push([a]);
  }
  return rows;
}
