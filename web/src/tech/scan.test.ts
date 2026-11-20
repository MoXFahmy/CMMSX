import { describe, expect, it } from 'vitest';
import { lookupCode, parseCode } from './scan';
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
