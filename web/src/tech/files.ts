/** Videos and documents a job can carry from the phone (0.11.0), as the server accepts them; photos stay photos. */
const FILE_EXTENSIONS = ['.mp4', '.mov', '.webm', '.pdf', '.doc', '.docx', '.xls', '.xlsx', '.txt', '.csv'];
const PHOTO_EXTENSIONS = ['.jpg', '.jpeg', '.png', '.webp'];

/** For the file picker. */
export const FILE_ACCEPT = ['video/*', ...FILE_EXTENSIONS].join(',');

const ext = (name: string) => { const i = name.lastIndexOf('.'); return i < 0 ? '' : name.slice(i).toLowerCase(); };

/** "photo" (JPEG, PNG, WebP), "file" (video or document) or null (the app can't attach it). */
export function fileKind(name: string, type: string): 'photo' | 'file' | null {
  const e = ext(name);
  if (PHOTO_EXTENSIONS.includes(e) || /^image\/(jpeg|png|webp)$/.test(type)) return 'photo';
  if (FILE_EXTENSIONS.includes(e)) return 'file';
  return null;
}

/** The limit in MB a file goes over, or null: photos 10 MB, videos and documents 50 MB. */
export function sizeProblem(kind: 'photo' | 'file', bytes: number): number | null {
  const mb = kind === 'photo' ? 10 : 50;
  return bytes > mb * 1024 * 1024 ? mb : null;
}
