import i18n from '@/i18n';
export class ApiError extends Error {
  status: number;
  fieldErrors: Record<string, string>;
  /** The parsed error body, for answers that carry more than a message (e.g. the reserved-stock warning). */
  body?: unknown;
  constructor(status: number, message: string, fieldErrors: Record<string, string> = {}, body?: unknown) {
    super(message);
    this.status = status;
    this.fieldErrors = fieldErrors;
    this.body = body;
  }
}

/** Shown when a request gets no answer. The full app needs a connection; the technician view (/tech) works without one. */
export const NO_CONNECTION = 'No connection. Check your signal and try again.';

type Query = Record<string, string | number | boolean | null | undefined | string[]>;

export function toQuery(params?: Query): string {
  if (!params) return '';
  const sp = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === null || v === '') continue;
    if (Array.isArray(v)) { if (v.length) sp.set(k, v.join(',')); } else sp.set(k, String(v));
  }
  const s = sp.toString();
  return s ? `?${s}` : '';
}

async function parseError(res: Response): Promise<ApiError> {
  let body: { title?: string; errors?: Record<string, string[]> } = {};
  try { body = await res.json(); } catch { /* not JSON */ }
  const fieldErrors: Record<string, string> = {};
  for (const [k, v] of Object.entries(body.errors ?? {})) fieldErrors[k.charAt(0).toLowerCase() + k.slice(1)] = v.join(' ');
  const message = res.status === 403 ? 'You do not have permission to do this.' : body.title ?? `Request failed (${res.status})`;
  return new ApiError(res.status, message, fieldErrors, body);
}

async function request(path: string, init?: RequestInit & { json?: unknown }): Promise<Response> {
  const headers = new Headers(init?.headers);
  let body = init?.body;
  if (init?.json !== undefined) { headers.set('Content-Type', 'application/json'); body = JSON.stringify(init.json); }
  headers.set('Accept', 'application/json');
  headers.set('Accept-Language', i18n.language || 'en'); // the server answers messages in it
  let res: Response;
  // No answer at all (no signal, or the connection dropped): say so, not the browser's "Failed to fetch".
  try { res = await fetch(`/api${path}`, { ...init, headers, body, credentials: 'same-origin' }); }
  catch { throw new ApiError(0, NO_CONNECTION); }
  if (res.status === 401 && !path.startsWith('/auth/')) window.dispatchEvent(new Event('cmms:unauthorized'));
  if (!res.ok) throw await parseError(res);
  return res;
}

/** A file the server makes (an Excel workbook…), with the same headers and error handling as {@link api}. */
export async function apiBlob(path: string, init?: RequestInit): Promise<Blob> {
  return (await request(path, init)).blob();
}

export async function api<T>(path: string, init?: RequestInit & { json?: unknown }): Promise<T> {
  const res = await request(path, init);
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export const get = <T>(path: string, q?: Query) => api<T>(path + toQuery(q));
export const post = <T>(path: string, json?: unknown) => api<T>(path, { method: 'POST', json: json ?? {} });
export const put = <T>(path: string, json: unknown) => api<T>(path, { method: 'PUT', json });
export const del = (path: string) => api<void>(path, { method: 'DELETE' });
