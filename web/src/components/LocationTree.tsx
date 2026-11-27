import { useMemo, useState } from 'react';
import { ActionIcon, Badge, Box, Group, ScrollArea, Text, TextInput, Tooltip, useDirection } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { IconArrowsMaximize, IconArrowsMinimize, IconChevronRight, IconSearch } from '@tabler/icons-react';
import type { HierarchyLocation } from '@/api/types';
import { LocationTypeIcon, SiteIcon } from '@/components/Badges';

export type TreeNode = {
  kind: 'site' | 'location'; id: string; siteId: string; name: string; otherName?: string | null; code: string; loc?: HierarchyLocation; children: TreeNode[]; assetCount: number; isActive: boolean;
};

export function buildTree(h: { sites: { id: string; code: string; name: string; isActive: boolean; assetCount: number }[]; locations: HierarchyLocation[] }): TreeNode[] {
  const kids = new Map<string, HierarchyLocation[]>();
  const ids = new Set(h.locations.map((l) => l.id));
  for (const l of h.locations) {
    // Under the site when its parent isn't listed (people limited to a branch don't see above it).
    const k = l.parentId && ids.has(l.parentId) ? l.parentId : `site:${l.siteId}`;
    kids.set(k, [...(kids.get(k) ?? []), l]);
  }
  const make = (l: HierarchyLocation): TreeNode => ({
    kind: 'location', id: l.id, siteId: l.siteId, name: l.name, otherName: l.otherName, code: l.code, loc: l, isActive: l.isActive, assetCount: l.assetCount,
    children: (kids.get(l.id) ?? []).sort((a, b) => a.name.localeCompare(b.name)).map(make),
  });
  return h.sites.map((s) => ({
    kind: 'site', id: s.id, siteId: s.id, name: s.name, code: s.code, isActive: s.isActive, assetCount: s.assetCount,
    children: (kids.get(`site:${s.id}`) ?? []).sort((a, b) => a.name.localeCompare(b.name)).map(make),
  }));
}

export const subtreeCount = (n: TreeNode): number => n.assetCount + n.children.reduce((s, c) => s + subtreeCount(c), 0);

export function filterTree(nodes: TreeNode[], q: string): TreeNode[] {
  if (!q) return nodes;
  const needle = q.toLowerCase();
  return nodes.flatMap((n) => {
    const kids = filterTree(n.children, q);
    const hit = n.name.toLowerCase().includes(needle) || (n.otherName ?? '').toLowerCase().includes(needle) || n.code.toLowerCase().includes(needle);
    return hit || kids.length ? [{ ...n, children: hit ? n.children : kids }] : [];
  });
}

function TreeRow({ node, depth, expanded, toggle, selected, onSelect, forceOpen }: {
  node: TreeNode; depth: number; expanded: Set<string>; toggle: (id: string) => void; selected: string | null; onSelect: (n: TreeNode) => void; forceOpen: boolean;
}) {
  const { dir } = useDirection();
  const { t } = useTranslation();
  const open = forceOpen || expanded.has(node.id);
  const total = subtreeCount(node);
  return (
    <>
      <Group gap={4} wrap="nowrap" className="tree-node" data-selected={selected === node.id} ps={depth * 16 + 4} pe={6} py={4}
        onClick={() => onSelect(node)} style={{ cursor: 'pointer', opacity: node.isActive ? 1 : 0.55 }}>
        <ActionIcon variant="subtle" color="gray" size="sm" onClick={(e) => { e.stopPropagation(); toggle(node.id); }}
          style={{ visibility: node.children.length ? 'visible' : 'hidden', transform: open ? 'rotate(90deg)' : dir === 'rtl' ? 'scaleX(-1)' : undefined, transition: 'transform 120ms' }}
          aria-label={t('locations.toggleChildren')}>
          <IconChevronRight size={14} />
        </ActionIcon>
        {node.kind === 'site' ? <SiteIcon size={22} /> : <Box c="dimmed" style={{ display: 'flex' }}><LocationTypeIcon type={node.loc!.type} /></Box>}
        <Text fz="sm" fw={node.kind === 'site' ? 600 : 400} lineClamp={1} style={{ flex: 1 }}>{node.name}</Text>
        {total > 0 && <Badge size="xs" variant="default" circle={total < 10}>{total}</Badge>}
      </Group>
      {open && node.children.map((c) => <TreeRow key={c.id} node={c} depth={depth + 1} expanded={expanded} toggle={toggle} selected={selected} onSelect={onSelect} forceOpen={forceOpen} />)}
    </>
  );
}

/** The site and location tree with search and expand/collapse: the locations page and the home page's location card. */
export function LocationTree({ tree, selected, onSelect, mah }: { tree: TreeNode[]; selected: string | null; onSelect: (n: TreeNode) => void; mah?: string | number }) {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const visible = useMemo(() => filterTree(tree, search.trim()), [tree, search]);
  const toggle = (id: string) => setExpanded((prev) => { const s = new Set(prev); if (s.has(id)) s.delete(id); else s.add(id); return s; });
  const allIds = () => { const ids = new Set<string>(); const walk = (ns: TreeNode[]) => ns.forEach((n) => { ids.add(n.id); walk(n.children); }); walk(tree); return ids; };
  const pick = (n: TreeNode) => { setExpanded((prev) => new Set(prev).add(n.id)); onSelect(n); };
  return (
    <>
      <Group gap={6} mb="xs" wrap="nowrap">
        <TextInput placeholder={t('locations.searchTree')} leftSection={<IconSearch size={16} />} value={search} onChange={(e) => setSearch(e.currentTarget.value)} style={{ flex: 1 }} />
        <Tooltip label={t('locations.expandAll')}><ActionIcon variant="default" size="lg" aria-label={t('locations.expandAll')} onClick={() => setExpanded(allIds())}><IconArrowsMaximize size={16} /></ActionIcon></Tooltip>
        <Tooltip label={t('locations.collapseAll')}><ActionIcon variant="default" size="lg" aria-label={t('locations.collapseAll')} onClick={() => setExpanded(new Set())}><IconArrowsMinimize size={16} /></ActionIcon></Tooltip>
      </Group>
      <ScrollArea.Autosize mah={mah ?? 'calc(100vh - 250px)'}>
        {visible.length === 0 ? <Text c="dimmed" fz="sm" p="sm">{t('common.noRecords')}</Text> :
          visible.map((n) => <TreeRow key={n.id} node={n} depth={0} expanded={expanded} toggle={toggle} selected={selected} onSelect={pick} forceOpen={!!search.trim()} />)}
      </ScrollArea.Autosize>
    </>
  );
}
