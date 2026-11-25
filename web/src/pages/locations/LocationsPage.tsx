import { useMemo, useState } from 'react';
import { ActionIcon, Anchor, Box, Breadcrumbs, Button, Card, Center, Grid, Group, Loader, Menu, SimpleGrid, Stack, Text, ThemeIcon, Title, UnstyledButton } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconDots, IconEdit, IconFolderPlus, IconPlus, IconTrash, IconBox, IconMapPin, IconQrcode } from '@tabler/icons-react';
import { openLabels } from '@/pages/labels/LabelsPage';
import { DataTable } from 'mantine-datatable';
import { del, get } from '@/api/client';
import type { AssetListItem, LocationDetail, Paged, Site } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { CriticalityBadge, LocationTypeIcon, SiteIcon, StatusBadge } from '@/components/Badges';
import { PageHeader } from '@/components/PageHeader';
import { useHierarchy } from '@/components/LocationPicker';
import { confirmDelete } from '@/components/confirm';
import { fmtDateTime, notifyError, notifySuccess } from '@/components/utils';
import { LocationFormModal, SiteFormModal } from './LocationForms';
import { buildTree, LocationTree, subtreeCount, type TreeNode } from '@/components/LocationTree';
import { TranslateButton } from '@/components/TranslateButton';
import { pick, pickNote } from '@/components/bilingual';

type Node = TreeNode;

export function LocationsPage() {
  const { t } = useTranslation();
  const { terms, can } = useAuth();
  const [params, setParams] = useSearchParams();
  const { data: h, isLoading } = useHierarchy();
  const [siteModal, siteM] = useDisclosure();
  const [editSite, setEditSite] = useState<Site | null>(null);
  const [locModal, locM] = useDisclosure();
  const [editLoc, setEditLoc] = useState<LocationDetail | null>(null);
  const [locInit, setLocInit] = useState<{ siteId: string; parentId?: string | null } | undefined>();

  const tree = useMemo(() => (h ? buildTree(h) : []), [h]);
  const selSite = params.get('site');
  const selLoc = params.get('loc');
  const selectedId = selLoc ?? selSite;

  const select = (n: Node) => setParams(n.kind === 'site' ? { site: n.id } : { loc: n.id }, { replace: true });

  const openNewLocation = (siteId: string, parentId?: string | null) => { setEditLoc(null); setLocInit({ siteId, parentId }); locM.open(); };

  if (isLoading) return <Center h={300}><Loader /></Center>;

  return (
    <>
      <PageHeader title={terms.locations} subtitle={t('locations.subtitle')}
        actions={can(P.locationsManage) && (
          <>
            <TranslateButton list="locations" refresh={[['locations'], ['hierarchy']]} />
            <Button variant="default" leftSection={<IconPlus size={16} />} onClick={() => { setEditSite(null); siteM.open(); }}>{t('locations.newSite')}</Button>
            <Button leftSection={<IconFolderPlus size={16} />} disabled={!h?.sites.length}
              onClick={() => openNewLocation(selSite ?? h?.locations.find((l) => l.id === selLoc)?.siteId ?? h!.sites[0].id, selLoc)}>{t('locations.newLocation')}</Button>
          </>
        )} />
      {h && h.sites.length === 0 ? (
        <Card p="xl"><Stack align="center" ta="center"><ThemeIcon size={52} radius="xl" variant="light"><IconMapPin size={28} /></ThemeIcon>
          <Title order={4}>{t('locations.noSites')}</Title><Text c="dimmed">{t('locations.noSitesBody')}</Text>
          {can(P.locationsManage) && <Button onClick={() => { setEditSite(null); siteM.open(); }}>{t('locations.newSite')}</Button>}</Stack></Card>
      ) : (
        <Grid gutter="lg">
          <Grid.Col span={{ base: 12, md: 4 }}>
            <Card p="sm">
              <LocationTree tree={tree} selected={selectedId} onSelect={select} />
            </Card>
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 8 }}>
            {selLoc ? <LocationPanel id={selLoc} onSelect={(id) => setParams({ loc: id }, { replace: true })} onSelectSite={(id) => setParams({ site: id }, { replace: true })}
                onEdit={(l) => { setEditLoc(l); setLocInit(undefined); locM.open(); }} onAddChild={(l) => openNewLocation(l.siteId, l.id)} />
              : selSite ? <SitePanel id={selSite} tree={tree} onSelect={(id) => setParams({ loc: id }, { replace: true })}
                onEdit={(s) => { setEditSite(s); siteM.open(); }} onAddChild={(s) => openNewLocation(s.id, null)} />
              : <Card p="xl"><Text c="dimmed" ta="center">{t('locations.selectNode')}</Text></Card>}
          </Grid.Col>
        </Grid>
      )}
      <SiteFormModal opened={siteModal} onClose={siteM.close} site={editSite} onSaved={(id) => setParams({ site: id }, { replace: true })} />
      <LocationFormModal opened={locModal} onClose={locM.close} location={editLoc} init={locInit} onSaved={(id) => setParams({ loc: id }, { replace: true })} />
    </>
  );
}

