/** Even spread (0.12.0): how many assets each of <parts> occurrences covers, in list order, as equal as possible (as the server splits them). */
export function spreadBlocks(count: number, parts: number): number[] {
  if (parts < 2 || count < 1) return [count];
  const size = Math.floor(count / parts);
  const extra = count % parts;
  return Array.from({ length: parts }, (_, k) => size + (k < extra ? 1 : 0));
}
