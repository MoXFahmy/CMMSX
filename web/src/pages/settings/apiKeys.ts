import type { ApiKeyStatus } from '@/api/types';

/** A key as lists show it: its first characters only ("cmx_4f9k2LmQ…"). */
export const keyLabel = (prefix: string) => `cmx_${prefix}…`;

export const keyStatusColor = (status: ApiKeyStatus) => (status === 'Active' ? 'teal' : status === 'AccountInactive' ? 'yellow' : 'gray');
