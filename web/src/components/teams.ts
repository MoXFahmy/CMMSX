import { useQuery } from '@tanstack/react-query';
import { get } from '@/api/client';
import type { Team } from '@/api/types';

/** Teams (0.9.0); with a site, the ones usable there (its own and those of every site). */
export function useTeams(siteId?: string | null, activeOnly = false) {
  return useQuery({
    queryKey: ['teams', siteId ?? null, activeOnly],
    queryFn: () => get<Team[]>('/teams', { siteId: siteId ?? undefined, active: activeOnly || undefined }),
    staleTime: 60_000,
  });
}

/** Select options; the current team stays listed even when it was made inactive since. */
export function teamOptions(teams: Team[] | undefined, current?: string | null) {
  return (teams ?? []).filter((x) => x.isActive || x.id === current).map((x) => ({ value: x.id, label: x.name }));
}
