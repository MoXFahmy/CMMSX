import { createContext, useContext, useEffect, type ReactNode } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { get } from '@/api/client';
import type { Me, Terminology } from '@/api/types';
import i18n from '@/i18n';
import { applyLanguage, pickLanguage } from '@/language';

type AuthValue = {
  me: Me | null;
  loading: boolean;
  can: (permission: string) => boolean;
  terms: Terminology;
  refresh: () => Promise<unknown>;
};

const defaultTerms: Terminology = { site: 'Site', sites: 'Sites', location: 'Location', locations: 'Locations', asset: 'Asset', assets: 'Assets' };
const arabicTerms: Terminology = { site: 'الموقع', sites: 'المواقع', location: 'المكان', locations: 'الأماكن', asset: 'الأصل', assets: 'الأصول' };
const AuthContext = createContext<AuthValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient();
  const { i18n: lang } = useTranslation(); // re-render when the language changes, for the terms
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
  // The account's language (the person's choice, else the organization's) wins over this device's last one.
  const accountLanguage = me ? pickLanguage(me.user.language) : null;
  useEffect(() => { if (accountLanguage && accountLanguage !== i18n.language) applyLanguage(accountLanguage); }, [accountLanguage]);
  const value: AuthValue = {
    me,
    loading: isLoading,
    can: (p) => !!me?.permissions.includes(p),
    // The organization's names for site / location / asset, in the language showing.
    terms: lang.language === 'ar' ? (me?.tenant.settings.terminologyArabic ?? arabicTerms) : (me?.tenant.settings.terminology ?? defaultTerms),
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
  woView: 'workorders.view', woCreate: 'workorders.create', woEdit: 'workorders.edit', woExecute: 'workorders.execute', woClose: 'workorders.close', woDelete: 'workorders.delete', woLogForOthers: 'workorders.logForOthers',
  pmView: 'pm.view', pmManage: 'pm.manage', pmGenerate: 'pm.generate', metersRecord: 'meters.record',
  inventoryView: 'inventory.view', inventoryManage: 'inventory.manage', inventoryTransact: 'inventory.transact',
  purchasingView: 'purchasing.view', purchasingCreate: 'purchasing.create', purchasingApprove: 'purchasing.approve', purchasingReceive: 'purchasing.receive',
  purchasingRequest: 'purchasing.request', purchasingApproveRequests: 'purchasing.approveRequests',
  suppliersManage: 'suppliers.manage', reportsView: 'reports.view',
  contractsView: 'contracts.view', contractsManage: 'contracts.manage',
} as const;
