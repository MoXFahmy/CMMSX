import { ApiError } from '@/api/client';

/** A rename onto a name another row already has (0.21.0 brands, models and units): the row the server offers to merge into. */
export function isMergeConflict(e: unknown): { mergeWith: string; message?: string } | null {
  if (!(e instanceof ApiError) || e.status !== 409) return null;
  const body = e.body as { mergeWith?: string; message?: string } | undefined;
  return body?.mergeWith ? { mergeWith: body.mergeWith, message: body.message } : null;
}
