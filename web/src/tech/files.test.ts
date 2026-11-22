import { describe, expect, it } from 'vitest';
import { FILE_ACCEPT, fileKind, sizeProblem } from './files';

describe('phone files', () => {
  it('tells photos from videos and documents', () => {
    expect(fileKind('a.jpg', 'image/jpeg')).toBe('photo');
    expect(fileKind('clip.MP4', 'video/mp4')).toBe('file');
    expect(fileKind('manual.pdf', '')).toBe('file');
    expect(fileKind('sheet.xlsx', 'application/octet-stream')).toBe('file');
    expect(fileKind('tool.exe', 'application/x-msdownload')).toBeNull();
    expect(FILE_ACCEPT).toContain('.pdf');
  });

  it('keeps photos to 10 MB and files to 50 MB', () => {
    expect(sizeProblem('photo', 10 * 1024 * 1024)).toBeNull();
    expect(sizeProblem('photo', 10 * 1024 * 1024 + 1)).toBe(10);
    expect(sizeProblem('file', 50 * 1024 * 1024)).toBeNull();
    expect(sizeProblem('file', 50 * 1024 * 1024 + 1)).toBe(50);
  });
});
