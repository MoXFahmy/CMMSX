import { P } from '@/auth/AuthContext';

/**
 * The menu items a role can hide (0.22.0), each with the permissions that show it (any one is enough). The server keeps
 * the same list (MenuModules.All); tests on both sides pin it. Home and Settings aren't here: they can't be hidden.
 */
export const MENU_MODULES: { key: string; anyOf: string[] }[] = [
  { key: 'locations', anyOf: [P.locationsView] },
  { key: 'assets', anyOf: [P.assetsView] },
  { key: 'work-requests', anyOf: [P.requestsCreate] },
  { key: 'work-orders', anyOf: [P.woView] },
  { key: 'work-groups', anyOf: [P.woView] },
  { key: 'planning', anyOf: [P.woView] },
  { key: 'availability', anyOf: [P.woView] },
  { key: 'timesheet', anyOf: [P.woView] },
  { key: 'pm', anyOf: [P.pmView] },
  { key: 'contracts', anyOf: [P.contractsView] },
  { key: 'inventory', anyOf: [P.inventoryView] },
  { key: 'purchasing', anyOf: [P.purchasingView, P.purchasingRequest, P.purchasingApproveRequests] },
  { key: 'approvals', anyOf: [P.purchasingApprove, P.purchasingApproveRequests] },
  { key: 'reports', anyOf: [P.reportsView] },
  { key: 'budgets', anyOf: [P.reportsView] },
  { key: 'people', anyOf: [P.peopleView] },
];

/** A module's permissions, for a menu item: `{ module, anyPerm }`. */
export function menuModule(key: string): { module: string; anyPerm: string[] } {
  const m = MENU_MODULES.find((x) => x.key === key);
  if (!m) throw new Error(`Unknown menu module ${key}`);
  return { module: m.key, anyPerm: m.anyOf };
}
