/** The next work order number as settings preview it (0.21.0): with the year in it when numbers restart each year. */
export function previewNumber(prefix: string, padding: number, yearly: boolean, now: Date): string {
  const number = '1'.padStart(Math.min(12, Math.max(1, padding)), '0');
  return yearly ? `${prefix}${now.getFullYear()}-${number}` : `${prefix}${number}`;
}
