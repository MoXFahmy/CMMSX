import { describe, expect, it } from 'vitest';
import { companyRoles, lineRate, rolesError } from './companies';

describe('companies', () => {
  it('lists the roles a company has', () => expect(companyRoles({ isSupplier: true, isContractor: true, isManufacturer: false })).toEqual(['supplier', 'contractor']));
  it('needs at least one role', () => {
    expect(rolesError({ isSupplier: false, isContractor: false, isManufacturer: false })).toBe('companies.chooseRole');
    expect(rolesError({ isSupplier: false, isContractor: false, isManufacturer: true })).toBeNull();
  });
  it('takes the company rate, else the category default, else 0', () => {
    const cats = [{ id: 'e', defaultRate: 100 }];
    expect(lineRate({ rates: [{ categoryId: 'e', rate: 150 }] }, cats, 'e')).toBe(150);
    expect(lineRate({ rates: [] }, cats, 'e')).toBe(100);
    expect(lineRate(undefined, cats, null)).toBe(0);
    expect(lineRate({ rates: [] }, cats, 'missing')).toBe(0);
  });
});
