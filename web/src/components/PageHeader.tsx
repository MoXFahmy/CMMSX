import { Group, Stack, Text, Title, Anchor, Breadcrumbs } from '@mantine/core';
import type { ReactNode } from 'react';
import { Link } from 'react-router';

type Crumb = { label: string; to?: string };

export function PageHeader({ title, subtitle, actions, crumbs, badge }: { title: ReactNode; subtitle?: ReactNode; actions?: ReactNode; crumbs?: Crumb[]; badge?: ReactNode }) {
  return (
    <Stack gap={6} mb="lg">
      {crumbs && crumbs.length > 0 && (
        <Breadcrumbs separator="›" fz="sm">
          {crumbs.map((c, i) => (c.to ? <Anchor key={i} component={Link} to={c.to} fz="sm">{c.label}</Anchor> : <Text key={i} fz="sm" c="dimmed">{c.label}</Text>))}
        </Breadcrumbs>
      )}
      <Group justify="space-between" align="flex-start" wrap="wrap" gap="sm">
        <Stack gap={2} style={{ minWidth: 0, flex: '1 1 280px' }}>
          <Group gap="sm" wrap="wrap"><Title order={2} style={{ minWidth: 0 }} lineClamp={2}>{title}</Title>{badge}</Group>
          {subtitle && <Text c="dimmed" fz="sm">{subtitle}</Text>}
        </Stack>
        {actions && <Group gap="xs">{actions}</Group>}
      </Group>
    </Stack>
  );
}
