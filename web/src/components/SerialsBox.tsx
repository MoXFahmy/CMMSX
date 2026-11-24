import { Textarea } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { parseSerials, serialProblem } from '@/pages/inventory/units';

/** A serial number per piece (0.24.0), one per line, with how many are entered against how many are needed. */
export function SerialsBox({ value, onChange, needed, label, size }: {
  value: string; onChange: (v: string) => void; needed: number; label?: string; size?: 'xs' | 'sm';
}) {
  const { t } = useTranslation();
  const serials = parseSerials(value);
  const problem = serialProblem(serials, needed);
  return (
    <Textarea label={label ?? t('units.serials')} size={size} autosize minRows={2} maxRows={8} value={value} onChange={(e) => onChange(e.currentTarget.value)}
      placeholder={t('units.serialsHint')} description={t('units.serialCount', { n: serials.length, m: needed })}
      error={value.trim() && problem === 'duplicate' ? t('units.duplicate') : undefined} />
  );
}

/** The message for serials that don't fit <needed> pieces, or null when they do. */
export function serialsError(t: (k: string, o?: Record<string, unknown>) => string, text: string, needed: number): string | null {
  const problem = serialProblem(parseSerials(text), needed);
  return problem === 'duplicate' ? t('units.duplicate') : problem === 'count' ? t('units.wrongCount', { m: needed }) : null;
}
