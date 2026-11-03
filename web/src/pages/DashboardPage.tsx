import { Button, Card, Grid, Group, Loader, SimpleGrid, Stack, Table, Text, ThemeIcon, Title, Center, Anchor } from '@mantine/core';
import { DonutChart, BarChart } from '@mantine/charts';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconAlertTriangle, IconBox, IconCash, IconShieldCheck, IconTool, IconDatabaseImport, IconClipboardList, IconClockExclamation, IconCircleCheck, IconInbox } from '@tabler/icons-react';
import { PriorityBadge, WoStatusBadge } from '@/components/WorkBadges';
import { get, post } from '@/api/client';
import type { Dashboard } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';
import { StatusBadge, statusColors } from '@/components/Badges';
import { PageHeader } from '@/components/PageHeader';
import { fmtDate, fmtMoney, fromNow, notifyError, notifySuccess } from '@/components/utils';

function Kpi({ label, value, icon: Icon, color, to }: { label: string; value: string; icon: typeof IconBox; color: string; to?: string }) {
  const inner = (
    <Group justify="space-between" wrap="nowrap">
      <Stack gap={2}>
        <Text c="dimmed" fz="xs" tt="uppercase" fw={700}>{label}</Text>
        <Text fz={28} fw={700} lh={1.1}>{value}</Text>
      </Stack>
      <ThemeIcon size={44} radius="md" variant="light" color={color}><Icon size={24} /></ThemeIcon>
    </Group>
  );
  return to ? <Card component={Link} to={to} className="kpi-card">{inner}</Card> : <Card className="kpi-card">{inner}</Card>;
}

