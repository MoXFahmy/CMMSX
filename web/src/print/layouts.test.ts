import { describe, expect, it } from 'vitest';
import { blocksInOrder, builtInLayout, LABEL_FIELDS, LABEL_SIZES, layoutText, PAPER_BLOCKS, PAPER_SIZES, poUnitPrice, shows, type PrintLayout } from './layouts';

// The same text as the server's PrintLayoutRulesTests.Catalogue: paper/block: fields.
const CATALOGUE = [
  'job-card/header: orgName,site,title,printedDate',
  'job-card/details: status,priority,workType,trade,team,site,location,asset,serialNumber,plannedStart,due,assignees,requestedBy,costCentre',
  'job-card/description: ', 'job-card/technician-notes: ', 'job-card/warranty: ', 'job-card/asset-fields: ', 'job-card/checklist: ', 'job-card/contractors: ',
  'job-card/labour: ', 'job-card/parts: ',
  'job-card/completion: completionNotes,causeNotes,preventionNotes,symptom,cause,remedy,assetWasDown,downtimeHours',
  'job-card/signatures: ', 'job-card/terms: ',
  'purchase-order/header: orgName,site,deliveryAddress,title,orderDate,expectedDate',
  'purchase-order/supplier: name,contact,address,email,phone,taxNumber',
  'purchase-order/details: paymentTerms,quoteRef,shippingMethod,requestedBy,currency,orderType',
  'purchase-order/lines: lineNumber,partNumber,description,supplierPartNumber,quantity,unit,unitPrice,discount,lineTotal',
  'purchase-order/totals: subtotal,tax,shipping,total',
  'purchase-order/notes: ', 'purchase-order/approvals: ', 'purchase-order/terms: ',
  'request/header: orgName,site,title,printedDate',
  'request/details: status,submitted,requester,phone,requestType,priority,workType,site,location,asset,requiredBy,symptom',
  'request/description: ', 'request/access-windows: ', 'request/progress: ', 'request/terms: ',
  'labels/fields: number,name,detail,site',
];

describe('print layouts', () => {
  it('match the server catalogue', () => {
    const lines = (['job-card', 'purchase-order', 'request'] as const).flatMap((p) => PAPER_BLOCKS[p].map((b) => `${p}/${b.key}: ${b.fields.join(',')}`));
    expect([...lines, `labels/fields: ${LABEL_FIELDS.join(',')}`]).toEqual(CATALOGUE);
    expect(PAPER_SIZES).toEqual(['A4', 'A5', 'Letter']);
    expect(LABEL_SIZES).toEqual(['a4-3x8', 'a4-3x7', 'a4-2x7', 'roll-50x25', 'roll-100x50']);
  });

  it("built-in layouts are today's prints", () => {
    const job = builtInLayout('job-card');
    expect(job.blocks.filter((b) => !b.shown).map((b) => b.key)).toEqual(['asset-fields', 'terms']);
    const details = job.blocks.find((b) => b.key === 'details')!.fields;
    expect(details).not.toContain('team');
    expect(details).not.toContain('costCentre');
    expect(job.showLogo).toBe(true);
    expect(builtInLayout('purchase-order').showLogo).toBe(false);
    expect(builtInLayout('labels')).toMatchObject({ labelSize: 'a4-3x8', labelFields: ['number', 'name', 'detail'] });
  });

  it('follows the stored order and adds missing blocks hidden', () => {
    const l: PrintLayout = { ...builtInLayout('request'), blocks: [{ key: 'description', shown: true, fields: [] }, { key: 'header', shown: true, fields: ['title'] }] };
    expect(blocksInOrder(l).map((b) => b.key)).toEqual(['description', 'header']);
    const all = blocksInOrder(l, true);
    expect(all.map((b) => b.key)).toEqual(['description', 'header', 'details', 'access-windows', 'progress', 'terms']);
    expect(all.slice(2).every((b) => !b.shown)).toBe(true);
  });

  it('always shows fixed fields', () => {
    const po: PrintLayout = { ...builtInLayout('purchase-order'), blocks: [{ key: 'lines', shown: true, fields: ['quantity'] }] };
    expect(shows(po, 'lines', 'description')).toBe(true);
    expect(shows(po, 'lines', 'lineTotal')).toBe(true);
    expect(shows(po, 'lines', 'unit')).toBe(false);
    expect(shows(po, 'lines')).toBe(true);
    expect(shows(po, 'notes')).toBe(false);
  });

  it('layoutText falls back to English', () => {
    const l = { ...builtInLayout('job-card'), terms: 'Wear gloves', termsAr: '', headerText: 'Form 12', headerTextAr: 'نموذج 12' };
    expect(layoutText(l, 'terms', 'ar')).toBe('Wear gloves');
    expect(layoutText(l, 'header', 'ar')).toBe('نموذج 12');
    expect(layoutText(l, 'header', 'en')).toBe('Form 12');
    expect(layoutText(l, 'footer', 'ar')).toBe('');
  });

  it('shows the price after discount when the discount is hidden', () => {
    const line = { unitPrice: 100, discountPercent: 10, quantity: 2, lineTotal: 180 };
    expect(poUnitPrice(line, false)).toBe(90);
    expect(poUnitPrice(line, true)).toBe(100);
    expect(poUnitPrice({ ...line, quantity: 0, lineTotal: 0 }, false)).toBe(90);
  });
});
