import { useQuery } from '@tanstack/react-query';
import { get } from '@/api/client';

/** Whether AI can be used here this month (0.15.0): the server's key, the organization's switch, its limit and use. */
export type AiStatus = { configured: boolean; enabled: boolean; ceiling: number; limit: number; used: number; renewsOn: string };

/** AI buttons show only when the server has a key and the organization switched AI on. */
export const aiOn = (s?: AiStatus | null) => !!s?.configured && !!s?.enabled;

export function useAiStatus() {
  return useQuery({ queryKey: ['ai-status'], queryFn: () => get<AiStatus>('/ai/status'), staleTime: 60_000 });
}
