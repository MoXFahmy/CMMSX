import type { TFunction } from 'i18next';

export type Change = { from?: unknown; to?: unknown };
export type Changes = Record<string, Change>;

/** "PasswordHash" → "Password Hash", "SiteId" → "Site". */
export const humanizeField = (k: string) => k.replace(/^settings\./, '').replace(/^security\./, '').replace(/Id$/, '').replace(/([a-z])([A-Z])/g, '$1 $2')
  .replace(/^./, (c) => c.toUpperCase());

/** A changed value as text: ∅ for nothing, … for an id, the value otherwise (masked values stay "••••"). */
export function showValue(v: unknown): string {
  if (v === null || v === undefined || v === '') return '∅';
  if (Array.isArray(v)) return v.length ? v.map(showValue).join(', ') : '∅';
  if (typeof v === 'object') return Object.entries(v as Record<string, unknown>).map(([k, x]) => `${k}: ${showValue(x)}`).join(', ') || '∅';
  const s = String(v);
  return /^[0-9a-f]{8}-[0-9a-f]{4}-/.test(s) ? '…' : s;
}

/** The stored changes JSON, or null when there are none (or it can't be read). */
export function parseChanges(json: string | null | undefined): Changes | null {
  if (!json) return null;
  try { const v = JSON.parse(json); return v && typeof v === 'object' ? (v as Changes) : null; } catch { return null; }
}

/** One line for a log row: up to three "field: old → new" pairs, then "+n more"; a summary entry shows its text. */
export function changeSummary(changes: Changes | null, t: TFunction): string {
  if (!changes) return '';
  const entries = Object.entries(changes);
  if (entries.length === 1 && entries[0][0] === 'Summary') return showValue(entries[0][1]?.to);
  const shown = entries.slice(0, 3).map(([k, c]) => `${humanizeField(k)}: ${showValue(c?.from)} → ${showValue(c?.to)}`);
  if (entries.length > 3) shown.push(t('activity.more', { count: entries.length - 3 }));
  return shown.join(' · ');
}

export type FileKind = 'Images' | 'Videos' | 'Documents' | 'Signatures' | 'Other';

/** The storage overview's kinds, as the server groups them. */
export function fileKind(contentType: string, isSignature: boolean): FileKind {
  if (isSignature) return 'Signatures';
  const t = contentType.toLowerCase();
  if (t.startsWith('image/')) return 'Images';
  if (t.startsWith('video/')) return 'Videos';
  if (t.startsWith('text/') || t === 'application/pdf' || t.includes('word') || t.includes('excel') || t.includes('spreadsheet')
    || t.includes('presentation') || t.includes('powerpoint') || t.includes('opendocument') || t === 'application/rtf') return 'Documents';
  return 'Other';
}

/** "1.4 MB" — files' sizes on the storage page. */
export function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let v = n / 1024;
  let i = 0;
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++; }
  return `${v < 10 ? v.toFixed(1) : Math.round(v)} ${units[i]}`;
}

/** The page a record opens on, when it has one. */
export function recordPath(type: string, id: string): string | null {
  switch (type) {
    case 'WorkOrder': return `/work-orders/${id}`;
    case 'Asset': return `/assets/${id}`;
    case 'Part': return `/inventory/parts/${id}`;
    case 'PurchaseOrder': return `/purchasing/orders/${id}`;
    case 'PurchaseRequest': return `/purchasing/requests/${id}`;
    case 'Contract': return `/contracts/${id}`;
    case 'PmSchedule': return `/pm/schedules/${id}`;
    case 'PmTask': return `/pm/templates/${id}`;
    case 'WorkOrderGroup': return `/work-groups/${id}`;
    case 'Location': return `/locations?loc=${id}`;
    default: return null;
  }
}

export const SOURCES = ['Web', 'Phone', 'Import', 'API', 'Job'] as const;
export const ACTIONS = ['Created', 'Updated', 'Deleted', 'SettingsChanged', 'SignedIn', 'SignInFailed', 'LockedOut', 'SignedOut', 'DeviceSignedOut',
  'OtherSessionsEnded', 'PasswordChanged', 'PasswordRenewed', 'Anonymised'] as const;
export const RECORD_TYPES = ['WorkOrder', 'WorkRequest', 'Asset', 'Location', 'Site', 'Part', 'Store', 'PurchaseOrder', 'PurchaseRequest', 'Supplier', 'Contract',
  'PmSchedule', 'PmTask', 'Employee', 'AppUser', 'Role', 'Tenant', 'Attachment'] as const;
