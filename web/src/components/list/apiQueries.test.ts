import { describe, expect, it } from 'vitest';
import { apiQuery } from './apiQueries';

const q = (s: string) => new URLSearchParams(s);
const all = () => true;
const none = () => false;

describe('apiQuery', () => {
  it('work orders: tabs and views map as the page did', () => {
    expect(apiQuery('work-orders', q(''), all)).toEqual({ status: ['Open', 'InProgress', 'OnHold'], sort: 'createdAt', dir: 'desc' });
    expect(apiQuery('work-orders', q('tab=all&view=mine'), all)).toEqual({ mine: 'true', sort: 'createdAt', dir: 'desc' });
    expect(apiQuery('work-orders', q('view=overdue&siteId=s1'), all)).toEqual({ overdue: 'true', siteId: 's1', status: ['Open', 'InProgress', 'OnHold'], sort: 'createdAt', dir: 'desc' });
    expect(apiQuery('work-orders', q('view=unassigned'), all)).toMatchObject({ unassigned: 'true' });
    expect(apiQuery('work-orders', q('tab=Completed&completedFrom=2026-11-01&sort=dueDate&dir=asc'), all))
      .toEqual({ status: ['Completed'], completedFrom: '2026-11-01', sort: 'dueDate', dir: 'asc' });
  });

  it('work requests: the default tab follows permissions', () => {
    expect(apiQuery('work-requests', q(''), (p) => p === 'requests.manage')).toEqual({ status: 'New', sort: 'createdAt', dir: 'desc' });
    expect(apiQuery('work-requests', q(''), none)).toEqual({ sort: 'createdAt', dir: 'desc' });
    expect(apiQuery('work-requests', q('tab=late&siteId=s1'), none)).toEqual({ late: 'true', siteId: 's1', sort: 'createdAt', dir: 'desc' });
  });

  it('PM schedules', () => {
    expect(apiQuery('pm-schedules', q(''), all)).toEqual({ activeOnly: true, sort: 'nextDueDate', dir: 'asc' });
    expect(apiQuery('pm-schedules', q('showInactive=true&search=pump'), all)).toEqual({ activeOnly: false, search: 'pump', sort: 'nextDueDate', dir: 'asc' });
  });

  it('parts', () => {
    expect(apiQuery('parts', q(''), all)).toEqual({ activeOnly: 'true', sort: 'partNumber', dir: 'asc' });
    expect(apiQuery('parts', q('low=true&inactive=1&storeId=st'), all)).toEqual({ belowReorder: 'true', storeId: 'st', sort: 'partNumber', dir: 'asc' });
  });

  it('purchase orders', () => {
    expect(apiQuery('purchase-orders', q(''), all)).toEqual({ status: ['Draft', 'Submitted', 'Approved', 'PartiallyReceived'], sort: 'orderDate', dir: 'desc' });
    expect(apiQuery('purchase-orders', q('view=all&overdue=true&supplierId=x'), all)).toEqual({ overdue: 'true', supplierId: 'x', sort: 'orderDate', dir: 'desc' });
  });

  it('assets', () => {
    expect(apiQuery('assets', q(''), all)).toEqual({ sort: 'assetNumber', dir: 'asc' });
    expect(apiQuery('assets', q('downNow=true&criticality=Critical&sort=name&dir=desc'), all)).toEqual({ downNow: 'true', criticality: 'Critical', sort: 'name', dir: 'desc' });
  });

  it("apiQuery ignores keys the list doesn't know", () => {
    expect(apiQuery('assets', q('foo=1&siteId=deleted-id&page=3&pageSize=50'), all)).toEqual({ siteId: 'deleted-id', sort: 'assetNumber', dir: 'asc' });
  });
});