export function DashboardPage() {
  const { t } = useTranslation();
  const { me, terms, can } = useAuth();
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({ queryKey: ['dashboard'], queryFn: () => get<Dashboard>('/dashboard'), enabled: can(P.dashboardView) });
  const sample = useMutation({
    mutationFn: () => post<{ created: number }>('/settings/sample-data'),
    onSuccess: (r) => { notifySuccess(t('dashboard.sampleLoaded', { count: r.created })); void qc.invalidateQueries(); },
    onError: notifyError,
  });
  const currency = me?.tenant.settings.currency ?? 'USD';

  if (isLoading || !data) return <Center h={300}><Loader /></Center>;
  const firstName = me?.user.fullName.split(' ')[0] ?? '';

  if (data.totals.assets === 0 && data.totals.sites === 0) {
    return (
      <>
        <PageHeader title={t('dashboard.greeting', { name: firstName })} />
        <Card p="xl" maw={720}>
          <Stack align="flex-start">
            <ThemeIcon size={48} radius="md" variant="light"><IconDatabaseImport size={26} /></ThemeIcon>
            <Title order={3}>{t('dashboard.emptyTitle')}</Title>
            <Text c="dimmed">{t('dashboard.emptyBody')}</Text>
            <Group>
              {can(P.locationsManage) && <Button component={Link} to="/locations">{t('locations.newSite')}</Button>}
              {can(P.settingsManage) && <Button variant="light" loading={sample.isPending} onClick={() => sample.mutate()}>{t('dashboard.loadSample')}</Button>}
            </Group>
          </Stack>
        </Card>
      </>
    );
  }

  return (
    <>
      <PageHeader title={t('dashboard.greeting', { name: firstName })} subtitle={t('dashboard.subtitle', { sites: terms.sites.toLowerCase() })} />
      {can(P.woView) && (
        <>
          <SimpleGrid cols={{ base: 1, xs: 2, lg: 4 }} mb="lg">
            <Kpi label={t('dashboard.openWork')} value={data.work.open.toLocaleString()} icon={IconClipboardList} color="blue" to="/work-orders" />
            <Kpi label={t('dashboard.overdueWork')} value={data.work.overdue.toLocaleString()} icon={IconClockExclamation} color="red" to="/work-orders?view=overdue" />
            <Kpi label={t('dashboard.completed30')} value={data.work.completedLast30.toLocaleString()} icon={IconCircleCheck} color="teal" to="/work-orders?tab=Completed" />
            <Kpi label={t('dashboard.requestsAwaiting')} value={data.work.requestsAwaiting.toLocaleString()} icon={IconInbox} color="grape" to="/work-requests" />
          </SimpleGrid>
          {data.work.urgent.length > 0 && (
            <Card mb="lg">
              <Title order={5} mb="sm">{t('dashboard.urgentWork')}</Title>
              <Table verticalSpacing={6}><Table.Tbody>
                {data.work.urgent.map((w) => (
                  <Table.Tr key={w.id}>
                    <Table.Td w={110}><Anchor component={Link} to={`/work-orders/${w.id}`} fz="sm" fw={600}>{w.number}</Anchor></Table.Td>
                    <Table.Td><Text fz="sm" lineClamp={1}>{w.title}</Text></Table.Td>
                    <Table.Td visibleFrom="sm"><Text fz="sm" c="dimmed">{w.assetNumber}</Text></Table.Td>
                    <Table.Td w={110}><PriorityBadge value={w.priority} /></Table.Td>
                    <Table.Td w={120}><WoStatusBadge status={w.status} /></Table.Td>
                    <Table.Td w={110} visibleFrom="sm"><Text fz="sm" c="dimmed">{fmtDate(w.dueDate)}</Text></Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody></Table>
            </Card>
          )}
        </>
      )}
      <SimpleGrid cols={{ base: 1, xs: 2, lg: 4 }} mb="lg">
        <Kpi label={t('dashboard.totalAssets')} value={data.totals.assets.toLocaleString()} icon={IconBox} color="brand" to="/assets" />
        <Kpi label={t('dashboard.critical')} value={data.totals.criticalAssets.toLocaleString()} icon={IconAlertTriangle} color="red" to="/assets?criticality=Critical" />
        <Kpi label={t('dashboard.underRepair')} value={data.totals.underRepair.toLocaleString()} icon={IconTool} color="orange" to="/assets?status=UnderRepair" />
        <Kpi label={t('dashboard.assetValue')} value={fmtMoney(data.totals.assetValue, currency)} icon={IconCash} color="teal" />
      </SimpleGrid>
      <Grid mb="lg">
        <Grid.Col span={{ base: 12, md: 5 }}>
          <Card h="100%">
            <Title order={5} mb="md">{t('dashboard.byStatus')}</Title>
            <Group justify="center" wrap="nowrap" align="center">
              <DonutChart size={170} thickness={26} withTooltip data={data.byStatus.map((s) => ({ name: t(`status.${s.status}`), value: s.count, color: `${statusColors[s.status]}.6` }))} />
              <Stack gap={6}>
                {data.byStatus.map((s) => (
                  <Anchor key={s.status} component={Link} to={`/assets?status=${s.status}`} underline="never" c="inherit">
                    <Group gap={8} wrap="nowrap"><StatusBadge status={s.status} /><Text fw={600}>{s.count}</Text></Group>
                  </Anchor>
                ))}
              </Stack>
            </Group>
          </Card>
        </Grid.Col>
        <Grid.Col span={{ base: 12, md: 7 }}>
          <Card h="100%">
            <Title order={5} mb="md">{t('dashboard.byCategory')}</Title>
            <BarChart h={Math.max(160, data.byCategory.length * 30 + 40)} data={data.byCategory.map((c) => ({ name: c.name, count: c.count }))} dataKey="name" orientation="vertical" yAxisProps={{ width: 170, interval: 0 }}
              series={[{ name: 'count', label: terms.assets, color: 'brand.6' }]} gridAxis="none" />
          </Card>
        </Grid.Col>
      </Grid>
      <Grid>
        <Grid.Col span={{ base: 12, md: 4 }}>
          <Card h="100%">
            <Title order={5} mb="sm">{t('dashboard.bySite')}</Title>
            <Table verticalSpacing={6}>
              <Table.Tbody>
                {data.bySite.map((s) => (
                  <Table.Tr key={s.siteId}><Table.Td><Anchor component={Link} to={`/assets?siteId=${s.siteId}`} fz="sm">{s.name}</Anchor></Table.Td><Table.Td ta="right" fw={600}>{s.count}</Table.Td></Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Card>
        </Grid.Col>
        <Grid.Col span={{ base: 12, md: 4 }}>
          <Card h="100%">
            <Group justify="space-between" mb="sm"><Title order={5}>{t('dashboard.upcomingWarranty')}</Title><ThemeIcon variant="light" color="yellow"><IconShieldCheck size={16} /></ThemeIcon></Group>
            {data.warrantyExpiring.length === 0 ? <Text c="dimmed" fz="sm">{t('common.noRecords')}</Text> : (
              <Stack gap={8}>
                {data.warrantyExpiring.map((a) => (
                  <Group key={a.id} justify="space-between" wrap="nowrap">
                    <Anchor component={Link} to={`/assets/${a.id}`} fz="sm" lineClamp={1}>{a.assetNumber} · {a.name}</Anchor>
                    <Text fz="xs" c="dimmed" style={{ whiteSpace: 'nowrap' }}>{fmtDate(a.warrantyExpiresOn)}</Text>
                  </Group>
                ))}
              </Stack>
            )}
          </Card>
        </Grid.Col>
        <Grid.Col span={{ base: 12, md: 4 }}>
          <Card h="100%">
            <Title order={5} mb="sm">{t('dashboard.recentAssets')}</Title>
            <Stack gap={8}>
              {data.recentAssets.map((a) => (
                <Group key={a.id} justify="space-between" wrap="nowrap">
                  <Stack gap={0} style={{ minWidth: 0 }}>
                    <Anchor component={Link} to={`/assets/${a.id}`} fz="sm" lineClamp={1}>{a.assetNumber} · {a.name}</Anchor>
                    <Text fz="xs" c="dimmed" lineClamp={1}>{a.siteName} · {fromNow(a.createdAt)}</Text>
                  </Stack>
                  <div style={{ flexShrink: 0 }}><StatusBadge status={a.status} /></div>
                </Group>
              ))}
            </Stack>
          </Card>
        </Grid.Col>
      </Grid>
    </>
  );
}
