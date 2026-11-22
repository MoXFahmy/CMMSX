import { useEffect, useState } from 'react';
import { Select } from '@mantine/core';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { IconBuildingFactory2 } from '@tabler/icons-react';
import { useAuth } from '@/auth/AuthContext';
import { getSite, setSite } from './siteSwitch';

const ALL = 'all';

/** Work in one site at a time (0.9.0): shown to people who can see more than one site. */
export function SiteSwitch() {
  const { t } = useTranslation();
  const { me, terms } = useAuth();
  const qc = useQueryClient();
  const sites = me?.access?.sites ?? [];
  const [current, setCurrent] = useState<string | null>(getSite());
  const stale = !!me && !!current && !sites.some((s) => s.id === current);
  // A site that is no longer this person's (access changed, another account on this browser): back to all of theirs.
  useEffect(() => {
    if (!stale) return;
    setSite(null); setCurrent(null);
    void qc.invalidateQueries();
  }, [stale, qc]);
  if (sites.length < 2) return null;
  const choose = (v: string | null) => {
    const id = !v || v === ALL ? null : v;
    setSite(id); setCurrent(id);
    void qc.invalidateQueries(); // everything on screen again, for the chosen site
  };
  return (
    <Select size="xs" w={{ base: 140, sm: 200 }} leftSection={<IconBuildingFactory2 size={14} />} aria-label={t('access.switchLabel', { site: terms.site.toLowerCase() })}
      data={[{ value: ALL, label: t('access.allSites', { sites: terms.sites.toLowerCase() }) }, ...sites.map((s) => ({ value: s.id, label: s.name }))]}
      value={current ?? ALL} onChange={choose} allowDeselect={false} searchable={sites.length > 8} comboboxProps={{ withinPortal: true }} />
  );
}
