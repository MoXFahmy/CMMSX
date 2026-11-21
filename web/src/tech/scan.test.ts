import { describe, expect, it } from 'vitest';
import { exactPart, lookupCode, parseCode, placeTarget } from './scan';
import type { TechPlaces } from './types';

const id = '01a1f8ce-0029-7cb1-8581-bffede8b3599';

describe('parseCode', () => {
  it('reads asset, location and part links on any host', () => {
    expect(parseCode(`https://dxvjhwlc6echh.cloudfront.net/q/asset/${id}`)).toEqual({ kind: 'asset', id });
    expect(parseCode(`https://cmms.cworks.com.eg/q/location/${id.toUpperCase()}?x=1`)).toEqual({ kind: 'location', id });
    expect(parseCode(` http://localhost:8093/q/part/${id} `)).toEqual({ kind: 'part', id });
  });

  it('treats anything else as a code', () => {
    expect(parseCode(' AST-00012 ')).toEqual({ kind: 'code', code: 'AST-00012' });
    expect(parseCode('https://example.com/q/asset/not-a-guid')).toEqual({ kind: 'code', code: 'https://example.com/q/asset/not-a-guid' });
  });
});

describe('lookupCode', () => {
  const places: TechPlaces = {
    assets: [
      { id: 'a1', assetNumber: 'AST-00012', name: 'Pump', siteId: 's', barcode: '4006381333931' },
      { id: 'a2', assetNumber: '4006381333931', name: 'Odd number', siteId: 's' },
    ],
    locations: [], meters: [],
  };
  it('finds a code as an asset number, then a barcode', () => {
    expect(lookupCode(places, 'ast-00012')).toEqual({ kind: 'asset', id: 'a1' });
    expect(lookupCode(places, '4006381333931')).toEqual({ kind: 'asset', id: 'a2' }); // the number wins over another asset's barcode
    expect(lookupCode({ ...places, assets: [places.assets[0]] }, '4006381333931')).toEqual({ kind: 'asset', id: 'a1' });
    expect(lookupCode(places, 'nothing')).toBeNull();
  });
});

describe('placeTarget', () => {
  const places = { assets: [{ id: 'a1', assetNumber: 'AST-1', name: 'Pump', siteId: 's1' }], locations: [{ id: 'l1', name: 'Hall', siteId: 's1' }], meters: [] } as unknown as TechPlaces;
  it('opens what is on the phone in the technician view', () => {
    expect(placeTarget('asset', 'a1', places, false)).toEqual({ tech: '/tech/place/asset/a1' });
    expect(placeTarget('location', 'l1', places, false)).toEqual({ tech: '/tech/place/location/l1' });
  });
  it('opens an asset from another site in the full app when online', () => {
    expect(placeTarget('asset', 'zz', places, true)).toEqual({ full: '/assets/zz' });
    expect(placeTarget('location', 'zz', places, true)).toEqual({ full: '/locations' });
  });
  it('says it is not on the phone when offline', () => {
    expect(placeTarget('asset', 'zz', places, false)).toEqual({ problem: 'notOnPhone' });
  });
});

describe('exactPart', () => {
  const found = [{ id: 'p1', partNumber: 'BRG-6205', barcode: '400123' }, { id: 'p2', partNumber: 'BRG-6205-2RS', barcode: null }];
  it('matches a part number or barcode exactly, ignoring case', () => {
    expect(exactPart(found, 'brg-6205')).toBe('p1');
    expect(exactPart(found, '400123')).toBe('p1');
  });
  it('does not pick a part that only contains the code', () => {
    expect(exactPart(found, 'BRG')).toBeNull();
  });
});
