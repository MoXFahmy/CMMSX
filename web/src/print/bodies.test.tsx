import { describe, expect, it } from 'vitest';
import type { PoDetail, WorkOrderDetail } from '@/api/types';
import { renderHtml } from '@/test/render';
import { builtInLayout, type PrintLayout } from './layouts';
import { JobCardBody } from './JobCardBody';
import { PoBody } from './PoBody';
import { RequestBody } from './RequestBody';
import type { WorkRequestDetail } from '@/api/types';
import { chooseLayout, readyToPrint } from './usePrintLayout';
import { PlainText } from './PrintTexts';

const ctx = { orgName: 'Cairo Hotel', terms: { site: 'Site', sites: 'Sites', location: 'Location', locations: 'Locations', asset: 'Asset', assets: 'Assets' }, logoUrl: null, language: 'en' };

const wo = {
  id: 'w1', number: 'WO-00042', title: 'Pump leak', status: 'Open', priority: 'High', type: 'Corrective', tradeName: 'Mechanical', site: { id: 's1', name: 'Cairo Hotel' },
  locationTrail: [{ id: 'l1', name: 'Basement' }], asset: { id: 'a1', assetNumber: 'P-1', name: 'Pump', serialNumber: 'SN9', underWarranty: false, warrantyType: 'None' },
  assets: [], tasks: [{ id: 't1', sequence: 1, description: 'Check seal', isDone: false, type: 'Check', photoRequired: false, beforeStart: false, photos: [] }],
  assignees: [{ id: 'e1', fullName: 'Amal' }], contractors: [], labor: [], parts: [], plannedParts: [], signatures: [],
  dueDate: '2026-11-30', team: { id: 'tm', name: 'Team A' }, costCentrePath: 'Ops › Plant',
} as unknown as WorkOrderDetail;

const po = {
  id: 'p1', number: 'PO-00007', status: 'Approved', siteName: 'Cairo Hotel', orderDate: '2026-11-24', currency: 'EGP', taxPercent: 0,
  supplier: { name: 'Filters Ltd', taxNumber: 'TX-77' }, paymentTerms: 'Net 60', shippingMethod: 'Courier',
  lines: [{ id: 'x1', lineNumber: 1, partNumber: 'F-1', description: 'Filter element', quantity: 2, unitOfMeasure: 'EA', unitPrice: 100, discountPercent: 10, lineTotal: 180 }],
  totals: { subtotal: 180, tax: 0, shippingCost: 0, total: 180 }, approval: null,
} as unknown as PoDetail;

const order = (html: string, words: string[]) => words.map((w) => html.indexOf(w));

