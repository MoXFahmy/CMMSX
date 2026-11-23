/** Companies (0.17.0): a supplier record may sell to us, work for us as a contractor and/or make equipment. */
export type CompanyRole = 'supplier' | 'contractor' | 'manufacturer';

type Roles = { isSupplier: boolean; isContractor: boolean; isManufacturer: boolean };

export function companyRoles(c: Roles): CompanyRole[] {
  const roles: CompanyRole[] = [];
  if (c.isSupplier) roles.push('supplier');
  if (c.isContractor) roles.push('contractor');
  if (c.isManufacturer) roles.push('manufacturer');
  return roles;
}

/** The i18n key of the problem when a company has no role, else null. */
export function rolesError(c: Roles): string | null {
  return companyRoles(c).length ? null : 'companies.chooseRole';
}

/** A contractor line's starting rate: the company's own rate for the category, else the category's default, else 0 (as the server does). */
export function lineRate(company: { rates: { categoryId: string; rate: number }[] } | undefined, categories: { id: string; defaultRate: number }[], categoryId: string | null): number {
  if (!categoryId) return 0;
  return company?.rates.find((r) => r.categoryId === categoryId)?.rate ?? categories.find((c) => c.id === categoryId)?.defaultRate ?? 0;
}
