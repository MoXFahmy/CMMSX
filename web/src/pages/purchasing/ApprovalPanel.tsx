import { Accordion, Alert, Badge, Button, Card, Group, Stack, Text, ThemeIcon, Title } from '@mantine/core';
import { modals } from '@mantine/modals';
import { useTranslation } from 'react-i18next';
import { IconCheck, IconClock, IconPlayerStop, IconRefresh, IconX } from '@tabler/icons-react';
import { useAuth } from '@/auth/AuthContext';
import i18n from '@/i18n';
import { fmtDateTime, fmtPrice } from '@/components/utils';
import { stepState, whoIsNext, type ApprovalPanel as Panel, type ApprovalStep } from './approvals';

/** Restarting drops the round's signatures and e-mails the first level again, so it asks first (0.29.1). */
export function askRestartApproval(onConfirm: () => void) {
  modals.openConfirmModal({
    title: i18n.t('approvals.restartTitle'),
    children: <Text size="sm">{i18n.t('approvals.restartBody')}</Text>,
    labels: { confirm: i18n.t('approvals.restart'), cancel: i18n.t('common.cancel') },
    onConfirm: () => onConfirm(),
  });
}

function Step({ s }: { s: ApprovalStep }) {
  const { t } = useTranslation();
  const { me } = useAuth();
  const currency = me?.tenant.settings.currency ?? 'USD';
  const state = stepState(s);
  const icon = s.status === 'Rejected' ? <IconX size={14} /> : state === 'done' ? <IconCheck size={14} /> : state === 'waiting' ? <IconClock size={14} /> : state === 'stopped' ? <IconPlayerStop size={14} /> : null;
  const color = s.status === 'Rejected' ? 'red' : state === 'done' ? 'teal' : state === 'waiting' ? 'blue' : 'gray';
  return (
    <Group align="flex-start" wrap="nowrap" gap="sm">
      <ThemeIcon size={24} radius="xl" color={color} variant={state === 'pending' ? 'light' : 'filled'}>{icon ?? <Text fz={11} fw={700}>{s.step}</Text>}</ThemeIcon>
      <Stack gap={2} style={{ flex: 1 }}>
        <Group gap={6}><Text fw={600} fz="sm">{s.name}</Text><Text fz="xs" c="dimmed">≥ {fmtPrice(s.minAmount, currency)} · {t(`approvals.rule${s.rule}`)}</Text></Group>
        {s.approvers.map((a) => (
          <Text key={a.id} fz="sm" c={a.signed ? undefined : 'dimmed'}>
            {a.signed ? (a.approved ? '✓ ' : '✗ ') : '· '}{a.name}
            {a.signed && a.at && <Text span fz="xs" c="dimmed"> · {fmtDateTime(a.at)}</Text>}
            {a.comment && <Text span fz="xs"> — “{a.comment}”</Text>}
          </Text>
        ))}
      </Stack>
    </Group>
  );
}

/** The order's approval levels: who signed, who's next, earlier rounds, and a way out when nobody listed can sign any more. */
export function ApprovalPanel({ approval, canRestart, onRestart }: { approval: Panel; canRestart: boolean; onRestart: () => void }) {
  const { t } = useTranslation();
  const next = whoIsNext(approval);
  return (
    <Card mb="md">
      <Group justify="space-between" mb="sm">
        <Title order={5}>{t('approvals.panelTitle')}{approval.round > 1 && <Text span c="dimmed" fz="sm" fw={400}> · {t('approvals.round', { round: approval.round })}</Text>}</Title>
        {approval.waiting && next.length > 0 && <Badge variant="light" radius="sm" style={{ textTransform: 'none' }}>{t('approvals.waitingFor', { names: next.join(', ') })}</Badge>}
      </Group>
      {approval.nobodyCanSign && (
        <Alert color="orange" mb="sm" title={t('approvals.stuckTitle')}>
          <Text fz="sm">{t('approvals.stuckBody')}</Text>
          {canRestart && <Button mt="xs" size="xs" leftSection={<IconRefresh size={14} />} onClick={() => askRestartApproval(onRestart)}>{t('approvals.restart')}</Button>}
        </Alert>
      )}
      <Stack gap="sm">{approval.steps.map((s) => <Step key={s.step} s={s} />)}</Stack>
      {!approval.nobodyCanSign && canRestart && approval.waiting && (
        <Button mt="sm" size="xs" variant="subtle" leftSection={<IconRefresh size={14} />} onClick={() => askRestartApproval(onRestart)}>{t('approvals.restart')}</Button>
      )}
      {approval.earlierRounds.length > 0 && (
        <Accordion variant="contained" mt="md">
          {approval.earlierRounds.map((r) => (
            <Accordion.Item key={r.round} value={String(r.round)}>
              <Accordion.Control><Text fz="sm">{t('approvals.round', { round: r.round })}</Text></Accordion.Control>
              <Accordion.Panel><Stack gap="sm">{r.steps.map((s) => <Step key={s.step} s={s} />)}</Stack></Accordion.Panel>
            </Accordion.Item>
          ))}
        </Accordion>
      )}
    </Card>
  );
}
