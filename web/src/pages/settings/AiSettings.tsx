import { Alert, Card, NumberInput, Stack, Switch, Text, Title } from '@mantine/core';
import type { UseFormReturnType } from '@mantine/form';
import { useTranslation } from 'react-i18next';
import type { TenantSettings } from '@/api/types';
import { useAiStatus } from '@/components/aiStatus';
import { limitError } from './aiUsage';

/** Settings › Organization › AI assistant (0.15.0): the switch and the organization's monthly limit under the server's ceiling. */
export function AiSettings({ form, editable }: { form: UseFormReturnType<{ name: string; settings: TenantSettings }>; editable: boolean }) {
  const { t } = useTranslation();
  const { data: status } = useAiStatus();
  if (!status) return null;
  const s = form.values.settings;
  const limit = s.aiMonthlyLimit ?? '';
  return (
    <Card><Stack>
      <div><Title order={5}>{t('ai.title')}</Title><Text c="dimmed" fz="sm">{t('ai.privacy')}</Text></div>
      {!status.configured && <Alert color="gray" variant="light">{t('ai.notSetUp')}</Alert>}
      <Switch label={t('ai.use')} disabled={!editable || (!status.configured && !s.aiEnabled)} checked={!!s.aiEnabled}
        onChange={(e) => form.setFieldValue('settings.aiEnabled', e.currentTarget.checked)} />
      <NumberInput w={260} label={t('ai.monthlyLimit')} description={t('ai.limitHint', { max: status.ceiling })} placeholder={String(status.ceiling)}
        min={0} max={status.ceiling} allowDecimal={false} disabled={!editable} value={limit}
        error={limitError(limit, status.ceiling, t) ?? form.errors['settings.aiMonthlyLimit']}
        onChange={(v) => form.setFieldValue('settings.aiMonthlyLimit', v === '' ? null : Number(v))} />
    </Stack></Card>
  );
}