function Stat({ label, value }: { label: string; value: React.ReactNode }) {
  return <Stack gap={0}><Text fz="xs" c="dimmed" tt="uppercase" fw={700}>{label}</Text><Text fw={600} fz="lg">{value}</Text></Stack>;
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <Stack gap={2}><Text fz="xs" c="dimmed">{label}</Text><Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{children || '—'}</Text></Stack>;
}

function SitePanel({ id, tree, onSelect, onEdit, onAddChild }: { id: string; tree: Node[]; onSelect: (id: string) => void; onEdit: (s: Site) => void; onAddChild: (s: Site) => void }) {
  const { t } = useTranslation();
  const { terms, can } = useAuth();
  const qc = useQueryClient();
  const [, setParams] = useSearchParams();
  const { data: s } = useQuery({ queryKey: ['site', id], queryFn: () => get<Site>(`/sites/${id}`) });
  const node = tree.find((n) => n.id === id);
  if (!s) return <Card><Center h={200}><Loader /></Center></Card>;
  const remove = () => confirmDelete(s.name, async () => {
    try { await del(`/sites/${s.id}`); notifySuccess(t('common.deleted')); setParams({}); await qc.invalidateQueries({ queryKey: ['hierarchy'] }); } catch (e) { notifyError(e); }
  });
  return (
    <Stack>
      <Card>
        <Group justify="space-between" align="flex-start" wrap="nowrap">
          <Group wrap="nowrap"><SiteIcon size={44} /><Stack gap={0}><Title order={3}>{s.name}</Title><Text c="dimmed" fz="sm">{terms.site} · {s.code}{!s.isActive && ` · ${t('common.inactive')}`}</Text></Stack></Group>
          <Group gap="xs" wrap="nowrap">
            <Button variant="default" component={Link} to={`/assets?siteId=${s.id}`} leftSection={<IconBox size={16} />}>{t('locations.viewAssets')}</Button>
            {can(P.locationsManage) && (
              <Menu position="bottom-end">
                <Menu.Target><ActionIcon variant="default" size="lg" aria-label={t('common.actions')}><IconDots size={16} /></ActionIcon></Menu.Target>
                <Menu.Dropdown>
                  <Menu.Item leftSection={<IconEdit size={16} />} onClick={() => onEdit(s)}>{t('common.edit')}</Menu.Item>
                  <Menu.Item leftSection={<IconFolderPlus size={16} />} onClick={() => onAddChild(s)}>{t('locations.newLocation')}</Menu.Item>
                  <Menu.Divider />
                  <Menu.Item color="red" leftSection={<IconTrash size={16} />} onClick={remove}>{t('common.delete')}</Menu.Item>
                </Menu.Dropdown>
              </Menu>
            )}
          </Group>
        </Group>
        <SimpleGrid cols={{ base: 2, sm: 4 }} mt="lg">
          <Stat label={t('locations.locationsCount')} value={s.locationCount} />
          <Stat label={terms.assets} value={s.assetCount} />
          <Field label={t('locations.city')}>{[s.city, s.country].filter(Boolean).join(', ')}</Field>
          <Field label={t('locations.timeZone')}>{s.timeZone}</Field>
        </SimpleGrid>
        {(s.address || s.notes) && <SimpleGrid cols={{ base: 1, sm: 2 }} mt="md"><Field label={t('locations.address')}>{s.address}</Field><Field label={t('common.notes')}>{s.notes}</Field></SimpleGrid>}
      </Card>
      <ChildrenCard nodes={node?.children ?? []} onSelect={onSelect} />
    </Stack>
  );
}

