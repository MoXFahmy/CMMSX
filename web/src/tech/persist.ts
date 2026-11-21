/**
 * Asks the browser to keep the technician view's saved data (jobs, places, changes waiting to be sent). Without this a
 * browser may clear it to free space, and Safari clears it after 7 days without a visit. Some browsers say no or can't:
 * the view works the same, it just may need a refresh after a long break.
 */
export async function keepData(): Promise<boolean> {
  const storage = typeof navigator === 'undefined' ? undefined : navigator.storage;
  if (!storage?.persist) return false;
  try {
    if (await storage.persisted?.()) return true;
    return await storage.persist();
  } catch { return false; }
}
