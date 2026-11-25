import type { ReactNode } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter, Route, Routes } from 'react-router';
import { describe, expect, it } from 'vitest';
import { CountPage, PartPage, StockSearchPage } from './StockPages';
import { TechContext } from './useTech';
import type { StockPart, TechBundle, TechJob, TechStock } from './types';

const part: StockPart = {
  id: 'p1', partNumber: 'BRG-1', name: 'Bearing 6204', unitOfMeasure: 'EA', barcode: null, isShelfItem: false, allowNegativeStock: false,
  levels: [{ storeId: 's1', onHand: 5, reserved: 0, bin: 'A1' }],
};
const stock = { stores: [{ id: 's1', code: 'ST1', name: 'Store', siteId: 'x' }], parts: [part], cancelReasons: [], pmTasks: [], counts: [{ id: 'c1', name: 'November count', storeId: 's1' }], purchaseOrders: null } as unknown as TechStock;
const bundle = (issueStock?: boolean) => ({ jobs: [{ id: 'j1', number: 'WO-1', title: 'Swap bearing' } as unknown as TechJob], can: { logForOthers: false, cancel: false, createWorkOrders: false, issueStock } }) as unknown as TechBundle;

const page = (path: string, route: string, el: ReactNode, b: TechBundle) => renderToStaticMarkup(
  <MantineProvider>
    <TechContext.Provider value={{ stock, bundle: b, queue: [], online: true, add: async () => {} } as never}>
      <MemoryRouter initialEntries={[path]}><Routes><Route path={route} element={el} /></Routes></MemoryRouter>
    </TechContext.Provider>
  </MantineProvider>);

describe('phone stock actions follow the stock permission', () => {
  it('shows "Issue to a job" and the open counts to someone with the stock permission', () => {
    expect(page('/tech/parts/p1', '/tech/parts/:id', <PartPage />, bundle(true))).toContain('Issue to a job');
    expect(page('/tech/stock', '/tech/stock', <StockSearchPage />, bundle(true))).toContain('November count');
    expect(page('/tech/counts/c1', '/tech/counts/:id', <CountPage />, bundle(true))).toContain('November count');
  });

  it('hides "Issue to a job" and the counts from a role without it', () => {
    expect(page('/tech/parts/p1', '/tech/parts/:id', <PartPage />, bundle(false))).not.toContain('Issue to a job');
    expect(page('/tech/stock', '/tech/stock', <StockSearchPage />, bundle(false))).not.toContain('November count');
    expect(page('/tech/counts/c1', '/tech/counts/:id', <CountPage />, bundle(false))).not.toContain('Saved counts');
    expect(page('/tech/counts/c1', '/tech/counts/:id', <CountPage />, bundle(false))).not.toContain('November count');
  });

  it('keeps showing them when the server is older and sends no flag', () => {
    expect(page('/tech/parts/p1', '/tech/parts/:id', <PartPage />, bundle(undefined))).toContain('Issue to a job');
  });
});
