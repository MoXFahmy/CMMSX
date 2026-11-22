/** The Sync page's label key for a waiting change: videos and documents travel as photo.add with kind 'file'. */
export const changeLabelKey = (c: { type: string; payload?: unknown }) =>
  c.type === 'photo.add' && (c.payload as { kind?: string } | null | undefined)?.kind === 'file' ? 'tech.change.file' : `tech.change.${c.type}`;
