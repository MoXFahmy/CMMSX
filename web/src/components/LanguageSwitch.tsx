import { SegmentedControl } from '@mantine/core';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { put } from '@/api/client';
import { applyLanguage, LANGUAGES, pickLanguage, type Language } from '@/language';
import { notifyError } from '@/components/utils';

/**
 * English | العربية. Signed in, the choice is saved on the account (every device follows); on the sign-in page it only
 * changes this device. Each language is named in itself, so it can be found whichever one is showing.
 */
export function LanguageSwitch({ saveToAccount = true, size = 'xs' }: { saveToAccount?: boolean; size?: 'xs' | 'sm' }) {
  const { i18n } = useTranslation();
  const qc = useQueryClient();
  const change = async (code: Language) => {
    applyLanguage(code);
    if (!saveToAccount) return;
    try {
      await put('/auth/me/language', { language: code });
      await qc.invalidateQueries({ queryKey: ['me'] });
    } catch (e) { notifyError(e); }
  };
  return (
    <SegmentedControl size={size} value={pickLanguage(i18n.language)} onChange={(v) => void change(v as Language)}
      data={LANGUAGES.map((l) => ({ value: l.code, label: l.name }))} />
  );
}
