import type { PurchaseOrderStatus, WorkOrderStatus, WorkRequestStatus } from '@/api/types';
import { P } from '@/auth/AuthContext';
import type { ListKey } from './types';

export type Can = (permission: string) => boolean;
export type ApiQuery = Record<string, string | string[] | boolean | undefined>;

/** Where each toolkit list's rows come from. */
export const LIST_API: Record<ListKey, string> = {
  'work-orders': '/work-orders', 'work-requests': '/work-requests', 'pm-schedules': '/pm-schedules',
  parts: '/parts', 'purchase-orders': '/purchase-orders', assets: '/assets',
};

/** The page that shows each list. */
export const LIST_PAGE: Record<ListKey, string> = {
  'work-orders': '/work-orders', 'work-requests': '/work-requests', 'pm-schedules': '/pm',
  parts: '/inventory', 'purchase-orders': '/purchasing/orders', assets: '/assets',
};

/** The permissions that open each list (any one is enough), as its endpoint checks them. */
export const LIST_VIEW_PERMS: Record<ListKey, string[]> = {
  'work-orders': [P.woView], 'work-requests': [P.requestsView, P.requestsCreate, P.requestsManage], 'pm-schedules': [P.pmView],
  parts: [P.inventoryView], 'purchase-orders': [P.purchasingView], assets: [P.assetsView],
};

/** The work order list's tabs and the statuses each shows. */
export const WO_TABS: { key: string; statuses: WorkOrderStatus[] | null }[] = [
  { key: 'active', statuses: ['Open', 'InProgress', 'OnHold'] }, { key: 'Open', statuses: ['Open'] }, { key: 'InProgress', statuses: ['InProgress'] },
  { key: 'OnHold', statuses: ['OnHold'] }, { key: 'Completed', statuses: ['Completed'] }, { key: 'Closed', statuses: ['Closed'] }, { key: 'all', statuses: null },
];

/** The work order list's view choice, each turning on one server filter. */
export const WO_VIEWS = ['everyone', 'mine', 'unassigned', 'overdue', 'dueSoon', 'responseLate'] as const;

/** The request list's tabs. */
export const WR_TABS: { key: string; status?: WorkRequestStatus; late?: boolean }[] = [
  { key: 'New', status: 'New' }, { key: 'late', late: true }, { key: 'Approved', status: 'Approved' }, { key: 'Closed', status: 'Closed' },
  { key: 'Rejected', status: 'Rejected' }, { key: 'Cancelled', status: 'Cancelled' }, { key: 'all' },
];

/** The purchase order list's views. */
export const PO_VIEWS: { key: string; statuses: PurchaseOrderStatus[] | null }[] = [
  { key: 'open', statuses: ['Draft', 'Submitted', 'Approved', 'PartiallyReceived'] }, { key: 'Draft', statuses: ['Draft'] }, { key: 'Submitted', statuses: ['Submitted'] },
  { key: 'receiving', statuses: ['Approved', 'PartiallyReceived'] }, { key: 'done', statuses: ['Received', 'Closed'] }, { key: 'Cancelled', statuses: ['Cancelled'] }, { key: 'all', statuses: null },
];

/** The request list opens on New for people who handle requests, and on everything for the rest. */
export const wrDefaultTab = (can: Can) => (can(P.requestsManage) ? 'New' : 'all');

const DEFAULT_SORT: Record<ListKey, { sort: string; dir: 'asc' | 'desc' }> = {
  'work-orders': { sort: 'createdAt', dir: 'desc' }, 'work-requests': { sort: 'createdAt', dir: 'desc' }, 'pm-schedules': { sort: 'nextDueDate', dir: 'asc' },
  parts: { sort: 'partNumber', dir: 'asc' }, 'purchase-orders': { sort: 'orderDate', dir: 'desc' }, assets: { sort: 'assetNumber', dir: 'asc' },
};

export const listDefaultSort = (list: ListKey) => DEFAULT_SORT[list];

/** Drops empty values (the server reads a missing value and an empty one the same way). */
function clean(q: ApiQuery): ApiQuery {
  return Object.fromEntries(Object.entries(q).filter(([, v]) => v !== undefined && v !== '' && !(Array.isArray(v) && v.length === 0)));
}

/**
 * The API query a list page sends for its address: filters, tab or view, sort and direction (not the page or page size).
 * The list pages, the home counters and the home list cards all use it, so a count always matches its list.
 */
export function apiQuery(list: ListKey, params: URLSearchParams, can: Can): ApiQuery {
  const p = (k: string) => params.get(k) ?? '';
  const d = DEFAULT_SORT[list];
  const sorted = { sort: p('sort') || d.sort, dir: (p('dir') as 'asc' | 'desc') || d.dir };
  switch (list) {
    case 'work-orders': {
      const tab = p('tab') || 'active';
      const view = p('view') || 'everyone';
      const statuses = WO_TABS.find((x) => x.key === tab)?.statuses ?? null;
      return clean({
        search: p('search'), siteId: p('siteId'), priority: p('priority'), type: p('type'), tradeId: p('tradeId'), assetId: p('assetId'), assetGroupId: p('assetGroupId'),
        mine: view === 'mine' ? 'true' : '', unassigned: view === 'unassigned' ? 'true' : '', overdue: view === 'overdue' ? 'true' : '',
        dueSoon: view === 'dueSoon' ? 'true' : '', responseLate: view === 'responseLate' ? 'true' : '',
        locationId: p('locationId'), assigneeId: p('assigneeId'), teamId: p('teamId'), createdFrom: p('createdFrom'), createdTo: p('createdTo'),
        dueFrom: p('dueFrom'), dueTo: p('dueTo'), source: p('source'), completedFrom: p('completedFrom'),
        cancelReasonId: p('cancelReasonId'), onlyWhenStopped: p('onlyWhenStopped'), groupId: p('groupId'),
        completedTo: p('completedTo'), symptomId: p('symptomId'), causeId: p('causeId'), remedyId: p('remedyId'), inFailureTree: p('inFailureTree'),
        status: statuses ?? undefined, ...sorted,
      });
    }
    case 'work-requests': {
      const tab = p('tab') || wrDefaultTab(can);
      const t = WR_TABS.find((x) => x.key === tab);
      return clean({ search: p('search'), siteId: p('siteId'), status: t?.status, late: t?.late ? 'true' : '', ...sorted });
    }
    case 'pm-schedules':
      return clean({ search: p('search'), siteId: p('siteId'), activeOnly: p('showInactive') !== 'true', ...sorted });
    case 'parts':
      return clean({
        search: p('search'), groupId: p('groupId'), storeId: p('storeId'), belowReorder: p('low'), critical: p('critical'), activeOnly: p('inactive') ? '' : 'true', ...sorted,
      });
    case 'purchase-orders': {
      const view = p('view') || 'open';
      const statuses = PO_VIEWS.find((v) => v.key === view)?.statuses ?? null;
      return clean({
        search: p('search'), supplierId: p('supplierId'), siteId: p('siteId'), overdue: p('overdue'), orderTypeId: p('orderTypeId'), status: statuses ?? undefined, ...sorted,
      });
    }
    case 'assets':
      return clean({
        search: p('search'), siteId: p('siteId'), locationId: p('locationId'), categoryId: p('categoryId'), status: p('status'), criticality: p('criticality'),
        topLevelOnly: p('topLevelOnly'), downNow: p('downNow'), brandId: p('brandId'), modelId: p('modelId'), assetGroupId: p('assetGroupId'), ...sorted,
      });
  }
}