describe('print bodies', () => {
  it("the built-in job card shows today's sections in order", () => {
    const html = renderHtml(<JobCardBody w={wo} layout={builtInLayout('job-card')} {...ctx} />);
    const at = order(html, ['Pump leak', 'Tasks', 'Labour', 'Parts', 'Completion']);
    expect(at.every((x) => x >= 0)).toBe(true);
    expect([...at].sort((a, b) => a - b)).toEqual(at);
    for (const label of ['Status', 'Priority', 'Due', 'Serial']) expect(html).toContain(label);
    expect(html).not.toContain('Team A');
    expect(html).not.toContain('Terms');
  });

  it('a job card layout hides blocks and fields and follows its order', () => {
    const layout: PrintLayout = {
      ...builtInLayout('job-card'), terms: 'Wear gloves',
      blocks: [
        { key: 'header', shown: true, fields: ['orgName', 'title'] },
        { key: 'parts', shown: true, fields: [] },
        { key: 'checklist', shown: true, fields: [] },
        { key: 'details', shown: true, fields: ['status', 'team'] },
        { key: 'labour', shown: false, fields: [] },
        { key: 'terms', shown: true, fields: [] },
      ],
    };
    const html = renderHtml(<JobCardBody w={wo} layout={layout} {...ctx} />);
    expect(html.indexOf('Parts')).toBeLessThan(html.indexOf('Tasks'));
    expect(html).not.toContain('Labour');
    expect(html).not.toContain('Priority');
    expect(html).toContain('Team A');
    expect(html).toContain('Wear gloves');
    expect(html).not.toContain('Completion');
  });

  it('prints Arabic-only safety notes for every reader (0.28.0-8)', () => {
    const arabicOnly = { ...wo, description: null, safetyNotes: '', safetyNotesAr: 'احذر الكهرباء' } as unknown as WorkOrderDetail;
    expect(renderHtml(<JobCardBody w={arabicOnly} layout={builtInLayout('job-card')} {...ctx} />)).toContain('احذر الكهرباء');
    expect(renderHtml(<JobCardBody w={arabicOnly} layout={builtInLayout('job-card')} {...ctx} language="ar" />)).toContain('احذر الكهرباء');
    const both = { ...wo, safetyNotes: 'Isolate first', safetyNotesAr: 'افصل أولاً' } as unknown as WorkOrderDetail;
    expect(renderHtml(<JobCardBody w={both} layout={builtInLayout('job-card')} {...ctx} />)).toContain('Isolate first');
  });

  it('texts are escaped', () => {
    const layout = { ...builtInLayout('job-card'), headerText: '<b>x</b>', footerText: 'Rev 3' };
    const html = renderHtml(<JobCardBody w={wo} layout={layout} {...ctx} />);
    expect(html).toContain('&lt;b&gt;x&lt;/b&gt;');
    expect(html).toContain('Rev 3');
  });

  it('the purchase order hides columns and shows the price after discount', () => {
    const built = renderHtml(<PoBody o={po} layout={builtInLayout('purchase-order')} {...ctx} />);
    expect(built).toContain('Disc.');
    expect(built).toContain('100.00');
    const layout: PrintLayout = { ...builtInLayout('purchase-order'), blocks: builtInLayout('purchase-order').blocks.map((b) =>
      b.key === 'lines' ? { ...b, fields: ['lineNumber', 'quantity', 'unitPrice'] } : b.key === 'supplier' ? { ...b, shown: false } : b) };
    const html = renderHtml(<PoBody o={po} layout={layout} {...ctx} />);
    expect(html).not.toContain('Disc.');
    expect(html).not.toContain('100.00');
    expect(html).toContain('90.00');
    expect(html).toContain('180.00');
    expect(html).toContain('Filter element'); // the description always prints
    expect(html).not.toContain('TX-77');
  });

  it('chooseLayout falls back when the chosen layout is missing', () => {
    const matched = builtInLayout('job-card');
    const other = { ...matched, id: 'x', name: 'Other' };
    expect(chooseLayout(other, matched)).toBe(other);
    expect(chooseLayout(null, matched)).toBe(matched);
    expect(chooseLayout(undefined, matched)).toBe(matched);
  });

  it('the request receipt shows its blocks', () => {
    const r = {
      id: 'r1', number: 'WR-00009', title: 'Warm room', description: 'Room 204 is hot', status: 'Approved', priority: 'Low', type: 'Corrective',
      site: { id: 's1', name: 'Cairo Hotel' }, locationName: 'Room 204', requesterName: 'Mona', requesterPhone: '0100', createdAt: '2026-11-24T08:00:00Z',
      accessWindows: [{ start: '2026-11-25T09:00:00Z', end: '2026-11-25T11:00:00Z', note: 'Guest out' }],
      workOrder: { id: 'w1', number: 'WO-00042', status: 'Open', dueDate: '2026-11-27' }, extraRecipients: [], feedback: [],
    } as unknown as WorkRequestDetail;
    const html = renderHtml(<RequestBody r={r} layout={builtInLayout('request')} {...ctx} />);
    for (const text of ['WR-00009', 'Warm room', 'Mona', '0100', 'Room 204 is hot', 'Guest out', 'WO-00042']) expect(html).toContain(text);
    const layout: PrintLayout = { ...builtInLayout('request'), blocks: builtInLayout('request').blocks.map((b) =>
      b.key === 'details' ? { ...b, fields: ['requester', 'status'] } : b.key === 'progress' ? { ...b, shown: false } : b) };
    const short = renderHtml(<RequestBody r={r} layout={layout} {...ctx} />);
    expect(short).toContain('Mona');
    expect(short).not.toContain('0100');
    expect(short).not.toContain('WO-00042');
  });

  it("never uses another paper's layout", () => {
    const matched = builtInLayout('job-card');
    expect(chooseLayout(builtInLayout('purchase-order'), matched)).toBe(matched);
  });

  it('warns that an order is not approved even without the header', () => {
    const layout: PrintLayout = { ...builtInLayout('purchase-order'), blocks: builtInLayout('purchase-order').blocks.map((b) => (b.key === 'header' ? { ...b, shown: false } : b)) };
    const html = renderHtml(<PoBody o={{ ...po, status: 'Draft' } as unknown as PoDetail} layout={layout} {...ctx} />);
    expect(html).toContain('not approved');
    expect(html).toContain('PO-00007');
  });

  it('waits for the asset fields before printing a job card that shows them', () => {
    expect(readyToPrint({ record: true, layoutReady: true, wantsFields: true, fieldsLoaded: false })).toBe(false);
    expect(readyToPrint({ record: true, layoutReady: true, wantsFields: true, fieldsLoaded: true })).toBe(true);
    expect(readyToPrint({ record: true, layoutReady: true, wantsFields: false, fieldsLoaded: false })).toBe(true);
    expect(readyToPrint({ record: false, layoutReady: true, wantsFields: false, fieldsLoaded: false })).toBe(false);
  });

  it('wraps long unbroken text instead of running off the page', () => {
    expect(renderHtml(<PlainText text={'https://example.test/' + 'a'.repeat(300)} />)).toMatch(/overflow-wrap:\s*anywhere/);
  });
});