function ChildrenCard({ nodes, onSelect }: { nodes: Node[]; onSelect: (id: string) => void }) {
  const { t } = useTranslation();
  if (!nodes.length) return null;
  return (
    <Card>
      <Title order={5} mb="sm">{t('locations.childLocations')} ({nodes.length})</Title>
      <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="xs">
        {nodes.map((c) => (
          <UnstyledButton key={c.id} onClick={() => onSelect(c.id)}>
            <Card padding="sm" className="kpi-card">
              <Group gap="xs" wrap="nowrap"><Box c="brand"><LocationTypeIcon type={c.loc!.type} size={20} /></Box>
                <Stack gap={0} style={{ minWidth: 0 }}><Text fz="sm" fw={500} lineClamp={1}>{c.name}</Text><Text fz="xs" c="dimmed">{t(`locationType.${c.loc!.type}`)} · {subtreeCount(c)} assets</Text></Stack></Group>
            </Card>
          </UnstyledButton>
        ))}
      </SimpleGrid>
    </Card>
  );
}

function LocationPanel({ id, onSelect, onSelectSite, onEdit, onAddChild }: { id: string; onSelect: (id: string) => void; onSelectSite: (id: string) => void; onEdit: (l: LocationDetail) => void; onAddChild: (l: LocationDetail) => void }) {
  const { t } = useTranslation();
  const { can } = useAuth();
  const qc = useQueryClient();
  const [, setParams] = useSearchParams();
  const navigate = useNavigate();
  const { data: h } = useHierarchy();
  const { data: l } = useQuery({ queryKey: ['location', id], queryFn: () => get<LocationDetail>(`/locations/${id}`) });
  const { data: assets, isFetching } = useQuery({
    queryKey: ['assets', 'at-location', id],
    queryFn: () => get<Paged<AssetListItem>>('/assets', { locationId: id, includeSubLocations: false, pageSize: 10, sort: 'assetNumber' }),
  });
  if (!l) return <Card><Center h={200}><Loader /></Center></Card>;
  const children = (h?.locations ?? []).filter((x) => x.parentId === id).sort((a, b) => a.name.localeCompare(b.name));
  const childNodes: Node[] = children.map((c) => ({ kind: 'location', id: c.id, siteId: c.siteId, name: c.name, code: c.code, loc: c, isActive: c.isActive, assetCount: c.assetCount, children: [] }));
  const remove = () => confirmDelete(l.name, async () => {
    try { await del(`/locations/${l.id}`); notifySuccess(t('common.deleted')); setParams(l.parentId ? { loc: l.parentId } : { site: l.siteId }); await qc.invalidateQueries({ queryKey: ['hierarchy'] }); } catch (e) { notifyError(e); }
  });
  return (
    <Stack>
      <Card>
        <Breadcrumbs separator="›" mb="xs" fz="sm">
          <Anchor fz="sm" onClick={() => onSelectSite(l.site.id)}>{l.site.name}</Anchor>
          {l.ancestors.map((a) => <Anchor key={a.id} fz="sm" onClick={() => onSelect(a.id)}>{a.name}</Anchor>)}
          <Text fz="sm" c="dimmed">{pick(l.name, l.nameAr)}</Text>
        </Breadcrumbs>
        <Group justify="space-between" align="flex-start" wrap="nowrap">
          <Group wrap="nowrap"><ThemeIcon size={44} variant="light" radius="md"><LocationTypeIcon type={l.type} size={24} /></ThemeIcon>
            <Stack gap={0}><Title order={3}>{pick(l.name, l.nameAr)}</Title><Text c="dimmed" fz="sm">{t(`locationType.${l.type}`)} · {l.code}{!l.isActive && ` · ${t('common.inactive')}`}</Text></Stack></Group>
          <Group gap="xs" wrap="nowrap">
            <Button variant="default" component={Link} to={`/assets?locationId=${l.id}`} leftSection={<IconBox size={16} />}>{t('locations.viewAssets')}</Button>
            <Menu position="bottom-end">
              <Menu.Target><ActionIcon variant="default" size="lg" aria-label={t('labels.printLabels')}><IconQrcode size={16} /></ActionIcon></Menu.Target>
              <Menu.Dropdown>
                <Menu.Item onClick={() => openLabels('location', [l.id])}>{t('labels.printLabel')}</Menu.Item>
                <Menu.Item onClick={() => openLabels('location', [l.id], true)}>{t('labels.withSubLocations')}</Menu.Item>
              </Menu.Dropdown>
            </Menu>
            {can(P.locationsManage) && (
              <Menu position="bottom-end">
                <Menu.Target><ActionIcon variant="default" size="lg" aria-label={t('common.actions')}><IconDots size={16} /></ActionIcon></Menu.Target>
                <Menu.Dropdown>
                  <Menu.Item leftSection={<IconEdit size={16} />} onClick={() => onEdit(l)}>{t('common.edit')} / {t('locations.move')}</Menu.Item>
                  <Menu.Item leftSection={<IconFolderPlus size={16} />} onClick={() => onAddChild(l)}>{t('locations.addChild')}</Menu.Item>
                  <Menu.Divider />
                  <Menu.Item color="red" leftSection={<IconTrash size={16} />} onClick={remove}>{t('common.delete')}</Menu.Item>
                </Menu.Dropdown>
              </Menu>
            )}
          </Group>
        </Group>
        <SimpleGrid cols={{ base: 2, sm: 4 }} mt="lg">
          <Stat label={t('locations.childLocations')} value={l.childCount} />
          <Stat label={t('locations.assetsHere')} value={l.assetCount} />
          <Stat label={t('locations.assetsSubtree')} value={l.subtreeAssetCount} />
          <Stack gap={4}><Text fz="xs" c="dimmed" tt="uppercase" fw={700}>{t('criticality.label')}</Text><CriticalityBadge value={l.criticality} /></Stack>
        </SimpleGrid>
        {(l.description || l.notesToTechnician) && (
          <SimpleGrid cols={{ base: 1, sm: 2 }} mt="md"><Field label={t('common.description')}>{l.description}</Field><Field label={t('locations.notesToTechnician')}>{pickNote(l.notesToTechnician, l.notesToTechnicianAr)}</Field></SimpleGrid>
        )}
        <Text fz="xs" c="dimmed" mt="md">{t('common.updatedAt', { date: fmtDateTime(l.updatedAt ?? l.createdAt) })}</Text>
      </Card>
      <ChildrenCard nodes={childNodes} onSelect={onSelect} />
      <Card>
        <Group justify="space-between" mb="sm"><Title order={5}>{t('locations.assetsHere')} ({assets?.total ?? 0})</Title>
          {(assets?.total ?? 0) > 10 && <Anchor component={Link} to={`/assets?locationId=${l.id}`} fz="sm">{t('common.viewAll')}</Anchor>}</Group>
        <DataTable minHeight={assets?.items.length ? undefined : 120} fetching={isFetching} records={assets?.items ?? []} noRecordsText={t('common.noRecords')}
          highlightOnHover rowClassName={() => 'clickable-row'} onRowClick={({ record }) => navigate(`/assets/${record.id}`)}
          columns={[
            { accessor: 'assetNumber', title: t('assets.number'), render: (a) => <Anchor component={Link} to={`/assets/${a.id}`} fz="sm" onClick={(e) => e.stopPropagation()}>{a.assetNumber}</Anchor> },
            { accessor: 'name', title: t('common.name') },
            { accessor: 'categoryName', title: t('assets.category'), visibleMediaQuery: (th) => `(min-width: ${th.breakpoints.sm})` },
            { accessor: 'status', title: t('common.status'), render: (a) => <StatusBadge status={a.status} /> },
          ]} />
      </Card>
    </Stack>
  );
}
