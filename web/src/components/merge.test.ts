import { describe, expect, it } from 'vitest';
import { ApiError } from '@/api/client';
import { isMergeConflict } from './merge';

describe('renaming onto a name already used', () => {
  it('reads the row to merge into from a 409', () => {
    expect(isMergeConflict(new ApiError(409, 'x', {}, { mergeWith: 'b1', message: 'Merge?' }))).toEqual({ mergeWith: 'b1', message: 'Merge?' });
  });
  it('is nothing for other answers', () => {
    expect(isMergeConflict(new ApiError(400, 'Bad'))).toBeNull();
    expect(isMergeConflict(new ApiError(409, 'Changed by someone else', {}, { title: 'x' }))).toBeNull();
    expect(isMergeConflict(new Error('x'))).toBeNull();
  });
});
