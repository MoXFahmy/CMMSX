import { createContext, useContext, useEffect, type ReactNode } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get } from '@/api/client';
import type { Me, Terminology } from '@/api/types';

type AuthValue = {
  me: Me | null;
  loading: boolean;
  can: (permission: string) => boolean;
  terms: Terminology;
  refresh: () => Promise<unknown>;
};

const defaultTerms: Terminology = { site: 'Site', sites: 'Sites', location: 'Location', locations: 'Locations', asset: 'Asset', assets: 'Assets' };
const AuthContext = createContext<AuthValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient();
  const { data, isLoading, refetch } = useQuery({
    queryKey: ['me'],
    queryFn: () => get<Me>('/auth/me').catch(() => null),
    staleTime: 5 * 60_000,
  });
  useEffect(() => {
    const onUnauthorized = () => qc.setQueryData(['me'], null);
    window.addEventListener('cmms:unauthorized', onUnauthorized);
    return () => window.removeEventListener('cmms:unauthorized', onUnauthorized);
  }, [qc]);
  const me = data ?? null;
  const value: AuthValue = {
    me,
    loading: isLoading,
    can: (p) => !!me?.permissions.includes(p),
    terms: me?.tenant.settings.terminology ?? defaultTerms,
    refresh: refetch,
  };
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}

export const P = {
  dashboardView: 'dashboard.view', locationsView: 'locations.view', locationsManage: 'locations.manage',
  assetsView: 'assets.view', assetsCreate: 'assets.create', assetsEdit: 'assets.edit', assetsDelete: 'assets.delete',
  assetsTransfer: 'assets.transfer', usersManage: 'users.manage', settingsManage: 'settings.manage',
  peopleView: 'people.view', peopleManage: 'people.manage', requestsView: 'requests.view', requestsCreate: 'requests.create', requestsManage: 'requests.manage',
  woView: 'workorders.view', woCreate: 'workorders.create', woEdit: 'workorders.edit', woExecute: 'workorders.execute', woClose: 'workorders.close', woDelete: 'workorders.delete',
} as const;
