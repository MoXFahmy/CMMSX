import { describe, expect, it } from 'vitest';
import { MENU_MODULES } from './menuModules';

// The same list as the server's MenuModules.All (pinned there by RoleHomeTests.Menu_modules_match_the_web_list).
const LIST: [string, string[]][] = [
  ['locations', ['locations.view']],
  ['assets', ['assets.view']],
  ['work-requests', ['requests.create']],
  ['work-orders', ['workorders.view']],
  ['work-groups', ['workorders.view']],
  ['planning', ['workorders.view']],
  ['availability', ['workorders.view']],
  ['timesheet', ['workorders.view']],
  ['pm', ['pm.view']],
  ['contracts', ['contracts.view']],
  ['inventory', ['inventory.view']],
  ['purchasing', ['purchasing.view', 'purchasing.request', 'purchasing.approveRequests']],
  ['approvals', ['purchasing.approve', 'purchasing.approveRequests']],
  ['reports', ['reports.view']],
  ['budgets', ['reports.view']],
  ['people', ['people.view']],
];

describe('menu modules', () => {
  it('match the server list', () => {
    expect(MENU_MODULES.map((m) => [m.key, m.anyOf])).toEqual(LIST);
  });
});
